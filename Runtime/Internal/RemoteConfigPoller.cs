using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Layers.Unity.Internal
{
    /// <summary>
    /// Coroutine-based remote config poller. Periodically fetches the remote config
    /// from <c>/config</c> using <see cref="UnityWebRequest"/> and feeds the response
    /// to the Rust core via <see cref="NativeBindings.layers_update_remote_config"/>.
    ///
    /// Supports HTTP ETag / 304 Not Modified to avoid re-downloading unchanged config.
    /// Default poll interval is 300 seconds (5 minutes), matching the Rust core's
    /// remote config TTL.
    ///
    /// Request ownership is explicit rather than scoped to a <c>using</c> block.
    /// A <c>using</c> only disposes on the paths that run to the end of the block,
    /// and a stopped coroutine is not one of them: Unity does not resume — or
    /// unwind — an iterator it has stopped, so the request a fetch is suspended on
    /// survives the coroutine that owns it. <see cref="StopPolling"/> therefore
    /// takes ownership of the in-flight request and aborts + disposes it itself.
    /// The invariant is <see cref="CreatedRequestCount"/> ==
    /// <see cref="ReleasedRequestCount"/> once polling has stopped.
    /// </summary>
    internal class RemoteConfigPoller
    {
        private readonly LayersRunner _runner;
        private readonly string _baseUrl;
        private readonly string _appId;

        // Supplies the core's own /config headers as JSON. Injected rather
        // than called directly so a test run — which has no native library to
        // P/Invoke into — can still assert on what goes onto the request.
        private readonly Func<string> _configHeadersJson;

        private string _etag;
        private Coroutine _pollingCoroutine;

        // The nested fetch coroutine. Stopping PollLoop does NOT stop this one:
        // it is a separate coroutine registered on the runner, and StopCoroutine
        // reaches only the handle it is given. StopPolling needs its own handle.
        private Coroutine _fetchCoroutine;

        // The request a fetch is currently suspended on. Whoever nulls this field
        // owns disposing that request, and nothing may read it afterwards — the
        // handle is native memory that Dispose frees.
        private UnityWebRequest _inFlightRequest;

        // Serializes fetches so there is only ever one owner of _inFlightRequest.
        // Mirrors FlushManager's _isFlushing guard: a periodic tick and a one-off
        // FetchNow must not both have a request in flight, or stopping releases
        // one and orphans the other.
        private bool _isFetching;

        /// <summary>
        /// Fired after a successful 200 response with the config JSON body.
        /// Subscribers (e.g., SKAN auto-config) can parse the JSON to extract
        /// platform-specific configuration sections.
        /// </summary>
        internal event Action<string> OnConfigUpdated;

        /// <summary>
        /// HTTP request timeout in seconds for config fetches.
        /// </summary>
        private const int RequestTimeoutSec = 10;

        /// <summary>
        /// Whether a config request is in flight right now.
        /// </summary>
        internal bool HasInFlightRequest => _inFlightRequest != null;

        /// <summary>
        /// How many <see cref="UnityWebRequest"/> handles this poller has created.
        /// </summary>
        internal int CreatedRequestCount { get; private set; }

        /// <summary>
        /// How many of those handles it has disposed. An abandoned native handle
        /// is invisible from the outside — no exception, no log, no failed test —
        /// so the accounting is kept here and asserted by
        /// <c>RemoteConfigPollerTests</c>.
        /// </summary>
        internal int ReleasedRequestCount { get; private set; }

        /// <param name="configHeadersJson">
        /// Returns the core's <c>/config</c> headers as JSON. Required: without
        /// it the request carries no <c>X-SDK-Version</c>, and a config the
        /// server targets by SDK version — a version-scoped killswitch, say —
        /// cannot reach this install at all.
        /// </param>
        internal RemoteConfigPoller(
            LayersRunner runner, string baseUrl, string appId, Func<string> configHeadersJson)
        {
            _runner = runner;
            // Ensure no trailing slash on the base URL
            _baseUrl = baseUrl != null ? baseUrl.TrimEnd('/') : "https://in.layers.com";
            _appId = appId;
            _configHeadersJson = configHeadersJson;
        }

        /// <summary>
        /// The value of <paramref name="name"/> on the request currently in
        /// flight, or null when none is. A narrow accessor rather than exposing
        /// the handle: the request is native memory this poller owns, and
        /// whoever nulls <c>_inFlightRequest</c> disposes it.
        /// </summary>
        internal string InFlightRequestHeader(string name)
        {
            UnityWebRequest request = _inFlightRequest;
            return request == null ? null : request.GetRequestHeader(name);
        }

        /// <summary>
        /// Start periodic config polling. Performs an initial fetch immediately,
        /// then repeats at the given interval. No-op if already polling.
        /// </summary>
        /// <param name="intervalSec">Seconds between polls. Default: 300 (5 minutes).</param>
        internal void StartPolling(float intervalSec = 300f)
        {
            if (_pollingCoroutine != null) return;
            _pollingCoroutine = _runner.StartCoroutine(PollLoop(intervalSec));
        }

        /// <summary>
        /// Stop polling and release everything the poller owns: the poll loop, the
        /// nested fetch coroutine, and the request that fetch is suspended on.
        ///
        /// Stopping the poll loop alone leaves the other two alive. That was the
        /// defect: <c>StopCoroutine(_pollingCoroutine)</c> does not stop the nested
        /// FetchConfig coroutine, does not abort the UnityWebRequest FetchConfig is
        /// yielded on, and does not unwind the block that was meant to dispose it.
        /// The request stayed live inside native curl with its handle never freed —
        /// so <c>LayersSDK.Shutdown()</c> left a live HTTP request on device, and in
        /// CI the accumulated orphans tore down at editor exit as a storm of
        /// "Curl error 42: Callback aborted" that ended in SIGSEGV (see the
        /// test-mode gate in Layers.Initialize).
        ///
        /// Safe to call repeatedly and safe with nothing in flight.
        /// </summary>
        internal void StopPolling()
        {
            // Claim the request BEFORE stopping the coroutines. Whichever side
            // nulls the field owns disposal, so claiming first makes this method
            // the sole owner regardless of what a given Unity version does with a
            // stopped iterator — the fetch's own release path re-checks ownership
            // and stands down.
            UnityWebRequest request = _inFlightRequest;
            _inFlightRequest = null;

            StopRunnerCoroutine(ref _pollingCoroutine);
            StopRunnerCoroutine(ref _fetchCoroutine);
            _isFetching = false;

            if (request == null) return;

            // Abort cancels the native transfer immediately; on a request that
            // already completed it is a no-op. Dispose then frees the handle.
            try
            {
                request.Abort();
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"Aborting in-flight config request threw: {e.Message}");
            }

            ReleaseRequest(request);
            LayersLogger.Log("Remote config polling stopped; in-flight request aborted");
        }

        /// <summary>
        /// Trigger a one-off config fetch outside the periodic schedule.
        /// No-op while a fetch is already in flight — its response is the same
        /// config this call would ask for, and a second concurrent request would
        /// leave one of the two handles unowned.
        /// </summary>
        internal void FetchNow()
        {
            if (StartFetch() == null)
                LayersLogger.Log("Remote config fetch skipped — one already in flight");
        }

        /// <summary>
        /// Start the fetch coroutine and record its handle, so StopPolling can
        /// stop it. Returns null when a fetch is already in flight.
        /// </summary>
        private Coroutine StartFetch()
        {
            if (_isFetching) return null;

            // Claimed before StartCoroutine: Unity runs the coroutine body
            // synchronously up to its first yield, so the flag must already be
            // set when FetchConfig's own bookkeeping runs.
            _isFetching = true;
            try
            {
                _fetchCoroutine = _runner.StartCoroutine(FetchConfig());
            }
            catch (Exception e)
            {
                // The runner is gone (quit tore the scene down) or the body threw
                // before its first yield. Release the guard rather than latch it —
                // a stuck flag would silently retire config polling for the rest
                // of the process.
                _isFetching = false;
                LayersLogger.Warn($"Remote config fetch could not start: {e.Message}");
                return null;
            }
            return _fetchCoroutine;
        }

        /// <summary>
        /// Stop a coroutine this poller started and forget its handle.
        /// </summary>
        private void StopRunnerCoroutine(ref Coroutine coroutine)
        {
            Coroutine handle = coroutine;
            coroutine = null;
            if (handle == null) return;

            // The runner is a MonoBehaviour: if it was destroyed first (quit tears
            // the scene down around Shutdown) its coroutines are already gone.
            // Unity's == null covers the destroyed-but-not-null case.
            if (_runner == null) return;
            _runner.StopCoroutine(handle);
        }

        /// <summary>
        /// Dispose a request handle and count it. Never throws.
        /// </summary>
        private void ReleaseRequest(UnityWebRequest request)
        {
            try
            {
                request.Dispose();
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"Disposing config request threw: {e.Message}");
            }
            ReleasedRequestCount++;
        }

        /// <summary>
        /// Copy the core's <c>/config</c> headers onto <paramref name="request"/>.
        ///
        /// Never throws and never aborts the fetch: a config poll that cannot
        /// reach the core is still worth sending, and the header set below is
        /// re-established by the caller. Content-Type is skipped — the core
        /// already omits it for a GET, and setting one on a bodyless request
        /// is meaningless.
        ///
        /// Returns whether <c>X-SDK-Version</c> ended up on the request, so the
        /// caller can stamp the fallback shape rather than send a GET the
        /// server cannot version-target at all.
        /// </summary>
        private bool ApplyCoreConfigHeaders(UnityWebRequest request)
        {
            if (_configHeadersJson == null) return false;

            Dictionary<string, string> headers;
            try
            {
                string json = _configHeadersJson();
                if (string.IsNullOrEmpty(json)) return false;
                headers = FlushManager.ParseHeaders(json);
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"Could not read config headers from the core: {e.Message}");
                return false;
            }

            bool sdkVersionApplied = false;
            foreach (var header in headers)
            {
                if (string.IsNullOrEmpty(header.Key)) continue;
                if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    request.SetRequestHeader(header.Key, header.Value ?? string.Empty);
                }
                catch (Exception e)
                {
                    // UnityWebRequest rejects malformed names/values outright.
                    LayersLogger.Warn($"Skipping config header '{header.Key}': {e.Message}");
                    continue;
                }

                if (string.Equals(header.Key, "X-SDK-Version", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(header.Value))
                {
                    sdkVersionApplied = true;
                }
            }

            return sdkVersionApplied;
        }

        private IEnumerator PollLoop(float intervalSec)
        {
            // Initial fetch immediately
            yield return StartFetch();

            while (true)
            {
                yield return new WaitForSecondsRealtime(intervalSec);
                yield return StartFetch();
            }
        }

        private IEnumerator FetchConfig()
        {
            // Everything runs inside the try, request construction included: an
            // exception before the first yield would otherwise leave _isFetching
            // latched true and kill polling for the rest of the process.
            UnityWebRequest request = null;
            try
            {
                // Build URL with query parameters matching the Flutter pattern
                string url = $"{_baseUrl}/config?app_id={UnityWebRequest.EscapeURL(_appId)}&platform={DeviceInfoCollector.RuntimePlatform}";

                request = UnityWebRequest.Get(url);
                _inFlightRequest = request;
                CreatedRequestCount++;

                // The core's headers first — X-SDK-Version above all, which is
                // what lets the server answer this GET version-specifically.
                // X-App-Id and Accept are re-set afterwards so the request is
                // still well-formed if the core could not answer.
                bool sdkVersionApplied = ApplyCoreConfigHeaders(request);

                request.SetRequestHeader("X-App-Id", _appId);
                request.SetRequestHeader("Accept", "application/json");

                // If the core could not answer, still stamp a version. A GET
                // carrying none is one the server cannot version-target at
                // all, which is the defect this whole path exists to remove.
                // Parity with Flutter's configRequestHeaders fallback and with
                // LayersSDK.SdkVersionHeader on /users/properties.
                // Via SdkVersionHeader() rather than re-composing the short
                // shape: that accessor asks the core first and falls back to a
                // string that still names the engine. A hand-built
                // "unity/{SdkVersion}" drops the engine token, so /config would
                // be the one endpoint where Unity traffic could not be told
                // apart from any other engine's.
                if (!sdkVersionApplied)
                    request.SetRequestHeader("X-SDK-Version", LayersSDK.SdkVersionHeader());

                // This poller owns its own ETag: it is the one the LAST 200 on
                // this request path returned, so it wins over whatever the core
                // may have cached.
                if (!string.IsNullOrEmpty(_etag))
                    request.SetRequestHeader("If-None-Match", _etag);

                request.timeout = RequestTimeoutSec;

                yield return request.SendWebRequest();

                // StopPolling claims _inFlightRequest before it disposes. If it ran
                // while this coroutine was suspended, `request` is a freed handle —
                // read nothing off it.
                if (!ReferenceEquals(_inFlightRequest, request)) yield break;

                if (request.responseCode == 200)
                {
                    string body = request.downloadHandler.text;
                    string newEtag = request.GetResponseHeader("ETag") ?? "";
                    _etag = newEtag;

                    if (!string.IsNullOrEmpty(body))
                    {
                        string error = NativeStringHelper.ProcessResult(
                            NativeBindings.layers_update_remote_config(body, newEtag));

                        if (error != null)
                        {
                            LayersLogger.Warn($"Remote config update failed: {error}");
                        }
                        else
                        {
                            LayersLogger.Log("Remote config updated");

                            // Notify subscribers (e.g., SKAN auto-config)
                            try
                            {
                                OnConfigUpdated?.Invoke(body);
                            }
                            catch (Exception e)
                            {
                                LayersLogger.Warn($"OnConfigUpdated handler threw: {e.Message}");
                            }
                        }
                    }
                }
                else if (request.responseCode == 304)
                {
                    LayersLogger.Log("Remote config not modified");
                }
                else
                {
                    LayersLogger.Warn(
                        $"Remote config fetch failed (HTTP {request.responseCode}): {request.error}");
                }
            }
            finally
            {
                // Release only if StopPolling has not already taken ownership —
                // double-disposing a native handle is the other half of this bug.
                if (request != null && ReferenceEquals(_inFlightRequest, request))
                {
                    _inFlightRequest = null;
                    ReleaseRequest(request);
                }
                _isFetching = false;
            }
        }
    }
}

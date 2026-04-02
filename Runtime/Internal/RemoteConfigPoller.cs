using System;
using System.Collections;
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
    /// </summary>
    internal class RemoteConfigPoller
    {
        private readonly LayersRunner _runner;
        private readonly string _baseUrl;
        private readonly string _appId;
        private string _etag;
        private Coroutine _pollingCoroutine;

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

        internal RemoteConfigPoller(LayersRunner runner, string baseUrl, string appId)
        {
            _runner = runner;
            // Ensure no trailing slash on the base URL
            _baseUrl = baseUrl != null ? baseUrl.TrimEnd('/') : "https://in.layers.com";
            _appId = appId;
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
        /// Stop the polling coroutine.
        /// </summary>
        internal void StopPolling()
        {
            if (_pollingCoroutine != null)
            {
                _runner.StopCoroutine(_pollingCoroutine);
                _pollingCoroutine = null;
            }
        }

        /// <summary>
        /// Trigger a one-off config fetch outside the periodic schedule.
        /// </summary>
        internal void FetchNow()
        {
            _runner.StartCoroutine(FetchConfig());
        }

        private IEnumerator PollLoop(float intervalSec)
        {
            // Initial fetch immediately
            yield return _runner.StartCoroutine(FetchConfig());

            while (true)
            {
                yield return new WaitForSecondsRealtime(intervalSec);
                yield return _runner.StartCoroutine(FetchConfig());
            }
        }

        private IEnumerator FetchConfig()
        {
            // Build URL with query parameters matching the Flutter pattern
            string url = $"{_baseUrl}/config?app_id={UnityWebRequest.EscapeURL(_appId)}&platform={DeviceInfoCollector.RuntimePlatform}";

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("X-App-Id", _appId);
                request.SetRequestHeader("Accept", "application/json");

                if (!string.IsNullOrEmpty(_etag))
                    request.SetRequestHeader("If-None-Match", _etag);

                request.timeout = RequestTimeoutSec;

                yield return request.SendWebRequest();

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
        }
    }
}

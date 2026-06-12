using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Layers.Unity.Internal
{
    /// <summary>
    /// Coroutine-based drain loop that pulls serialized event batches from the Rust
    /// core via <see cref="NativeBindings.layers_drain_batch"/> and POSTs them to the
    /// ingest endpoint using <see cref="UnityWebRequest"/>.
    ///
    /// This is the Unity equivalent of Flutter's <c>_flushViaHttp()</c> and the WASM
    /// SDKs' drain-then-fetch pattern. The Rust core owns the queue; this class only
    /// handles HTTP transport.
    ///
    /// On HTTP failure the batch is returned to the Rust queue via
    /// <see cref="NativeBindings.layers_requeue_events"/> and the drain loop stops.
    /// The periodic timer will retry on the next tick.
    /// </summary>
    internal class FlushManager
    {
        private readonly LayersRunner _runner;
        private readonly uint _batchSize;
        private bool _isFlushing;
        private Coroutine _periodicCoroutine;

        // The batch currently handed to an in-flight UnityWebRequest. It has
        // already been drained out of the Rust queue, so until the response
        // arrives this string is the ONLY copy — PersistPendingForSuspend
        // requeues it before the app suspends.
        private string _inFlightBatch;

        // Number of events PersistPendingForSuspend put back into the Rust
        // queue from the in-flight batch. Used by the Drop verdict to pull
        // exactly those events back out — a non-retryable rejection must
        // discard the payload even when the suspend path requeued it first.
        private int _suspendRequeuedCount;

        internal FlushManager(LayersRunner runner, uint batchSize = 20)
        {
            _runner = runner;
            _batchSize = batchSize;
        }

        /// <summary>
        /// Start the periodic flush coroutine. Flushes at the given interval in seconds.
        /// No-op if already started.
        /// </summary>
        internal void StartPeriodicFlush(float intervalSec)
        {
            if (_periodicCoroutine != null) return;
            _periodicCoroutine = _runner.StartCoroutine(PeriodicFlushLoop(intervalSec));
        }

        /// <summary>
        /// Stop the periodic flush coroutine.
        /// </summary>
        internal void StopPeriodicFlush()
        {
            if (_periodicCoroutine != null)
            {
                _runner.StopCoroutine(_periodicCoroutine);
                _periodicCoroutine = null;
            }
        }

        /// <summary>
        /// Trigger an immediate flush. No-op if a flush is already in progress.
        /// </summary>
        internal void FlushNow()
        {
            if (!_isFlushing)
                _runner.StartCoroutine(DrainAndSend());
        }

        /// <summary>
        /// Trigger an immediate flush with a completion callback.
        /// The callback is invoked after the flush finishes (success or failure).
        /// No-op (callback invoked immediately) if a flush is already in progress.
        /// </summary>
        internal void FlushWithCallback(System.Action onComplete)
        {
            if (_isFlushing)
            {
                onComplete?.Invoke();
                return;
            }
            _runner.StartCoroutine(DrainAndSendWithCallback(onComplete));
        }

        private IEnumerator DrainAndSendWithCallback(System.Action onComplete)
        {
            yield return _runner.StartCoroutine(DrainAndSend());
            onComplete?.Invoke();
        }

        /// <summary>
        /// Synchronous flush for shutdown. Drains the queue and persists events to disk
        /// via the Rust core's <c>layers_flush()</c> (which writes to the persistence
        /// layer rather than sending HTTP). This is safe to call from
        /// <see cref="MonoBehaviour.OnApplicationQuit"/> where coroutines cannot run.
        /// </summary>
        internal void FlushBlocking()
        {
            string error = NativeStringHelper.ProcessResult(NativeBindings.layers_flush());
            if (error != null)
                LayersLogger.Warn($"Blocking flush failed: {error}");
        }

        /// <summary>
        /// Make queued events durable before the app is suspended.
        ///
        /// Unity freezes coroutines while suspended, so a batch that
        /// <see cref="DrainAndSend"/> pulled out of the Rust queue and handed
        /// to an in-flight <see cref="UnityWebRequest"/> exists only in C#
        /// memory — if the OS kills the app in the background (routine on
        /// mobile), that batch is lost. Requeue any in-flight batch back into
        /// the Rust queue, then run the blocking persist. Worst case the
        /// request also completed and the batch is delivered twice — the
        /// server's event_id dedup absorbs that; losing it is unrecoverable.
        /// </summary>
        internal void PersistPendingForSuspend()
        {
            if (_inFlightBatch != null)
            {
                // layers_requeue_events returns the number of re-enqueued
                // events on success — remember it so a later Drop verdict
                // can discard exactly those events (they sit at the FRONT
                // of the queue via requeue_front, and no other drain can
                // run while _isFlushing is held).
                string result = NativeStringHelper.ReadAndFree(
                    NativeBindings.layers_requeue_events(_inFlightBatch));
                int count;
                _suspendRequeuedCount =
                    result != null && int.TryParse(result, out count) ? count : 0;
                _inFlightBatch = null;
            }
            FlushBlocking();
        }

        private IEnumerator PeriodicFlushLoop(float intervalSec)
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(intervalSec);
                if (!_isFlushing)
                    yield return _runner.StartCoroutine(DrainAndSend());
            }
        }

        /// <summary>
        /// Core drain loop: pull batches from Rust, POST each via UnityWebRequest,
        /// and act on the core's verdict.
        ///
        /// Delivery policy lives in the Rust core
        /// (adr/0001-rust-owned-delivery-policy.md): one pre-flight gate
        /// (consent/DNT + Retry-After + circuit breaker via
        /// <see cref="NativeBindings.layers_should_attempt_flush"/>), one HTTP
        /// attempt per batch, one post-flight report
        /// (<see cref="NativeBindings.layers_record_flush_result"/>) whose
        /// verdict decides requeue vs drop. No wrapper-side retry loops or
        /// backoff — the periodic timer retries, gated by the core.
        /// </summary>
        private IEnumerator DrainAndSend()
        {
            if (_isFlushing) yield break;
            _isFlushing = true;

            try
            {
                // Gate only when there's something to send: the gate may claim
                // the circuit breaker's half-open probe slot, which an idle
                // tick must not consume.
                if (NativeBindings.layers_queue_depth() <= 0) yield break;
                if (NativeBindings.layers_should_attempt_flush() == 0)
                {
                    LayersLogger.Log("Flush skipped — core delivery gate closed");
                    yield break;
                }

                // After a passed gate, EVERY pre-wire abort path must call
                // layers_abort_flush_attempt() — it releases a claimed
                // half-open breaker probe WITHOUT counting a delivery
                // failure (no request was made).
                string url = NativeStringHelper.ReadAndFree(NativeBindings.layers_events_url());
                if (string.IsNullOrEmpty(url))
                {
                    NativeBindings.layers_abort_flush_attempt();
                    LayersLogger.Warn("Flush skipped: no events URL available");
                    yield break;
                }

                string headersJson = NativeStringHelper.ReadAndFree(
                    NativeBindings.layers_flush_headers_json());
                var headers = ParseHeaders(headersJson);

                // Tracks whether any outcome was reported this flush — if
                // the queue raced empty after the gate passed, the claimed
                // probe must still be released.
                bool reportedOutcome = false;

                while (true)
                {
                    string batch = NativeStringHelper.ReadAndFree(
                        NativeBindings.layers_drain_batch(_batchSize));
                    if (string.IsNullOrEmpty(batch))
                    {
                        if (!reportedOutcome)
                            NativeBindings.layers_abort_flush_attempt();
                        break;
                    }

                    byte[] bodyRaw = Encoding.UTF8.GetBytes(batch);

                    using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
                    {
                        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                        request.downloadHandler = new DownloadHandlerBuffer();
                        request.SetRequestHeader("Content-Type", "application/json");

                        foreach (var kv in headers)
                            request.SetRequestHeader(kv.Key, kv.Value);

                        // Track the drained batch while the request is in
                        // flight so PersistPendingForSuspend can requeue it
                        // if the app is suspended mid-request.
                        _inFlightBatch = batch;
                        yield return request.SendWebRequest();
                        // If PersistPendingForSuspend ran while we were
                        // yielded (app suspended mid-request), it already
                        // requeued this batch and cleared the field — the
                        // requeue branch below must not requeue it a second
                        // time.
                        bool requeuedBySuspend = _inFlightBatch == null;
                        int suspendRequeuedCount = _suspendRequeuedCount;
                        _suspendRequeuedCount = 0;
                        _inFlightBatch = null;

                        // Report the outcome to the core; status 0 = no
                        // response (network error / timeout).
                        ushort status =
                            request.responseCode >= 100 && request.responseCode <= ushort.MaxValue
                                ? (ushort)request.responseCode
                                : (ushort)0;
                        string retryAfterHeader = request.GetResponseHeader("Retry-After");
                        byte verdict = NativeBindings.layers_record_flush_result(
                            status, retryAfterHeader);
                        reportedOutcome = true;

                        if (verdict == 1) // Delivered
                        {
                            LayersLogger.Log($"Flushed batch ({bodyRaw.Length} bytes)");
                            continue; // keep draining the backlog
                        }

                        if (verdict == 3) // Drop — non-retryable rejection
                        {
                            // Identical bytes can't succeed later; retrying
                            // would wedge the queue behind a poison batch.
                            if (requeuedBySuspend && suspendRequeuedCount > 0)
                            {
                                // The suspend path put this exact batch back
                                // at the FRONT of the Rust queue before the
                                // verdict arrived — pull those events out and
                                // discard them, or the poison batch would be
                                // drained and POSTed again.
                                string discarded = NativeStringHelper.ReadAndFree(
                                    NativeBindings.layers_drain_batch(
                                        (uint)suspendRequeuedCount));
                                LayersLogger.Warn(
                                    $"Discarded suspend-requeued poison batch ({(discarded == null ? 0 : suspendRequeuedCount)} events)");
                            }
                            LayersLogger.Warn(
                                $"Batch dropped: HTTP {status} (non-retryable)");
                            continue; // keep draining
                        }

                        // RetryLater — requeue; the periodic timer retries,
                        // gated by the core's Retry-After guard and breaker.
                        if (!requeuedBySuspend)
                        {
                            NativeStringHelper.ProcessResult(
                                NativeBindings.layers_requeue_events(batch));
                        }
                        LayersLogger.Warn(
                            $"Flush deferred (HTTP {status}): {request.error}");
                        break;
                    }
                }
            }
            finally
            {
                _isFlushing = false;
            }
        }

        /// <summary>
        /// Parse the headers JSON returned by <c>layers_flush_headers_json()</c>.
        ///
        /// The Rust core returns headers in one of two formats:
        ///   - Array of pairs: <c>[["X-Api-Key","..."],["X-App-Id","..."]]</c>
        ///   - Object:         <c>{"X-Api-Key":"...","X-App-Id":"..."}</c>
        ///
        /// This method handles both without pulling in a full JSON parser.
        /// </summary>
        internal static Dictionary<string, string> ParseHeaders(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json)) return result;

            string trimmed = json.Trim();

            if (trimmed.StartsWith("["))
            {
                // Array-of-pairs format: [["key","val"],["key","val"]]
                ParseArrayOfPairs(trimmed, result);
            }
            else if (trimmed.StartsWith("{"))
            {
                // Object format: {"key":"val","key":"val"}
                ParseObjectHeaders(trimmed, result);
            }

            return result;
        }

        /// <summary>
        /// Parse <c>[["key","val"],["key","val"],...]</c> format.
        /// Minimal state-machine parser that handles JSON string escapes.
        /// </summary>
        private static void ParseArrayOfPairs(string json, Dictionary<string, string> result)
        {
            // Strategy: extract all JSON strings in order, then pair them up.
            // Each pair is [key, value], so strings at index 0,1 are pair 1, 2,3 are pair 2, etc.
            var strings = ExtractJsonStrings(json);

            for (int i = 0; i + 1 < strings.Count; i += 2)
            {
                result[strings[i]] = strings[i + 1];
            }
        }

        /// <summary>
        /// Parse <c>{"key":"val","key":"val"}</c> format.
        /// Extracts strings in order: key, value, key, value, ...
        /// </summary>
        private static void ParseObjectHeaders(string json, Dictionary<string, string> result)
        {
            var strings = ExtractJsonStrings(json);

            for (int i = 0; i + 1 < strings.Count; i += 2)
            {
                result[strings[i]] = strings[i + 1];
            }
        }

        /// <summary>
        /// Extract all JSON string literals from a JSON string, in order.
        /// Handles standard JSON escape sequences: \\, \", \/, \b, \f, \n, \r, \t, \uXXXX.
        /// </summary>
        private static List<string> ExtractJsonStrings(string json)
        {
            var strings = new List<string>();
            int i = 0;
            int len = json.Length;

            while (i < len)
            {
                // Find the next unescaped double quote
                if (json[i] == '"')
                {
                    i++; // skip opening quote
                    var sb = new StringBuilder();

                    while (i < len && json[i] != '"')
                    {
                        if (json[i] == '\\' && i + 1 < len)
                        {
                            char next = json[i + 1];
                            switch (next)
                            {
                                case '"':  sb.Append('"');  i += 2; break;
                                case '\\': sb.Append('\\'); i += 2; break;
                                case '/':  sb.Append('/');  i += 2; break;
                                case 'b':  sb.Append('\b'); i += 2; break;
                                case 'f':  sb.Append('\f'); i += 2; break;
                                case 'n':  sb.Append('\n'); i += 2; break;
                                case 'r':  sb.Append('\r'); i += 2; break;
                                case 't':  sb.Append('\t'); i += 2; break;
                                case 'u':
                                    // \uXXXX — 4 hex digits
                                    if (i + 5 < len)
                                    {
                                        string hex = json.Substring(i + 2, 4);
                                        if (int.TryParse(hex,
                                            System.Globalization.NumberStyles.HexNumber,
                                            System.Globalization.CultureInfo.InvariantCulture,
                                            out int codePoint))
                                        {
                                            sb.Append((char)codePoint);
                                        }
                                        i += 6;
                                    }
                                    else
                                    {
                                        sb.Append(json[i]);
                                        i++;
                                    }
                                    break;
                                default:
                                    sb.Append(json[i]);
                                    i++;
                                    break;
                            }
                        }
                        else
                        {
                            sb.Append(json[i]);
                            i++;
                        }
                    }

                    if (i < len) i++; // skip closing quote
                    strings.Add(sb.ToString());
                }
                else
                {
                    i++;
                }
            }

            return strings;
        }
    }
}

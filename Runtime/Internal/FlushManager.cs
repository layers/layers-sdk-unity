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
        /// requeue on failure and break.
        /// </summary>
        private IEnumerator DrainAndSend()
        {
            if (_isFlushing) yield break;
            _isFlushing = true;

            try
            {
                string url = NativeStringHelper.ReadAndFree(NativeBindings.layers_events_url());
                if (string.IsNullOrEmpty(url))
                {
                    LayersLogger.Warn("Flush skipped: no events URL available");
                    yield break;
                }

                string headersJson = NativeStringHelper.ReadAndFree(
                    NativeBindings.layers_flush_headers_json());
                var headers = ParseHeaders(headersJson);

                while (true)
                {
                    string batch = NativeStringHelper.ReadAndFree(
                        NativeBindings.layers_drain_batch(_batchSize));
                    if (string.IsNullOrEmpty(batch)) break;

                    byte[] bodyRaw = Encoding.UTF8.GetBytes(batch);

                    using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
                    {
                        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                        request.downloadHandler = new DownloadHandlerBuffer();
                        request.SetRequestHeader("Content-Type", "application/json");

                        foreach (var kv in headers)
                            request.SetRequestHeader(kv.Key, kv.Value);

                        yield return request.SendWebRequest();

                        bool success =
                            request.result == UnityWebRequest.Result.Success &&
                            request.responseCode >= 200 &&
                            request.responseCode < 300;

                        if (!success)
                        {
                            // Requeue failed batch and stop draining — timer will retry
                            NativeStringHelper.ProcessResult(
                                NativeBindings.layers_requeue_events(batch));
                            LayersLogger.Warn(
                                $"Flush failed (HTTP {request.responseCode}): {request.error}");
                            break;
                        }

                        LayersLogger.Log($"Flushed batch ({bodyRaw.Length} bytes)");
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

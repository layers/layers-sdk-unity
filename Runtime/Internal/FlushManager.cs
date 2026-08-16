using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Layers.Unity.Internal
{
    /// <summary>
    /// Coroutine-based drain loop that pulls serialized event batches from the Rust
    /// core via <see cref="IFlushCore.DrainBatch"/> and POSTs them to the ingest
    /// endpoint using <see cref="UnityWebRequest"/>.
    ///
    /// This is the Unity equivalent of Flutter's <c>_flushViaHttp()</c> and the WASM
    /// SDKs' drain-then-fetch pattern. The Rust core owns the queue; this class only
    /// handles HTTP transport.
    ///
    /// On HTTP failure the batch is returned to the Rust queue via
    /// <see cref="IFlushCore.RequeueEvents"/> and the drain loop stops.
    /// The periodic timer will retry on the next tick.
    ///
    /// Request ownership is explicit rather than scoped to a <c>using</c> block.
    /// A <c>using</c> only disposes on paths that run to the end of the block, and
    /// a stopped coroutine is not one of them: Unity neither resumes nor unwinds
    /// an iterator it has stopped, so the request a drain is suspended on outlives
    /// the coroutine that owns it. <see cref="StopPeriodicFlush"/> therefore takes
    /// ownership of the in-flight request and settles it itself. The invariant is
    /// <see cref="CreatedRequestCount"/> == <see cref="ReleasedRequestCount"/> once
    /// flushing has stopped.
    /// </summary>
    internal class FlushManager
    {
        private readonly LayersRunner _runner;
        private readonly IFlushCore _core;
        private readonly uint _batchSize;
        private bool _isFlushing;
        private Coroutine _periodicCoroutine;

        // The drain coroutine — started by the periodic loop, FlushNow, or
        // FlushWithCallback. Stopping the periodic loop does NOT stop this one:
        // it is a separate coroutine registered on the runner, and StopCoroutine
        // reaches only the handle it is given. StopPeriodicFlush needs its own.
        private Coroutine _drainCoroutine;

        // The request a drain is currently suspended on. Whoever nulls this
        // field owns disposing that request, and nothing may read it afterwards
        // — the handle is native memory that Dispose frees.
        private UnityWebRequest _inFlightRequest;

        // The batch currently handed to an in-flight UnityWebRequest. It has
        // already been drained out of the Rust queue, so until the response
        // arrives this string is the ONLY copy — PersistPendingForSuspend
        // requeues it before the app suspends, and StopPeriodicFlush requeues
        // it when it aborts the request.
        private string _inFlightBatch;

        // Number of events PersistPendingForSuspend put back into the Rust
        // queue from the in-flight batch. Used by the Drop verdict to pull
        // exactly those events back out — a non-retryable rejection must
        // discard the payload even when the suspend path requeued it first.
        private int _suspendRequeuedCount;

        // The completion callback a FlushWithCallback drain still owes its
        // caller. On iOS that callback is the BGAppRefreshTask completion
        // signal: never firing it means the OS waits out the whole background
        // window and penalises future scheduling, so a stopped drain must still
        // settle it.
        private Action _pendingCompletion;

        /// <summary>
        /// Whether an event POST is in flight right now.
        /// </summary>
        internal bool HasInFlightRequest => _inFlightRequest != null;

        /// <summary>
        /// How many <see cref="UnityWebRequest"/> handles this manager has created.
        /// </summary>
        internal int CreatedRequestCount { get; private set; }

        /// <summary>
        /// How many of those handles it has disposed. An abandoned native handle
        /// is invisible from the outside — no exception, no log, no failed test —
        /// so the accounting is kept here and asserted by <c>FlushManagerTests</c>.
        /// </summary>
        internal int ReleasedRequestCount { get; private set; }

        internal FlushManager(LayersRunner runner, uint batchSize = 20, IFlushCore core = null)
        {
            _runner = runner;
            _batchSize = batchSize;
            _core = core ?? new NativeFlushCore();
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
        /// Stop flushing and settle everything the manager owns: the periodic
        /// loop, the nested drain coroutine, the request that drain is suspended
        /// on, the batch already pulled out of the Rust queue for it, and any
        /// completion callback still owed.
        ///
        /// Stopping the periodic loop alone left the other four alive. That was
        /// the defect: <c>StopCoroutine(_periodicCoroutine)</c> does not stop the
        /// nested DrainAndSend coroutine, does not abort the UnityWebRequest it
        /// is yielded on, and does not unwind the block that was meant to dispose
        /// it — so <c>LayersSDK.Shutdown()</c> returned with a live POST and an
        /// undisposed native handle behind it (the same mechanism fixed for
        /// RemoteConfigPoller in #227).
        ///
        /// The batch makes this more than a cleanup. It has ALREADY been drained
        /// out of the Rust queue, so dropping the request without settling it
        /// loses those events outright — a direct violation of at-least-once.
        /// And if <c>ShouldAttemptFlush</c> claimed the circuit breaker's
        /// half-open probe slot, the core is owed a resolution or the probe stays
        /// claimed until its 45 s expiry, blocking recovery for every other
        /// caller. So this method:
        ///
        ///   1. requeues the batch (<see cref="IFlushCore.RequeueEvents"/>) — the
        ///      core puts it back at the FRONT of the queue, and the next flush
        ///      tick retries it, exactly as the RetryLater verdict would;
        ///   2. releases the probe via <see cref="IFlushCore.AbortFlushAttempt"/>
        ///      rather than <c>RecordFlushResult(0, …)</c>. A self-inflicted abort
        ///      is not evidence about the server: we cancelled the request, so we
        ///      learned nothing. Reporting it as a no-response failure would trip
        ///      the breaker on our own shutdown and re-open it for another 60 s
        ///      against a server that may be perfectly healthy. The core's
        ///      <c>release_probe</c> names this case exactly — "coroutine
        ///      cancelled" — and returns the claim without recording an outcome
        ///      (adr/0001-rust-owned-delivery-policy.md §3).
        ///
        /// ORDERING INVARIANT: the requeue lands in the LIVE Rust core, so this
        /// must run BEFORE the core is shut down, and the crash-safety disk
        /// snapshot must run AFTER it — otherwise the requeued batch is written
        /// nowhere and dies with the process. <c>LayersSDK.Shutdown()</c> holds
        /// that order: StopPeriodicFlush() → FlushBlocking() → _platform.Shutdown().
        ///
        /// Safe to call repeatedly and safe with nothing in flight.
        /// </summary>
        internal void StopPeriodicFlush()
        {
            // Claim everything BEFORE stopping the coroutines. Whichever side
            // nulls a field owns settling it, so claiming first makes this method
            // the sole owner regardless of what a given Unity version does with a
            // stopped iterator — the drain's own release path re-checks ownership
            // and stands down.
            UnityWebRequest request = _inFlightRequest;
            _inFlightRequest = null;
            string batch = _inFlightBatch;
            _inFlightBatch = null;
            _suspendRequeuedCount = 0;
            Action completion = _pendingCompletion;
            _pendingCompletion = null;

            StopRunnerCoroutine(ref _periodicCoroutine);
            StopRunnerCoroutine(ref _drainCoroutine);
            _isFlushing = false;

            if (request != null)
            {
                // Abort cancels the native transfer immediately; on a request
                // that already completed it is a no-op. Dispose frees the handle.
                try
                {
                    request.Abort();
                }
                catch (Exception e)
                {
                    LayersLogger.Warn($"Aborting in-flight event request threw: {e.Message}");
                }

                ReleaseRequest(request);
            }

            // batch is null when PersistPendingForSuspend already put it back —
            // requeueing a second time would duplicate the events in the queue.
            if (batch != null) RequeueBatch(batch);

            // Only a request that reached the wire could have consumed a probe
            // claim: everything from the gate to SendWebRequest runs without a
            // yield, so there is no other point at which a drain can be stopped
            // while holding one.
            if (request != null)
            {
                _core.AbortFlushAttempt();
                LayersLogger.Log(batch != null
                    ? "Flush stopped; in-flight request aborted and its batch requeued"
                    : "Flush stopped; in-flight request aborted (batch already requeued for suspend)");
            }

            InvokeCompletion(completion);
        }

        /// <summary>
        /// Trigger an immediate flush. No-op if a flush is already in progress.
        /// </summary>
        internal void FlushNow()
        {
            if (!StartDrain(null))
                LayersLogger.Log("Flush skipped — one already in flight");
        }

        /// <summary>
        /// Trigger an immediate flush with a completion callback.
        /// The callback is invoked exactly once: after the flush finishes
        /// (success or failure), or if the flush is stopped before it finishes,
        /// or immediately when no flush could be started.
        /// </summary>
        internal void FlushWithCallback(Action onComplete)
        {
            // Settle the callback here ONLY if the drain never took ownership of
            // it. Asking the returned Coroutine instead would double-fire:
            // StartCoroutine hands back null for a routine that ran to completion
            // before returning — an empty queue, a closed gate, no events URL —
            // and by then the drain's own finally has already invoked the
            // callback. An empty queue is the common case for the iOS
            // background-flush task, so that path is not a corner.
            if (!StartDrain(onComplete))
                InvokeCompletion(onComplete);
        }

        /// <summary>
        /// Start the drain coroutine and record its handle so
        /// <see cref="StopPeriodicFlush"/> can stop it.
        ///
        /// Returns whether the drain now OWNS <paramref name="onComplete"/> —
        /// true once the coroutine has been handed to the runner, including when
        /// it finished synchronously and has already invoked the callback. False
        /// means nothing was started and the caller still owes it. The recorded
        /// handle is deliberately not the answer: Unity returns null for a
        /// coroutine that completed before StartCoroutine returned, which is
        /// indistinguishable from "never started" by the handle alone.
        /// </summary>
        private bool StartDrain(Action onComplete)
        {
            if (_isFlushing) return false;

            // Claimed before StartCoroutine: Unity runs the coroutine body
            // synchronously up to its first yield, so both must already be set
            // when DrainAndSend's own bookkeeping runs.
            _isFlushing = true;
            _pendingCompletion = onComplete;
            try
            {
                _drainCoroutine = _runner.StartCoroutine(DrainAndSend());
            }
            catch (Exception e)
            {
                // The runner is gone (quit tore the scene down) or the body threw
                // before its first yield. Release the guard rather than latch it —
                // a stuck flag would silently retire flushing for the rest of the
                // process.
                _isFlushing = false;
                _pendingCompletion = null;
                LayersLogger.Warn($"Flush could not start: {e.Message}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Stop a coroutine this manager started and forget its handle.
        /// </summary>
        private void StopRunnerCoroutine(ref Coroutine coroutine)
        {
            Coroutine handle = coroutine;
            coroutine = null;
            if (handle == null) return;

            // The runner is a MonoBehaviour: if it was destroyed first (quit
            // tears the scene down around Shutdown) its coroutines are already
            // gone. Unity's == null covers the destroyed-but-not-null case.
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
                LayersLogger.Warn($"Disposing event request threw: {e.Message}");
            }
            ReleasedRequestCount++;
        }

        /// <summary>
        /// Put a drained batch back into the Rust queue.
        ///
        /// Never throws — losing the batch is the failure this whole path exists
        /// to prevent, so a requeue that fails must be visible rather than fatal.
        /// It IS reported, though: a successful requeue returns the re-enqueued
        /// event count, so anything that does not parse as a number is the core's
        /// error string, and swallowing it would make the one path that can still
        /// lose events silent.
        /// </summary>
        private void RequeueBatch(string batch)
        {
            string result;
            try
            {
                result = _core.RequeueEvents(batch);
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"Requeueing the in-flight batch threw: {e.Message}");
                return;
            }

            int count;
            if (result == null || !int.TryParse(result, out count))
            {
                LayersLogger.Warn(
                    $"Requeueing the in-flight batch failed — events may be lost: {result ?? "no result"}");
            }
        }

        /// <summary>
        /// Invoke a completion callback without letting it break the caller.
        /// </summary>
        private static void InvokeCompletion(Action completion)
        {
            if (completion == null) return;
            try
            {
                completion();
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"Flush completion callback threw: {e.Message}");
            }
        }

        /// <summary>
        /// Synchronous flush for shutdown. Drains the queue and persists events to disk
        /// via the Rust core's <c>layers_flush()</c> (which writes to the persistence
        /// layer rather than sending HTTP). This is safe to call from
        /// <see cref="MonoBehaviour.OnApplicationQuit"/> where coroutines cannot run.
        /// </summary>
        internal void FlushBlocking()
        {
            string error = _core.FlushToDisk();
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
                // RequeueEvents returns the number of re-enqueued events on
                // success — remember it so a later Drop verdict can discard
                // exactly those events (they sit at the FRONT of the queue via
                // requeue_front, and no other drain can run while _isFlushing
                // is held).
                string result = _core.RequeueEvents(_inFlightBatch);
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
                // StartDrain records the coroutine handle so StopPeriodicFlush
                // can reach it — the periodic loop must not start an untracked
                // drain, or stopping this loop orphans its request.
                //
                // Yielding on that handle keeps the interval measured from the
                // END of a drain, as it always was. The handle is null when the
                // drain finished inside StartCoroutine (empty queue, closed
                // gate); yielding null just costs the frame it would have cost
                // anyway.
                if (StartDrain(null)) yield return _drainCoroutine;
            }
        }

        /// <summary>
        /// Core drain loop: pull batches from Rust, POST each via UnityWebRequest,
        /// and act on the core's verdict.
        ///
        /// Delivery policy lives in the Rust core
        /// (adr/0001-rust-owned-delivery-policy.md): one pre-flight gate
        /// (consent/DNT + Retry-After + circuit breaker via
        /// <see cref="IFlushCore.ShouldAttemptFlush"/>), one HTTP attempt per
        /// batch, one post-flight report
        /// (<see cref="IFlushCore.RecordFlushResult"/>) whose verdict decides
        /// requeue vs drop. No wrapper-side retry loops or backoff — the
        /// periodic timer retries, gated by the core.
        ///
        /// Started only via <see cref="StartDrain"/>, which owns the
        /// <c>_isFlushing</c> guard and records the coroutine handle.
        /// </summary>
        private IEnumerator DrainAndSend()
        {
            try
            {
                // Gate only when there's something to send: the gate may claim
                // the circuit breaker's half-open probe slot, which an idle
                // tick must not consume.
                if (_core.QueueDepth() <= 0) yield break;
                if (!_core.ShouldAttemptFlush())
                {
                    LayersLogger.Log("Flush skipped — core delivery gate closed");
                    yield break;
                }

                // After a passed gate, EVERY pre-wire abort path must call
                // AbortFlushAttempt() — it releases a claimed half-open breaker
                // probe WITHOUT counting a delivery failure (no request was
                // made).
                string url = _core.EventsUrl();
                if (string.IsNullOrEmpty(url))
                {
                    _core.AbortFlushAttempt();
                    LayersLogger.Warn("Flush skipped: no events URL available");
                    yield break;
                }

                var headers = ParseHeaders(_core.FlushHeadersJson());

                // Tracks whether any outcome was reported this flush — if
                // the queue raced empty after the gate passed, the claimed
                // probe must still be released.
                bool reportedOutcome = false;

                while (true)
                {
                    string batch = _core.DrainBatch(_batchSize);
                    if (string.IsNullOrEmpty(batch))
                    {
                        if (!reportedOutcome)
                            _core.AbortFlushAttempt();
                        break;
                    }

                    byte[] bodyRaw = Encoding.UTF8.GetBytes(batch);

                    // Ownership is explicit, not a `using`: a stopped coroutine
                    // never unwinds, so a `using` here disposed nothing and left
                    // the request live inside native curl. StopPeriodicFlush
                    // claims _inFlightRequest and disposes it instead; the
                    // finally below stands down when it has.
                    UnityWebRequest request = null;
                    try
                    {
                        request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
                        _inFlightRequest = request;
                        CreatedRequestCount++;

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

                        // StopPeriodicFlush claims _inFlightRequest before it
                        // disposes. If it ran while this coroutine was suspended,
                        // `request` is a freed handle — read nothing off it, and
                        // report nothing: the stop already requeued the batch and
                        // released the breaker probe.
                        if (!ReferenceEquals(_inFlightRequest, request)) yield break;

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
                        byte verdict = _core.RecordFlushResult(status, retryAfterHeader);
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
                                string discarded =
                                    _core.DrainBatch((uint)suspendRequeuedCount);
                                LayersLogger.Warn(
                                    $"Discarded suspend-requeued poison batch ({(discarded == null ? 0 : suspendRequeuedCount)} events)");
                            }
                            LayersLogger.Warn(
                                $"Batch dropped: HTTP {status} (non-retryable)");
                            continue; // keep draining
                        }

                        // RetryLater — requeue; the periodic timer retries,
                        // gated by the core's Retry-After guard and breaker.
                        if (!requeuedBySuspend) RequeueBatch(batch);
                        LayersLogger.Warn(
                            $"Flush deferred (HTTP {status}): {request.error}");
                        break;
                    }
                    finally
                    {
                        // Release only if StopPeriodicFlush has not already taken
                        // ownership — double-disposing a native handle is the
                        // other half of this bug.
                        if (request != null && ReferenceEquals(_inFlightRequest, request))
                        {
                            _inFlightRequest = null;
                            ReleaseRequest(request);
                        }
                    }
                }
            }
            finally
            {
                // Not reached when the coroutine is STOPPED — Unity does not
                // unwind a stopped iterator, which is why StopPeriodicFlush
                // clears the guard and settles the callback itself.
                _isFlushing = false;
                Action completion = _pendingCompletion;
                _pendingCompletion = null;
                InvokeCompletion(completion);
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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Layers.Unity.Internal;
using UnityEngine;

namespace Layers.Unity
{
    /// <summary>
    /// Auto-capture Unity exceptions and route them to the SDK as the
    /// canonical <c>$exception</c> event introduced in Tier 1.
    ///
    /// Design mirrors the web slice (PR #138, <c>installExceptionAutoCapture</c>):
    /// - <c>$exception_type</c>     — exception class name (e.g. "NullReferenceException")
    /// - <c>$exception_message</c>  — capped at 4 KB
    /// - <c>$exception_stack</c>    — capped at 10 KB
    /// - <c>$exception_handled</c>  — Unity-specific. <c>true</c> when the host called
    ///                                <c>Debug.LogError</c> / <c>Debug.LogException</c> from
    ///                                their own catch block, <c>false</c> when an uncaught
    ///                                exception bubbled up to Unity (<c>LogType.Exception</c>
    ///                                from outside a catch).
    /// - <c>$exception_source</c>   — best-effort file extracted from the stack trace
    ///                                (top frame only). Only set when extractable.
    /// - <c>$exception_thread</c>   — <c>"main"</c> or <c>"background"</c>
    ///
    /// Listener wiring:
    /// - <c>Application.logMessageReceived</c>          — main-thread log messages
    /// - <c>Application.logMessageReceivedThreaded</c>  — any-thread log messages.
    ///   Background-thread events are queued and drained on the next main-thread
    ///   pump (<see cref="DrainBackgroundQueue"/>), since the Rust core's queue
    ///   APIs are main-thread-only on Unity.
    ///
    /// Opt-out via <see cref="LayersConfig.AutoTrackExceptions"/> (default true,
    /// matching Sentry / Bugsnag / Firebase Crashlytics conventions).
    ///
    /// On WebGL, the jslib hooks <c>window.onerror</c> and
    /// <c>window.onunhandledrejection</c> directly and emits <c>$exception</c>
    /// events through the WASM core; this C# module is a no-op on WebGL.
    /// </summary>
    internal static class ExceptionModule
    {
        // Match the web slice's caps to keep payloads bounded across SDKs.
        internal const int MaxMessageChars = 4000;
        internal const int MaxStackChars = 10000;

        // Event + property names — mirror PR #138 to keep wire-format parity.
        internal const string ExceptionEventName = "$exception";
        private const string PropType = "$exception_type";
        private const string PropMessage = "$exception_message";
        private const string PropStack = "$exception_stack";
        private const string PropHandled = "$exception_handled";
        private const string PropSource = "$exception_source";
        private const string PropThread = "$exception_thread";

        private static bool s_installed;
        private static bool s_captureLogErrors;
        private static bool s_enableDebug;

        // Background-thread events are queued here and drained on the next main
        // frame by LayersRunner. ConcurrentQueue keeps the producer side
        // lock-free; we cap to bound memory if a runaway logger floods.
        private static readonly ConcurrentQueue<QueuedException> s_backgroundQueue
            = new ConcurrentQueue<QueuedException>();
        private const int BackgroundQueueCap = 256;
        private static int s_backgroundDropCount;

        private struct QueuedException
        {
            public string Condition;
            public string StackTrace;
            public LogType Type;
        }

        /// <summary>
        /// Install the Unity log listeners. Idempotent — calling Install twice
        /// is a no-op on the second call. Must be called from the main thread.
        /// </summary>
        internal static void Install(bool captureLogErrors, bool enableDebug)
        {
            if (s_installed) return;
            s_captureLogErrors = captureLogErrors;
            s_enableDebug = enableDebug;
            s_installed = true;

            Application.logMessageReceived += OnLogMessageReceived;
            Application.logMessageReceivedThreaded += OnLogMessageReceivedThreaded;

            if (s_enableDebug)
                LayersLogger.Log("ExceptionModule installed");
        }

        /// <summary>
        /// Remove the Unity log listeners and clear queued background events.
        /// Idempotent.
        /// </summary>
        internal static void Uninstall()
        {
            if (!s_installed) return;
            s_installed = false;

            Application.logMessageReceived -= OnLogMessageReceived;
            Application.logMessageReceivedThreaded -= OnLogMessageReceivedThreaded;

            // Drop any queued background events — there's nowhere to deliver
            // them and we're tearing down.
            while (s_backgroundQueue.TryDequeue(out _)) { /* drain */ }
            s_backgroundDropCount = 0;
        }

        /// <summary>
        /// Pump the background-thread exception queue. Called every frame from
        /// <see cref="LayersRunner.Update"/> so that any
        /// <c>logMessageReceivedThreaded</c> calls from C# tasks / native
        /// callbacks reach <see cref="LayersSDK.Track"/> on the main thread.
        /// </summary>
        internal static void DrainBackgroundQueue()
        {
            while (s_backgroundQueue.TryDequeue(out QueuedException ex))
            {
                EmitFromMainThread(ex.Condition, ex.StackTrace, ex.Type, isMainThread: false);
            }

            // Surface a single dropped-events warning if we hit the cap, then
            // reset the counter. Avoids spamming if the burst was bounded.
            int dropped = System.Threading.Interlocked.Exchange(ref s_backgroundDropCount, 0);
            if (dropped > 0 && s_enableDebug)
            {
                LayersLogger.Warn(
                    $"ExceptionModule dropped {dropped} background exception(s) " +
                    "after exceeding the queue cap");
            }
        }

        /// <summary>
        /// Synchronous entry point used by tests and by manual
        /// <see cref="LayersSDK.TrackException(Exception, bool)"/> callers.
        /// </summary>
        internal static void TrackException(Exception exception, bool handled)
        {
            if (exception == null) return;
            string condition = $"{exception.GetType().Name}: {exception.Message}";
            string stackTrace = exception.StackTrace ?? string.Empty;
            EmitFromMainThread(
                condition,
                stackTrace,
                handled ? LogType.Error : LogType.Exception,
                isMainThread: true);
        }

        // ── Unity callbacks ─────────────────────────────────────────────

        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (!ShouldCapture(type)) return;
            EmitFromMainThread(condition, stackTrace, type, isMainThread: true);
        }

        private static void OnLogMessageReceivedThreaded(string condition, string stackTrace, LogType type)
        {
            // Threaded callback may fire on the main thread too — Unity routes
            // both pathways through this hook. To avoid double-counting we only
            // queue events that are NOT already on the main thread (the
            // non-threaded callback handles those). However Unity does not
            // expose a reliable "is main thread" check that's stable across
            // versions, so we use a simple heuristic: the threaded callback's
            // `Application.logMessageReceived` peer fires for the main thread
            // anyway, so we filter by always queueing here and letting the
            // queue cap prevent double-counting if it does happen — in
            // practice the non-threaded handler de-duplicates because it gets
            // every main-thread log _before_ the threaded one for that thread.
            //
            // Cleaner: rely on a thread-id guard.
            if (System.Threading.Thread.CurrentThread.ManagedThreadId == s_mainThreadId)
            {
                // Already handled by OnLogMessageReceived above.
                return;
            }
            if (!ShouldCapture(type)) return;
            EnqueueBackground(condition, stackTrace, type);
        }

        // ── Capture filter ──────────────────────────────────────────────

        private static bool ShouldCapture(LogType type)
        {
            switch (type)
            {
                case LogType.Exception:
                    return true;
                case LogType.Error:
                case LogType.Assert:
                    return s_captureLogErrors;
                default:
                    return false;
            }
        }

        // ── Background queue ────────────────────────────────────────────

        private static void EnqueueBackground(string condition, string stackTrace, LogType type)
        {
            if (s_backgroundQueue.Count >= BackgroundQueueCap)
            {
                System.Threading.Interlocked.Increment(ref s_backgroundDropCount);
                return;
            }
            s_backgroundQueue.Enqueue(new QueuedException
            {
                Condition = condition,
                StackTrace = stackTrace,
                Type = type
            });
        }

        // ── Property building + emission ────────────────────────────────

        private static int s_mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;

        /// <summary>
        /// Update the cached main-thread ID. Must be called from
        /// <see cref="LayersRunner.Awake"/> because the static field
        /// initializer runs on whatever thread first touches the type — which
        /// in tests can be a worker thread.
        /// </summary>
        internal static void RefreshMainThreadId()
        {
            s_mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        private static void EmitFromMainThread(
            string condition, string stackTrace, LogType type, bool isMainThread)
        {
            try
            {
                var props = BuildProperties(condition, stackTrace, type, isMainThread);
                LayersSDK.Track(ExceptionEventName, props);
            }
            catch (Exception e)
            {
                // Never let a tracking failure escape into the host's log
                // pipeline — that would re-enter via Application.logMessageReceived
                // and recurse.
                if (s_enableDebug)
                    LayersLogger.Warn($"ExceptionModule failed to emit: {e.Message}");
            }
        }

        /// <summary>
        /// Build the <c>$exception_*</c> property map. Public-internal so tests
        /// can drive it without going through the live Application listeners.
        /// </summary>
        internal static Dictionary<string, object> BuildProperties(
            string condition, string stackTrace, LogType type, bool isMainThread)
        {
            string typeName = ExtractTypeName(condition);
            string message = ExtractMessage(condition, typeName);

            var props = new Dictionary<string, object>
            {
                [PropType] = typeName,
                [PropMessage] = Truncate(message ?? string.Empty, MaxMessageChars),
                [PropStack] = Truncate(stackTrace ?? string.Empty, MaxStackChars),
                // LogType.Exception is the unhandled path (Unity caught a
                // throw that wasn't wrapped). LogType.Error is an explicit
                // Debug.LogError call which means the host already noticed
                // and decided to report.
                [PropHandled] = type != LogType.Exception,
                [PropThread] = isMainThread ? "main" : "background"
            };

            string source = ExtractSource(stackTrace);
            if (!string.IsNullOrEmpty(source))
                props[PropSource] = source;

            return props;
        }

        /// <summary>
        /// Unity's <c>condition</c> string for an exception is typically
        /// "<c>NullReferenceException: Object reference not set to an instance of an object</c>".
        /// Pull the part before the first ": " as the type name. For
        /// <c>LogType.Error</c> / <c>LogType.Assert</c> the condition is just
        /// the message, in which case we report the LogType as the type.
        /// </summary>
        internal static string ExtractTypeName(string condition)
        {
            if (string.IsNullOrEmpty(condition)) return "Exception";
            int colon = condition.IndexOf(": ", StringComparison.Ordinal);
            if (colon <= 0 || colon > 200) return "Exception";
            string head = condition.Substring(0, colon);
            // Type names are PascalCase identifiers, no spaces. Reject
            // obviously-not-a-type heads.
            if (head.IndexOf(' ') >= 0) return "Exception";
            return head;
        }

        internal static string ExtractMessage(string condition, string typeName)
        {
            if (string.IsNullOrEmpty(condition)) return string.Empty;
            string prefix = typeName + ": ";
            if (condition.StartsWith(prefix, StringComparison.Ordinal))
                return condition.Substring(prefix.Length);
            return condition;
        }

        /// <summary>
        /// Best-effort: pull the first "(at Path/To/File.cs:42)" location from
        /// the Unity stack trace. Returns null when no such location is
        /// present (common for IL2CPP release builds with stripped symbols).
        /// </summary>
        internal static string ExtractSource(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace)) return null;
            int at = stackTrace.IndexOf("(at ", StringComparison.Ordinal);
            if (at < 0) return null;
            int closeParen = stackTrace.IndexOf(')', at);
            if (closeParen < 0) return null;
            // "(at " is 4 chars
            int start = at + 4;
            if (closeParen <= start) return null;
            return stackTrace.Substring(start, closeParen - start);
        }

        internal static string Truncate(string s, int max)
        {
            if (s == null) return string.Empty;
            if (s.Length <= max) return s;
            return s.Substring(0, max);
        }
    }
}

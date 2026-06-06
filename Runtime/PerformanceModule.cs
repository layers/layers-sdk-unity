using System;
using System.Collections.Generic;
using System.Diagnostics;
using Layers.Unity.Internal;
using UnityEngine;

namespace Layers.Unity
{
    /// <summary>
    /// Disposable handle returned by <see cref="LayersSDK.StartTrace"/>.
    /// Disposing emits a <c>$performance_trace</c> event with the elapsed
    /// duration and any metadata attached via <see cref="SetMetadata"/>.
    ///
    /// Thread-safe: <see cref="Dispose"/> is idempotent and safe from any
    /// thread (the actual <see cref="LayersSDK.Track"/> call is
    /// dispatched to the main thread by the SDK facade).
    /// </summary>
    public sealed class LayersPerformanceTrace : IDisposable
    {
        private readonly string _name;
        private readonly long _startTicks;
        private Dictionary<string, object> _metadata;
        private int _disposed;

        internal LayersPerformanceTrace(string name)
        {
            _name = name;
            _startTicks = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Attach a metadata key/value to the trace. Subsequent calls with
        /// the same key overwrite. Common uses: route name, item count,
        /// cache hit/miss, error code on failed traces.
        /// </summary>
        public LayersPerformanceTrace SetMetadata(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) return this;
            if (_metadata == null) _metadata = new Dictionary<string, object>();
            _metadata[key] = value;
            return this;
        }

        /// <summary>
        /// Stop the trace and emit the <c>$performance_trace</c> event.
        /// Safe to call multiple times — only the first call emits.
        /// </summary>
        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0) return;

            long elapsedTicks = Stopwatch.GetTimestamp() - _startTicks;
            double elapsedMs = elapsedTicks * 1000.0 / Stopwatch.Frequency;

            PerformanceModule.EmitTrace(_name, elapsedMs, _metadata);
        }

        /// <summary>
        /// Elapsed milliseconds since the trace started. Useful for tests
        /// and for emitting intermediate measurements without disposing.
        /// </summary>
        public double ElapsedMs
        {
            get
            {
                long ticks = Stopwatch.GetTimestamp() - _startTicks;
                return ticks * 1000.0 / Stopwatch.Frequency;
            }
        }
    }

    /// <summary>
    /// Auto-capture performance signals and route them as <c>$performance</c>
    /// (sampled metrics) and <c>$performance_trace</c> (custom timed scopes)
    /// events.
    ///
    /// Surfaces:
    /// 1. <b>App start time</b> — elapsed milliseconds from process start to
    ///    the first <see cref="LayersRunner"/> Awake. Emitted exactly
    ///    once per launch as <c>$app_start_ms</c> on the <c>$performance</c>
    ///    event.
    /// 2. <b>Frame timing</b> — sampled CPU/GPU frame time via Unity's
    ///    <c>FrameTimingManager</c> (Unity 2018.1+). Emitted every
    ///    <see cref="LayersConfig.PerformanceFrameSamplingIntervalSec"/>
    ///    seconds while the app is foregrounded.
    /// 3. <b>Custom traces</b> — <see cref="LayersSDK.Trace(string, Action)"/>
    ///    and <see cref="LayersSDK.StartTrace"/> wrap a scope of work
    ///    and emit a <c>$performance_trace</c> event with the elapsed
    ///    duration when the scope exits.
    ///
    /// On WebGL the jslib bridges the Performance API
    /// (<c>performance.now()</c>, navigation timings) and emits matching
    /// <c>$performance</c> events; this C# module is a no-op on WebGL.
    /// </summary>
    internal static class PerformanceModule
    {
        // Wire-format constants. Mirror the Tier 1 schema's $-namespace.
        internal const string PerformanceEventName = "$performance";
        internal const string PerformanceTraceEventName = "$performance_trace";

        private const string PropAppStartMs = "$app_start_ms";
        private const string PropFrameCpuMs = "$frame_cpu_ms";
        private const string PropFrameGpuMs = "$frame_gpu_ms";
        private const string PropFrameCount = "$frame_count";
        private const string PropTraceName = "$trace_name";
        private const string PropDurationMs = "$duration_ms";

        private static bool s_installed;
        private static bool s_appStartEmitted;
        private static double s_appStartMsCache;
        private static float s_intervalSec = 60f;
        private static float s_intervalAccumulator;

        /// <summary>
        /// Wire the module up. Captures the app start timestamp on the first
        /// call and sets the periodic frame-timing sampling interval.
        /// </summary>
        internal static void Install(float intervalSec)
        {
            if (s_installed) return;
            s_installed = true;
            s_intervalSec = intervalSec > 0 ? intervalSec : 60f;
            s_intervalAccumulator = 0f;
        }

        internal static void Uninstall()
        {
            s_installed = false;
            s_appStartEmitted = false;
            s_appStartMsCache = 0;
            s_intervalAccumulator = 0f;
        }

        /// <summary>
        /// Compute the app-start duration. Uses
        /// <see cref="Time.realtimeSinceStartup"/> (seconds since the engine
        /// started) as the reference. Returns the cached value on subsequent
        /// calls. Test-only override of the input is supported via
        /// <see cref="SetAppStartMsForTesting"/>.
        /// </summary>
        internal static double GetAppStartMs()
        {
            if (s_appStartMsCache > 0) return s_appStartMsCache;
            try
            {
                s_appStartMsCache = Time.realtimeSinceStartup * 1000.0;
            }
            catch (Exception)
            {
                // Time API is unavailable in some test contexts; fall back
                // to 0 so the property is omitted rather than poisoned.
                s_appStartMsCache = 0;
            }
            return s_appStartMsCache;
        }

        internal static void SetAppStartMsForTesting(double valueMs)
        {
            s_appStartMsCache = valueMs;
        }

        /// <summary>
        /// Emit <c>$performance</c> with <c>$app_start_ms</c>. No-op after the
        /// first call per launch.
        /// </summary>
        internal static void EmitAppStartIfNeeded()
        {
            if (!s_installed || s_appStartEmitted) return;
            double appStartMs = GetAppStartMs();
            if (appStartMs <= 0) return;
            s_appStartEmitted = true;

            try
            {
                LayersSDK.Track(PerformanceEventName, new Dictionary<string, object>
                {
                    [PropAppStartMs] = appStartMs
                });
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"PerformanceModule app-start emit failed: {e.Message}");
            }
        }

        /// <summary>
        /// Pump the periodic frame-timing sampler. Called from
        /// <see cref="LayersRunner.Update"/>. Non-allocating fast-path
        /// when the interval has not yet elapsed.
        /// </summary>
        internal static void Tick(float deltaTime)
        {
            if (!s_installed) return;
            s_intervalAccumulator += deltaTime;
            if (s_intervalAccumulator < s_intervalSec) return;
            s_intervalAccumulator = 0f;

            EmitFrameTimingSample();
        }

        private static void EmitFrameTimingSample()
        {
#if UNITY_2018_1_OR_NEWER && !UNITY_WEBGL
            try
            {
                UnityEngine.FrameTimingManager.CaptureFrameTimings();
                var buffer = new UnityEngine.FrameTiming[1];
                uint count = UnityEngine.FrameTimingManager.GetLatestTimings(1, buffer);
                if (count == 0) return;

                var t = buffer[0];
                var props = new Dictionary<string, object>
                {
                    [PropFrameCpuMs] = t.cpuFrameTime,
                    [PropFrameGpuMs] = t.gpuFrameTime,
                    [PropFrameCount] = (long)count
                };
                LayersSDK.Track(PerformanceEventName, props);
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"PerformanceModule frame-timing emit failed: {e.Message}");
            }
#else
            // FrameTimingManager is unavailable on WebGL — the jslib bridge
            // emits navigation-timing-derived frame metrics instead.
#endif
        }

        /// <summary>
        /// Emit a <c>$performance_trace</c> event for a custom trace. Called
        /// from <see cref="LayersPerformanceTrace.Dispose"/>.
        /// </summary>
        internal static void EmitTrace(
            string name, double elapsedMs, IDictionary<string, object> metadata)
        {
            if (string.IsNullOrEmpty(name)) return;
            var props = new Dictionary<string, object>
            {
                [PropTraceName] = name,
                [PropDurationMs] = elapsedMs
            };
            if (metadata != null)
            {
                foreach (var kv in metadata)
                {
                    // Don't let user metadata clobber the canonical
                    // $-prefixed properties — they're reserved.
                    if (kv.Key == PropTraceName || kv.Key == PropDurationMs) continue;
                    props[kv.Key] = kv.Value;
                }
            }

            try
            {
                LayersSDK.Track(PerformanceTraceEventName, props);
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"PerformanceModule trace emit failed: {e.Message}");
            }
        }
    }
}

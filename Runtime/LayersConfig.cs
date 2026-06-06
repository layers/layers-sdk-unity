namespace Layers.Unity
{
    /// <summary>
    /// Environment for the Layers SDK. Controls which ingest endpoint is used
    /// and is sent as the X-Environment header.
    /// </summary>
    public enum LayersEnvironment
    {
        Development,
        Staging,
        Production
    }

    /// <summary>
    /// Configuration for initializing the Layers SDK.
    /// Only AppId is required; all other fields have sensible defaults.
    /// </summary>
    public class LayersConfig
    {
        /// <summary>
        /// The application ID assigned in the Layers dashboard. Required.
        /// </summary>
        public string AppId { get; set; }

        /// <summary>
        /// Environment (development, staging, production). Default: Production.
        /// </summary>
        public LayersEnvironment Environment { get; set; } = LayersEnvironment.Production;

        /// <summary>
        /// Override the base URL for the ingest endpoint. Null uses the default (in.layers.com).
        /// Useful for local testing with the mock server.
        /// </summary>
        public string BaseUrl { get; set; }

        /// <summary>
        /// Enable debug logging via Debug.Log. Default: false.
        /// </summary>
        public bool EnableDebug { get; set; }

        /// <summary>
        /// Enable per-device DebugView. When true, the SDK mints a stable UUID
        /// the first time it initializes, persists it alongside the identity
        /// record, and sends `X-Debug-Token: &lt;uuid&gt;` in every request to
        /// the ingest server. Use <see cref="LayersSDK.GetDebugToken"/>
        /// to retrieve the token for displaying in dev UIs. Default: false.
        /// </summary>
        public bool Debug { get; set; }

        /// <summary>
        /// How often to flush events in milliseconds. Default: 30000 (30 seconds).
        /// </summary>
        public int FlushIntervalMs { get; set; } = 30000;

        /// <summary>
        /// Number of queued events that triggers an automatic flush. Default: 20.
        /// </summary>
        public int FlushThreshold { get; set; } = 20;

        /// <summary>
        /// Maximum number of events in the in-memory queue. Default: 10000.
        /// Events are dropped (FIFO eviction) when the queue is full.
        /// </summary>
        public int MaxQueueSize { get; set; } = 10000;

        /// <summary>
        /// Maximum number of events per HTTP batch. Default: 20.
        /// </summary>
        public int MaxBatchSize { get; set; } = 20;

        /// <summary>
        /// Automatically track app_open events on application focus. Default: true.
        /// </summary>
        public bool AutoTrackAppOpen { get; set; } = true;

        /// <summary>
        /// Automatically track deep link events. Default: true.
        /// </summary>
        public bool AutoTrackDeepLinks { get; set; } = true;

        // ── Tier 5: Exception capture ──────────────────────────────────

        /// <summary>
        /// Auto-capture Unity exceptions and emit <c>$exception</c> events.
        /// Default: <c>true</c> (matches Sentry / Bugsnag / Firebase Crashlytics
        /// conventions). Disable if your app already ships a separate crash
        /// reporter.
        /// </summary>
        public bool AutoTrackExceptions { get; set; } = true;

        /// <summary>
        /// Whether <c>Debug.LogError</c> / <c>Debug.LogAssertion</c> calls are
        /// captured as <c>$exception</c> events in addition to true exceptions.
        /// Default: <c>false</c> — Debug.LogError is often used for non-error
        /// warnings, so opt-in only.
        /// </summary>
        public bool CaptureLogErrors { get; set; }

        // ── Tier 6: Performance ────────────────────────────────────────

        /// <summary>
        /// Auto-capture performance signals (app start time, frame timing).
        /// Default: <c>true</c>.
        /// </summary>
        public bool AutoTrackPerformance { get; set; } = true;

        /// <summary>
        /// How often (in seconds) to sample CPU/GPU frame timing while the
        /// app is foregrounded. Default: 60. Set to 0 to disable periodic
        /// frame-timing sampling but keep app-start + manual traces.
        /// </summary>
        public float PerformanceFrameSamplingIntervalSec { get; set; } = 60f;

        /// <summary>
        /// Tier 2 lifecycle auto-capture (opt-out, default true). When enabled
        /// the SDK auto-fires the canonical $-prefixed lifecycle events:
        /// <c>$app_open</c>, <c>$app_background</c>, <c>$app_terminate</c>,
        /// <c>$first_open</c>, and <c>$app_update</c>. Disable this if your
        /// app emits its own lifecycle telemetry and you want to avoid
        /// duplicates.
        /// </summary>
        public bool AutoCaptureLifecycle { get; set; } = true;

        /// <summary>
        /// Optional Tier 4 SSR-style bootstrap. When set, the values are
        /// applied via the Rust core's <c>set_feature_flag_bootstrap</c>
        /// during <see cref="LayersSDK.Initialize"/> — before the first
        /// <c>/config</c> poll completes — so flag reads on the first frame
        /// hit a non-empty value. Server definitions arriving later
        /// supersede bootstrap values for the same key.
        /// </summary>
        public LayersFeatureFlagBootstrap FeatureFlagBootstrap { get; set; }
    }
}

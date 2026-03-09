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
    }
}

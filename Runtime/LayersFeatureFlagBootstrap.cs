using System.Collections.Generic;

namespace Layers.Unity
{
    /// <summary>
    /// SSR-style bootstrap seed for the Tier 4 feature flag engine.
    ///
    /// Set via <see cref="LayersConfig.FeatureFlagBootstrap"/> at init,
    /// or post-init via <see cref="LayersSDK.SetFeatureFlagBootstrap"/>.
    /// Matches the Rust core's <c>BootstrapData</c> shape on the wire:
    /// <code>
    /// { "feature_flags": { "key": value, ... },
    ///   "feature_flag_payloads": { "key": &lt;json&gt;, ... } }
    /// </code>
    ///
    /// <para>Bootstrap values shadow live evaluations until a <c>/config</c>
    /// poll arrives with definitions for the same keys (server-side wins).
    /// Use this to give the user a flag value on the very first frame —
    /// useful for Experiments where the variant must be known before the
    /// first paint.</para>
    /// </summary>
    public class LayersFeatureFlagBootstrap
    {
        /// <summary>
        /// Flag key → value map. Values must be one of:
        /// - <see cref="bool"/> for binary flags
        /// - <see cref="string"/> for multivariate variant keys
        ///
        /// Other types are JSON-serialized and may not deserialize cleanly
        /// on the Rust side; stick to bool / string.
        /// </summary>
        public Dictionary<string, object> FeatureFlags { get; set; }
            = new Dictionary<string, object>();

        /// <summary>
        /// Flag key → payload map. Payload values are arbitrary JSON
        /// (objects, arrays, scalars). Looked up via
        /// <see cref="LayersSDK.GetFeatureFlagPayload"/>; payload reads
        /// do NOT emit <c>$feature_flag_called</c>.
        /// </summary>
        public Dictionary<string, object> FeatureFlagPayloads { get; set; }
            = new Dictionary<string, object>();
    }
}

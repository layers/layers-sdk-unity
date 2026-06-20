using UnityEngine;

namespace Layers.Unity.Editor
{
    /// <summary>
    /// ScriptableObject holding Layers SDK build settings for iOS and Android.
    /// Create via Assets > Create > Layers > Settings, then place in a Resources folder.
    /// The post-build processors read these settings automatically.
    /// </summary>
    [CreateAssetMenu(fileName = "LayersSettings", menuName = "Layers/Settings")]
    public class LayersSettings : ScriptableObject
    {
        // ── iOS ─────────────────────────────────────────────────────────

        [Header("iOS — App Tracking Transparency")]
        [Tooltip("Usage description shown in the iOS ATT permission dialog (NSUserTrackingUsageDescription).")]
        public string attUsageDescription =
            "This app uses data for personalized ads and to measure ad performance.";

        [Header("iOS — SKAdNetwork")]
        [Tooltip("Include the default set of 23 SKAdNetwork IDs (Meta, Google, TikTok, Snapchat, X, Unity Ads, AppLovin, IronSource, Mintegral, Vungle/Liftoff, Moloco, Pangle, Chartboost, Digital Turbine/Fyber, InMobi).")]
        public bool includeDefaultSKAdNetworkIds = true;

        [Tooltip("Additional SKAdNetwork identifiers to register beyond the defaults. Each must end with .skadnetwork.")]
        public string[] additionalSKAdNetworkIds;

        [Tooltip("Set NSAdvertisingAttributionReportEndpoint so Apple delivers SKAdNetwork postbacks to Layers. Disable only if another MMP already owns the postback endpoint (Apple allows exactly one).")]
        public bool setAdvertisingAttributionReportEndpoint = true;

        [Tooltip("The SKAdNetwork postback endpoint Apple copies postbacks to. Apple appends /.well-known/skadnetwork/report. Must be an https URL.")]
        public string advertisingAttributionReportEndpoint = "https://layers.click";

        [Tooltip("Overwrite an existing NSAdvertisingAttributionReportEndpoint (e.g. set by another MMP/plugin) with the Layers endpoint above. Apple allows exactly one endpoint, so only one provider can receive postbacks. Off by default (don't clobber other providers).")]
        public bool forceAttributionEndpointOverride = false;

        [Header("iOS — Deep Linking")]
        [Tooltip("Custom URL schemes for deep linking (e.g., myapp). Do not include '://'.")]
        public string[] urlSchemes;

        [Tooltip("Associated domains for Universal Links. Prefix with 'applinks:' or just provide the domain (e.g., example.com).")]
        public string[] associatedDomains;

        // ── Android ─────────────────────────────────────────────────────

        [Header("Android — Deep Linking")]
        [Tooltip("Intent filters added to the main Activity for deep linking.")]
        public AndroidIntentFilter[] intentFilters;

        // ── Singleton accessor ──────────────────────────────────────────

        private static LayersSettings _instance;

        /// <summary>
        /// Load the first LayersSettings asset found in any Resources folder.
        /// Returns null if no asset has been created.
        /// </summary>
        public static LayersSettings Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Resources.Load<LayersSettings>("LayersSettings");
                return _instance;
            }
        }

        // ── Default SKAN IDs ────────────────────────────────────────────

        /// <summary>
        /// The 23 default SKAdNetwork identifiers shared across all Layers SDKs
        /// (Expo, Swift, React Native, Unity). Covers Meta, Google, TikTok,
        /// Snapchat, X, Unity Ads, AppLovin, IronSource, Mintegral, Vungle/Liftoff,
        /// Moloco, Pangle, Chartboost, Digital Turbine/Fyber, and InMobi.
        /// </summary>
        public static readonly string[] DefaultSKAdNetworkIds =
        {
            // Meta / Facebook
            "v9wttpbfk9.skadnetwork",
            "n38lu8286q.skadnetwork",
            // Google / YouTube
            "cstr6suwn9.skadnetwork",
            "4fzdc2evr5.skadnetwork",
            // TikTok
            "22mmun2rn5.skadnetwork",
            // Snapchat
            "424m5254lk.skadnetwork",
            // Twitter / X
            "kbd757ywx3.skadnetwork",
            // Unity Ads
            "4468km3ulz.skadnetwork",
            "v72qych5uu.skadnetwork",
            // AppLovin
            "24t9a8vw3c.skadnetwork",
            "ludvb6z3bs.skadnetwork",
            "hs6bdukanm.skadnetwork",
            "c6k4g5qg8m.skadnetwork",
            // IronSource
            "su67r6k2v3.skadnetwork",
            "578prtvx9j.skadnetwork",
            "4dzt52r2t5.skadnetwork",
            // Mintegral
            "kbmxgpxpgc.skadnetwork",
            // Vungle / Liftoff Monetize
            "gta9lk7p23.skadnetwork",
            // Moloco
            "2u9pt9hc89.skadnetwork",
            // Pangle (ByteDance; non-CN shares TikTok's 22mmun2rn5 above)
            "238da6jt44.skadnetwork",
            // Chartboost
            "f38h382jlk.skadnetwork",
            // Digital Turbine / Fyber
            "m8dbw4sv7c.skadnetwork",
            // InMobi
            "wzmmz9fp6w.skadnetwork"
        };
    }

    /// <summary>
    /// Describes a single Android intent-filter for deep linking.
    /// Added to the main Activity in AndroidManifest.xml during the build.
    /// </summary>
    [System.Serializable]
    public class AndroidIntentFilter
    {
        [Tooltip("URI scheme (e.g., 'https' or 'myapp').")]
        public string scheme = "https";

        [Tooltip("Host to match (e.g., 'example.com'). Leave empty for scheme-only matching.")]
        public string host;

        [Tooltip("Path prefix to match (e.g., '/app'). Only used with a host.")]
        public string pathPrefix;
    }
}

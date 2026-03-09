namespace Layers.Unity
{
    /// <summary>
    /// Attribution data extracted from deep link query parameters.
    /// Contains UTM parameters and ad-network click IDs, matching
    /// the attribution fields tracked by all other Layers SDKs
    /// (Swift, Kotlin, React Native, Expo).
    /// </summary>
    public class AttributionData
    {
        // ── UTM Parameters ────────────────────────────────────────────

        /// <summary>UTM source (e.g. "google", "newsletter").</summary>
        public string UtmSource { get; set; }

        /// <summary>UTM medium (e.g. "cpc", "email").</summary>
        public string UtmMedium { get; set; }

        /// <summary>UTM campaign name.</summary>
        public string UtmCampaign { get; set; }

        /// <summary>UTM term (paid search keyword).</summary>
        public string UtmTerm { get; set; }

        /// <summary>UTM content (ad variation identifier).</summary>
        public string UtmContent { get; set; }

        // ── Click IDs (same as all other Layers SDKs) ─────────────────

        /// <summary>Google Ads click ID.</summary>
        public string Gclid { get; set; }

        /// <summary>Google Ads click ID for iOS (App campaigns).</summary>
        public string Gbraid { get; set; }

        /// <summary>Google Ads web-to-app click ID.</summary>
        public string Wbraid { get; set; }

        /// <summary>Facebook / Meta click ID.</summary>
        public string Fbclid { get; set; }

        /// <summary>TikTok click ID.</summary>
        public string Ttclid { get; set; }

        /// <summary>Twitter / X click ID.</summary>
        public string Twclid { get; set; }

        /// <summary>Microsoft Ads click ID.</summary>
        public string Msclkid { get; set; }

        /// <summary>LinkedIn click ID.</summary>
        public string LiFatId { get; set; }

        /// <summary>Snapchat click ID.</summary>
        public string Sclid { get; set; }

        /// <summary>Impact Radius click ID.</summary>
        public string Irclickid { get; set; }
    }
}

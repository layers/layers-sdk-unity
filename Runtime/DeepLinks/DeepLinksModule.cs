using System;
using System.Collections.Generic;
using Layers.Unity.Internal;
using UnityEngine;

namespace Layers.Unity
{
    /// <summary>
    /// Deep linking module for Unity.
    /// Listens for incoming deep links via <see cref="Application.deepLinkActivated"/>
    /// (warm start) and <see cref="Application.absoluteURL"/> (cold start), parses
    /// the URL into <see cref="DeepLinkData"/>, extracts UTM attribution and click IDs,
    /// and auto-tracks a <c>deep_link_opened</c> event via the Rust core.
    ///
    /// This module is a static class with internal init/teardown methods called by
    /// the main <c>Layers</c> class. Consumer code registers listeners via
    /// <see cref="OnDeepLinkReceived"/>.
    /// </summary>
    public static class DeepLinksModule
    {
        // ── Public API ────────────────────────────────────────────────

        /// <summary>
        /// Fired when a deep link is received (cold start or warm start).
        /// Register your handler to react to incoming deep links:
        /// <code>DeepLinksModule.OnDeepLinkReceived += data => Debug.Log(data.RawUrl);</code>
        /// </summary>
        public static event Action<DeepLinkData> OnDeepLinkReceived;

        /// <summary>
        /// Parse a raw URL string into <see cref="DeepLinkData"/> without tracking.
        /// Returns null if the URL cannot be parsed.
        /// </summary>
        public static DeepLinkData ParseUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            try
            {
                return ParseUrlInternal(url);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ── Internal Init / Teardown (called by Layers class) ─────────

        private static bool s_initialized;
        private static bool s_enabled;
        private static string s_lastProcessedUrl;
        private static bool s_enableDebug;

        /// <summary>
        /// Initialize the deep links module. Called by the main Layers class during init.
        /// </summary>
        /// <param name="enabled">
        /// Whether to auto-track <c>deep_link_opened</c> events.
        /// Controlled by <c>LayersConfig.AutoTrackDeepLinks</c>.
        /// </param>
        /// <param name="enableDebug">Whether to log debug messages.</param>
        internal static void Init(bool enabled, bool enableDebug)
        {
            if (s_initialized) return;

            s_enabled = enabled;
            s_enableDebug = enableDebug;
            s_initialized = true;
            s_lastProcessedUrl = null;

            // Subscribe to warm-start deep links
            Application.deepLinkActivated += OnDeepLinkActivated;

            // Check for cold-start deep link
            if (!string.IsNullOrEmpty(Application.absoluteURL))
            {
                OnDeepLinkActivated(Application.absoluteURL);
            }

            if (s_enableDebug)
            {
                Debug.Log("[Layers] DeepLinksModule initialized" +
                          (s_enabled ? " (auto-tracking enabled)" : " (auto-tracking disabled)"));
            }
        }

        /// <summary>
        /// Tear down the deep links module. Called by the main Layers class during shutdown.
        /// </summary>
        internal static void Teardown()
        {
            if (!s_initialized) return;

            Application.deepLinkActivated -= OnDeepLinkActivated;
            s_initialized = false;
            s_lastProcessedUrl = null;
            OnDeepLinkReceived = null;

            if (s_enableDebug)
            {
                Debug.Log("[Layers] DeepLinksModule torn down");
            }
        }

        // ── Click ID parameter names (matching Swift DeepLinksModule) ──

        private static readonly string[] ClickIdParams =
        {
            "gclid", "gbraid", "wbraid",   // Google
            "fbclid",                        // Meta
            "ttclid",                        // TikTok
            "twclid",                        // X (Twitter)
            "msclkid",                       // Microsoft
            "li_fat_id",                     // LinkedIn
            "sclid",                         // Snapchat
            "irclickid"                      // Impact
        };

        // ── Deep link handler ─────────────────────────────────────────

        private static void OnDeepLinkActivated(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            // Deduplicate: cold-start URL from absoluteURL and the deepLinkActivated
            // event can fire for the same URL on some platforms.
            if (url == s_lastProcessedUrl) return;
            s_lastProcessedUrl = url;

            DeepLinkData data;
            try
            {
                data = ParseUrlInternal(url);
            }
            catch (Exception e)
            {
                if (s_enableDebug)
                {
                    Debug.LogWarning($"[Layers] Failed to parse deep link URL: {url} — {e.Message}");
                }
                return;
            }

            if (data == null) return;

            // Persist click IDs from the deep link so they flow into all
            // subsequent events via DeviceContext (matches iOS/Android behavior).
            PersistClickIds(data.Attribution);

            // Auto-track deep_link_opened event via Rust core
            if (s_enabled)
            {
                TrackDeepLinkOpened(data);
            }

            // Notify consumer listeners
            try
            {
                OnDeepLinkReceived?.Invoke(data);
            }
            catch (Exception e)
            {
                if (s_enableDebug)
                {
                    Debug.LogWarning($"[Layers] Exception in OnDeepLinkReceived listener: {e.Message}");
                }
            }
        }

        // ── Track deep_link_opened event ──────────────────────────────

        private static void TrackDeepLinkOpened(DeepLinkData data)
        {
            // Build flat properties matching the pattern from Swift and React Native SDKs:
            //   url, scheme, host, path, utm_source, utm_medium, utm_campaign,
            //   utm_term, utm_content, gclid, fbclid, ttclid, etc.
            var props = new Dictionary<string, object>();

            props["url"] = data.RawUrl;

            if (!string.IsNullOrEmpty(data.Scheme))
                props["scheme"] = data.Scheme;

            if (!string.IsNullOrEmpty(data.Host))
                props["host"] = data.Host;

            if (!string.IsNullOrEmpty(data.Path))
                props["path"] = data.Path;

            // UTM parameters
            var attr = data.Attribution;
            if (attr != null)
            {
                if (!string.IsNullOrEmpty(attr.UtmSource))   props["utm_source"]   = attr.UtmSource;
                if (!string.IsNullOrEmpty(attr.UtmMedium))   props["utm_medium"]   = attr.UtmMedium;
                if (!string.IsNullOrEmpty(attr.UtmCampaign)) props["utm_campaign"] = attr.UtmCampaign;
                if (!string.IsNullOrEmpty(attr.UtmTerm))     props["utm_term"]     = attr.UtmTerm;
                if (!string.IsNullOrEmpty(attr.UtmContent))  props["utm_content"]  = attr.UtmContent;

                // Click IDs
                if (!string.IsNullOrEmpty(attr.Gclid))      props["gclid"]      = attr.Gclid;
                if (!string.IsNullOrEmpty(attr.Gbraid))     props["gbraid"]     = attr.Gbraid;
                if (!string.IsNullOrEmpty(attr.Wbraid))     props["wbraid"]     = attr.Wbraid;
                if (!string.IsNullOrEmpty(attr.Fbclid))     props["fbclid"]     = attr.Fbclid;
                if (!string.IsNullOrEmpty(attr.Ttclid))     props["ttclid"]     = attr.Ttclid;
                if (!string.IsNullOrEmpty(attr.Twclid))     props["twclid"]     = attr.Twclid;
                if (!string.IsNullOrEmpty(attr.Msclkid))    props["msclkid"]    = attr.Msclkid;
                if (!string.IsNullOrEmpty(attr.LiFatId))    props["li_fat_id"]  = attr.LiFatId;
                if (!string.IsNullOrEmpty(attr.Sclid))      props["sclid"]      = attr.Sclid;
                if (!string.IsNullOrEmpty(attr.Irclickid))  props["irclickid"]  = attr.Irclickid;
            }

            string propsJson = JsonHelper.Serialize(props);
            string error = NativeStringHelper.ProcessResult(
                NativeBindings.layers_track("deep_link_opened", propsJson));

            if (error != null && s_enableDebug)
            {
                Debug.LogWarning($"[Layers] deep_link_opened track failed: {error}");
            }
            else if (s_enableDebug)
            {
                Debug.Log($"[Layers] auto-tracked deep_link_opened: {data.RawUrl}");
            }
        }

        // ── Click ID Persistence ─────────────────────────────────────

        /// <summary>
        /// Persist click IDs extracted from the deep link URL via
        /// <see cref="LayersSDK.SetAttributionData"/> so they are included
        /// in all subsequent events via DeviceContext.
        /// Preserves the existing deeplinkId to avoid clearing a previously-set value.
        /// </summary>
        private static void PersistClickIds(AttributionData attr)
        {
            if (attr == null) return;

            // Only call SetAttributionData if at least one click ID is present
            bool hasAny = !string.IsNullOrEmpty(attr.Gclid) ||
                          !string.IsNullOrEmpty(attr.Fbclid) ||
                          !string.IsNullOrEmpty(attr.Ttclid) ||
                          !string.IsNullOrEmpty(attr.Msclkid);

            if (!hasAny) return;

            LayersSDK.SetAttributionData(
                deeplinkId: LayersSDK.DeeplinkId,
                gclid: !string.IsNullOrEmpty(attr.Gclid) ? attr.Gclid : LayersSDK.Gclid,
                fbclid: !string.IsNullOrEmpty(attr.Fbclid) ? attr.Fbclid : LayersSDK.Fbclid,
                ttclid: !string.IsNullOrEmpty(attr.Ttclid) ? attr.Ttclid : LayersSDK.Ttclid,
                msclkid: !string.IsNullOrEmpty(attr.Msclkid) ? attr.Msclkid : LayersSDK.Msclkid
            );

            if (s_enableDebug)
            {
                Debug.Log("[Layers] DeepLinksModule persisted click IDs from deep link URL");
            }
        }

        // ── URL Parsing ───────────────────────────────────────────────

        /// <summary>
        /// Parse a URL string into DeepLinkData using System.Uri.
        /// Extracts scheme, host, path, query parameters, and attribution data.
        /// </summary>
        private static DeepLinkData ParseUrlInternal(string url)
        {
            // System.Uri requires a scheme to parse correctly.
            // Deep link URLs always have a scheme (e.g. "myapp://open/product?id=123"
            // or "https://myapp.com/app/product?id=123").
            var uri = new Uri(url);

            var queryParams = ParseQueryString(uri.Query);

            var attribution = ExtractAttribution(queryParams);

            return new DeepLinkData
            {
                RawUrl = url,
                Scheme = uri.Scheme,
                Host = uri.Host,
                Path = uri.AbsolutePath,
                QueryParameters = queryParams,
                Attribution = attribution
            };
        }

        /// <summary>
        /// Parse a query string (e.g. "?key1=val1&amp;key2=val2") into a dictionary.
        /// Handles URL-encoded values. Duplicate keys are overwritten (last wins).
        /// </summary>
        private static Dictionary<string, string> ParseQueryString(string query)
        {
            var result = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(query)) return result;

            // Strip leading '?'
            var raw = query;
            if (raw.StartsWith("?"))
            {
                raw = raw.Substring(1);
            }

            if (raw.Length == 0) return result;

            var pairs = raw.Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                var pair = pairs[i];
                if (pair.Length == 0) continue;

                int eqIndex = pair.IndexOf('=');
                if (eqIndex < 0)
                {
                    // Key with no value (e.g. "flag")
                    string key = Uri.UnescapeDataString(pair);
                    result[key] = "";
                }
                else
                {
                    string key = Uri.UnescapeDataString(pair.Substring(0, eqIndex));
                    string value = Uri.UnescapeDataString(pair.Substring(eqIndex + 1));
                    result[key] = value;
                }
            }

            return result;
        }

        /// <summary>
        /// Extract UTM parameters and click IDs from query parameters
        /// into an <see cref="AttributionData"/> instance.
        /// </summary>
        private static AttributionData ExtractAttribution(Dictionary<string, string> queryParams)
        {
            var attr = new AttributionData();

            string val;

            // UTM parameters
            if (queryParams.TryGetValue("utm_source",   out val)) attr.UtmSource   = val;
            if (queryParams.TryGetValue("utm_medium",   out val)) attr.UtmMedium   = val;
            if (queryParams.TryGetValue("utm_campaign", out val)) attr.UtmCampaign = val;
            if (queryParams.TryGetValue("utm_term",     out val)) attr.UtmTerm     = val;
            if (queryParams.TryGetValue("utm_content",  out val)) attr.UtmContent  = val;

            // Click IDs (same order as Swift DeepLinksModule.clickIdParams)
            if (queryParams.TryGetValue("gclid",      out val)) attr.Gclid      = val;
            if (queryParams.TryGetValue("gbraid",     out val)) attr.Gbraid     = val;
            if (queryParams.TryGetValue("wbraid",     out val)) attr.Wbraid     = val;
            if (queryParams.TryGetValue("fbclid",     out val)) attr.Fbclid     = val;
            if (queryParams.TryGetValue("ttclid",     out val)) attr.Ttclid     = val;
            if (queryParams.TryGetValue("twclid",     out val)) attr.Twclid     = val;
            if (queryParams.TryGetValue("msclkid",    out val)) attr.Msclkid    = val;
            if (queryParams.TryGetValue("li_fat_id",  out val)) attr.LiFatId    = val;
            if (queryParams.TryGetValue("sclid",      out val)) attr.Sclid      = val;
            if (queryParams.TryGetValue("irclickid",  out val)) attr.Irclickid  = val;

            return attr;
        }
    }
}

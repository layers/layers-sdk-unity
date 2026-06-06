using System;

namespace Layers.Unity.Internal
{
    /// <summary>
    /// WebGL platform implementation of <see cref="ILayersPlatform"/>.
    /// Delegates to the JavaScript bridge via <see cref="WebGLBindings"/>,
    /// which in turn calls the Rust WASM core.
    ///
    /// On WebGL, the Rust core is compiled to WASM and loaded as a separate
    /// module by the jslib. HTTP delivery uses browser <c>fetch()</c> and
    /// <c>navigator.sendBeacon()</c> instead of <c>UnityWebRequest</c>.
    ///
    /// Key differences from <see cref="NativePlatform"/>:
    /// - Strings are allocated via Emscripten's <c>_malloc</c>, freed via
    ///   <see cref="System.Runtime.InteropServices.Marshal.FreeHGlobal"/>
    /// - The jslib manages its own flush timer and lifecycle listeners
    /// - No Coroutine-based flush needed (jslib handles async fetch internally)
    /// - CAPI properties (cookies, page URL) are available via browser APIs
    /// </summary>
#if UNITY_WEBGL && !UNITY_EDITOR
    internal class WebGLPlatform : ILayersPlatform
    {
        public string Init(string configJson)
        {
            // WebGL init is fire-and-forget from C# side.
            // The jslib handles WASM loading asynchronously and manages its own
            // flush timer and lifecycle listeners.
            WebGLBindings.LayersWebGL_Init(configJson);
            return null; // Success — errors are logged to the JS console
        }

        public string Shutdown()
        {
            WebGLBindings.LayersWebGL_Shutdown();
            return null;
        }

        public string Track(string eventName, string propertiesJson)
        {
            WebGLBindings.LayersWebGL_Track(eventName, propertiesJson);
            return null;
        }

        public string Screen(string screenName, string propertiesJson)
        {
            WebGLBindings.LayersWebGL_Screen(screenName, propertiesJson);
            return null;
        }

        public string Identify(string userId)
        {
            WebGLBindings.LayersWebGL_Identify(userId);
            return null;
        }

        public string SetUserProperties(string propertiesJson)
        {
            WebGLBindings.LayersWebGL_SetUserProperties(propertiesJson);
            return null;
        }

        public string SetUserPropertiesOnce(string propertiesJson)
        {
            WebGLBindings.LayersWebGL_SetUserPropertiesOnce(propertiesJson);
            return null;
        }

        public string Group(string groupId, string propertiesJson)
        {
            WebGLBindings.LayersWebGL_Group(groupId, propertiesJson);
            return null;
        }

        public string SetConsent(string consentJson)
        {
            WebGLBindings.LayersWebGL_SetConsent(consentJson);
            return null;
        }

        public string SetDeviceContext(string contextJson)
        {
            WebGLBindings.LayersWebGL_SetDeviceContext(contextJson);
            return null;
        }

        public string Flush()
        {
            WebGLBindings.LayersWebGL_Flush();
            return null;
        }

        public string DrainBatch(uint count)
        {
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_DrainBatch(count));
        }

        public string RequeueEvents(string eventsJson)
        {
            WebGLBindings.LayersWebGL_RequeueEvents(eventsJson);
            return null;
        }

        public string FlushHeadersJson()
        {
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_FlushHeaders());
        }

        public string EventsUrl()
        {
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_EventsUrl());
        }

        public int QueueDepth()
        {
            return WebGLBindings.LayersWebGL_QueueDepth();
        }

        public string GetSessionId()
        {
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetSessionId());
        }

        public string GetDebugToken()
        {
            // The WebGL bridge can route this to either the Rust core's WASM
            // export (debugToken) or the JS-side debug-token state. Today we
            // forward to the Rust WASM side for parity with native; the JS
            // wrapper layer in `packages/wasm` mints its own token in parallel
            // for the WASM SDKs that don't go through Unity.
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetDebugToken());
        }

        public string GetRemoteConfigJson()
        {
            return WebGLStringHelper.ReadAndFree(
                WebGLBindings.LayersWebGL_GetRemoteConfigJson());
        }

        public string UpdateRemoteConfig(string configJson, string etag)
        {
            WebGLBindings.LayersWebGL_UpdateRemoteConfig(configJson, etag);
            return null;
        }

        // ── WebGL-specific CAPI Properties ─────────────────────────────

        /// <summary>
        /// Get Meta's _fbp cookie value, or null if unavailable.
        /// </summary>
        public string GetFbpCookie()
        {
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetFbpCookie());
        }

        /// <summary>
        /// Get TikTok's _ttp cookie value, or null if unavailable.
        /// </summary>
        public string GetTtpCookie()
        {
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetTtpCookie());
        }

        /// <summary>
        /// Get the current page URL, or null if unavailable.
        /// </summary>
        public string GetPageUrl()
        {
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetPageUrl());
        }

        /// <summary>
        /// Get the fbc cookie/parameter value (Meta CAPI), or null if unavailable.
        /// </summary>
        public string GetFbc()
        {
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetFbc());
        }

        /// <summary>
        /// Get URL attribution parameters as a JSON string, or null if none found.
        /// Includes: fbclid, gclid, gbraid, wbraid, ttclid, msclkid, rclid,
        /// utm_source, utm_medium, utm_campaign, utm_content, utm_term, referrer_url.
        /// </summary>
        public string GetUrlParameters()
        {
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetUrlParameters());
        }

        /// <summary>
        /// Check if the browser is online.
        /// </summary>
        public bool IsOnline()
        {
            return WebGLBindings.LayersWebGL_IsOnline() != 0;
        }

        // ── localStorage Persistence ───────────────────────────────────

        /// <summary>
        /// Set a key-value pair in localStorage.
        /// </summary>
        public void SetItem(string key, string value)
        {
            WebGLBindings.LayersWebGL_SetItem(key, value);
        }

        /// <summary>
        /// Get a value from localStorage, or null if not found.
        /// </summary>
        public string GetItem(string key)
        {
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetItem(key));
        }

        /// <summary>
        /// Remove a key from localStorage.
        /// </summary>
        public void RemoveItem(string key)
        {
            WebGLBindings.LayersWebGL_RemoveItem(key);
        }

        // ── Browser Info ───────────────────────────────────────────────

        /// <summary>
        /// Get the browser's user agent string.
        /// </summary>
        public string GetUserAgent()
        {
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetUserAgent());
        }

        /// <summary>
        /// Get the browser's language (e.g. "en-US").
        /// </summary>
        public string GetLanguage()
        {
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetLanguage());
        }

        /// <summary>
        /// Get the screen size (e.g. "1920x1080").
        /// </summary>
        public string GetScreenSize()
        {
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetScreenSize());
        }

        /// <summary>
        /// Get the browser's timezone (e.g. "America/New_York").
        /// </summary>
        public string GetTimezone()
        {
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetTimezone());
        }

        /// <summary>
        /// Get the platform OS name from the browser's user agent.
        /// </summary>
        public string GetPlatformOS()
        {
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetPlatformOS());
        }

        // ── Tier 1: Super-properties ───────────────────────────────────

        public string SetSuperProperties(string propertiesJson)
        {
            WebGLBindings.LayersWebGL_SetSuperProperties(propertiesJson);
            return null;
        }

        public string SetSuperPropertiesOnce(string propertiesJson)
        {
            WebGLBindings.LayersWebGL_SetSuperPropertiesOnce(propertiesJson);
            return null;
        }

        public string UnregisterSuperProperty(string key)
        {
            WebGLBindings.LayersWebGL_UnregisterSuperProperty(key);
            return null;
        }

        public string ClearSuperProperties()
        {
            WebGLBindings.LayersWebGL_ClearSuperProperties();
            return null;
        }

        public string GetSuperPropertiesJson()
        {
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetSuperPropertiesJson())
                ?? "{}";
        }

        // ── Tier 1: Timed events ───────────────────────────────────────

        public string TimeEvent(string eventName)
        {
            WebGLBindings.LayersWebGL_TimeEvent(eventName);
            return null;
        }

        public ulong CancelTimedEvent(string eventName)
        {
            double ms = WebGLBindings.LayersWebGL_CancelTimedEvent(eventName);
            if (double.IsNaN(ms) || ms < 0) return 0;
            return (ulong)ms;
        }

        // ── Tier 1: Multi-group ────────────────────────────────────────

        public string SetGroup(string groupType, string groupId)
        {
            WebGLBindings.LayersWebGL_SetGroup(groupType, groupId ?? string.Empty);
            return null;
        }

        public string AddGroup(string groupType, string groupId)
        {
            WebGLBindings.LayersWebGL_AddGroup(groupType, groupId ?? string.Empty);
            return null;
        }

        public string RemoveGroup(string groupType)
        {
            WebGLBindings.LayersWebGL_RemoveGroup(groupType);
            return null;
        }

        public string GetGroupsJson()
        {
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetGroupsJson()) ?? "{}";
        }

        // ── Tier 1: User-property mutators ─────────────────────────────

        public string Increment(string key, double delta)
        {
            WebGLBindings.LayersWebGL_Increment(key, delta);
            return null;
        }

        public string Append(string key, string valueJson)
        {
            WebGLBindings.LayersWebGL_Append(key, valueJson);
            return null;
        }

        public string Union(string key, string valuesJson)
        {
            WebGLBindings.LayersWebGL_Union(key, valuesJson);
            return null;
        }

        public string Unset(string key)
        {
            WebGLBindings.LayersWebGL_Unset(key);
            return null;
        }

        // ── Tier 1: Identity reset + ID accessors ──────────────────────

        public string Reset()
        {
            WebGLBindings.LayersWebGL_Reset();
            return null;
        }

        public string GetDeviceId()
        {
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetDeviceId());
        }

        public string GetAnonymousId()
        {
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetAnonymousId());
        }

        public uint GetSessionNumber()
        {
            return WebGLBindings.LayersWebGL_GetSessionNumber();
        }

        public string GetFirstOpenTime()
        {
            return WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetFirstOpenTime());
        }

        // ── Tier 1: before_send filter hook ────────────────────────────

        /// <summary>
        /// before_send is not implemented for WebGL — the WASM bridge runs all
        /// events through the JS layer, where the filter would have to be
        /// installed on the WASM <c>onBeforeSend</c> hook directly. Returns
        /// null (no error) and logs a debug message; user filtering is best
        /// done at the call site for WebGL.
        /// </summary>
        public string SetBeforeSend(System.Func<string, string> callback)
        {
            LayersLogger.Log("before_send filter is not supported on WebGL");
            return null;
        }

        public string ClearBeforeSend()
        {
            return null;
        }

        // ── Tier 4: Feature flags ──────────────────────────────────────

        public string GetFeatureFlagJson(string flagKey)
        {
            return WebGLStringHelper.ReadAndFree(
                WebGLBindings.LayersWebGL_GetFeatureFlag(flagKey));
        }

        public int IsFeatureEnabled(string flagKey)
        {
            return WebGLBindings.LayersWebGL_IsFeatureEnabled(flagKey);
        }

        public string GetFeatureFlagPayloadJson(string flagKey)
        {
            return WebGLStringHelper.ReadAndFree(
                WebGLBindings.LayersWebGL_GetFeatureFlagPayload(flagKey));
        }

        public string GetAllFlagsJson()
        {
            return WebGLStringHelper.ReadAndFree(
                WebGLBindings.LayersWebGL_GetAllFlagsJson());
        }

        public int ReloadFeatureFlags()
        {
            return WebGLBindings.LayersWebGL_ReloadFeatureFlags();
        }

        public string SetPersonPropertiesForFlags(string propertiesJson)
        {
            WebGLBindings.LayersWebGL_SetPersonPropertiesForFlags(propertiesJson);
            return null;
        }

        public string SetFeatureFlagBootstrap(string bootstrapJson)
        {
            WebGLBindings.LayersWebGL_SetFeatureFlagBootstrap(bootstrapJson);
            return null;
        }
    }
#endif
}

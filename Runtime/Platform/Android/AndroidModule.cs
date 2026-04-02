using System;
using System.Collections.Generic;
using UnityEngine;

namespace Layers.Unity
{
    /// <summary>
    /// Android-specific module for the Layers Unity SDK.
    ///
    /// Uses Unity's AndroidJavaClass / AndroidJavaObject to call Android APIs
    /// directly via JNI — no separate AAR required.
    ///
    /// Provides:
    /// - Google Advertising ID (GAID) via AdvertisingIdClient
    /// - Google Play Install Referrer via InstallReferrerClient
    /// - Device info from android.os.Build
    /// - Deep link handling via Intent data URI
    ///
    /// All methods are no-ops outside UNITY_ANDROID or inside the Unity Editor.
    /// Exceptions are caught and logged — the SDK never crashes the host app.
    /// </summary>
    public static class AndroidModule
    {
        private const string Tag = "LayersSDK";

        // ── Advertising ID ─────────────────────────────────────────────

        /// <summary>
        /// Fetch the Google Advertising ID (GAID) asynchronously.
        /// Must run on a background thread because AdvertisingIdClient.getAdvertisingIdInfo() blocks.
        /// Returns null via callback if:
        /// - Google Play Services is unavailable
        /// - Limit Ad Tracking is enabled
        /// - The GAID is the zeroed-out placeholder
        /// - Any exception occurs
        /// </summary>
        /// <param name="callback">
        /// Called with (advertisingId, isLimitAdTrackingEnabled).
        /// advertisingId is null if unavailable. Invoked on a background thread;
        /// callers must dispatch to the main thread if needed.
        /// </param>
        public static void GetAdvertisingId(Action<string, bool> callback)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Capture the application context on the main thread where
            // UnityPlayer.currentActivity is guaranteed to be available.
            // Only the blocking getAdvertisingIdInfo() call runs on a background thread.
            AndroidJavaObject appContext;
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    appContext = activity.Call<AndroidJavaObject>("getApplicationContext");
                }

                if (appContext == null)
                {
                    Debug.LogWarning($"[{Tag}] GAID fetch skipped: applicationContext is null");
                    callback?.Invoke(null, false);
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{Tag}] GAID fetch failed (no activity): {e.Message}");
                callback?.Invoke(null, false);
                return;
            }

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using (appContext)
                    using (var adIdClient = new AndroidJavaClass(
                        "com.google.android.gms.ads.identifier.AdvertisingIdClient"))
                    using (var adInfo = adIdClient.CallStatic<AndroidJavaObject>(
                        "getAdvertisingIdInfo", appContext))
                    {
                        if (adInfo == null)
                        {
                            Debug.LogWarning("[LayersSDK] GAID fetch failed: Google Play Services unavailable or returned null");
                            callback?.Invoke(null, false);
                            return;
                        }

                        bool limitTracking = adInfo.Call<bool>("isLimitAdTrackingEnabled");
                        string id = adInfo.Call<string>("getId");

                        // Zeroed-out GAID means tracking is unavailable
                        if (string.IsNullOrEmpty(id) ||
                            id == "00000000-0000-0000-0000-000000000000")
                        {
                            callback?.Invoke(null, limitTracking);
                            return;
                        }

                        if (limitTracking)
                        {
                            callback?.Invoke(null, true);
                            return;
                        }

                        callback?.Invoke(id, false);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[{Tag}] GAID fetch failed: {e.Message}");
                    callback?.Invoke(null, false);
                }
            });
#else
            callback?.Invoke(null, false);
#endif
        }

        // ── Install Referrer ───────────────────────────────────────────

        /// <summary>
        /// Fetch the Google Play Install Referrer using the InstallReferrerClient API.
        ///
        /// The referrer is only fetched once per install. After a successful fetch,
        /// a flag is persisted in SharedPreferences to prevent duplicate collection.
        ///
        /// Callback receives an InstallReferrerResult with the raw referrer string,
        /// parsed UTM parameters, click/install timestamps, and Play Instant flag.
        /// Returns null via callback if the referrer is unavailable or already collected.
        /// </summary>
        /// <param name="callback">
        /// Called with the referrer result, or null if unavailable / already collected.
        /// Invoked on the main thread via UnitySendMessage-compatible dispatch.
        /// </param>
        public static void GetInstallReferrer(Action<InstallReferrerResult> callback)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject context = null;
            AndroidJavaObject client = null;
            try
            {
                // Get the application context. Do NOT wrap context in `using` —
                // startConnection is async and the proxy callback needs context alive.
                // The proxy owns context and disposes it after the callback completes.
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    context = activity.Call<AndroidJavaObject>("getApplicationContext");
                }

                // Check if referrer has already been collected
                using (var prefs = context.Call<AndroidJavaObject>(
                    "getSharedPreferences", "layers_sdk", 0 /* MODE_PRIVATE */))
                {
                    bool alreadyCollected = prefs.Call<bool>(
                        "getBoolean", "layers_referrer_collected", false);
                    if (alreadyCollected)
                    {
                        context.Dispose();
                        callback?.Invoke(null);
                        return;
                    }
                }

                // Build the InstallReferrerClient.
                // Do NOT wrap client in `using` — startConnection is async
                // and the callback fires later. The proxy owns both client and
                // context, disposing them in EndConnection() after completion.
                using (var builderClass = new AndroidJavaClass(
                    "com.android.installreferrer.api.InstallReferrerClient"))
                using (var builder = builderClass.CallStatic<AndroidJavaObject>(
                    "newBuilder", context))
                {
                    client = builder.Call<AndroidJavaObject>("build");
                }

                var listener = new InstallReferrerStateListenerProxy(
                    client, context, callback);
                client.Call("startConnection", listener);
            }
            catch (Exception e)
            {
                // Dispose JNI objects if created before the exception — since they
                // are not in `using` blocks, we must clean up manually on error.
                try { client?.Dispose(); } catch (Exception) { }
                try { context?.Dispose(); } catch (Exception) { }
                Debug.LogWarning($"[{Tag}] Install referrer fetch failed: {e.Message}");
                callback?.Invoke(null);
            }
#else
            callback?.Invoke(null);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>
        /// AndroidJavaProxy that implements InstallReferrerStateListener.
        /// Routes the onInstallReferrerSetupFinished callback to C# land.
        /// </summary>
        private class InstallReferrerStateListenerProxy : AndroidJavaProxy
        {
            private readonly AndroidJavaObject _client;
            private readonly AndroidJavaObject _context;
            private readonly Action<InstallReferrerResult> _callback;
            private bool _completed;

            // Response codes from InstallReferrerClient.InstallReferrerResponse
            private const int RESPONSE_OK = 0;
            private const int RESPONSE_SERVICE_UNAVAILABLE = 1;
            private const int RESPONSE_FEATURE_NOT_SUPPORTED = 2;

            public InstallReferrerStateListenerProxy(
                AndroidJavaObject client,
                AndroidJavaObject context,
                Action<InstallReferrerResult> callback)
                : base("com.android.installreferrer.api.InstallReferrerStateListener")
            {
                _client = client;
                _context = context;
                _callback = callback;
            }

            // Called by the Android Install Referrer API
            // ReSharper disable once InconsistentNaming — must match Java method name
            public void onInstallReferrerSetupFinished(int responseCode)
            {
                if (_completed) return;
                _completed = true;

                try
                {
                    if (responseCode != RESPONSE_OK)
                    {
                        string reason = responseCode == RESPONSE_SERVICE_UNAVAILABLE
                            ? "service unavailable"
                            : responseCode == RESPONSE_FEATURE_NOT_SUPPORTED
                                ? "feature not supported"
                                : $"error code {responseCode}";
                        Debug.Log($"[{Tag}] Install referrer: {reason}");
                        _callback?.Invoke(null);
                        EndConnection();
                        return;
                    }

                    using (var details = _client.Call<AndroidJavaObject>("getInstallReferrer"))
                    {
                        string rawReferrer = details.Call<string>("getInstallReferrer");
                        long clickTimestamp = details.Call<long>(
                            "getReferrerClickTimestampSeconds");
                        long installBeginTimestamp = details.Call<long>(
                            "getInstallBeginTimestampSeconds");
                        long clickTimestampServer = details.Call<long>(
                            "getReferrerClickTimestampServerSeconds");
                        long installBeginTimestampServer = details.Call<long>(
                            "getInstallBeginTimestampServerSeconds");
                        string installVersion = details.Call<string>("getInstallVersion");
                        bool googlePlayInstant = details.Call<bool>(
                            "getGooglePlayInstantParam");

                        var parsed = ParseReferrer(rawReferrer);

                        var result = new InstallReferrerResult
                        {
                            RawReferrer = rawReferrer ?? "",
                            ReferrerClickTimestamp = clickTimestamp,
                            InstallBeginTimestamp = installBeginTimestamp,
                            ReferrerClickTimestampServer = clickTimestampServer,
                            InstallBeginTimestampServer = installBeginTimestampServer,
                            InstallVersion = installVersion ?? "",
                            GooglePlayInstant = googlePlayInstant,
                            UtmSource = parsed.GetValueOrDefault("utm_source"),
                            UtmMedium = parsed.GetValueOrDefault("utm_medium"),
                            UtmCampaign = parsed.GetValueOrDefault("utm_campaign"),
                            UtmContent = parsed.GetValueOrDefault("utm_content"),
                            UtmTerm = parsed.GetValueOrDefault("utm_term"),
                            Gclid = parsed.GetValueOrDefault("gclid")
                        };

                        // Mark as collected so we don't fetch again
                        MarkReferrerCollected(_context);

                        _callback?.Invoke(result);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[{Tag}] Install referrer read failed: {e.Message}");
                    _callback?.Invoke(null);
                }
                finally
                {
                    EndConnection();
                }
            }

            // Called by the Android Install Referrer API
            // ReSharper disable once InconsistentNaming — must match Java method name
            public void onInstallReferrerServiceDisconnected()
            {
                // No retry — one-shot fetch, same as Kotlin SDK
            }

            private void EndConnection()
            {
                try
                {
                    _client?.Call("endConnection");
                }
                catch (Exception)
                {
                    // Ignore — best effort cleanup
                }

                // Dispose Java objects that the proxy owns (not managed by `using` blocks)
                try { _client?.Dispose(); } catch (Exception) { }
                try { _context?.Dispose(); } catch (Exception) { }
            }
        }

        private static void MarkReferrerCollected(AndroidJavaObject context)
        {
            try
            {
                using (var prefs = context.Call<AndroidJavaObject>(
                    "getSharedPreferences", "layers_sdk", 0 /* MODE_PRIVATE */))
                using (var editor = prefs.Call<AndroidJavaObject>("edit"))
                {
                    editor.Call<AndroidJavaObject>(
                        "putBoolean", "layers_referrer_collected", true);
                    editor.Call("apply");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{Tag}] Failed to persist referrer flag: {e.Message}");
            }
        }
#endif

        // ── Install ID ─────────────────────────────────────────────────

        /// <summary>
        /// Get or create a persistent install ID stored in SharedPreferences.
        /// This survives app updates but not uninstalls.
        /// </summary>
        public static string GetOrCreateInstallId()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var context = activity.Call<AndroidJavaObject>("getApplicationContext"))
                using (var prefs = context.Call<AndroidJavaObject>(
                    "getSharedPreferences", "layers_sdk", 0 /* MODE_PRIVATE */))
                {
                    string existingId = prefs.Call<string>(
                        "getString", "layers_install_id", (string)null);

                    if (!string.IsNullOrEmpty(existingId))
                    {
                        return existingId;
                    }

                    string newId = Guid.NewGuid().ToString();
                    using (var editor = prefs.Call<AndroidJavaObject>("edit"))
                    {
                        editor.Call<AndroidJavaObject>("putString", "layers_install_id", newId);
                        editor.Call("apply");
                    }

                    return newId;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{Tag}] Install ID retrieval failed: {e.Message}");
                // Fallback: return a non-persistent GUID so tracking can continue
                return Guid.NewGuid().ToString();
            }
#else
            return Guid.NewGuid().ToString();
#endif
        }

        // ── Deep Links ─────────────────────────────────────────────────

        /// <summary>
        /// Extract the launch deep link URL from the current Activity's Intent.
        /// Returns null if no deep link data is present.
        ///
        /// Call this during initialization to capture the URL that launched the app.
        /// For deep links that arrive while the app is running, use
        /// Unity's Application.deepLinkActivated event instead.
        /// </summary>
        public static string GetLaunchDeepLink()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                {
                    if (intent == null) return null;

                    using (var uri = intent.Call<AndroidJavaObject>("getData"))
                    {
                        if (uri == null) return null;
                        return uri.Call<string>("toString");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{Tag}] Launch deep link extraction failed: {e.Message}");
                return null;
            }
#else
            return null;
#endif
        }

        /// <summary>
        /// Parse a deep link URL and extract structured attribution data.
        /// Returns a dictionary containing: url, scheme, host, path,
        /// UTM parameters, and click ID parameters (gclid, fbclid, etc.).
        /// Only non-null, non-empty values are included.
        /// </summary>
        /// <param name="url">The deep link URL string to parse.</param>
        public static Dictionary<string, string> ParseDeepLink(string url)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(url)) return result;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var uriClass = new AndroidJavaClass("android.net.Uri"))
                using (var uri = uriClass.CallStatic<AndroidJavaObject>("parse", url))
                {
                    if (uri == null) return result;

                    result["url"] = url;

                    string scheme = uri.Call<string>("getScheme");
                    if (!string.IsNullOrEmpty(scheme)) result["scheme"] = scheme;

                    string host = uri.Call<string>("getHost");
                    if (!string.IsNullOrEmpty(host)) result["host"] = host;

                    string path = uri.Call<string>("getPath");
                    if (!string.IsNullOrEmpty(path)) result["path"] = path;

                    // UTM parameters
                    foreach (string param in UtmParams)
                    {
                        string value = uri.Call<string>("getQueryParameter", param);
                        if (!string.IsNullOrEmpty(value)) result[param] = value;
                    }

                    // Click ID parameters (gclid, fbclid, ttclid, etc.)
                    foreach (string param in ClickIdParams)
                    {
                        string value = uri.Call<string>("getQueryParameter", param);
                        if (!string.IsNullOrEmpty(value)) result[param] = value;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{Tag}] Deep link parse failed: {e.Message}");
                // Return what we have with at least the raw URL
                if (!result.ContainsKey("url")) result["url"] = url;
            }
#else
            // Fallback: basic URI parsing for Editor / non-Android
            result["url"] = url;
            try
            {
                var uri = new Uri(url);
                if (!string.IsNullOrEmpty(uri.Scheme)) result["scheme"] = uri.Scheme;
                if (!string.IsNullOrEmpty(uri.Host)) result["host"] = uri.Host;
                if (!string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/")
                    result["path"] = uri.AbsolutePath;

                if (!string.IsNullOrEmpty(uri.Query))
                {
                    var queryParams = ParseQueryString(uri.Query);
                    foreach (string param in UtmParams)
                    {
                        if (queryParams.TryGetValue(param, out string value) &&
                            !string.IsNullOrEmpty(value))
                            result[param] = value;
                    }
                    foreach (string param in ClickIdParams)
                    {
                        if (queryParams.TryGetValue(param, out string value) &&
                            !string.IsNullOrEmpty(value))
                            result[param] = value;
                    }
                }
            }
            catch (Exception)
            {
                // URI parsing failed — return just the raw URL
            }
#endif

            return result;
        }

        // ── Referrer Parsing ───────────────────────────────────────────

        /// <summary>
        /// Parse a referrer query string into a dictionary of attribution parameters.
        /// Matches the same logic as the Kotlin SDK's InstallReferrerTracker.parseReferrer().
        /// Only returns entries for known attribution parameters that are present and non-empty.
        /// </summary>
        internal static Dictionary<string, string> ParseReferrer(string referrer)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(referrer)) return result;

            try
            {
                string[] pairs = referrer.Split('&');
                foreach (string pair in pairs)
                {
                    int eqIndex = pair.IndexOf('=');
                    if (eqIndex < 0) continue;

                    string key = Uri.UnescapeDataString(pair.Substring(0, eqIndex));
                    string value = Uri.UnescapeDataString(pair.Substring(eqIndex + 1));

                    if (!string.IsNullOrWhiteSpace(value) && IsAttributionParam(key))
                    {
                        result[key] = value;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{Tag}] Referrer parse error: {e.Message}");
            }

            return result;
        }

        // ── Private Helpers ────────────────────────────────────────────

        /// <summary>UTM parameters to extract from deep links and referrer strings.</summary>
        private static readonly string[] UtmParams =
        {
            "utm_source", "utm_medium", "utm_campaign", "utm_content", "utm_term"
        };

        /// <summary>
        /// Click ID parameters for ad platform attribution.
        /// Matches the Kotlin SDK's DeepLinksModule.CLICK_ID_PARAMS.
        /// </summary>
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

        /// <summary>
        /// All attribution parameters recognized by ParseReferrer().
        /// Matches the Kotlin SDK's InstallReferrerTracker.ATTRIBUTION_PARAMS.
        /// </summary>
        private static readonly HashSet<string> AttributionParams = new HashSet<string>
        {
            "gclid", "utm_source", "utm_medium", "utm_campaign", "utm_content", "utm_term"
        };

        private static bool IsAttributionParam(string key)
        {
            return AttributionParams.Contains(key);
        }

        /// <summary>
        /// Simple query string parser for non-Android platforms (Editor fallback).
        /// </summary>
        private static Dictionary<string, string> ParseQueryString(string query)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(query)) return result;

            // Strip leading '?'
            if (query.StartsWith("?")) query = query.Substring(1);

            string[] pairs = query.Split('&');
            foreach (string pair in pairs)
            {
                int eqIndex = pair.IndexOf('=');
                if (eqIndex < 0) continue;

                string key = Uri.UnescapeDataString(pair.Substring(0, eqIndex));
                string value = Uri.UnescapeDataString(pair.Substring(eqIndex + 1));
                result[key] = value;
            }

            return result;
        }
    }

    /// <summary>
    /// Structured result from a successful Install Referrer fetch.
    /// Contains the raw referrer string, parsed UTM parameters,
    /// timestamps, and Google Play Instant flag.
    /// </summary>
    public class InstallReferrerResult
    {
        /// <summary>Raw referrer query string from Google Play.</summary>
        public string RawReferrer { get; set; }

        /// <summary>Timestamp (seconds since epoch) when the referrer link was clicked.</summary>
        public long ReferrerClickTimestamp { get; set; }

        /// <summary>Timestamp (seconds since epoch) when the app install began.</summary>
        public long InstallBeginTimestamp { get; set; }

        /// <summary>Server-side timestamp (seconds since epoch) of the referrer click.</summary>
        public long ReferrerClickTimestampServer { get; set; }

        /// <summary>Server-side timestamp (seconds since epoch) when install began.</summary>
        public long InstallBeginTimestampServer { get; set; }

        /// <summary>App version that was installed (from the Play Store).</summary>
        public string InstallVersion { get; set; }

        /// <summary>Whether the app was installed via Google Play Instant.</summary>
        public bool GooglePlayInstant { get; set; }

        // Parsed UTM parameters (null if not present in referrer)

        /// <summary>UTM source parameter, or null.</summary>
        public string UtmSource { get; set; }

        /// <summary>UTM medium parameter, or null.</summary>
        public string UtmMedium { get; set; }

        /// <summary>UTM campaign parameter, or null.</summary>
        public string UtmCampaign { get; set; }

        /// <summary>UTM content parameter, or null.</summary>
        public string UtmContent { get; set; }

        /// <summary>UTM term parameter, or null.</summary>
        public string UtmTerm { get; set; }

        /// <summary>Google Click Identifier, or null.</summary>
        public string Gclid { get; set; }

        /// <summary>
        /// Convert to a properties dictionary suitable for tracking as an install_referrer event.
        /// Matches the Kotlin SDK's InstallReferrerTracker event format.
        /// </summary>
        public Dictionary<string, object> ToEventProperties()
        {
            var props = new Dictionary<string, object>
            {
                ["referrer"] = RawReferrer ?? "",
                ["referrer_click_timestamp"] = ReferrerClickTimestamp,
                ["install_begin_timestamp"] = InstallBeginTimestamp,
                ["referrer_click_timestamp_server"] = ReferrerClickTimestampServer,
                ["install_begin_timestamp_server"] = InstallBeginTimestampServer,
                ["install_version"] = InstallVersion ?? "",
                ["google_play_instant"] = GooglePlayInstant
            };

            if (!string.IsNullOrEmpty(UtmSource)) props["utm_source"] = UtmSource;
            if (!string.IsNullOrEmpty(UtmMedium)) props["utm_medium"] = UtmMedium;
            if (!string.IsNullOrEmpty(UtmCampaign)) props["utm_campaign"] = UtmCampaign;
            if (!string.IsNullOrEmpty(UtmContent)) props["utm_content"] = UtmContent;
            if (!string.IsNullOrEmpty(UtmTerm)) props["utm_term"] = UtmTerm;
            if (!string.IsNullOrEmpty(Gclid)) props["gclid"] = Gclid;

            return props;
        }
    }
}

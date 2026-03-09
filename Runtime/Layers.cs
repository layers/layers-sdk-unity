using System;
using System.Collections.Generic;
using Layers.Unity.Internal;
using UnityEngine;

namespace Layers.Unity
{
    /// <summary>
    /// Main public API for the Layers Unity SDK.
    ///
    /// Static singleton facade that delegates all analytics logic to the Rust core
    /// via P/Invoke (<see cref="NativeBindings"/>). Platform-specific modules (ATT,
    /// SKAN, deep links, Android GAID/install referrer) are initialized automatically
    /// based on the target platform.
    ///
    /// Usage:
    /// <code>
    /// Layers.Initialize(new LayersConfig { AppId = "your-app-id" });
    /// Layers.Track("button_clicked", new Dictionary&lt;string, object&gt; { ["button"] = "signup" });
    /// Layers.Identify("user-123");
    /// Layers.Flush();
    /// </code>
    ///
    /// Lifecycle is managed automatically via <see cref="LayersRunner"/>:
    /// - Background: flushes queued events
    /// - Foreground: resumes periodic flush
    /// - Quit: synchronous shutdown with persistence
    /// </summary>
    public static class Layers
    {
        // ── Constants ────────────────────────────────────────────────────

        private const string SdkVersion = "0.1.0";

        // ── State ────────────────────────────────────────────────────────

        private static bool _isInitialized;
        private static LayersConfig _config;
        private static FlushManager _flushManager;
        private static RemoteConfigPoller _configPoller;
        private static string _userId;

        // ── Events ───────────────────────────────────────────────────────

        /// <summary>
        /// Fired when an SDK error occurs. The first argument is the method name,
        /// the second is the error message. Errors are always logged via
        /// <see cref="Debug.LogError"/> regardless of this event.
        /// </summary>
        public static event Action<string, string> OnError;

        // ── Public Accessors ─────────────────────────────────────────────

        /// <summary>
        /// Whether the SDK has been successfully initialized.
        /// </summary>
        public static bool IsInitialized => _isInitialized;

        /// <summary>
        /// The user ID set by the most recent <see cref="Identify"/> call,
        /// or null if no user has been identified.
        /// </summary>
        public static string UserId => _userId;

        /// <summary>
        /// The current session ID assigned by the Rust core, or null if the
        /// SDK has not been initialized.
        /// </summary>
        public static string SessionId
        {
            get
            {
                if (!_isInitialized) return null;
                return NativeStringHelper.ReadAndFree(NativeBindings.layers_get_session_id());
            }
        }

        /// <summary>
        /// The number of events currently waiting in the outbound queue.
        /// Returns -1 if the SDK has not been initialized.
        /// </summary>
        public static int QueueDepth => NativeBindings.layers_queue_depth();

        /// <summary>
        /// The latest remote config JSON fetched from the server, or null if
        /// the SDK has not been initialized or no config has been fetched yet.
        /// </summary>
        public static string RemoteConfig
        {
            get
            {
                if (!_isInitialized) return null;
                return NativeStringHelper.ReadAndFree(
                    NativeBindings.layers_get_remote_config_json());
            }
        }

        // ── Initialization ───────────────────────────────────────────────

        /// <summary>
        /// Initialize the Layers SDK. Must be called once before any tracking,
        /// identification, or configuration calls.
        ///
        /// This method is idempotent -- calling it again after a successful
        /// initialization logs a warning and returns immediately.
        ///
        /// Throws <see cref="ArgumentException"/> if <see cref="LayersConfig.AppId"/>
        /// is null or empty.
        /// </summary>
        /// <param name="config">SDK configuration. Only <c>AppId</c> is required.</param>
        public static void Initialize(LayersConfig config)
        {
            if (_isInitialized)
            {
                LayersLogger.Warn("Layers SDK already initialized");
                return;
            }

            if (config == null || string.IsNullOrEmpty(config.AppId))
                throw new ArgumentException("AppId is required");

            _config = config;
            LayersLogger.Enabled = config.EnableDebug;

            // Build config JSON for the Rust core
            var configDict = new Dictionary<string, object>
            {
                ["app_id"] = config.AppId,
                ["environment"] = config.Environment.ToString().ToLowerInvariant(),
                ["sdk_version"] = $"unity/{SdkVersion}",
                ["persistence_dir"] = Application.persistentDataPath,
                ["enable_debug"] = config.EnableDebug,
                ["flush_interval_ms"] = config.FlushIntervalMs,
                ["flush_threshold"] = config.FlushThreshold,
                ["max_queue_size"] = config.MaxQueueSize,
                ["max_batch_size"] = config.MaxBatchSize
            };

            if (!string.IsNullOrEmpty(config.BaseUrl))
                configDict["base_url"] = config.BaseUrl;

            string configJson = JsonHelper.Serialize(configDict);
            string error = NativeStringHelper.ProcessResult(NativeBindings.layers_init(configJson));
            if (error != null)
            {
                RaiseError("Initialize", error);
                return;
            }

            _isInitialized = true;

            // Set device context (platform, os_version, device_model, etc.)
            var deviceInfo = DeviceInfoCollector.Collect();
            NativeStringHelper.ProcessResult(
                NativeBindings.layers_set_device_context(JsonHelper.Serialize(deviceInfo)));

            // Create the runner singleton (hosts coroutines + lifecycle hooks)
            var runner = LayersRunner.Instance;

            // Start periodic flush
            _flushManager = new FlushManager(runner, (uint)config.MaxBatchSize);
            _flushManager.StartPeriodicFlush(config.FlushIntervalMs / 1000f);

            // Start remote config polling (default 5 minute interval)
            string baseUrl = !string.IsNullOrEmpty(config.BaseUrl)
                ? config.BaseUrl
                : "https://in.layers.com";
            _configPoller = new RemoteConfigPoller(runner, baseUrl, config.AppId);

            // Subscribe to config updates for SKAN auto-config (iOS only)
#if UNITY_IOS && !UNITY_EDITOR
            _configPoller.OnConfigUpdated += OnRemoteConfigUpdated;
#endif

            _configPoller.StartPolling(300f);

            // Initialize deep links module
            if (config.AutoTrackDeepLinks)
                DeepLinksModule.Init(true, config.EnableDebug);
            else
                DeepLinksModule.Init(false, config.EnableDebug);

            // Collect attribution signals and fire app_open with them (if enabled).
            // This checks remote config for clipboard_attribution_enabled,
            // collects AdServices token (iOS), clipboard URL, and timezone.
            TrackAttributionSignals(config);

            // Platform-specific initialization
#if UNITY_IOS && !UNITY_EDITOR
            InitIOSModules();
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
            InitAndroidModules();
#endif

            LayersLogger.Log($"Layers SDK initialized (appId={config.AppId}, env={config.Environment})");
        }

        // ── Event Tracking ───────────────────────────────────────────────

        /// <summary>
        /// Track a custom event with optional properties.
        ///
        /// Supported property value types: string, int, long, float, double, bool,
        /// nested Dictionary&lt;string, object&gt;, and IList.
        /// </summary>
        /// <param name="eventName">The event name. Must not be null or empty.</param>
        /// <param name="properties">Optional event properties.</param>
        public static void Track(string eventName, Dictionary<string, object> properties = null)
        {
            if (!CheckInitialized("Track")) return;

            if (string.IsNullOrEmpty(eventName))
            {
                RaiseError("Track", "eventName must not be null or empty");
                return;
            }

            string propsJson = properties != null ? JsonHelper.Serialize(properties) : null;
            string error = NativeStringHelper.ProcessResult(
                NativeBindings.layers_track(eventName, propsJson));

            if (error != null)
            {
                RaiseError("Track", error);
                return;
            }

            // Process event against SKAN rules (iOS only)
#if UNITY_IOS && !UNITY_EDITOR
            if (SKANModule.IsAutoConfigured)
                SKANModule.ProcessEvent(eventName, properties);
#endif
        }

        /// <summary>
        /// Track a screen view event with optional properties.
        /// Internally calls the Rust core's <c>screen</c> function which creates
        /// a <c>screen</c> event with the screen name as a property.
        /// </summary>
        /// <param name="screenName">The screen name. Must not be null or empty.</param>
        /// <param name="properties">Optional additional properties.</param>
        public static void Screen(string screenName, Dictionary<string, object> properties = null)
        {
            if (!CheckInitialized("Screen")) return;

            if (string.IsNullOrEmpty(screenName))
            {
                RaiseError("Screen", "screenName must not be null or empty");
                return;
            }

            string propsJson = properties != null ? JsonHelper.Serialize(properties) : null;
            string error = NativeStringHelper.ProcessResult(
                NativeBindings.layers_screen(screenName, propsJson));

            if (error != null)
                RaiseError("Screen", error);
        }

        // ── User Identity ────────────────────────────────────────────────

        /// <summary>
        /// Associate subsequent events with the given user ID.
        /// </summary>
        /// <param name="userId">The user ID. Must not be null or empty.</param>
        public static void Identify(string userId)
        {
            if (!CheckInitialized("Identify")) return;

            if (string.IsNullOrEmpty(userId))
            {
                RaiseError("Identify", "userId must not be null or empty");
                return;
            }

            string error = NativeStringHelper.ProcessResult(
                NativeBindings.layers_identify(userId));

            if (error != null)
                RaiseError("Identify", error);
            else
                _userId = userId;
        }

        /// <summary>
        /// Set user properties (upsert semantics). These properties are attached
        /// to the user and sent with every subsequent event.
        /// </summary>
        /// <param name="properties">Key-value properties to set.</param>
        public static void SetUserProperties(Dictionary<string, object> properties)
        {
            if (!CheckInitialized("SetUserProperties")) return;

            if (properties == null || properties.Count == 0)
            {
                RaiseError("SetUserProperties", "properties must not be null or empty");
                return;
            }

            string json = JsonHelper.Serialize(properties);
            string error = NativeStringHelper.ProcessResult(
                NativeBindings.layers_set_user_properties(json));

            if (error != null)
                RaiseError("SetUserProperties", error);
        }

        /// <summary>
        /// Set user properties with "set once" semantics. Only properties whose keys
        /// have not been previously set via this method are forwarded.
        /// Typical use: <c>first_seen_date</c>, <c>initial_utm_source</c>, etc.
        /// </summary>
        /// <param name="properties">Key-value properties to set once.</param>
        public static void SetUserPropertiesOnce(Dictionary<string, object> properties)
        {
            if (!CheckInitialized("SetUserPropertiesOnce")) return;

            if (properties == null || properties.Count == 0)
            {
                RaiseError("SetUserPropertiesOnce", "properties must not be null or empty");
                return;
            }

            string json = JsonHelper.Serialize(properties);
            string error = NativeStringHelper.ProcessResult(
                NativeBindings.layers_set_user_properties_once(json));

            if (error != null)
                RaiseError("SetUserPropertiesOnce", error);
        }

        // ── Consent ──────────────────────────────────────────────────────

        /// <summary>
        /// Update the user's consent preferences for analytics and/or advertising.
        /// Pass null for either parameter to leave that consent category unchanged.
        /// </summary>
        /// <param name="analytics">Whether analytics tracking is allowed, or null to leave unchanged.</param>
        /// <param name="advertising">Whether advertising tracking is allowed, or null to leave unchanged.</param>
        public static void SetConsent(bool? analytics = null, bool? advertising = null)
        {
            if (!CheckInitialized("SetConsent")) return;

            var consent = new Dictionary<string, object>();
            if (analytics.HasValue) consent["analytics"] = analytics.Value;
            if (advertising.HasValue) consent["advertising"] = advertising.Value;

            string json = JsonHelper.Serialize(consent);
            string error = NativeStringHelper.ProcessResult(
                NativeBindings.layers_set_consent(json));

            if (error != null)
                RaiseError("SetConsent", error);
        }

        // ── Flush & Shutdown ─────────────────────────────────────────────

        /// <summary>
        /// Trigger an immediate flush of the event queue to the server.
        /// The flush runs asynchronously via a Unity coroutine.
        /// </summary>
        public static void Flush()
        {
            if (!CheckInitialized("Flush")) return;
            _flushManager?.FlushNow();
        }

        /// <summary>
        /// Reset user state for logout flows. Clears the current user identity
        /// and user properties, but keeps the SDK initialized. Call this when a
        /// user logs out so that subsequent events are not associated with the
        /// previous user.
        /// </summary>
        public static void Reset()
        {
            if (!CheckInitialized("Reset")) return;

            // Flush pending events before clearing identity
            _flushManager?.FlushNow();

            // Clear identity on the Rust core
            NativeStringHelper.ProcessResult(NativeBindings.layers_identify(""));
            NativeStringHelper.ProcessResult(NativeBindings.layers_set_user_properties("{}"));

            _userId = null;
            LayersLogger.Log("User state reset");
        }

        /// <summary>
        /// Shut down the SDK, persisting remaining events and releasing resources.
        /// After shutdown, the SDK must be re-initialized via <see cref="Initialize"/>
        /// before any further calls.
        /// </summary>
        public static void Shutdown()
        {
            if (!_isInitialized) return;

            _flushManager?.StopPeriodicFlush();
            _flushManager?.FlushBlocking();

#if UNITY_IOS && !UNITY_EDITOR
            if (_configPoller != null)
                _configPoller.OnConfigUpdated -= OnRemoteConfigUpdated;
            SKANModule.ResetAutoConfig();
#endif

            _configPoller?.StopPolling();
            DeepLinksModule.Teardown();

            NativeStringHelper.ProcessResult(NativeBindings.layers_shutdown());

            _isInitialized = false;
            _flushManager = null;
            _configPoller = null;
            _userId = null;
            _config = null;

            LayersLogger.Log("Layers SDK shut down");
        }

        // ── ATT (iOS) ────────────────────────────────────────────────────

        /// <summary>
        /// Request App Tracking Transparency authorization (iOS only).
        ///
        /// After the user responds, this method automatically:
        /// - Collects IDFA if authorized
        /// - Collects IDFV (always available)
        /// - Updates device context with the identifiers
        /// - Sets advertising consent based on the ATT result
        ///
        /// The callback receives the resulting <see cref="ATTStatus"/>.
        /// On non-iOS platforms, the callback receives <see cref="ATTStatus.NotDetermined"/>.
        /// </summary>
        /// <param name="callback">Called with the ATT status after the user responds.</param>
        public static void RequestTrackingPermission(Action<ATTStatus> callback = null)
        {
            if (!CheckInitialized("RequestTrackingPermission")) return;

            ATTModule.RequestTracking(status =>
            {
                // Collect IDFV unconditionally (first-party identifier, no consent required)
                string idfv = ATTModule.GetVendorId();

                // Collect IDFA only when authorized
                string idfa = null;
                if (status == ATTStatus.Authorized)
                    idfa = ATTModule.GetAdvertisingId();

                // Update device context with identifiers
                if (idfa != null || idfv != null)
                {
                    var ctx = new Dictionary<string, object>();
                    if (idfa != null) ctx["idfa"] = idfa;
                    if (idfv != null) ctx["idfv"] = idfv;
                    ctx["att_status"] = status.ToString().ToLowerInvariant();
                    NativeStringHelper.ProcessResult(
                        NativeBindings.layers_set_device_context(JsonHelper.Serialize(ctx)));
                }

                // Auto-set advertising consent based on ATT result
                bool advertisingAllowed = status == ATTStatus.Authorized;
                SetConsent(advertising: advertisingAllowed);

                LayersLogger.Log(
                    $"ATT status: {status}, advertising consent: {advertisingAllowed}");

                callback?.Invoke(status);
            });
        }

        // ── Internal Lifecycle Callbacks (called by LayersRunner) ─────────

        internal static void OnBackgrounded()
        {
            if (!_isInitialized) return;
            LayersLogger.Log("App backgrounded, flushing...");
            _flushManager?.FlushNow();
        }

        internal static void OnForegrounded()
        {
            if (!_isInitialized) return;
            LayersLogger.Log("App foregrounded");
            // Trigger a remote config refresh on foreground to pick up changes
            _configPoller?.FetchNow();
        }

        internal static void OnReconnected()
        {
            if (!_isInitialized) return;
            LayersLogger.Log("Network reconnected, flushing...");
            _flushManager?.FlushNow();
        }

        internal static void OnQuitting()
        {
            Shutdown();
        }

        // ── Private Helpers ──────────────────────────────────────────────

        private static bool CheckInitialized(string method)
        {
            if (_isInitialized) return true;
            RaiseError(method, "Layers SDK not initialized. Call Layers.Initialize() first.");
            return false;
        }

        private static void RaiseError(string method, string message)
        {
            LayersLogger.Error($"[{method}] {message}");
            try
            {
                OnError?.Invoke(method, message);
            }
            catch (Exception e)
            {
                // Never let a consumer's error handler crash the SDK
                LayersLogger.Warn($"OnError handler threw: {e.Message}");
            }
        }

        // ── Attribution Signals ──────────────────────────────────────────

        /// <summary>
        /// Collect AdServices token (iOS), clipboard URL (if enabled by remote config),
        /// timezone, and first-launch flag, then fire an <c>app_open</c> event with
        /// attribution signals as properties (unless AutoTrackAppOpen is false).
        ///
        /// Mirrors the Swift SDK's <c>trackAttributionSignals</c> pattern.
        /// </summary>
        private static void TrackAttributionSignals(LayersConfig config)
        {
            if (!config.AutoTrackAppOpen)
                return;

            var props = new Dictionary<string, object>
            {
                ["timezone"] = System.TimeZoneInfo.Local.Id,
                ["is_first_launch"] = IsFirstLaunch()
            };

            // AdServices token (iOS only, does not require ATT consent)
#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                string adServicesToken = AdServicesModule.GetToken();
                if (!string.IsNullOrEmpty(adServicesToken))
                    props["adservices_token"] = adServicesToken;
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"AdServices token collection failed: {e.Message}");
            }
#endif

            // Clipboard attribution (gated by remote config)
            try
            {
                bool clipboardEnabled = false;
                string remoteConfigJson = RemoteConfig;
                if (!string.IsNullOrEmpty(remoteConfigJson))
                {
                    var remoteConfigDict = JsonHelper.Deserialize(remoteConfigJson);
                    if (remoteConfigDict != null &&
                        remoteConfigDict.TryGetValue("clipboard_attribution_enabled", out object val))
                    {
                        if (val is bool b)
                            clipboardEnabled = b;
                    }
                }

                if (clipboardEnabled)
                {
                    var clipboardData = ClipboardAttribution.Check();
                    if (clipboardData != null)
                    {
                        props["clipboard_attribution_url"] = clipboardData.ClickUrl;
                        if (!string.IsNullOrEmpty(clipboardData.ClickId))
                            props["clipboard_click_id"] = clipboardData.ClickId;
                    }
                }
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"Clipboard attribution check failed: {e.Message}");
            }

            Track("app_open", props);
        }

        /// <summary>
        /// Determine if this is the first launch by checking a PlayerPrefs flag.
        /// The flag is set on first call and persists across app restarts.
        /// </summary>
        private static bool IsFirstLaunch()
        {
            const string firstLaunchKey = "layers_first_launch_tracked";
            if (PlayerPrefs.GetInt(firstLaunchKey, 0) == 1)
                return false;

            PlayerPrefs.SetInt(firstLaunchKey, 1);
            PlayerPrefs.Save();
            return true;
        }

        // ── Platform-specific Init ───────────────────────────────────────

#if UNITY_IOS && !UNITY_EDITOR
        private static void InitIOSModules()
        {
            // Collect IDFV immediately (always available, no consent required)
            string idfv = ATTModule.GetVendorId();
            if (!string.IsNullOrEmpty(idfv))
            {
                var ctx = new Dictionary<string, object> { ["idfv"] = idfv };
                NativeStringHelper.ProcessResult(
                    NativeBindings.layers_set_device_context(JsonHelper.Serialize(ctx)));
            }
        }

        /// <summary>
        /// Called when remote config is successfully fetched. Reads the "skan"
        /// section and auto-configures the SKAN module with presets or custom
        /// rules. Mirrors the Swift SDK's <c>configureSkanFromRemoteConfig</c>.
        /// </summary>
        private static void OnRemoteConfigUpdated(string configJson)
        {
            try
            {
                SKANModule.ConfigureFromRemoteConfig(configJson);
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"SKAN auto-config failed: {e.Message}");
            }
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        private static void InitAndroidModules()
        {
            // Fetch GAID asynchronously (requires background thread on Android)
            AndroidModule.GetAdvertisingId((gaid, isLimitAdTracking) =>
            {
                if (!string.IsNullOrEmpty(gaid))
                {
                    var ctx = new Dictionary<string, object>
                    {
                        ["idfa"] = gaid,
                        ["att_status"] = isLimitAdTracking ? "denied" : "authorized"
                    };
                    NativeStringHelper.ProcessResult(
                        NativeBindings.layers_set_device_context(JsonHelper.Serialize(ctx)));

                    if (_config != null && _config.EnableDebug)
                    {
                        LayersLogger.Log(
                            $"GAID set: {(gaid.Length > 8 ? gaid.Substring(0, 8) : gaid)}..., LAT={isLimitAdTracking}");
                    }
                }
            });

            // Fetch install referrer (one-time, persisted via SharedPreferences)
            AndroidModule.GetInstallReferrer(result =>
            {
                if (result != null && _isInitialized)
                {
                    var props = result.ToEventProperties();
                    string propsJson = JsonHelper.Serialize(props);
                    NativeStringHelper.ProcessResult(
                        NativeBindings.layers_track("install_referrer", propsJson));

                    if (_config != null && _config.EnableDebug)
                    {
                        LayersLogger.Log($"Install referrer tracked: {result.RawReferrer}");
                    }
                }
            });
        }
#endif
    }
}

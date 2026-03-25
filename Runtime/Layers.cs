using System;
using System.Collections.Generic;
using System.Diagnostics;
using Layers.Unity.Internal;
using UnityEngine;

namespace Layers.Unity
{
    /// <summary>
    /// Main public API for the Layers Unity SDK.
    ///
    /// Static singleton facade that delegates all analytics logic to the Rust core
    /// via <see cref="ILayersPlatform"/>. On native targets (iOS, Android, desktop),
    /// this uses P/Invoke to the Rust FFI C ABI. On WebGL, it uses
    /// [DllImport("__Internal")] to call a jslib bridge which loads the Rust WASM
    /// binary. Platform-specific modules (ATT, SKAN, deep links, Android
    /// GAID/install referrer, WebGL CAPI) are initialized automatically based on
    /// the target platform.
    ///
    /// Usage:
    /// <code>
    /// LayersSDK.Initialize(new LayersConfig { AppId = "your-app-id" });
    /// LayersSDK.Track("button_clicked", new Dictionary&lt;string, object&gt; { ["button"] = "signup" });
    /// LayersSDK.Identify("user-123");
    /// LayersSDK.Flush();
    /// </code>
    ///
    /// Lifecycle is managed automatically via <see cref="LayersRunner"/>:
    /// - Background: flushes queued events
    /// - Foreground: resumes periodic flush
    /// - Quit: synchronous shutdown with persistence
    /// </summary>
    public static class LayersSDK
    {
        // ── Constants ────────────────────────────────────────────────────

        private const string SdkVersion = "0.1.0";

        // ── State ────────────────────────────────────────────────────────

        private static bool _isInitialized;
        private static LayersConfig _config;
        private static FlushManager _flushManager;
        private static RemoteConfigPoller _configPoller;
        private static string _userId;
        private static ILayersPlatform _platform;

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
                if (!_isInitialized || _platform == null) return null;
                return _platform.GetSessionId();
            }
        }

        /// <summary>
        /// The number of events currently waiting in the outbound queue.
        /// Returns -1 if the SDK has not been initialized.
        /// </summary>
        public static int QueueDepth
        {
            get
            {
                if (_platform == null) return -1;
                return _platform.QueueDepth();
            }
        }

        /// <summary>
        /// The latest remote config JSON fetched from the server, or null if
        /// the SDK has not been initialized or no config has been fetched yet.
        /// </summary>
        public static string RemoteConfig
        {
            get
            {
                if (!_isInitialized || _platform == null) return null;
                return _platform.GetRemoteConfigJson();
            }
        }

        // ── Internal Accessors (used by DebugOverlay) ────────────────────

        /// <summary>
        /// The current environment setting, or null if not initialized.
        /// </summary>
        internal static string Environment =>
            _config?.Environment.ToString().ToLowerInvariant();

        /// <summary>
        /// The configured app ID, or null if not initialized.
        /// </summary>
        internal static string AppId => _config?.AppId;

        /// <summary>
        /// The current SDK configuration, or null if not initialized.
        /// </summary>
        internal static LayersConfig Config => _config;

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

            // Snapshot whether an install_id already exists before DeviceInfoCollector
            // creates one. This is needed by InstallEventGate to distinguish a genuine
            // first launch from an existing app that just added the SDK.
            InstallEventGate.CapturePreInitState();

            // Select the correct platform implementation
            _platform = LayersPlatformFactory.Create();

            // Measure initialization time
            var initStopwatch = Stopwatch.StartNew();

            // Build config JSON for the Rust core
            var configDict = new Dictionary<string, object>
            {
                ["app_id"] = config.AppId,
                ["environment"] = config.Environment.ToString().ToLowerInvariant(),
                ["sdk_version"] = $"unity/{SdkVersion}",
                ["enable_debug"] = config.EnableDebug,
                ["flush_interval_ms"] = config.FlushIntervalMs,
                ["flush_threshold"] = config.FlushThreshold,
                ["max_queue_size"] = config.MaxQueueSize,
                ["max_batch_size"] = config.MaxBatchSize
            };

            // Native platforms use file-based persistence; WebGL uses localStorage via jslib
#if !UNITY_WEBGL || UNITY_EDITOR
            configDict["persistence_dir"] = Application.persistentDataPath;
#endif

            if (!string.IsNullOrEmpty(config.BaseUrl))
                configDict["base_url"] = config.BaseUrl;

            string configJson = JsonHelper.Serialize(configDict);
            string error = _platform.Init(configJson);
            if (error != null)
            {
                RaiseError("Initialize", error);
                return;
            }

            _isInitialized = true;

            // Set device context (platform, os_version, device_model, etc.)
#if UNITY_WEBGL && !UNITY_EDITOR
            var deviceInfo = WebGLDeviceInfoCollector.Collect();
#else
            var deviceInfo = DeviceInfoCollector.Collect();
#endif
            _platform.SetDeviceContext(JsonHelper.Serialize(deviceInfo));

            // Create the runner singleton (hosts coroutines + lifecycle hooks)
            var runner = LayersRunner.Instance;

            // On WebGL, the jslib manages its own flush timer, lifecycle listeners,
            // and HTTP delivery via fetch/sendBeacon. FlushManager uses NativeBindings
            // (P/Invoke) which is not available on WebGL, so skip it entirely.
#if !UNITY_WEBGL || UNITY_EDITOR
            // Native: start the coroutine-based periodic flush with UnityWebRequest
            _flushManager = new FlushManager(runner, (uint)config.MaxBatchSize);
            _flushManager.StartPeriodicFlush(config.FlushIntervalMs / 1000f);
#endif

            // Start remote config polling (default 5 minute interval).
            // On WebGL, the jslib handles config polling via fetch (UnityWebRequest
            // is not available in WebGL builds). On native, use the coroutine-based poller.
#if UNITY_WEBGL && !UNITY_EDITOR
            // jslib handles config polling — see LayersWebGL_StartConfigPolling
#else
            string baseUrl = !string.IsNullOrEmpty(config.BaseUrl)
                ? config.BaseUrl
                : "https://in.layers.com";
            _configPoller = new RemoteConfigPoller(runner, baseUrl, config.AppId);

            // Subscribe to config updates for SKAN auto-config (iOS only)
#if UNITY_IOS && !UNITY_EDITOR
            _configPoller.OnConfigUpdated += OnRemoteConfigUpdated;
#endif

            _configPoller.StartPolling(300f);
#endif

            // Initialize deep links module.
            // On WebGL, the jslib handles deep link tracking via popstate/hashchange
            // listeners and fires deep_link_opened events directly through the WASM
            // core. DeepLinksModule uses NativeBindings (P/Invoke to layers_core)
            // which is not available on WebGL, so skip it entirely.
#if !UNITY_WEBGL || UNITY_EDITOR
            if (config.AutoTrackDeepLinks)
                DeepLinksModule.Init(true, config.EnableDebug);
            else
                DeepLinksModule.Init(false, config.EnableDebug);
#endif

            // Restore persisted attribution data (deeplink_id, gclid) BEFORE firing
            // app_open so that the Rust core's DeviceContext includes them in the
            // first event.
            RestoreAttributionData();

            // Collect attribution signals and fire app_open with them (if enabled).
            TrackAttributionSignals(config);

            // Platform-specific initialization
#if UNITY_IOS && !UNITY_EDITOR
            InitIOSModules();
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
            InitAndroidModules();
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
            InitWebGLModules();
#endif

            // Record init timing
            initStopwatch.Stop();
            long initDurationMs = initStopwatch.ElapsedMilliseconds;
            Track("layers_init_timing", new Dictionary<string, object>
            {
                ["duration_ms"] = initDurationMs
            });

            LayersLogger.Log($"Layers SDK initialized in {initDurationMs}ms (appId={config.AppId}, env={config.Environment})");
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

            // Queue depth gating: verify the Rust core actually accepted the event.
            // Skipped on WebGL because the jslib may buffer events in the pre-init
            // queue before WASM is ready, making QueueDepth() unreliable.
#if !UNITY_WEBGL || UNITY_EDITOR
            int depthBefore = _platform.QueueDepth();
#endif

            string error = _platform.Track(eventName, propsJson);

            if (error != null)
            {
                RaiseError("Track", error);
                return;
            }

#if !UNITY_WEBGL || UNITY_EDITOR
            int depthAfter = _platform.QueueDepth();
            if (depthAfter <= depthBefore)
            {
                LayersLogger.Warn(
                    $"Event '{eventName}' was not accepted by the core (queue depth {depthBefore} -> {depthAfter}). " +
                    "It may have been filtered by sampling, rate limiting, or consent.");
            }
#endif

            // Record event in debug overlay (if visible)
            if (_debugOverlay != null)
                DebugOverlay.RecordEvent(eventName, properties?.Count ?? 0);

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

            // Queue depth gating: verify the Rust core actually accepted the event.
            // Skipped on WebGL because the jslib may buffer events in the pre-init
            // queue before WASM is ready, making QueueDepth() unreliable.
#if !UNITY_WEBGL || UNITY_EDITOR
            int depthBefore = _platform.QueueDepth();
#endif

            string error = _platform.Screen(screenName, propsJson);

            if (error != null)
            {
                RaiseError("Screen", error);
                return;
            }

#if !UNITY_WEBGL || UNITY_EDITOR
            int depthAfter = _platform.QueueDepth();
            if (depthAfter <= depthBefore)
            {
                LayersLogger.Warn(
                    $"Screen '{screenName}' was not accepted by the core (queue depth {depthBefore} -> {depthAfter}). " +
                    "It may have been filtered by sampling, rate limiting, or consent.");
            }
#endif
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

            string error = _platform.Identify(userId);

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
            string error = _platform.SetUserProperties(json);

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
            string error = _platform.SetUserPropertiesOnce(json);

            if (error != null)
                RaiseError("SetUserPropertiesOnce", error);
        }

        // ── Group ────────────────────────────────────────────────────

        /// <summary>
        /// Associate subsequent events with a group (company, team, organization).
        /// Group properties are upserted and attached to the user-group relationship.
        /// </summary>
        /// <param name="groupId">The group identifier. Must not be null or empty.</param>
        /// <param name="properties">Optional group properties (e.g. name, plan, industry).</param>
        public static void Group(string groupId, Dictionary<string, object> properties = null)
        {
            if (!CheckInitialized("Group")) return;

            if (string.IsNullOrEmpty(groupId))
            {
                RaiseError("Group", "groupId must not be null or empty");
                return;
            }

            string propsJson = properties != null ? JsonHelper.Serialize(properties) : null;
            string error = _platform.Group(groupId, propsJson);

            if (error != null)
                RaiseError("Group", error);
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
            string error = _platform.SetConsent(json);

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
#if UNITY_WEBGL && !UNITY_EDITOR
            // On WebGL, delegate directly to the jslib which handles fetch internally
            _platform?.Flush();
#else
            _flushManager?.FlushNow();
#endif
        }

        /// <summary>
        /// Trigger an immediate flush with a completion callback. Used internally
        /// by <see cref="BackgroundFlush"/> to signal the native plugin only after
        /// the HTTP flush has actually completed.
        /// </summary>
        internal static void FlushWithCallback(Action onComplete)
        {
            if (!CheckInitialized("FlushWithCallback"))
            {
                onComplete?.Invoke();
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL flush is fire-and-forget from the jslib side
            _platform?.Flush();
            onComplete?.Invoke();
#else
            if (_flushManager != null)
                _flushManager.FlushWithCallback(onComplete);
            else
                onComplete?.Invoke();
#endif
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
            Flush();

            // Clear identity on the Rust core
            _platform.Identify("");
            _platform.SetUserProperties("{}");

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

#if !UNITY_WEBGL || UNITY_EDITOR
            _flushManager?.StopPeriodicFlush();
            _flushManager?.FlushBlocking();
#endif

#if UNITY_IOS && !UNITY_EDITOR
            if (_configPoller != null)
                _configPoller.OnConfigUpdated -= OnRemoteConfigUpdated;
            SKANModule.ResetAutoConfig();
#endif

            _configPoller?.StopPolling();
#if !UNITY_WEBGL || UNITY_EDITOR
            DeepLinksModule.Teardown();
#endif

            // Destroy debug overlay if visible
            if (_debugOverlay != null)
            {
                UnityEngine.Object.Destroy(_debugOverlay);
                _debugOverlay = null;
            }
            DebugOverlay.ResetState();

            _platform?.Shutdown();

            _isInitialized = false;
            _flushManager = null;
            _configPoller = null;
            _userId = null;
            _config = null;
            _platform = null;

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
        /// The callback receives the resulting <see cref="LayersATTStatus"/>.
        /// On non-iOS platforms, the callback receives <see cref="LayersATTStatus.NotDetermined"/>.
        /// </summary>
        /// <param name="callback">Called with the ATT status after the user responds.</param>
        public static void RequestTrackingPermission(Action<LayersATTStatus> callback = null)
        {
            if (!CheckInitialized("RequestTrackingPermission")) return;

            ATTModule.RequestTracking(status =>
            {
                // Collect IDFV unconditionally (first-party identifier, no consent required)
                string idfv = ATTModule.GetVendorId();

                // Collect IDFA only when authorized
                string idfa = null;
                if (status == LayersATTStatus.Authorized)
                    idfa = ATTModule.GetAdvertisingId();

                // Update device context with identifiers
                if (idfa != null || idfv != null)
                {
                    var ctx = new Dictionary<string, object>();
                    if (idfa != null) ctx["idfa"] = idfa;
                    if (idfv != null) ctx["idfv"] = idfv;
                    ctx["att_status"] = status.ToString().ToLowerInvariant();
                    _platform?.SetDeviceContext(JsonHelper.Serialize(ctx));
                }

                // Auto-set advertising consent based on ATT result
                bool advertisingAllowed = status == LayersATTStatus.Authorized;
                SetConsent(advertising: advertisingAllowed);

                LayersLogger.Log(
                    $"ATT status: {status}, advertising consent: {advertisingAllowed}");

                callback?.Invoke(status);
            });
        }

        // ── Attribution Data ─────────────────────────────────────────

        /// <summary>
        /// Store attribution data that will be included in every subsequent event
        /// via the Rust core's DeviceContext. Values are persisted in PlayerPrefs
        /// so they survive app restarts.
        ///
        /// Pass null for a parameter to clear that value.
        ///
        /// <c>deeplink_id</c> and <c>gclid</c> flow through DeviceContext on the
        /// Rust core (top-level event fields), not the properties bag.
        /// </summary>
        /// <param name="deeplinkId">Deep link identifier for server-side attribution matching.</param>
        /// <param name="gclid">Google Click Identifier from ad click URLs.</param>
        public static void SetAttributionData(string deeplinkId = null, string gclid = null)
        {
            if (!CheckInitialized("SetAttributionData")) return;

            // Update the Rust core's DeviceContext with the attribution fields.
            // When both params are null, explicitly clear the fields in DeviceContext
            // by sending empty strings so the Rust core removes them.
            var ctx = new Dictionary<string, object>();
            if (deeplinkId != null)
                ctx["deeplink_id"] = deeplinkId;
            if (gclid != null)
                ctx["gclid"] = gclid;

            if (ctx.Count > 0)
            {
                _platform.SetDeviceContext(JsonHelper.Serialize(ctx));
            }
            else
            {
                // Both params null — clear attribution from DeviceContext
                ctx["deeplink_id"] = "";
                ctx["gclid"] = "";
                _platform.SetDeviceContext(JsonHelper.Serialize(ctx));
            }

            // Persist to PlayerPrefs for restore across app restarts
            const string deeplinkIdKey = "layers_attribution_deeplink_id";
            const string gclidKey = "layers_attribution_gclid";

            if (deeplinkId != null)
                PlayerPrefs.SetString(deeplinkIdKey, deeplinkId);
            else
                PlayerPrefs.DeleteKey(deeplinkIdKey);

            if (gclid != null)
                PlayerPrefs.SetString(gclidKey, gclid);
            else
                PlayerPrefs.DeleteKey(gclidKey);

            PlayerPrefs.Save();

            LayersLogger.Log(
                $"SetAttributionData(deeplinkId={deeplinkId ?? "null"}, gclid={gclid ?? "null"})");
        }

        /// <summary>
        /// Restore persisted attribution data from PlayerPrefs.
        /// Called during initialization to survive app restarts.
        /// </summary>
        private static void RestoreAttributionData()
        {
            const string deeplinkIdKey = "layers_attribution_deeplink_id";
            const string gclidKey = "layers_attribution_gclid";

            string deeplinkId = PlayerPrefs.GetString(deeplinkIdKey, null);
            string gclid = PlayerPrefs.GetString(gclidKey, null);

            if (string.IsNullOrEmpty(deeplinkId)) deeplinkId = null;
            if (string.IsNullOrEmpty(gclid)) gclid = null;

            if (deeplinkId != null || gclid != null)
            {
                var ctx = new Dictionary<string, object>();
                if (deeplinkId != null) ctx["deeplink_id"] = deeplinkId;
                if (gclid != null) ctx["gclid"] = gclid;
                _platform.SetDeviceContext(JsonHelper.Serialize(ctx));

                LayersLogger.Log(
                    $"Restored attribution data: deeplinkId={deeplinkId ?? "null"}, gclid={gclid ?? "null"}");
            }
        }

        // ── Debug Overlay ────────────────────────────────────────────────

        private static DebugOverlay _debugOverlay;

        /// <summary>
        /// Show the IMGUI debug overlay. Displays real-time SDK state including
        /// queue depth, session ID, install ID, app ID, environment, consent,
        /// recent events, and a "Flush Now" button.
        ///
        /// The overlay is draggable and auto-refreshes every 1.5 seconds.
        /// </summary>
        public static void ShowDebugOverlay()
        {
            if (_debugOverlay != null) return;

            var runner = LayersRunner.Instance;
            _debugOverlay = runner.gameObject.AddComponent<DebugOverlay>();
            LayersLogger.Log("Debug overlay shown");
        }

        /// <summary>
        /// Hide the IMGUI debug overlay.
        /// Safe to call even if the overlay is not currently shown.
        /// </summary>
        public static void HideDebugOverlay()
        {
            if (_debugOverlay != null)
            {
                UnityEngine.Object.Destroy(_debugOverlay);
                _debugOverlay = null;
                LayersLogger.Log("Debug overlay hidden");
            }
        }

        /// <summary>
        /// Whether the debug overlay is currently visible.
        /// </summary>
        public static bool IsDebugOverlayVisible => _debugOverlay != null;

        // ── Background Flush ────────────────────────────────────────────

        /// <summary>
        /// Enable periodic background flush using platform-specific APIs.
        ///
        /// On iOS, schedules a <c>BGAppRefreshTask</c> (requires Info.plist setup).
        /// On Android, enqueues a periodic WorkManager job.
        /// The minimum interval is 15 minutes on both platforms.
        ///
        /// Returns <c>true</c> if scheduling succeeded.
        /// </summary>
        public static bool EnableBackgroundFlush()
        {
            if (!CheckInitialized("EnableBackgroundFlush")) return false;
            BackgroundFlush.EnsureReceiverExists();
            return BackgroundFlush.Enable();
        }

        /// <summary>
        /// Disable periodic background flush.
        /// Safe to call even if background flush was never enabled.
        /// </summary>
        public static void DisableBackgroundFlush()
        {
            BackgroundFlush.Disable();
        }

        /// <summary>
        /// Whether background flush is currently enabled.
        /// </summary>
        public static bool IsBackgroundFlushEnabled => BackgroundFlush.IsEnabled;

        // ── Internal Lifecycle Callbacks (called by LayersRunner) ─────────

        internal static void OnBackgrounded()
        {
            if (!_isInitialized) return;
            LayersLogger.Log("App backgrounded, flushing...");
#if UNITY_WEBGL && !UNITY_EDITOR
            _platform?.Flush();
#else
            _flushManager?.FlushNow();
#endif
        }

        internal static void OnForegrounded()
        {
            if (!_isInitialized) return;
            LayersLogger.Log("App foregrounded");
            // Trigger a remote config refresh on foreground to pick up changes
#if !UNITY_WEBGL || UNITY_EDITOR
            _configPoller?.FetchNow();
#endif
        }

        internal static void OnReconnected()
        {
            if (!_isInitialized) return;
            LayersLogger.Log("Network reconnected, flushing...");
#if UNITY_WEBGL && !UNITY_EDITOR
            _platform?.Flush();
#else
            _flushManager?.FlushNow();
#endif
        }

        internal static void OnQuitting()
        {
            Shutdown();
        }

        // ── Private Helpers ──────────────────────────────────────────────

        private static bool CheckInitialized(string method)
        {
            if (_isInitialized) return true;
            RaiseError(method, "Layers SDK not initialized. Call LayersSDK.Initialize() first.");
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

            // WebGL CAPI properties and URL attribution
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                var webgl = _platform as WebGLPlatform;
                if (webgl != null)
                {
                    // CAPI properties (_fbp, _ttp, page URL, fbc)
                    string fbp = webgl.GetFbpCookie();
                    if (!string.IsNullOrEmpty(fbp)) props["$fbp"] = fbp;

                    string ttp = webgl.GetTtpCookie();
                    if (!string.IsNullOrEmpty(ttp)) props["$ttp"] = ttp;

                    string pageUrl = webgl.GetPageUrl();
                    if (!string.IsNullOrEmpty(pageUrl)) props["$page_url"] = pageUrl;

                    string fbc = webgl.GetFbc();
                    if (!string.IsNullOrEmpty(fbc)) props["$fbc"] = fbc;

                    // URL attribution parameters (fbclid, gclid, utm_*, referrer)
                    string urlParamsJson = webgl.GetUrlParameters();
                    if (!string.IsNullOrEmpty(urlParamsJson))
                    {
                        var urlParams = JsonHelper.Deserialize(urlParamsJson);
                        if (urlParams != null)
                        {
                            foreach (var kv in urlParams)
                            {
                                string key = "$attribution_" + kv.Key;
                                props[key] = kv.Value;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"WebGL CAPI/attribution capture failed: {e.Message}");
            }
#endif

            // Clipboard attribution (gated by remote config, not available on WebGL)
#if !UNITY_WEBGL || UNITY_EDITOR
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
#endif

            Track("app_open", props);
        }

        /// <summary>
        /// Determine if this is the first launch using the install event gate.
        /// Applies 24-hour gating to suppress false first-launch events when
        /// the SDK is added to an existing app.
        /// </summary>
        private static bool IsFirstLaunch()
        {
            return InstallEventGate.DetermineIsFirstLaunch();
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
                _platform.SetDeviceContext(JsonHelper.Serialize(ctx));
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
                    _platform.SetDeviceContext(JsonHelper.Serialize(ctx));

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
                    _platform.Track("install_referrer", propsJson);

                    if (_config != null && _config.EnableDebug)
                    {
                        LayersLogger.Log($"Install referrer tracked: {result.RawReferrer}");
                    }
                }
            });
        }
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>
        /// WebGL-specific initialization: the jslib handles lifecycle listeners
        /// (visibilitychange, beforeunload, online/offline), periodic flush,
        /// and HTTP delivery via fetch/sendBeacon internally.
        ///
        /// CAPI properties and URL attribution are captured during
        /// TrackAttributionSignals and merged into the app_open event.
        ///
        /// Deep link tracking on WebGL:
        /// - The jslib fires a <c>deep_link_opened</c> event on init when the page
        ///   URL contains attribution params (fbclid, gclid, ttclid, utm_*, etc.),
        ///   matching iOS/Android DeepLinksModule cold-start behavior.
        /// - The jslib also listens for <c>popstate</c> and <c>hashchange</c> events
        ///   to detect SPA navigation, firing <c>deep_link_opened</c> when the new
        ///   URL contains attribution params. A 2-second deduplication window
        ///   prevents the same URL from firing twice.
        /// </summary>
        private static void InitWebGLModules()
        {
            LayersLogger.Log("WebGL platform initialized (jslib manages lifecycle, HTTP, and SPA deep link tracking)");
        }
#endif
    }
}


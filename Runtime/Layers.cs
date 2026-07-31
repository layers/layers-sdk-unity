using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Layers.Unity.Internal;
using UnityEngine;
using UnityEngine.Networking;

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

        // Kept in sync with package.json by the release pipeline's version
        // injection (release.yml) and verified by scripts/check-versions.
        internal const string SdkVersion = "3.2.1";

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
        /// Per-device DebugView token, or null if `Debug` was not enabled on
        /// the <see cref="LayersConfig"/> passed to <see cref="Initialize"/>.
        /// </summary>
        /// <remarks>
        /// When debug mode is on, the Rust core mints a stable UUID, persists
        /// it alongside the identity record, and includes it in the
        /// <c>X-Debug-Token</c> header on every request. Surface the token in
        /// dev UIs so operators can filter the dashboard's live tail to
        /// events from this device only.
        /// </remarks>
        public static string GetDebugToken()
        {
            if (!_isInitialized || _platform == null) return null;
            return _platform.GetDebugToken();
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
                // DebugView per-device toggle (sends X-Debug-Token header).
                ["debug"] = config.Debug,
                ["flush_interval_ms"] = config.FlushIntervalMs,
                ["flush_threshold"] = config.FlushThreshold,
                ["max_queue_size"] = config.MaxQueueSize,
                ["max_batch_size"] = config.MaxBatchSize,
                // Tier 5/6 — read by LayersWebGL.jslib to install
                // window.error / window.unhandledrejection / nav-timing
                // listeners on the JS side. Native platforms ignore these
                // keys; the C# ExceptionModule / PerformanceModule install
                // hooks check the LayersConfig fields directly.
                ["auto_track_exceptions"] = config.AutoTrackExceptions,
                ["auto_track_performance"] = config.AutoTrackPerformance
            };

            // Native platforms use file-based persistence; WebGL uses localStorage via jslib
#if !UNITY_WEBGL || UNITY_EDITOR
            configDict["persistence_dir"] = Application.persistentDataPath;
#endif

            if (!string.IsNullOrEmpty(config.BaseUrl))
                configDict["base_url"] = config.BaseUrl;

            string configJson = JsonHelper.Serialize(configDict);
            string error;
            try
            {
                error = _platform.Init(configJson);
            }
            catch (DllNotFoundException)
            {
                // No native layers_core library for this platform — the
                // common case is Editor play mode without a desktop dylib.
                // Never crash the host: report through OnError and abort
                // initialization cleanly. (Use LayersTestMode.Enable() in
                // tests, or build a desktop library for Editor play mode.)
                RaiseError(
                    "Initialize",
                    "Native layers_core library not found for this platform. "
                        + "In the Unity Editor this is expected unless a desktop build of "
                        + "the Rust core is installed; the SDK is disabled for this session. "
                        + "Use LayersTestMode.Enable() for play-mode testing without the native library.");
                return;
            }
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
            // Tier 4: notify feature-flag listeners after every successful
            // remote config update — the Rust core swaps in fresh
            // definitions inside _platform.UpdateRemoteConfig (which runs
            // before this callback fires), so calling GetAllFlags() in the
            // listener observes the new state.
            _configPoller.OnConfigUpdated += _ => NotifyFeatureFlagListeners();

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

            // Tier 4: apply SSR-style feature flag bootstrap (if provided)
            // BEFORE any user code reads flags. This must happen before
            // TrackAttributionSignals so app_open's properties can branch
            // on a flag value if the host has wired that.
            if (config.FeatureFlagBootstrap != null)
            {
                string bootstrapJson = SerializeBootstrap(config.FeatureFlagBootstrap);
                string bootstrapErr = _platform.SetFeatureFlagBootstrap(bootstrapJson);
                if (bootstrapErr != null)
                    LayersLogger.Warn($"FeatureFlagBootstrap apply failed: {bootstrapErr}");
            }

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

            // Tier 5: install exception auto-capture (no-op on WebGL — the
            // jslib bridges window.onerror / window.onunhandledrejection and
            // emits $exception through the WASM core directly).
#if !UNITY_WEBGL || UNITY_EDITOR
            if (config.AutoTrackExceptions)
                ExceptionModule.Install(config.CaptureLogErrors, config.EnableDebug);
#endif

            // Tier 6: install performance auto-capture. Same WebGL caveat —
            // the jslib emits navigation-timing-derived $performance events.
#if !UNITY_WEBGL || UNITY_EDITOR
            if (config.AutoTrackPerformance)
            {
                PerformanceModule.Install(config.PerformanceFrameSamplingIntervalSec);
                PerformanceModule.EmitAppStartIfNeeded();
            }
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

            // Merge stored click IDs into event properties so attribution
            // data flows through to the server (matching Kotlin/Swift SDKs).
            var merged = MergeAttributionProperties(properties);
            string propsJson = merged != null ? JsonHelper.Serialize(merged) : null;

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

            // Merge stored click IDs into event properties so attribution
            // data flows through to the server (matching Kotlin/Swift SDKs).
            var merged = MergeAttributionProperties(properties);
            string propsJson = merged != null ? JsonHelper.Serialize(merged) : null;

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

            // Forward as a screen_view against SKAN rules (iOS only). Preset rules
            // key off screen_name, matching the native iOS / Flutter / RN wrappers.
#if UNITY_IOS && !UNITY_EDITOR
            if (SKANModule.IsAutoConfigured)
            {
                var skanProps = merged != null
                    ? new Dictionary<string, object>(merged)
                    : new Dictionary<string, object>();
                if (!skanProps.ContainsKey("screen_name"))
                    skanProps["screen_name"] = screenName;
                SKANModule.ProcessEvent("screen_view", skanProps);
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
            else
                SendUserPropertiesAsync(properties, setOnce: false);
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
            else
                SendUserPropertiesAsync(properties, setOnce: true);
        }

        // ── User Properties HTTP POST ────────────────────────────────

        /// <summary>
        /// Fire-and-forget POST to /users/properties.
        /// Matches the pattern from Web, Node, and React Native SDKs.
        /// Best-effort: errors are logged but not propagated.
        /// </summary>
        private static void SendUserPropertiesAsync(
            Dictionary<string, object> properties, bool setOnce)
        {
            if (_config == null) return;

            string baseUrl = !string.IsNullOrEmpty(_config?.BaseUrl)
                ? _config.BaseUrl.TrimEnd('/')
                : "https://in.layers.com";

            string appUserId = _userId ?? InstallIdProvider.GetOrCreate();

            var payload = new Dictionary<string, object>
            {
                ["app_id"] = _config.AppId,
                ["app_user_id"] = appUserId,
                ["properties"] = properties,
                ["timestamp"] = DateTime.UtcNow.ToString("o")
            };
            if (setOnce)
            {
                payload["set_once"] = true;
            }

            string url = $"{baseUrl}/users/properties";
            string body = JsonHelper.Serialize(payload);

            LayersRunner.Instance.StartCoroutine(PostUserProperties(url, body));
        }

        private static IEnumerator PostUserProperties(string url, string body)
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(body);

            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("X-App-Id", _config.AppId);
                request.SetRequestHeader("X-SDK-Version", $"unity/{SdkVersion}");
                request.timeout = 10;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    LayersLogger.Warn(
                        $"User properties POST failed (HTTP {request.responseCode}): {request.error}");
                }
            }
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

            // Flush pending events before clearing identity. The Rust core
            // also drops in-flight events as part of reset(), so any failure
            // here is best-effort.
            Flush();

            // Delegate to the Rust core. This:
            // - clears `app_user_id`
            // - rotates `device_id` and `anonymous_id`
            // - clears all super-properties (and their once-keys history)
            // - clears multi-group memberships
            // - drops queued events that belonged to the prior identity
            // - resets screen-breadcrumb state and timed-event timers
            string error = _platform.Reset();
            if (error != null)
            {
                RaiseError("Reset", error);
                return;
            }

            _userId = null;
            LayersLogger.Log("User state reset");
        }

        // ── Tier 1: Identity accessors ─────────────────────────────────

        /// <summary>
        /// Stable per-install device identifier minted by the Rust core. Sent
        /// alongside <see cref="UserId"/> after <see cref="Identify"/> so the
        /// server can stitch anonymous and identified activity. Rotated by
        /// <see cref="Reset"/>. Returns null if the SDK is not initialized.
        /// </summary>
        public static string DeviceId
        {
            get
            {
                if (!_isInitialized || _platform == null) return null;
                return _platform.GetDeviceId();
            }
        }

        /// <summary>
        /// Anonymous identifier minted by the Rust core. Used for users who
        /// have not been identified via <see cref="Identify"/>. Rotated by
        /// <see cref="Reset"/>. Returns null if not initialized.
        /// </summary>
        public static string AnonymousId
        {
            get
            {
                if (!_isInitialized || _platform == null) return null;
                return _platform.GetAnonymousId();
            }
        }

        /// <summary>
        /// Monotonically-increasing session counter persisted across launches.
        /// Returns 0 if the SDK is not initialized.
        /// </summary>
        public static uint SessionNumber
        {
            get
            {
                if (!_isInitialized || _platform == null) return 0;
                return _platform.GetSessionNumber();
            }
        }

        /// <summary>
        /// ISO-8601 timestamp of the SDK's first launch on this install.
        /// Null until the SDK has run at least once.
        /// </summary>
        public static string FirstOpenTime
        {
            get
            {
                if (!_isInitialized || _platform == null) return null;
                return _platform.GetFirstOpenTime();
            }
        }

        // ── Tier 1: Super-properties ───────────────────────────────────

        /// <summary>
        /// Register one or more super-properties. Super-properties are merged
        /// into every subsequent <see cref="Track"/> and <see cref="Screen"/>
        /// call. Caller's per-event properties always win on key collisions.
        /// </summary>
        public static void SetSuperProperties(IDictionary<string, object> properties)
        {
            if (!CheckInitialized("SetSuperProperties")) return;
            if (properties == null)
            {
                RaiseError("SetSuperProperties", "properties must not be null");
                return;
            }
            string json = JsonHelper.Serialize(new Dictionary<string, object>(properties));
            string error = _platform.SetSuperProperties(json);
            if (error != null) RaiseError("SetSuperProperties", error);
        }

        /// <summary>
        /// Register super-properties only if they have not been registered
        /// before. Useful for initial-touch attribution snapshots
        /// (initial_referrer, initial_utm_*, initial_gclid, …).
        /// </summary>
        public static void SetSuperPropertiesOnce(IDictionary<string, object> properties)
        {
            if (!CheckInitialized("SetSuperPropertiesOnce")) return;
            if (properties == null)
            {
                RaiseError("SetSuperPropertiesOnce", "properties must not be null");
                return;
            }
            string json = JsonHelper.Serialize(new Dictionary<string, object>(properties));
            string error = _platform.SetSuperPropertiesOnce(json);
            if (error != null) RaiseError("SetSuperPropertiesOnce", error);
        }

        /// <summary>
        /// Remove a single super-property by key. The key remains "once-locked"
        /// (i.e. <see cref="SetSuperPropertiesOnce"/> continues to skip it).
        /// Use <see cref="ClearSuperProperties"/> for a full reset.
        /// </summary>
        public static void UnregisterSuperProperty(string key)
        {
            if (!CheckInitialized("UnregisterSuperProperty")) return;
            if (string.IsNullOrEmpty(key))
            {
                RaiseError("UnregisterSuperProperty", "key must not be null or empty");
                return;
            }
            string error = _platform.UnregisterSuperProperty(key);
            if (error != null) RaiseError("UnregisterSuperProperty", error);
        }

        /// <summary>
        /// Clear ALL super-properties and the once-keys history. After this
        /// call <see cref="SetSuperPropertiesOnce"/> may re-register
        /// previously-locked keys.
        /// </summary>
        public static void ClearSuperProperties()
        {
            if (!CheckInitialized("ClearSuperProperties")) return;
            string error = _platform.ClearSuperProperties();
            if (error != null) RaiseError("ClearSuperProperties", error);
        }

        /// <summary>
        /// Snapshot the registered super-properties. Returns an empty
        /// dictionary if none are registered or the SDK is not initialized.
        /// </summary>
        public static Dictionary<string, object> GetSuperProperties()
        {
            if (!_isInitialized || _platform == null) return new Dictionary<string, object>();
            string json = _platform.GetSuperPropertiesJson();
            if (string.IsNullOrEmpty(json)) return new Dictionary<string, object>();
            try
            {
                return JsonHelper.Deserialize(json) ?? new Dictionary<string, object>();
            }
            catch (Exception)
            {
                return new Dictionary<string, object>();
            }
        }

        // ── Tier 1: Timed events ───────────────────────────────────────

        /// <summary>
        /// Start a duration timer for the next <see cref="Track"/> call with
        /// matching <paramref name="eventName"/>. When that event is tracked,
        /// the elapsed milliseconds are auto-attached as <c>$duration_ms</c>
        /// (Mixpanel-compatible).
        ///
        /// Calling <c>TimeEvent</c> again with the same name resets the timer.
        /// </summary>
        public static void TimeEvent(string eventName)
        {
            if (!CheckInitialized("TimeEvent")) return;
            if (string.IsNullOrEmpty(eventName))
            {
                RaiseError("TimeEvent", "eventName must not be null or empty");
                return;
            }
            string error = _platform.TimeEvent(eventName);
            if (error != null) RaiseError("TimeEvent", error);
        }

        /// <summary>
        /// Cancel a previously-started timer without emitting it. Returns the
        /// elapsed milliseconds if a timer was active, or 0 otherwise.
        /// </summary>
        public static ulong CancelTimedEvent(string eventName)
        {
            if (!CheckInitialized("CancelTimedEvent")) return 0;
            if (string.IsNullOrEmpty(eventName)) return 0;
            return _platform.CancelTimedEvent(eventName);
        }

        // ── Tier 1: Multi-group ────────────────────────────────────────

        /// <summary>
        /// Set the membership for a single <paramref name="groupType"/>,
        /// overwriting any existing value. Subsequent events will carry the
        /// <c>$groups</c> map. Pass an empty <paramref name="groupId"/> to
        /// remove the type.
        /// </summary>
        public static void SetGroup(string groupType, string groupId)
        {
            if (!CheckInitialized("SetGroup")) return;
            if (string.IsNullOrEmpty(groupType))
            {
                RaiseError("SetGroup", "groupType must not be null or empty");
                return;
            }
            string error = _platform.SetGroup(groupType, groupId ?? string.Empty);
            if (error != null) RaiseError("SetGroup", error);
        }

        /// <summary>
        /// Add a group membership without overwriting other types. Equivalent
        /// to <see cref="SetGroup"/>.
        /// </summary>
        public static void AddGroup(string groupType, string groupId)
        {
            if (!CheckInitialized("AddGroup")) return;
            if (string.IsNullOrEmpty(groupType))
            {
                RaiseError("AddGroup", "groupType must not be null or empty");
                return;
            }
            string error = _platform.AddGroup(groupType, groupId ?? string.Empty);
            if (error != null) RaiseError("AddGroup", error);
        }

        /// <summary>
        /// Remove a single <paramref name="groupType"/> from the membership map.
        /// </summary>
        public static void RemoveGroup(string groupType)
        {
            if (!CheckInitialized("RemoveGroup")) return;
            if (string.IsNullOrEmpty(groupType))
            {
                RaiseError("RemoveGroup", "groupType must not be null or empty");
                return;
            }
            string error = _platform.RemoveGroup(groupType);
            if (error != null) RaiseError("RemoveGroup", error);
        }

        /// <summary>
        /// Snapshot the current <c>$groups</c> membership map. Returns an
        /// empty dictionary if not initialized or no memberships are set.
        /// </summary>
        public static Dictionary<string, string> GetGroups()
        {
            if (!_isInitialized || _platform == null) return new Dictionary<string, string>();
            string json = _platform.GetGroupsJson();
            if (string.IsNullOrEmpty(json)) return new Dictionary<string, string>();
            try
            {
                var dict = JsonHelper.Deserialize(json);
                var result = new Dictionary<string, string>();
                if (dict == null) return result;
                foreach (var kv in dict)
                {
                    if (kv.Value is string s) result[kv.Key] = s;
                    else if (kv.Value != null) result[kv.Key] = kv.Value.ToString();
                }
                return result;
            }
            catch (Exception)
            {
                return new Dictionary<string, string>();
            }
        }

        // ── Tier 1: User-property mutators ─────────────────────────────

        /// <summary>
        /// Increment a numeric user property by <paramref name="delta"/>
        /// (negative decrements). Maps to the server-side <c>$add</c> verb.
        /// Non-finite deltas are rejected.
        /// </summary>
        public static void Increment(string key, double delta = 1.0)
        {
            if (!CheckInitialized("Increment")) return;
            if (string.IsNullOrEmpty(key))
            {
                RaiseError("Increment", "key must not be null or empty");
                return;
            }
            string error = _platform.Increment(key, delta);
            if (error != null) RaiseError("Increment", error);
        }

        /// <summary>
        /// Append <paramref name="value"/> to a list-valued user property.
        /// Maps to the server-side <c>$append</c> verb.
        /// </summary>
        public static void Append(string key, object value)
        {
            if (!CheckInitialized("Append")) return;
            if (string.IsNullOrEmpty(key))
            {
                RaiseError("Append", "key must not be null or empty");
                return;
            }
            // JSON-encode the value scalar/object so the FFI bridge can pass
            // it through opaquely.
            string valueJson;
            try
            {
                valueJson = JsonHelper.SerializeAny(value);
            }
            catch (Exception e)
            {
                RaiseError("Append", $"value not JSON-encodable: {e.Message}");
                return;
            }
            string error = _platform.Append(key, valueJson);
            if (error != null) RaiseError("Append", error);
        }

        /// <summary>
        /// Union an array of values into a list-valued user property.
        /// Duplicates are removed server-side. Maps to <c>$union</c>.
        /// </summary>
        public static void Union(string key, IEnumerable<object> values)
        {
            if (!CheckInitialized("Union")) return;
            if (string.IsNullOrEmpty(key))
            {
                RaiseError("Union", "key must not be null or empty");
                return;
            }
            if (values == null)
            {
                RaiseError("Union", "values must not be null");
                return;
            }
            var list = new List<object>(values);
            string valuesJson;
            try
            {
                valuesJson = JsonHelper.SerializeAny(list);
            }
            catch (Exception e)
            {
                RaiseError("Union", $"values not JSON-encodable: {e.Message}");
                return;
            }
            string error = _platform.Union(key, valuesJson);
            if (error != null) RaiseError("Union", error);
        }

        /// <summary>
        /// Remove a user property. Maps to <c>$unset</c>.
        /// </summary>
        public static void Unset(string key)
        {
            if (!CheckInitialized("Unset")) return;
            if (string.IsNullOrEmpty(key))
            {
                RaiseError("Unset", "key must not be null or empty");
                return;
            }
            string error = _platform.Unset(key);
            if (error != null) RaiseError("Unset", error);
        }

        // ── Tier 1: before_send filter hook ────────────────────────────

        /// <summary>
        /// Register a <c>before_send</c> filter callback. The callback receives
        /// every event as a JSON string before it is queued. Return:
        /// - the (possibly modified) JSON string to keep / mutate the event
        /// - <c>null</c> to drop the event
        ///
        /// The callback is invoked from the SDK's flush worker thread on
        /// native targets and must be thread-safe. Callback exceptions are
        /// treated as "drop" (fail-closed).
        ///
        /// Pass <c>null</c> to clear a previously-registered callback.
        ///
        /// Note: this is a no-op on WebGL — the WASM bridge runs all events
        /// through the JS layer, where the filter would have to be installed
        /// on the WASM <c>onBeforeSend</c> hook directly.
        /// </summary>
        public static void SetBeforeSend(Func<string, string> callback)
        {
            if (!CheckInitialized("SetBeforeSend")) return;
            string error = _platform.SetBeforeSend(callback);
            if (error != null) RaiseError("SetBeforeSend", error);
        }

        /// <summary>
        /// Clear a previously-registered <see cref="SetBeforeSend"/> callback.
        /// </summary>
        public static void ClearBeforeSend()
        {
            if (!CheckInitialized("ClearBeforeSend")) return;
            string error = _platform.ClearBeforeSend();
            if (error != null) RaiseError("ClearBeforeSend", error);
        }

        // ── Tier 4: Feature flags ───────────────────────────────────────

        // Local listener registry. Notified after every successful remote
        // config poll (the Rust core swaps in new flag definitions inside
        // _platform.UpdateRemoteConfig) and after every ReloadFeatureFlags
        // call. Listeners run on the main thread (the RemoteConfigPoller
        // coroutine and ReloadFeatureFlags both execute there).
        private static readonly List<Action<Dictionary<string, object>>>
            _featureFlagListeners = new List<Action<Dictionary<string, object>>>();
        private static readonly object _featureFlagListenerLock = new object();

        /// <summary>
        /// Evaluate a feature flag.
        /// <para>Returns:
        /// <list type="bullet">
        ///   <item><see cref="bool"/> for binary flags</item>
        ///   <item><see cref="string"/> (variant key) for multivariate flags</item>
        ///   <item><c>null</c> if the flag is unknown / SDK not initialized</item>
        /// </list></para>
        /// <para>Side effect: emits one <c>$feature_flag_called</c> event
        /// per (flag_key, response) per session for analytics on flag
        /// exposure. Use <see cref="GetFeatureFlagPayload"/> for payload
        /// reads that should NOT count as an exposure.</para>
        /// </summary>
        public static object GetFeatureFlag(string key)
        {
            if (!_isInitialized || _platform == null) return null;
            if (string.IsNullOrEmpty(key))
            {
                RaiseError("GetFeatureFlag", "key must not be null or empty");
                return null;
            }
            string json = _platform.GetFeatureFlagJson(key);
            return ParseFlagValueJson(json);
        }

        /// <summary>
        /// Truthy-check shortcut over <see cref="GetFeatureFlag"/>. Returns
        /// <c>false</c> on errors / missing flags so consumer code can
        /// safely guard with <c>if (LayersSDK.IsFeatureEnabled("x"))</c>.
        /// Emits the same <c>$feature_flag_called</c> exposure event as
        /// <see cref="GetFeatureFlag"/>.
        /// </summary>
        public static bool IsFeatureEnabled(string key)
        {
            if (!_isInitialized || _platform == null) return false;
            if (string.IsNullOrEmpty(key)) return false;
            return _platform.IsFeatureEnabled(key) == 1;
        }

        /// <summary>
        /// Look up the JSON payload attached to a flag (e.g. content for an
        /// in-app message variant). Returns <c>null</c> if the flag has no
        /// payload or is unknown. Does NOT emit an exposure event.
        /// </summary>
        public static object GetFeatureFlagPayload(string key)
        {
            if (!_isInitialized || _platform == null) return null;
            if (string.IsNullOrEmpty(key))
            {
                RaiseError("GetFeatureFlagPayload", "key must not be null or empty");
                return null;
            }
            string json = _platform.GetFeatureFlagPayloadJson(key);
            return ParsePayloadJson(json);
        }

        /// <summary>
        /// Snapshot every known flag's current value as a dictionary. Does
        /// NOT emit exposure events. Use for diagnostic UIs (DebugOverlay,
        /// admin panels) — not for individual flag reads.
        /// </summary>
        public static Dictionary<string, object> GetAllFlags()
        {
            if (!_isInitialized || _platform == null) return new Dictionary<string, object>();
            string json = _platform.GetAllFlagsJson();
            if (string.IsNullOrEmpty(json) || json == "null")
                return new Dictionary<string, object>();
            try
            {
                return JsonHelper.Deserialize(json) ?? new Dictionary<string, object>();
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"GetAllFlags: failed to parse JSON: {e.Message}");
                return new Dictionary<string, object>();
            }
        }

        /// <summary>
        /// Override person properties used during flag evaluation. Useful
        /// when the host app knows attributes the SDK doesn't track yet
        /// (e.g. account tier, custom A/B segment). Clears the exposure
        /// dedup cache so any flag whose response flips re-emits one fresh
        /// <c>$feature_flag_called</c>.
        /// </summary>
        public static void SetPersonPropertiesForFlags(Dictionary<string, object> properties)
        {
            if (!CheckInitialized("SetPersonPropertiesForFlags")) return;
            if (properties == null)
            {
                RaiseError("SetPersonPropertiesForFlags", "properties must not be null");
                return;
            }
            string json = JsonHelper.Serialize(new Dictionary<string, object>(properties));
            string error = _platform.SetPersonPropertiesForFlags(json);
            if (error != null) RaiseError("SetPersonPropertiesForFlags", error);
            else NotifyFeatureFlagListeners(); // properties changed → values may have flipped
        }

        /// <summary>
        /// Drop cached flag definitions and re-fetch on the next
        /// <c>/config</c> poll. Returns <c>true</c> if any flags were
        /// dropped, <c>false</c> if the cache was already empty.
        /// </summary>
        public static bool ReloadFeatureFlags()
        {
            if (!CheckInitialized("ReloadFeatureFlags")) return false;
            int result = _platform.ReloadFeatureFlags();
            if (result < 0)
            {
                RaiseError("ReloadFeatureFlags", "reload failed (see Rust logs)");
                return false;
            }
            NotifyFeatureFlagListeners();
            // Force the poller to refetch immediately rather than waiting
            // for the next 5-minute tick.
#if !UNITY_WEBGL || UNITY_EDITOR
            _configPoller?.FetchNow();
#endif
            return result == 1;
        }

        /// <summary>
        /// Subscribe to flag-state changes. Callback fires after every
        /// <c>/config</c> poll that updated definitions, after every
        /// <see cref="ReloadFeatureFlags"/>, and after
        /// <see cref="SetPersonPropertiesForFlags"/>. The argument is the
        /// new full flag-state snapshot (same shape as
        /// <see cref="GetAllFlags"/>).
        ///
        /// <para>Returns an <see cref="Action"/> that unsubscribes the
        /// callback when invoked. Calling the unsubscribe handle more than
        /// once is a no-op.</para>
        /// </summary>
        public static Action OnFeatureFlags(Action<Dictionary<string, object>> callback)
        {
            if (callback == null)
            {
                RaiseError("OnFeatureFlags", "callback must not be null");
                return () => { };
            }
            lock (_featureFlagListenerLock)
            {
                _featureFlagListeners.Add(callback);
            }
            return () =>
            {
                lock (_featureFlagListenerLock)
                {
                    _featureFlagListeners.Remove(callback);
                }
            };
        }

        /// <summary>
        /// Apply an SSR-style bootstrap to the feature flag engine. Values
        /// take effect immediately and are superseded by server-side
        /// definitions when the next <c>/config</c> poll arrives.
        /// </summary>
        public static void SetFeatureFlagBootstrap(LayersFeatureFlagBootstrap bootstrap)
        {
            if (!CheckInitialized("SetFeatureFlagBootstrap")) return;
            if (bootstrap == null)
            {
                RaiseError("SetFeatureFlagBootstrap", "bootstrap must not be null");
                return;
            }
            string json = SerializeBootstrap(bootstrap);
            string error = _platform.SetFeatureFlagBootstrap(json);
            if (error != null) RaiseError("SetFeatureFlagBootstrap", error);
            else NotifyFeatureFlagListeners();
        }

        // ── Tier 4 helpers (private) ────────────────────────────────────

        /// <summary>Serialize an <see cref="LayersFeatureFlagBootstrap"/>
        /// to the wire JSON shape expected by the Rust core.</summary>
        private static string SerializeBootstrap(LayersFeatureFlagBootstrap b)
        {
            var dict = new Dictionary<string, object>();
            // The Rust BootstrapData fields are #[serde(default)] so we
            // can omit them when empty, but emitting both keys keeps the
            // wire format stable for tests and for human inspection.
            dict["feature_flags"] = b.FeatureFlags ?? new Dictionary<string, object>();
            dict["feature_flag_payloads"] = b.FeatureFlagPayloads ?? new Dictionary<string, object>();
            return JsonHelper.Serialize(dict);
        }

        /// <summary>
        /// Parse the JSON the Rust core returns for a flag value. Possible
        /// shapes: <c>true</c>, <c>false</c>, <c>"variant_key"</c>, <c>null</c>.
        /// Falls back to null on any parse error.
        /// </summary>
        private static object ParseFlagValueJson(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "null") return null;
            // Fast-path: scalar JSON. The minimal JsonHelper only handles
            // objects, so we hand-parse the three legal shapes.
            json = json.Trim();
            if (json == "true") return true;
            if (json == "false") return false;
            if (json.Length >= 2 && json[0] == '"' && json[json.Length - 1] == '"')
            {
                // Strip quotes; assume Rust core emits ASCII keys (PostHog
                // variant keys are slug-style). Falls back to raw on any
                // unexpected escape sequence.
                return json.Substring(1, json.Length - 2);
            }
            return null;
        }

        /// <summary>
        /// Parse the JSON payload returned by <c>GetFeatureFlagPayload</c>.
        /// Payloads can be any JSON value — we only fully decode objects
        /// (via JsonHelper) and pass primitives through as the raw token
        /// string for callers that need them.
        /// </summary>
        private static object ParsePayloadJson(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "null") return null;
            json = json.Trim();
            if (json.Length > 0 && json[0] == '{')
            {
                try { return JsonHelper.Deserialize(json); }
                catch (Exception) { return json; }
            }
            // Scalar / array — return the raw JSON token for the caller to
            // parse with their own deserializer.
            if (json == "true") return true;
            if (json == "false") return false;
            if (json.Length >= 2 && json[0] == '"' && json[json.Length - 1] == '"')
                return json.Substring(1, json.Length - 2);
            // Numbers and arrays come back as the raw JSON string.
            return json;
        }

        /// <summary>
        /// Fan out a flag-state change to every registered listener. Each
        /// callback runs in its own try/catch so a misbehaving consumer
        /// can't break the chain or leak into the SDK's error surface.
        /// </summary>
        private static void NotifyFeatureFlagListeners()
        {
            // Snapshot the listener list under lock so concurrent
            // subscribe/unsubscribe doesn't reshape the list mid-iteration.
            Action<Dictionary<string, object>>[] snapshot;
            lock (_featureFlagListenerLock)
            {
                if (_featureFlagListeners.Count == 0) return;
                snapshot = _featureFlagListeners.ToArray();
            }
            Dictionary<string, object> flags;
            try { flags = GetAllFlags(); }
            catch (Exception e)
            {
                LayersLogger.Warn($"NotifyFeatureFlagListeners: GetAllFlags failed: {e.Message}");
                return;
            }
            foreach (var cb in snapshot)
            {
                try { cb(flags); }
                catch (Exception e)
                {
                    LayersLogger.Warn($"OnFeatureFlags listener threw: {e.Message}");
                }
            }
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
            // Tier 5/6 teardown — no-op on WebGL where the jslib owns the
            // hooks and tears them down via LayersWebGL_Shutdown.
            ExceptionModule.Uninstall();
            PerformanceModule.Uninstall();
#endif

            // Destroy debug overlay if visible
            if (_debugOverlay != null)
            {
                UnityEngine.Object.Destroy(_debugOverlay);
                _debugOverlay = null;
            }
            DebugOverlay.ResetState();

            // Unregister the before_send trampoline BEFORE the core shuts
            // down: if the native library retains the registration across a
            // shutdown/re-init (or an Editor domain reload kills the managed
            // delegate), the next filtered event would invoke a dangling
            // function pointer.
            _platform?.ClearBeforeSend();
            _platform?.Shutdown();

            _isInitialized = false;
            _flushManager = null;
            _configPoller = null;
            _userId = null;
            _deeplinkId = null;
            _gclid = null;
            _fbclid = null;
            _fbc = null;
            _ttclid = null;
            _msclkid = null;
            _config = null;
            _platform = null;

            // Tier 4: clear feature-flag listener registry. Listeners
            // registered before Initialize-Shutdown-Initialize cycles are
            // not preserved across the shutdown — consumers must re-register.
            lock (_featureFlagListenerLock)
            {
                _featureFlagListeners.Clear();
            }

            LayersLogger.Log("Layers SDK shut down");
        }

        // ── Tier 5: Manual exception reporting ──────────────────────────

        /// <summary>
        /// Manually report an exception. Useful for handled errors that you
        /// want to surface as <c>$exception</c> events (set
        /// <paramref name="handled"/> to <c>true</c> for those — the wire
        /// property is <c>$exception_handled</c>).
        ///
        /// <para>Auto-capture is enabled by default and intercepts all
        /// uncaught exceptions; you only need this entry point if you've
        /// disabled <see cref="LayersConfig.AutoTrackExceptions"/> or
        /// want to flag a caught exception explicitly.</para>
        /// </summary>
        public static void TrackException(Exception exception, bool handled = true)
        {
            if (!CheckInitialized("TrackException")) return;
            if (exception == null)
            {
                RaiseError("TrackException", "exception must not be null");
                return;
            }
            ExceptionModule.TrackException(exception, handled);
        }

        // ── Tier 6: Performance traces ──────────────────────────────────

        /// <summary>
        /// Time a synchronous block of work and emit a
        /// <c>$performance_trace</c> event with the elapsed duration.
        ///
        /// <code>
        /// LayersSDK.Trace("load_user_profile", () => {
        ///     // ... work ...
        /// });
        /// </code>
        ///
        /// Exceptions thrown by <paramref name="body"/> are NOT swallowed —
        /// the trace is emitted with an <c>$error</c> metadata flag and the
        /// exception is re-thrown so the host's existing flow control is
        /// preserved.
        /// </summary>
        public static void Trace(string name, Action body)
        {
            if (!CheckInitialized("Trace")) return;
            if (string.IsNullOrEmpty(name))
            {
                RaiseError("Trace", "name must not be null or empty");
                return;
            }
            if (body == null)
            {
                RaiseError("Trace", "body must not be null");
                return;
            }
            var trace = new LayersPerformanceTrace(name);
            try
            {
                body();
            }
            catch (Exception)
            {
                trace.SetMetadata("$error", true);
                trace.Dispose();
                throw;
            }
            trace.Dispose();
        }

        /// <summary>
        /// Start a long-running performance trace. Dispose the returned
        /// handle to stop the timer and emit the
        /// <c>$performance_trace</c> event.
        ///
        /// <code>
        /// using (var trace = LayersSDK.StartTrace("checkout_flow")) {
        ///     trace.SetMetadata("step_count", 4);
        ///     // ... multi-step work ...
        /// }
        /// </code>
        /// </summary>
        public static LayersPerformanceTrace StartTrace(string name)
        {
            if (!CheckInitialized("StartTrace")) return new LayersPerformanceTrace(name ?? "unknown");
            if (string.IsNullOrEmpty(name))
            {
                RaiseError("StartTrace", "name must not be null or empty");
                name = "unknown";
            }
            return new LayersPerformanceTrace(name);
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

        // PlayerPrefs keys for attribution persistence
        private static string _deeplinkId;
        /// <summary>
        /// The current deep link ID, or null if not set. Used internally by
        /// DeepLinksModule to preserve the value when persisting click IDs.
        /// </summary>
        internal static string DeeplinkId => _deeplinkId;
        private const string PrefDeeplinkId = "layers_attribution_deeplink_id";
        private const string PrefGclid = "layers_attribution_gclid";
        private const string PrefFbclid = "layers_attribution_fbclid";
        private const string PrefFbc = "layers_attribution_fbc";
        private const string PrefTtclid = "layers_attribution_ttclid";
        private const string PrefMsclkid = "layers_attribution_msclkid";

        // In-memory cache of click IDs for merging into event properties
        private static string _gclid;
        internal static string Gclid => _gclid;
        private static string _fbclid;
        internal static string Fbclid => _fbclid;
        private static string _fbc;
        private static string _ttclid;
        internal static string Ttclid => _ttclid;
        private static string _msclkid;
        internal static string Msclkid => _msclkid;

        /// <summary>
        /// Store attribution data that will be included in every subsequent event
        /// via the Rust core's DeviceContext. Values are persisted in PlayerPrefs
        /// so they survive app restarts.
        ///
        /// Pass null for a parameter to leave that value unchanged. Pass an
        /// empty string to clear a single value. (Attribution arrives from
        /// asynchronous sources — deep links, the install referrer — that
        /// each know only their own click IDs, so an unset parameter must
        /// never clobber a value another source already stored.)
        ///
        /// <c>deeplink_id</c> and <c>gclid</c> flow through DeviceContext on the
        /// Rust core (top-level event fields), not the properties bag.
        /// When <c>fbclid</c> is set, a composite <c>fbc</c> value is computed:
        /// <c>fb.1.{unix_ms}.{fbclid}</c>.
        /// </summary>
        /// <param name="deeplinkId">Deep link identifier for server-side attribution matching.</param>
        /// <param name="gclid">Google Click Identifier from ad click URLs.</param>
        /// <param name="fbclid">Facebook Click Identifier from ad click URLs.</param>
        /// <param name="ttclid">TikTok Click Identifier from ad click URLs.</param>
        /// <param name="msclkid">Microsoft Click Identifier from ad click URLs.</param>
        public static void SetAttributionData(
            string deeplinkId = null,
            string gclid = null,
            string fbclid = null,
            string ttclid = null,
            string msclkid = null)
        {
            if (!CheckInitialized("SetAttributionData")) return;

            // Contract (matches the doc): pass NULL to leave a value
            // unchanged; pass an EMPTY STRING to clear it. The old code
            // overwrote every field with its (null) argument and deleted the
            // PlayerPrefs keys — so the asynchronous install-referrer
            // callback could erase a click ID a deep link had just set (its
            // `?? _fbclid` fallbacks existed to work around exactly that),
            // and the Rust DeviceContext kept the stale value while the
            // event-merge cache lost it.
            var ctx = new Dictionary<string, object>();

            if (deeplinkId != null)
            {
                _deeplinkId = deeplinkId.Length == 0 ? null : deeplinkId;
                ctx["deeplink_id"] = deeplinkId; // "" clears the Rust-side context
                PersistOrClear(PrefDeeplinkId, _deeplinkId);
            }
            if (gclid != null)
            {
                _gclid = gclid.Length == 0 ? null : gclid;
                ctx["gclid"] = gclid;
                PersistOrClear(PrefGclid, _gclid);
            }
            if (fbclid != null)
            {
                _fbclid = fbclid.Length == 0 ? null : fbclid;
                _fbc = _fbclid != null
                    ? $"fb.1.{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.{_fbclid}"
                    : null;
                ctx["fbclid"] = fbclid;
                ctx["fbc"] = _fbc ?? "";
                PersistOrClear(PrefFbclid, _fbclid);
                PersistOrClear(PrefFbc, _fbc);
            }
            if (ttclid != null)
            {
                _ttclid = ttclid.Length == 0 ? null : ttclid;
                ctx["ttclid"] = ttclid;
                PersistOrClear(PrefTtclid, _ttclid);
            }
            if (msclkid != null)
            {
                _msclkid = msclkid.Length == 0 ? null : msclkid;
                ctx["msclkid"] = msclkid;
                PersistOrClear(PrefMsclkid, _msclkid);
            }

            if (ctx.Count > 0)
            {
                _platform.SetDeviceContext(JsonHelper.Serialize(ctx));
                PlayerPrefs.Save();
            }

            LayersLogger.Log(
                $"SetAttributionData(deeplinkId={deeplinkId ?? "null"}, gclid={gclid ?? "null"}, " +
                $"fbclid={fbclid ?? "null"}, ttclid={ttclid ?? "null"}, msclkid={msclkid ?? "null"})");
        }

        private static void PersistOrClear(string key, string value)
        {
            if (value != null)
                PlayerPrefs.SetString(key, value);
            else
                PlayerPrefs.DeleteKey(key);
        }

        /// <summary>
        /// Restore persisted attribution data from PlayerPrefs.
        /// Called during initialization to survive app restarts.
        /// </summary>
        private static void RestoreAttributionData()
        {
            string deeplinkId = RestoreString(PrefDeeplinkId);
            string gclid = RestoreString(PrefGclid);
            string fbclid = RestoreString(PrefFbclid);
            string fbc = RestoreString(PrefFbc);
            string ttclid = RestoreString(PrefTtclid);
            string msclkid = RestoreString(PrefMsclkid);

            // Restore in-memory cache
            _deeplinkId = deeplinkId;
            _gclid = gclid;
            _fbclid = fbclid;
            _fbc = fbc;
            _ttclid = ttclid;
            _msclkid = msclkid;

            var ctx = new Dictionary<string, object>();
            if (deeplinkId != null) ctx["deeplink_id"] = deeplinkId;
            if (gclid != null) ctx["gclid"] = gclid;
            if (fbclid != null) ctx["fbclid"] = fbclid;
            if (fbc != null) ctx["fbc"] = fbc;
            if (ttclid != null) ctx["ttclid"] = ttclid;
            if (msclkid != null) ctx["msclkid"] = msclkid;

            if (ctx.Count > 0)
            {
                _platform.SetDeviceContext(JsonHelper.Serialize(ctx));

                LayersLogger.Log(
                    $"Restored attribution data: deeplinkId={deeplinkId ?? "null"}, gclid={gclid ?? "null"}, " +
                    $"fbclid={fbclid ?? "null"}, ttclid={ttclid ?? "null"}, msclkid={msclkid ?? "null"}");
            }
        }

        private static string RestoreString(string key)
        {
            string val = PlayerPrefs.GetString(key, null);
            return string.IsNullOrEmpty(val) ? null : val;
        }

        /// <summary>
        /// Merge stored click IDs into the given event properties dictionary.
        /// Returns a new dictionary with click IDs injected (user-supplied
        /// properties take precedence if they share the same key).
        /// </summary>
        private static Dictionary<string, object> MergeAttributionProperties(
            Dictionary<string, object> properties)
        {
            if (_gclid == null && _fbclid == null && _fbc == null &&
                _ttclid == null && _msclkid == null)
            {
                return properties;
            }

            var merged = properties != null
                ? new Dictionary<string, object>(properties)
                : new Dictionary<string, object>();

            if (_gclid != null && !merged.ContainsKey("gclid"))
                merged["gclid"] = _gclid;
            if (_fbclid != null && !merged.ContainsKey("fbclid"))
                merged["fbclid"] = _fbclid;
            if (_fbc != null && !merged.ContainsKey("$fbc"))
                merged["$fbc"] = _fbc;
            if (_ttclid != null && !merged.ContainsKey("ttclid"))
                merged["ttclid"] = _ttclid;
            if (_msclkid != null && !merged.ContainsKey("msclkid"))
                merged["msclkid"] = _msclkid;

            return merged;
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

            // Tier 2: emit $app_background BEFORE flushing so it lands in the
            // outgoing batch.
            if (_config != null && _config.AutoCaptureLifecycle)
            {
                try
                {
                    Track(StandardEvents.LayersAppBackground);
                }
                catch (Exception e)
                {
                    LayersLogger.Warn($"Failed to emit $app_background: {e.Message}");
                }
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            _platform?.Flush();
#else
            // Make the queue (and any batch mid-HTTP-request) durable FIRST:
            // coroutines freeze while suspended, so if the OS kills the app
            // in the background the in-flight request and the in-memory
            // queue would both be lost. Then still attempt a network flush —
            // the OS often grants a moment before suspending.
            _flushManager?.PersistPendingForSuspend();
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

        // ── Tier 2: Lifecycle auto-capture ───────────────────────────

        /// <summary>
        /// PlayerPrefs key holding the Application.version value last seen by
        /// the SDK. Compared against <see cref="Application.version"/> on each
        /// launch to emit <c>$app_update</c>.
        /// </summary>
        private const string LastAppVersionKey = "layers_last_app_version";

        /// <summary>
        /// Cold launch (called from <see cref="LayersRunner.Start"/>).
        /// Emits <c>$first_open</c> on a genuine first launch, <c>$app_update</c>
        /// when <see cref="Application.version"/> has changed since last launch,
        /// and always emits <c>$app_open</c>.
        ///
        /// All Tier 2 events are no-ops when
        /// <see cref="LayersConfig.AutoCaptureLifecycle"/> is false.
        /// </summary>
        internal static void OnLifecycleColdLaunch()
        {
            if (!_isInitialized) return;
            if (_config == null || !_config.AutoCaptureLifecycle) return;

            // First-open detection. The legacy app_install / app_open path uses
            // PlayerPrefs("layers_first_launch_tracked") via InstallEventGate;
            // we share that signal via the cached IsFirstLaunch() helper so
            // we never double-emit $first_open AND never double-flip the
            // PlayerPrefs flag.
            bool isFirstLaunch = IsFirstLaunch();
            if (isFirstLaunch)
            {
                Track(StandardEvents.LayersFirstOpen, new Dictionary<string, object>
                {
                    ["app_version"] = SafeAppVersion(),
                    ["timezone"] = System.TimeZoneInfo.Local.Id
                });
            }

            // App-update detection. A version change between launches emits a
            // single $app_update event with the previous + current versions
            // so consumers can build update funnels (e.g. retention by version).
            string current = SafeAppVersion();
            string previous = null;
            try { previous = PlayerPrefs.GetString(LastAppVersionKey, null); }
            catch (Exception) { /* PlayerPrefs unavailable on some platforms */ }

            if (!isFirstLaunch
                && !string.IsNullOrEmpty(previous)
                && !string.IsNullOrEmpty(current)
                && previous != current)
            {
                Track(StandardEvents.LayersAppUpdate, new Dictionary<string, object>
                {
                    ["previous_version"] = previous,
                    ["app_version"] = current
                });
            }

            try
            {
                if (!string.IsNullOrEmpty(current))
                {
                    PlayerPrefs.SetString(LastAppVersionKey, current);
                    PlayerPrefs.Save();
                }
            }
            catch (Exception) { /* best-effort */ }

            EmitAppOpen(coldLaunch: true);
        }

        /// <summary>
        /// Foreground transition after a background pause. Emits a fresh
        /// <c>$app_open</c> event without re-running install/version detection.
        /// </summary>
        internal static void OnLifecycleResume()
        {
            if (!_isInitialized) return;
            if (_config == null || !_config.AutoCaptureLifecycle) return;
            EmitAppOpen(coldLaunch: false);
        }

        /// <summary>
        /// App is about to terminate (<see cref="Application.quitting"/>).
        /// Emits <c>$app_terminate</c> synchronously so it sits in the queue
        /// before <see cref="Shutdown"/> drains it.
        /// </summary>
        internal static void OnLifecycleTerminate()
        {
            if (!_isInitialized) return;
            if (_config == null || !_config.AutoCaptureLifecycle) return;
            try
            {
                Track(StandardEvents.LayersAppTerminate);
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"Failed to emit $app_terminate: {e.Message}");
            }
        }

        private static void EmitAppOpen(bool coldLaunch)
        {
            try
            {
                var props = new Dictionary<string, object>
                {
                    ["cold_launch"] = coldLaunch,
                    ["app_version"] = SafeAppVersion()
                };
                Track(StandardEvents.LayersAppOpen, props);
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"Failed to emit $app_open: {e.Message}");
            }
        }

        private static string SafeAppVersion()
        {
            try { return Application.version; }
            catch (Exception) { return null; }
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

            // Send app_install before app_open on first launch for CAPI forwarding
            if (props.ContainsKey("is_first_launch") && props["is_first_launch"] is bool firstLaunch && firstLaunch)
            {
                Track("app_install", props);
            }
            Track("app_open", props);
        }

        /// <summary>
        /// Determine if this is the first launch using the install event gate.
        /// Applies 24-hour gating to suppress false first-launch events when
        /// the SDK is added to an existing app.
        ///
        /// The result is cached for the lifetime of the process — both the
        /// legacy <c>app_install</c>/<c>app_open</c> path and the Tier 2
        /// <c>$first_open</c> path consult this single answer so the side-
        /// effecting <see cref="InstallEventGate.DetermineIsFirstLaunch"/>
        /// runs at most once per launch.
        /// </summary>
        private static bool IsFirstLaunch()
        {
            if (_isFirstLaunchCached.HasValue) return _isFirstLaunchCached.Value;
            bool v = InstallEventGate.DetermineIsFirstLaunch();
            _isFirstLaunchCached = v;
            return v;
        }

        private static bool? _isFirstLaunchCached;

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

                    // Hand any extracted click IDs to SetAttributionData so subsequent
                    // events (app_open, purchase_success, etc.) carry them. Without this,
                    // CAPI relay would only see click IDs on the install_referrer event,
                    // which isn't a CAPI-mapped event.
                    //
                    // Merge with any previously restored / deep-link-set values so we
                    // don't clobber a deeplinkId (or other click ID) already in effect.
                    // The install referrer callback is asynchronous and may fire after
                    // a deep link has already called SetAttributionData.
                    if (!string.IsNullOrEmpty(result.Fbclid) ||
                        !string.IsNullOrEmpty(result.Gclid) ||
                        !string.IsNullOrEmpty(result.Ttclid) ||
                        !string.IsNullOrEmpty(result.Msclkid))
                    {
                        try
                        {
                            SetAttributionData(
                                deeplinkId: _deeplinkId,
                                gclid: result.Gclid ?? _gclid,
                                fbclid: result.Fbclid ?? _fbclid,
                                ttclid: result.Ttclid ?? _ttclid,
                                msclkid: result.Msclkid ?? _msclkid
                            );
                        }
                        catch (Exception e)
                        {
                            LayersLogger.Warn($"SetAttributionData from install referrer failed: {e.Message}");
                        }
                    }

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


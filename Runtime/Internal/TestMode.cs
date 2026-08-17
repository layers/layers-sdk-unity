using System.Collections.Generic;

namespace Layers.Unity.Internal
{
    /// <summary>
    /// Mock platform implementation for unit testing.
    ///
    /// When enabled via <see cref="LayersTestMode.Enable"/>, the SDK uses this
    /// in-memory mock instead of the real Rust native library. All tracked events,
    /// identify calls, group calls, and device context updates are captured in
    /// lists that tests can assert against.
    ///
    /// Usage in tests:
    /// <code>
    /// [SetUp]
    /// public void SetUp()
    /// {
    ///     LayersTestMode.Enable();
    ///     LayersSDK.Initialize(new LayersConfig { AppId = "test-app" });
    /// }
    ///
    /// [TearDown]
    /// public void TearDown()
    /// {
    ///     LayersSDK.Shutdown();
    ///     LayersTestMode.Disable();
    /// }
    /// </code>
    /// </summary>
    internal class MockPlatform : ILayersPlatform
    {
        internal readonly List<(string eventName, string propertiesJson)> TrackedEvents
            = new List<(string, string)>();

        internal readonly List<(string screenName, string propertiesJson)> ScreenedEvents
            = new List<(string, string)>();

        internal readonly List<string> IdentifyCalls = new List<string>();
        internal readonly List<string> UserPropertiesCalls = new List<string>();
        internal readonly List<string> UserPropertiesOnceCalls = new List<string>();
        internal readonly List<(string groupId, string propertiesJson)> GroupCalls
            = new List<(string, string)>();

        internal readonly List<string> ConsentCalls = new List<string>();
        internal readonly List<string> DeviceContextCalls = new List<string>();
        internal readonly List<(string configJson, string etag)> RemoteConfigCalls
            = new List<(string, string)>();

        internal int SimulatedQueueDepth;
        internal bool AutoIncrementQueueDepth = true;
        internal string SimulatedSessionId = "test-session-001";
        internal string SimulatedDebugToken;
        internal string SimulatedRemoteConfigJson;
        internal bool IsInitialized;
        internal bool IsShutdown;
        internal int FlushCount;

        public string Init(string configJson)
        {
            IsInitialized = true;
            IsShutdown = false;
            return null; // success
        }

        public string Shutdown()
        {
            IsShutdown = true;
            IsInitialized = false;
            return null;
        }

        public string Track(string eventName, string propertiesJson)
        {
            TrackedEvents.Add((eventName, propertiesJson));
            if (AutoIncrementQueueDepth) SimulatedQueueDepth++;
            return null;
        }

        public string Screen(string screenName, string propertiesJson)
        {
            ScreenedEvents.Add((screenName, propertiesJson));
            if (AutoIncrementQueueDepth) SimulatedQueueDepth++;
            return null;
        }

        public string Identify(string userId)
        {
            IdentifyCalls.Add(userId);
            return null;
        }

        public string SetUserProperties(string propertiesJson)
        {
            UserPropertiesCalls.Add(propertiesJson);
            return null;
        }

        public string SetUserPropertiesOnce(string propertiesJson)
        {
            UserPropertiesOnceCalls.Add(propertiesJson);
            return null;
        }

        public string Group(string groupId, string propertiesJson)
        {
            GroupCalls.Add((groupId, propertiesJson));
            return null;
        }

        public string SetConsent(string consentJson)
        {
            ConsentCalls.Add(consentJson);
            return null;
        }

        /// <summary>
        /// Whether this mock models the core's own <c>$first_open</c>
        /// emission. Default true — the real core always does it.
        ///
        /// The Rust core builds and queues <c>$first_open</c> from INSIDE
        /// <c>set_device_context</c> (core/src/client.rs — the
        /// <c>emit_pending_first_open()</c> call), once per install, gated by
        /// its own persisted identity record. That emission is invisible to a
        /// mock that only records the context JSON, so a wrapper emitting its
        /// own <c>$first_open</c> looked like exactly one event to every test
        /// while shipping two to the server. Modelling it here makes
        /// <see cref="TrackedEvents"/> a CROSS-LAYER counter: the core's copy
        /// and anything the wrapper tracks land in the same list.
        /// </summary>
        internal bool SimulateCoreFirstOpen = true;

        /// <summary>
        /// Once per mock instance, mirroring "once per install" — the real
        /// gate is persisted, so a new install is a new
        /// <see cref="LayersTestMode.Enable"/>. Deliberately NOT cleared by
        /// <see cref="ClearTestState"/>.
        /// </summary>
        private bool _coreFirstOpenEmitted;

        public string SetDeviceContext(string contextJson)
        {
            DeviceContextCalls.Add(contextJson);
            if (SimulateCoreFirstOpen && !_coreFirstOpenEmitted)
            {
                _coreFirstOpenEmitted = true;
                // Recorded directly rather than through Track(): this is the
                // core emitting on its own behalf, not a wrapper call
                // crossing the FFI boundary. Both land in TrackedEvents,
                // which is the point.
                TrackedEvents.Add(("$first_open", "{}"));
                if (AutoIncrementQueueDepth) SimulatedQueueDepth++;
            }
            return null;
        }

        public string Flush()
        {
            FlushCount++;
            return null;
        }

        public string DrainBatch(uint count)
        {
            return null; // empty queue
        }

        public string RequeueEvents(string eventsJson)
        {
            return null;
        }

        /// <summary>
        /// The <c>X-SDK-Version</c> the simulated core stamps on EVERY
        /// endpoint. Deliberately unrelated to <c>LayersSDK.SdkVersion</c>: a
        /// wrapper that re-composes <c>unity/&lt;version&gt;</c> by hand would
        /// produce a different string and miss this one, which is the drift
        /// the header tests exist to catch.
        /// </summary>
        internal const string MockSdkVersionHeader =
            "unity/0.0.0-mock rust-core/0.0.0-mock engine/rust";

        public string FlushHeadersJson()
        {
            return "{\"Content-Type\":\"application/json\",\"X-App-Id\":\"mock-app\","
                + "\"X-Environment\":\"development\",\"X-SDK-Version\":\""
                + MockSdkVersionHeader + "\"}";
        }

        public string ConfigHeadersJson()
        {
            // Array-of-pairs — the shape layers_config_headers_json returns.
            return "[[\"Accept\",\"application/json\"],[\"X-App-Id\",\"mock-app\"],"
                + "[\"X-Environment\",\"development\"],[\"X-SDK-Version\",\""
                + MockSdkVersionHeader + "\"]]";
        }

        public string EventsUrl()
        {
            return "https://mock.test/events";
        }

        public int QueueDepth()
        {
            return SimulatedQueueDepth;
        }

        public string GetSessionId()
        {
            return SimulatedSessionId;
        }

        public string GetDebugToken()
        {
            return SimulatedDebugToken;
        }

        public string GetRemoteConfigJson()
        {
            return SimulatedRemoteConfigJson;
        }

        public string UpdateRemoteConfig(string configJson, string etag)
        {
            RemoteConfigCalls.Add((configJson, etag));
            SimulatedRemoteConfigJson = configJson;
            return null;
        }

        // ── Tier 1 — Super-properties / timed events / multi-group / mutators ──
        // Captured for assertion just like the legacy mock surface.

        internal readonly List<string> SetSuperPropertiesCalls = new List<string>();
        internal readonly List<string> SetSuperPropertiesOnceCalls = new List<string>();
        internal readonly List<string> UnregisterSuperPropertyCalls = new List<string>();
        internal int ClearSuperPropertiesCalls;
        internal string SimulatedSuperPropertiesJson = "{}";

        internal readonly List<string> TimeEventCalls = new List<string>();
        internal readonly List<string> CancelTimedEventCalls = new List<string>();
        internal ulong SimulatedCancelTimedEventResult;

        internal readonly List<(string groupType, string groupId)> SetGroupCalls
            = new List<(string, string)>();
        internal readonly List<(string groupType, string groupId)> AddGroupCalls
            = new List<(string, string)>();
        internal readonly List<string> RemoveGroupCalls = new List<string>();
        internal string SimulatedGroupsJson = "{}";

        internal readonly List<(string key, double delta)> IncrementCalls
            = new List<(string, double)>();
        internal readonly List<(string key, string valueJson)> AppendCalls
            = new List<(string, string)>();
        internal readonly List<(string key, string valuesJson)> UnionCalls
            = new List<(string, string)>();
        internal readonly List<string> UnsetCalls = new List<string>();

        internal int ResetCalls;
        internal string SimulatedDeviceId;
        internal string SimulatedAnonymousId;
        internal uint SimulatedSessionNumber;
        internal string SimulatedFirstOpenTime;

        internal System.Func<string, string> LastBeforeSendCallback;
        internal int ClearBeforeSendCalls;

        public string SetSuperProperties(string propertiesJson)
        {
            SetSuperPropertiesCalls.Add(propertiesJson);
            return null;
        }

        public string SetSuperPropertiesOnce(string propertiesJson)
        {
            SetSuperPropertiesOnceCalls.Add(propertiesJson);
            return null;
        }

        public string UnregisterSuperProperty(string key)
        {
            UnregisterSuperPropertyCalls.Add(key);
            return null;
        }

        public string ClearSuperProperties()
        {
            ClearSuperPropertiesCalls++;
            return null;
        }

        public string GetSuperPropertiesJson() => SimulatedSuperPropertiesJson;

        public string TimeEvent(string eventName)
        {
            TimeEventCalls.Add(eventName);
            return null;
        }

        public ulong CancelTimedEvent(string eventName)
        {
            CancelTimedEventCalls.Add(eventName);
            return SimulatedCancelTimedEventResult;
        }

        public string SetGroup(string groupType, string groupId)
        {
            SetGroupCalls.Add((groupType, groupId));
            return null;
        }

        public string AddGroup(string groupType, string groupId)
        {
            AddGroupCalls.Add((groupType, groupId));
            return null;
        }

        public string RemoveGroup(string groupType)
        {
            RemoveGroupCalls.Add(groupType);
            return null;
        }

        public string GetGroupsJson() => SimulatedGroupsJson;

        public string Increment(string key, double delta)
        {
            IncrementCalls.Add((key, delta));
            return null;
        }

        public string Append(string key, string valueJson)
        {
            AppendCalls.Add((key, valueJson));
            return null;
        }

        public string Union(string key, string valuesJson)
        {
            UnionCalls.Add((key, valuesJson));
            return null;
        }

        public string Unset(string key)
        {
            UnsetCalls.Add(key);
            return null;
        }

        // The interface method `Reset()` collides with the legacy
        // `internal void Reset()` test-state-clear helper, so the interface
        // implementation is forwarded as `ILayersPlatform.Reset()` and the
        // test helper is renamed to `ClearTestState`.
        string ILayersPlatform.Reset()
        {
            ResetCalls++;
            return null;
        }

        public string GetDeviceId() => SimulatedDeviceId;
        public string GetAnonymousId() => SimulatedAnonymousId;
        public uint GetSessionNumber() => SimulatedSessionNumber;
        public string GetFirstOpenTime() => SimulatedFirstOpenTime;

        public string SetBeforeSend(System.Func<string, string> callback)
        {
            LastBeforeSendCallback = callback;
            return null;
        }

        public string ClearBeforeSend()
        {
            ClearBeforeSendCalls++;
            return null;
        }

        // ── Tier 4 — Feature flag mock state ────────────────────────────

        /// <summary>
        /// Map of flag key → simulated JSON value string. Tests can seed
        /// this directly, or use the helper <see cref="SimulateFlag"/>.
        /// Reads of unknown keys return <c>"null"</c> (the canonical "no
        /// such flag" wire value).
        /// </summary>
        internal readonly Dictionary<string, string> SimulatedFlagJson
            = new Dictionary<string, string>();

        internal readonly Dictionary<string, string> SimulatedFlagPayloadJson
            = new Dictionary<string, string>();

        /// <summary>Recorded flag-evaluation calls (key per call).</summary>
        internal readonly List<string> GetFeatureFlagCalls = new List<string>();
        internal readonly List<string> IsFeatureEnabledCalls = new List<string>();
        internal readonly List<string> GetFeatureFlagPayloadCalls = new List<string>();
        internal int GetAllFlagsCalls;
        internal int ReloadFeatureFlagsCalls;
        internal readonly List<string> SetPersonPropertiesForFlagsCalls = new List<string>();
        internal readonly List<string> SetFeatureFlagBootstrapCalls = new List<string>();

        /// <summary>
        /// Simulated return value for the next <c>ReloadFeatureFlags</c>
        /// call: 1 (dropped some), 0 (cache empty), -1 (error). Defaults
        /// to 0.
        /// </summary>
        internal int SimulatedReloadResult;

        /// <summary>
        /// Test helper: seed a flag value as a typed C# value. Encodes
        /// to the wire JSON shape the Rust core would produce. Pass
        /// <c>null</c> as <paramref name="value"/> to clear the seed.
        /// </summary>
        internal void SimulateFlag(string key, object value)
        {
            if (value == null) { SimulatedFlagJson.Remove(key); return; }
            string json;
            if (value is bool b) json = b ? "true" : "false";
            else if (value is string s) json = "\"" + s + "\"";
            else json = "null";
            SimulatedFlagJson[key] = json;
        }

        public string GetFeatureFlagJson(string flagKey)
        {
            GetFeatureFlagCalls.Add(flagKey);
            return SimulatedFlagJson.TryGetValue(flagKey, out var v) ? v : "null";
        }

        public int IsFeatureEnabled(string flagKey)
        {
            IsFeatureEnabledCalls.Add(flagKey);
            // Match the C ABI semantics: 1 / 0 / -1.
            if (!SimulatedFlagJson.TryGetValue(flagKey, out var v)) return 0;
            if (v == "true") return 1;
            if (v == "false") return 0;
            // Multivariate flags: any non-empty variant string is truthy.
            if (v != null && v.Length >= 2 && v[0] == '"' && v != "\"\"") return 1;
            return 0;
        }

        public string GetFeatureFlagPayloadJson(string flagKey)
        {
            GetFeatureFlagPayloadCalls.Add(flagKey);
            return SimulatedFlagPayloadJson.TryGetValue(flagKey, out var v) ? v : "null";
        }

        public string GetAllFlagsJson()
        {
            GetAllFlagsCalls++;
            // Build a JSON object string from the simulated flag map. Only
            // concerns itself with the key set + values that are valid JSON.
            if (SimulatedFlagJson.Count == 0) return "{}";
            var sb = new System.Text.StringBuilder("{");
            bool first = true;
            foreach (var kv in SimulatedFlagJson)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(kv.Key).Append("\":").Append(kv.Value);
            }
            sb.Append('}');
            return sb.ToString();
        }

        public int ReloadFeatureFlags()
        {
            ReloadFeatureFlagsCalls++;
            return SimulatedReloadResult;
        }

        public string SetPersonPropertiesForFlags(string propertiesJson)
        {
            SetPersonPropertiesForFlagsCalls.Add(propertiesJson);
            return null;
        }

        public string SetFeatureFlagBootstrap(string bootstrapJson)
        {
            SetFeatureFlagBootstrapCalls.Add(bootstrapJson);
            return null;
        }

        /// <summary>
        /// Clear all captured mock state. Distinct from the
        /// <see cref="ILayersPlatform.Reset"/> SDK reset path so callers
        /// can choose whether to reset the SDK identity (which records into
        /// <see cref="ResetCalls"/>) or just wipe the captured fixtures.
        /// </summary>
        internal void ClearTestState()
        {
            TrackedEvents.Clear();
            ScreenedEvents.Clear();
            IdentifyCalls.Clear();
            UserPropertiesCalls.Clear();
            UserPropertiesOnceCalls.Clear();
            GroupCalls.Clear();
            ConsentCalls.Clear();
            DeviceContextCalls.Clear();
            RemoteConfigCalls.Clear();
            SimulatedQueueDepth = 0;
            SimulatedSessionId = "test-session-001";
            SimulatedDebugToken = null;
            SimulatedRemoteConfigJson = null;
            IsInitialized = false;
            IsShutdown = false;
            FlushCount = 0;

            // Tier 1
            SetSuperPropertiesCalls.Clear();
            SetSuperPropertiesOnceCalls.Clear();
            UnregisterSuperPropertyCalls.Clear();
            ClearSuperPropertiesCalls = 0;
            SimulatedSuperPropertiesJson = "{}";
            TimeEventCalls.Clear();
            CancelTimedEventCalls.Clear();
            SimulatedCancelTimedEventResult = 0;
            SetGroupCalls.Clear();
            AddGroupCalls.Clear();
            RemoveGroupCalls.Clear();
            SimulatedGroupsJson = "{}";
            IncrementCalls.Clear();
            AppendCalls.Clear();
            UnionCalls.Clear();
            UnsetCalls.Clear();
            ResetCalls = 0;
            SimulatedDeviceId = null;
            SimulatedAnonymousId = null;
            SimulatedSessionNumber = 0;
            SimulatedFirstOpenTime = null;
            LastBeforeSendCallback = null;
            ClearBeforeSendCalls = 0;

            // Tier 4
            SimulatedFlagJson.Clear();
            SimulatedFlagPayloadJson.Clear();
            GetFeatureFlagCalls.Clear();
            IsFeatureEnabledCalls.Clear();
            GetFeatureFlagPayloadCalls.Clear();
            GetAllFlagsCalls = 0;
            ReloadFeatureFlagsCalls = 0;
            SetPersonPropertiesForFlagsCalls.Clear();
            SetFeatureFlagBootstrapCalls.Clear();
            SimulatedReloadResult = 0;
        }
    }

    /// <summary>
    /// Public API for enabling/disabling test mode.
    ///
    /// When test mode is enabled, <see cref="LayersPlatformFactory.Create"/> returns
    /// a <see cref="MockPlatform"/> instead of the real native or WebGL platform.
    /// This allows unit tests to run without the Rust native library.
    /// </summary>
    public static class LayersTestMode
    {
        private static MockPlatform _mockPlatform;
        private static bool _enabled;

        /// <summary>
        /// Whether test mode is currently enabled.
        /// </summary>
        public static bool IsEnabled => _enabled;

        /// <summary>
        /// Enable test mode. Subsequent calls to <see cref="LayersPlatformFactory.Create"/>
        /// will return the mock platform.
        /// </summary>
        public static void Enable()
        {
            _mockPlatform = new MockPlatform();
            _enabled = true;
        }

        /// <summary>
        /// Disable test mode and restore the real platform factory behavior.
        /// </summary>
        public static void Disable()
        {
            _enabled = false;
            _mockPlatform = null;
        }

        /// <summary>
        /// Reset all captured test data without disabling test mode.
        /// </summary>
        public static void Reset()
        {
            _mockPlatform?.ClearTestState();
        }

        /// <summary>
        /// Get the list of tracked events. Each entry is a tuple of (eventName, propertiesJson).
        /// </summary>
        public static List<(string eventName, string propertiesJson)> TrackedEvents
            => _mockPlatform?.TrackedEvents ?? new List<(string, string)>();

        /// <summary>
        /// Get the list of screen view events. Each entry is a tuple of (screenName, propertiesJson).
        /// </summary>
        public static List<(string screenName, string propertiesJson)> ScreenedEvents
            => _mockPlatform?.ScreenedEvents ?? new List<(string, string)>();

        /// <summary>
        /// Get the list of identify calls.
        /// </summary>
        public static List<string> IdentifyCalls
            => _mockPlatform?.IdentifyCalls ?? new List<string>();

        /// <summary>
        /// Get the list of group calls. Each entry is a tuple of (groupId, propertiesJson).
        /// </summary>
        public static List<(string groupId, string propertiesJson)> GroupCalls
            => _mockPlatform?.GroupCalls ?? new List<(string, string)>();

        /// <summary>
        /// Get the list of device context calls (JSON strings).
        /// </summary>
        public static List<string> DeviceContextCalls
            => _mockPlatform?.DeviceContextCalls ?? new List<string>();

        /// <summary>
        /// Get the list of user properties calls (JSON strings).
        /// </summary>
        public static List<string> UserPropertiesCalls
            => _mockPlatform?.UserPropertiesCalls ?? new List<string>();

        /// <summary>
        /// Get the list of consent calls (JSON strings).
        /// </summary>
        public static List<string> ConsentCalls
            => _mockPlatform?.ConsentCalls ?? new List<string>();

        /// <summary>
        /// Get the number of flush calls.
        /// </summary>
        public static int FlushCount => _mockPlatform?.FlushCount ?? 0;

        /// <summary>
        /// Internal: get the mock platform instance for the factory.
        /// </summary>
        internal static ILayersPlatform GetMockPlatform() => _mockPlatform;
    }
}

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
    ///     Layers.Initialize(new LayersConfig { AppId = "test-app" });
    /// }
    ///
    /// [TearDown]
    /// public void TearDown()
    /// {
    ///     Layers.Shutdown();
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

        public string SetDeviceContext(string contextJson)
        {
            DeviceContextCalls.Add(contextJson);
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

        public string FlushHeadersJson()
        {
            return "{}";
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

        internal void Reset()
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
            SimulatedRemoteConfigJson = null;
            IsInitialized = false;
            IsShutdown = false;
            FlushCount = 0;
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
            _mockPlatform?.Reset();
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

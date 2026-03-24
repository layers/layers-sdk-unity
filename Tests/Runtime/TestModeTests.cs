using NUnit.Framework;
using System.Collections.Generic;
using Layers.Unity;
using Layers.Unity.Internal;

namespace Layers.Unity.Tests
{
    [TestFixture]
    public class TestModeTests
    {
        [TearDown]
        public void TearDown()
        {
            // Ensure SDK is shut down and test mode is disabled
            if (Layers.IsInitialized)
                Layers.Shutdown();
            if (LayersTestMode.IsEnabled)
                LayersTestMode.Disable();
        }

        // ── Enable / Disable ──────────────────────────────────────────────

        [Test]
        public void Enable_SetsIsEnabledTrue()
        {
            LayersTestMode.Enable();

            Assert.IsTrue(LayersTestMode.IsEnabled);
        }

        [Test]
        public void Disable_SetsIsEnabledFalse()
        {
            LayersTestMode.Enable();
            LayersTestMode.Disable();

            Assert.IsFalse(LayersTestMode.IsEnabled);
        }

        // ── SDK Initializes with Mock ─────────────────────────────────────

        [Test]
        public void Initialize_WithTestMode_Succeeds()
        {
            LayersTestMode.Enable();

            Layers.Initialize(new LayersConfig { AppId = "test-init" });

            Assert.IsTrue(Layers.IsInitialized);
        }

        [Test]
        public void Initialize_WithTestMode_DoesNotCallNativeLib()
        {
            LayersTestMode.Enable();

            // This should NOT crash even without the Rust native library
            Assert.DoesNotThrow(() =>
            {
                Layers.Initialize(new LayersConfig { AppId = "test-no-crash" });
            });
        }

        // ── Track Capture ─────────────────────────────────────────────────

        [Test]
        public void Track_CapturedByTestMode()
        {
            LayersTestMode.Enable();
            Layers.Initialize(new LayersConfig { AppId = "test-track" });
            LayersTestMode.Reset(); // Clear init events

            Layers.Track("test_event", new Dictionary<string, object>
            {
                ["key"] = "value"
            });

            Assert.AreEqual(1, LayersTestMode.TrackedEvents.Count);
            Assert.AreEqual("test_event", LayersTestMode.TrackedEvents[0].eventName);
            Assert.That(LayersTestMode.TrackedEvents[0].propertiesJson,
                Does.Contain("\"key\":\"value\""));
        }

        // ── Screen Capture ────────────────────────────────────────────────

        [Test]
        public void Screen_CapturedByTestMode()
        {
            LayersTestMode.Enable();
            Layers.Initialize(new LayersConfig { AppId = "test-screen" });
            LayersTestMode.Reset();

            Layers.Screen("HomeScreen");

            Assert.AreEqual(1, LayersTestMode.ScreenedEvents.Count);
            Assert.AreEqual("HomeScreen", LayersTestMode.ScreenedEvents[0].screenName);
        }

        // ── Identify Capture ──────────────────────────────────────────────

        [Test]
        public void Identify_CapturedByTestMode()
        {
            LayersTestMode.Enable();
            Layers.Initialize(new LayersConfig { AppId = "test-identify" });
            LayersTestMode.Reset();

            Layers.Identify("user_123");

            Assert.AreEqual(1, LayersTestMode.IdentifyCalls.Count);
            Assert.AreEqual("user_123", LayersTestMode.IdentifyCalls[0]);
        }

        // ── Group Capture ─────────────────────────────────────────────────

        [Test]
        public void Group_CapturedByTestMode()
        {
            LayersTestMode.Enable();
            Layers.Initialize(new LayersConfig { AppId = "test-group" });
            LayersTestMode.Reset();

            Layers.Group("org_456", new Dictionary<string, object>
            {
                ["name"] = "Test Org"
            });

            Assert.AreEqual(1, LayersTestMode.GroupCalls.Count);
            Assert.AreEqual("org_456", LayersTestMode.GroupCalls[0].groupId);
        }

        // ── Reset Clears All ──────────────────────────────────────────────

        [Test]
        public void Reset_ClearsAllCapturedData()
        {
            LayersTestMode.Enable();
            Layers.Initialize(new LayersConfig { AppId = "test-reset" });

            Layers.Track("event_1");
            Layers.Screen("Screen1");
            Layers.Identify("user_1");
            Layers.Group("group_1");

            LayersTestMode.Reset();

            Assert.AreEqual(0, LayersTestMode.TrackedEvents.Count);
            Assert.AreEqual(0, LayersTestMode.ScreenedEvents.Count);
            Assert.AreEqual(0, LayersTestMode.IdentifyCalls.Count);
            Assert.AreEqual(0, LayersTestMode.GroupCalls.Count);
            Assert.AreEqual(0, LayersTestMode.DeviceContextCalls.Count);
            Assert.AreEqual(0, LayersTestMode.UserPropertiesCalls.Count);
            Assert.AreEqual(0, LayersTestMode.ConsentCalls.Count);
            Assert.AreEqual(0, LayersTestMode.FlushCount);
        }

        // ── Consent Capture ───────────────────────────────────────────────

        [Test]
        public void SetConsent_CapturedByTestMode()
        {
            LayersTestMode.Enable();
            Layers.Initialize(new LayersConfig { AppId = "test-consent" });
            LayersTestMode.Reset();

            Layers.SetConsent(analytics: true, advertising: false);

            Assert.AreEqual(1, LayersTestMode.ConsentCalls.Count);
            Assert.That(LayersTestMode.ConsentCalls[0], Does.Contain("\"analytics\":true"));
            Assert.That(LayersTestMode.ConsentCalls[0], Does.Contain("\"advertising\":false"));
        }

        // ── Flush Count ───────────────────────────────────────────────────

        [Test]
        public void Flush_IncreasesFlushCount()
        {
            LayersTestMode.Enable();
            Layers.Initialize(new LayersConfig { AppId = "test-flush" });
            LayersTestMode.Reset();

            Layers.Flush();

            Assert.AreEqual(1, LayersTestMode.FlushCount);
        }

        // ── SetUserProperties Capture ─────────────────────────────────────

        [Test]
        public void SetUserProperties_CapturedByTestMode()
        {
            LayersTestMode.Enable();
            Layers.Initialize(new LayersConfig { AppId = "test-user-props" });
            LayersTestMode.Reset();

            Layers.SetUserProperties(new Dictionary<string, object>
            {
                ["plan"] = "premium"
            });

            Assert.AreEqual(1, LayersTestMode.UserPropertiesCalls.Count);
            Assert.That(LayersTestMode.UserPropertiesCalls[0],
                Does.Contain("\"plan\":\"premium\""));
        }
    }
}

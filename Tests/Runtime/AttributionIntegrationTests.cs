using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using Layers.Unity;
using Layers.Unity.Internal;

namespace Layers.Unity.Tests
{
    [TestFixture]
    public class AttributionIntegrationTests
    {
        [SetUp]
        public void SetUp()
        {
            LayersTestMode.Enable();
            Layers.Initialize(new LayersConfig { AppId = "test-attribution" });
            LayersTestMode.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up PlayerPrefs keys used by SetAttributionData
            UnityEngine.PlayerPrefs.DeleteKey("layers_attribution_deeplink_id");
            UnityEngine.PlayerPrefs.DeleteKey("layers_attribution_gclid");
            UnityEngine.PlayerPrefs.Save();

            Layers.Shutdown();
            LayersTestMode.Disable();
        }

        // ── SetAttributionData with gclid ─────────────────────────────────

        [Test]
        public void SetAttributionData_WithGclid_SetsDeviceContext()
        {
            Layers.SetAttributionData(gclid: "CL123_abc");

            var ctxCalls = LayersTestMode.DeviceContextCalls;
            Assert.IsTrue(ctxCalls.Count > 0, "Expected at least one DeviceContext call");

            string lastCtx = ctxCalls[ctxCalls.Count - 1];
            Assert.That(lastCtx, Does.Contain("\"gclid\":\"CL123_abc\""));
        }

        [Test]
        public void SetAttributionData_WithDeeplinkId_SetsDeviceContext()
        {
            Layers.SetAttributionData(deeplinkId: "dl_456");

            var ctxCalls = LayersTestMode.DeviceContextCalls;
            Assert.IsTrue(ctxCalls.Count > 0, "Expected at least one DeviceContext call");

            string lastCtx = ctxCalls[ctxCalls.Count - 1];
            Assert.That(lastCtx, Does.Contain("\"deeplink_id\":\"dl_456\""));
        }

        [Test]
        public void SetAttributionData_WithBothFields_SetsDeviceContext()
        {
            Layers.SetAttributionData(deeplinkId: "dl_789", gclid: "CL_xyz");

            var ctxCalls = LayersTestMode.DeviceContextCalls;
            Assert.IsTrue(ctxCalls.Count > 0);

            string lastCtx = ctxCalls[ctxCalls.Count - 1];
            Assert.That(lastCtx, Does.Contain("\"deeplink_id\":\"dl_789\""));
            Assert.That(lastCtx, Does.Contain("\"gclid\":\"CL_xyz\""));
        }

        [Test]
        public void SetAttributionData_PersistsToPlayerPrefs()
        {
            Layers.SetAttributionData(gclid: "CL_persist");

            string stored = UnityEngine.PlayerPrefs.GetString("layers_attribution_gclid", null);
            Assert.AreEqual("CL_persist", stored);
        }

        [Test]
        public void SetAttributionData_NullGclid_ClearsPlayerPrefs()
        {
            // First set a value
            UnityEngine.PlayerPrefs.SetString("layers_attribution_gclid", "old_value");
            UnityEngine.PlayerPrefs.Save();

            // Now clear it
            Layers.SetAttributionData(gclid: null);

            // Should be deleted
            Assert.IsFalse(UnityEngine.PlayerPrefs.HasKey("layers_attribution_gclid"),
                "gclid should be cleared from PlayerPrefs when null is passed");
        }

        [Test]
        public void SetAttributionData_NullDeeplinkId_ClearsPlayerPrefs()
        {
            UnityEngine.PlayerPrefs.SetString("layers_attribution_deeplink_id", "old_dl");
            UnityEngine.PlayerPrefs.Save();

            Layers.SetAttributionData(deeplinkId: null);

            Assert.IsFalse(UnityEngine.PlayerPrefs.HasKey("layers_attribution_deeplink_id"),
                "deeplink_id should be cleared from PlayerPrefs when null is passed");
        }

        // ── SetAttributionData requires initialization ────────────────────

        [Test]
        public void SetAttributionData_BeforeInit_RaisesError()
        {
            Layers.Shutdown();
            LayersTestMode.Disable();

            // Re-enable test mode but do NOT initialize
            LayersTestMode.Enable();

            bool errorRaised = false;
            Layers.OnError += (method, msg) =>
            {
                if (method == "SetAttributionData") errorRaised = true;
            };

            Layers.SetAttributionData(gclid: "should_fail");

            Assert.IsTrue(errorRaised, "SetAttributionData should raise error when not initialized");

            // Re-initialize for TearDown
            Layers.Initialize(new LayersConfig { AppId = "test-attribution" });
        }
    }
}

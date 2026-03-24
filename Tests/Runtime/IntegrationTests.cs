using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using Layers.Unity;
using Layers.Unity.Internal;

namespace Layers.Unity.Tests
{
    [TestFixture]
    public class IntegrationTests
    {
        [SetUp]
        public void SetUp()
        {
            LayersTestMode.Enable();
            Layers.Initialize(new LayersConfig { AppId = "test-integrations" });
            LayersTestMode.Reset();
            RevenueCatIntegration.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            RevenueCatIntegration.Reset();
            Layers.Shutdown();
            LayersTestMode.Disable();
        }

        // ── Helper ────────────────────────────────────────────────────────

        private (string eventName, string propertiesJson) FindLastEvent(string name)
        {
            return LayersTestMode.TrackedEvents.LastOrDefault(e => e.eventName == name);
        }

        private List<(string eventName, string propertiesJson)> FindAllEvents(string name)
        {
            return LayersTestMode.TrackedEvents.Where(e => e.eventName == name).ToList();
        }

        // ══════════════════════════════════════════════════════════════════
        // Superwall Integration
        // ══════════════════════════════════════════════════════════════════

        [Test]
        public void Superwall_TrackPresentation_FiresPaywallShowEvent()
        {
            SuperwallIntegration.TrackPresentation("pw_001", "main_paywall");

            var evt = FindLastEvent("paywall_show");
            Assert.AreEqual("paywall_show", evt.eventName);
            Assert.That(evt.propertiesJson, Does.Contain("\"paywall_id\":\"pw_001\""));
            Assert.That(evt.propertiesJson, Does.Contain("\"placement\":\"main_paywall\""));
        }

        [Test]
        public void Superwall_TrackPresentation_WithAbTest()
        {
            SuperwallIntegration.TrackPresentation(
                "pw_002", "main",
                experimentId: "exp_1", variantId: "var_b");

            var evt = FindLastEvent("paywall_show");
            Assert.That(evt.propertiesJson, Does.Contain("\"ab_test\":{"));
            Assert.That(evt.propertiesJson, Does.Contain("\"id\":\"exp_1\""));
            Assert.That(evt.propertiesJson, Does.Contain("\"variant\":\"var_b\""));
        }

        [Test]
        public void Superwall_TrackPresentation_NullPaywallId_UsesUnknown()
        {
            SuperwallIntegration.TrackPresentation(null);

            var evt = FindLastEvent("paywall_show");
            Assert.That(evt.propertiesJson, Does.Contain("\"paywall_id\":\"unknown\""));
        }

        [Test]
        public void Superwall_TrackDismiss_FiresPaywallDismissEvent()
        {
            SuperwallIntegration.TrackDismiss("pw_001");

            var evt = FindLastEvent("paywall_dismiss");
            Assert.AreEqual("paywall_dismiss", evt.eventName);
            Assert.That(evt.propertiesJson, Does.Contain("\"paywall_id\":\"pw_001\""));
        }

        [Test]
        public void Superwall_TrackPurchase_FiresPaywallPurchaseEvent()
        {
            SuperwallIntegration.TrackPurchase("pw_001", "premium_monthly", 9.99, "USD");

            var evt = FindLastEvent("paywall_purchase");
            Assert.AreEqual("paywall_purchase", evt.eventName);
            Assert.That(evt.propertiesJson, Does.Contain("\"paywall_id\":\"pw_001\""));
            Assert.That(evt.propertiesJson, Does.Contain("\"product_id\":\"premium_monthly\""));
            Assert.That(evt.propertiesJson, Does.Contain("\"source\":\"superwall\""));
        }

        [Test]
        public void Superwall_TrackSkip_FiresPaywallSkipEvent()
        {
            SuperwallIntegration.TrackSkip("pw_001", "no_rule_match");

            var evt = FindLastEvent("paywall_skip");
            Assert.AreEqual("paywall_skip", evt.eventName);
            Assert.That(evt.propertiesJson, Does.Contain("\"paywall_id\":\"pw_001\""));
            Assert.That(evt.propertiesJson, Does.Contain("\"reason\":\"no_rule_match\""));
        }

        [Test]
        public void Superwall_TrackSkip_NullReason_UsesUnknown()
        {
            SuperwallIntegration.TrackSkip("pw_001", null);

            var evt = FindLastEvent("paywall_skip");
            Assert.That(evt.propertiesJson, Does.Contain("\"reason\":\"unknown\""));
        }

        [Test]
        public void Superwall_OnEvent_ForwardsGenericEvent()
        {
            SuperwallIntegration.OnEvent("custom_paywall_event",
                new Dictionary<string, object> { ["key"] = "value" });

            var evt = FindLastEvent("custom_paywall_event");
            Assert.AreEqual("custom_paywall_event", evt.eventName);
        }

        [Test]
        public void Superwall_OnEvent_NullEventName_DoesNotTrack()
        {
            int countBefore = LayersTestMode.TrackedEvents.Count;
            SuperwallIntegration.OnEvent(null);
            Assert.AreEqual(countBefore, LayersTestMode.TrackedEvents.Count);
        }

        [Test]
        public void Superwall_UserAttributes_ReturnsSessionId()
        {
            var attrs = SuperwallIntegration.UserAttributes();
            Assert.IsTrue(attrs.ContainsKey("layers_session_id"));
        }

        [Test]
        public void Superwall_UserAttributes_WithIdentifiedUser_ReturnsUserId()
        {
            Layers.Identify("user_sw_123");

            var attrs = SuperwallIntegration.UserAttributes();
            Assert.AreEqual("user_sw_123", attrs["layers_user_id"]);
        }

        // ══════════════════════════════════════════════════════════════════
        // RevenueCat Integration
        // ══════════════════════════════════════════════════════════════════

        [Test]
        public void RevenueCat_TrackPurchase_FiresPurchaseSuccessEvent()
        {
            RevenueCatIntegration.TrackPurchase("premium_monthly", 9.99, "USD");

            var evt = FindLastEvent("purchase_success");
            Assert.AreEqual("purchase_success", evt.eventName);
            Assert.That(evt.propertiesJson, Does.Contain("\"product_id\":\"premium_monthly\""));
            Assert.That(evt.propertiesJson, Does.Contain("\"source\":\"revenuecat\""));
        }

        [Test]
        public void RevenueCat_TrackPurchase_WithStore()
        {
            RevenueCatIntegration.TrackPurchase("premium_monthly", 9.99, "USD", "app_store");

            var evt = FindLastEvent("purchase_success");
            Assert.That(evt.propertiesJson, Does.Contain("\"store\":\"app_store\""));
        }

        [Test]
        public void RevenueCat_TrackPurchase_NullProductId_DoesNotTrack()
        {
            int countBefore = LayersTestMode.TrackedEvents.Count;
            RevenueCatIntegration.TrackPurchase(null, 9.99, "USD");
            Assert.AreEqual(countBefore, LayersTestMode.TrackedEvents.Count);
        }

        [Test]
        public void RevenueCat_SyncAttributes_SetsUserProperties()
        {
            RevenueCatIntegration.SyncAttributes(true, "rc_user_123");

            Assert.IsTrue(LayersTestMode.UserPropertiesCalls.Count > 0);
            string lastProps = LayersTestMode.UserPropertiesCalls.Last();
            Assert.That(lastProps, Does.Contain("\"is_subscriber\":true"));
            Assert.That(lastProps, Does.Contain("\"revenuecat_original_app_user_id\":\"rc_user_123\""));
        }

        [Test]
        public void RevenueCat_SyncAttributes_NotSubscriber_SetsFalse()
        {
            RevenueCatIntegration.SyncAttributes(false);

            string lastProps = LayersTestMode.UserPropertiesCalls.Last();
            Assert.That(lastProps, Does.Contain("\"is_subscriber\":false"));
        }

        [Test]
        public void RevenueCat_OnCustomerInfoUpdated_NewSubscription_TracksSubscriptionStart()
        {
            // First call initializes the state
            RevenueCatIntegration.OnCustomerInfoUpdated(new List<string>());

            // Second call with a new subscription should track it
            LayersTestMode.Reset();
            RevenueCatIntegration.OnCustomerInfoUpdated(
                new List<string> { "annual_premium" });

            var evt = FindLastEvent("subscription_start");
            Assert.AreEqual("subscription_start", evt.eventName);
            Assert.That(evt.propertiesJson, Does.Contain("\"product_id\":\"annual_premium\""));
            Assert.That(evt.propertiesJson, Does.Contain("\"source\":\"revenuecat\""));
        }

        [Test]
        public void RevenueCat_OnCustomerInfoUpdated_ExistingSubscription_DoesNotRetrack()
        {
            // Initialize with an existing subscription
            RevenueCatIntegration.OnCustomerInfoUpdated(
                new List<string> { "annual_premium" });

            LayersTestMode.Reset();

            // Same subscription on next update -- should NOT fire subscription_start
            RevenueCatIntegration.OnCustomerInfoUpdated(
                new List<string> { "annual_premium" });

            var subscriptionEvents = FindAllEvents("subscription_start");
            Assert.AreEqual(0, subscriptionEvents.Count,
                "Existing subscriptions should not fire subscription_start again");
        }

        [Test]
        public void RevenueCat_OnCustomerInfoUpdated_MultipleNew_TracksEach()
        {
            // Initialize empty
            RevenueCatIntegration.OnCustomerInfoUpdated(new List<string>());

            LayersTestMode.Reset();

            // Add multiple new subscriptions
            RevenueCatIntegration.OnCustomerInfoUpdated(
                new List<string> { "monthly", "add_on_storage" });

            var subscriptionEvents = FindAllEvents("subscription_start");
            Assert.AreEqual(2, subscriptionEvents.Count);
        }

        [Test]
        public void RevenueCat_Reset_ClearsState()
        {
            RevenueCatIntegration.OnCustomerInfoUpdated(
                new List<string> { "annual_premium" });

            RevenueCatIntegration.Reset();

            LayersTestMode.Reset();

            // After reset, initial call should NOT track (it's re-initializing)
            RevenueCatIntegration.OnCustomerInfoUpdated(
                new List<string> { "annual_premium" });

            var subscriptionEvents = FindAllEvents("subscription_start");
            Assert.AreEqual(0, subscriptionEvents.Count,
                "After reset, first call is initialization -- no subscription_start events");
        }
    }
}

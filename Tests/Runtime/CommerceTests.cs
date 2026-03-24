using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using Layers.Unity;
using Layers.Unity.Internal;

namespace Layers.Unity.Tests
{
    [TestFixture]
    public class CommerceTests
    {
        [SetUp]
        public void SetUp()
        {
            LayersTestMode.Enable();
            Layers.Initialize(new LayersConfig { AppId = "test-commerce" });
            LayersTestMode.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            Layers.Shutdown();
            LayersTestMode.Disable();
        }

        // ── Helper ────────────────────────────────────────────────────────

        /// <summary>
        /// Find the last tracked event with the given name (skipping init events).
        /// </summary>
        private (string eventName, string propertiesJson) FindLastEvent(string name)
        {
            return LayersTestMode.TrackedEvents.LastOrDefault(e => e.eventName == name);
        }

        /// <summary>
        /// Check that a JSON string contains a given key-value pair.
        /// Uses simple string matching -- good enough for flat property bags.
        /// </summary>
        private void AssertJsonContains(string json, string key, string expectedValue)
        {
            Assert.IsNotNull(json, $"Properties JSON should not be null for key '{key}'");
            Assert.That(json, Does.Contain($"\"{key}\":\"{expectedValue}\""),
                $"Expected JSON to contain \"{key}\":\"{expectedValue}\", got: {json}");
        }

        private void AssertJsonContainsNumber(string json, string key, double expectedValue)
        {
            Assert.IsNotNull(json, $"Properties JSON should not be null for key '{key}'");
            // Handle both integer and float representations
            string intRepr = $"\"{key}\":{(int)expectedValue}";
            string floatRepr = $"\"{key}\":{expectedValue}";
            Assert.IsTrue(json.Contains(intRepr) || json.Contains(floatRepr),
                $"Expected JSON to contain \"{key}\":{expectedValue}, got: {json}");
        }

        // ── TrackPurchase ─────────────────────────────────────────────────

        [Test]
        public void TrackPurchase_FiresPurchaseSuccessEvent()
        {
            Commerce.TrackPurchase(9.99, "USD");

            var evt = FindLastEvent("purchase_success");
            Assert.AreEqual("purchase_success", evt.eventName);
        }

        [Test]
        public void TrackPurchase_IncludesPrice()
        {
            Commerce.TrackPurchase(9.99, "USD");

            var evt = FindLastEvent("purchase_success");
            AssertJsonContainsNumber(evt.propertiesJson, "price", 9.99);
        }

        [Test]
        public void TrackPurchase_IncludesCurrency()
        {
            Commerce.TrackPurchase(9.99, "EUR");

            var evt = FindLastEvent("purchase_success");
            AssertJsonContains(evt.propertiesJson, "currency", "EUR");
        }

        [Test]
        public void TrackPurchase_IncludesRevenue()
        {
            Commerce.TrackPurchase(10.0, "USD", quantity: 3);

            var evt = FindLastEvent("purchase_success");
            // Revenue = price * quantity = 10 * 3 = 30
            AssertJsonContainsNumber(evt.propertiesJson, "revenue", 30);
        }

        [Test]
        public void TrackPurchase_WithProductId()
        {
            Commerce.TrackPurchase(9.99, "USD", productId: "sku_abc");

            var evt = FindLastEvent("purchase_success");
            AssertJsonContains(evt.propertiesJson, "product_id", "sku_abc");
        }

        [Test]
        public void TrackPurchase_WithTransactionId()
        {
            Commerce.TrackPurchase(9.99, "USD", transactionId: "txn_123");

            var evt = FindLastEvent("purchase_success");
            AssertJsonContains(evt.propertiesJson, "transaction_id", "txn_123");
        }

        [Test]
        public void TrackPurchase_WithStore()
        {
            Commerce.TrackPurchase(9.99, "USD", store: "steam");

            var evt = FindLastEvent("purchase_success");
            AssertJsonContains(evt.propertiesJson, "store", "steam");
        }

        [Test]
        public void TrackPurchase_WithIsRestored()
        {
            Commerce.TrackPurchase(9.99, "USD", isRestored: true);

            var evt = FindLastEvent("purchase_success");
            Assert.That(evt.propertiesJson, Does.Contain("\"is_restored\":true"));
        }

        // ── TrackPurchaseFailed ───────────────────────────────────────────

        [Test]
        public void TrackPurchaseFailed_FiresPurchaseFailedEvent()
        {
            Commerce.TrackPurchaseFailed("sku_abc", "USD");

            var evt = FindLastEvent("purchase_failed");
            Assert.AreEqual("purchase_failed", evt.eventName);
        }

        [Test]
        public void TrackPurchaseFailed_IncludesErrorCode()
        {
            Commerce.TrackPurchaseFailed("sku_abc", "USD", errorCode: "E_DECLINED");

            var evt = FindLastEvent("purchase_failed");
            AssertJsonContains(evt.propertiesJson, "error_code", "E_DECLINED");
        }

        [Test]
        public void TrackPurchaseFailed_IncludesErrorMessage()
        {
            Commerce.TrackPurchaseFailed("sku_abc", "USD",
                errorMessage: "Card declined");

            var evt = FindLastEvent("purchase_failed");
            AssertJsonContains(evt.propertiesJson, "error_message", "Card declined");
        }

        // ── TrackSubscription ─────────────────────────────────────────────

        [Test]
        public void TrackSubscription_FiresSubscribeEvent()
        {
            Commerce.TrackSubscription(49.99, "USD", "pro_annual");

            var evt = FindLastEvent("subscribe");
            Assert.AreEqual("subscribe", evt.eventName);
        }

        [Test]
        public void TrackSubscription_IncludesProductId()
        {
            Commerce.TrackSubscription(49.99, "USD", "pro_annual");

            var evt = FindLastEvent("subscribe");
            AssertJsonContains(evt.propertiesJson, "product_id", "pro_annual");
        }

        [Test]
        public void TrackSubscription_WithPeriod()
        {
            Commerce.TrackSubscription(49.99, "USD", "pro_annual", period: "P1Y");

            var evt = FindLastEvent("subscribe");
            AssertJsonContains(evt.propertiesJson, "period", "P1Y");
        }

        [Test]
        public void TrackSubscription_WithRenewalFlag()
        {
            Commerce.TrackSubscription(49.99, "USD", "pro_annual", isRenewal: true);

            var evt = FindLastEvent("subscribe");
            Assert.That(evt.propertiesJson, Does.Contain("\"is_renewal\":true"));
        }

        [Test]
        public void TrackSubscription_WithTrialFlag()
        {
            Commerce.TrackSubscription(0, "USD", "pro_trial", isTrial: true);

            var evt = FindLastEvent("subscribe");
            Assert.That(evt.propertiesJson, Does.Contain("\"is_trial\":true"));
        }

        // ── TrackOrder ────────────────────────────────────────────────────

        [Test]
        public void TrackOrder_FiresPurchaseSuccessEvent()
        {
            Commerce.TrackOrder("order_456", 100.0);

            var evt = FindLastEvent("purchase_success");
            Assert.AreEqual("purchase_success", evt.eventName);
        }

        [Test]
        public void TrackOrder_IncludesOrderId()
        {
            Commerce.TrackOrder("order_456", 100.0);

            var evt = FindLastEvent("purchase_success");
            AssertJsonContains(evt.propertiesJson, "order_id", "order_456");
        }

        [Test]
        public void TrackOrder_CalculatesTotalWithTaxShippingDiscount()
        {
            Commerce.TrackOrder("order_789", 100.0,
                tax: 8.0, shipping: 5.0, discount: 10.0);

            var evt = FindLastEvent("purchase_success");
            // total = 100 + 8 + 5 - 10 = 103
            AssertJsonContainsNumber(evt.propertiesJson, "total", 103);
        }

        [Test]
        public void TrackOrder_IncludesCouponCode()
        {
            Commerce.TrackOrder("order_789", 100.0, couponCode: "SAVE10");

            var evt = FindLastEvent("purchase_success");
            AssertJsonContains(evt.propertiesJson, "coupon_code", "SAVE10");
        }

        // ── TrackAddToCart ────────────────────────────────────────────────

        [Test]
        public void TrackAddToCart_FiresAddToCartEvent()
        {
            Commerce.TrackAddToCart("sku_1", "Widget", 19.99);

            var evt = FindLastEvent("add_to_cart");
            Assert.AreEqual("add_to_cart", evt.eventName);
        }

        [Test]
        public void TrackAddToCart_IncludesProductFields()
        {
            Commerce.TrackAddToCart("sku_1", "Widget", 19.99);

            var evt = FindLastEvent("add_to_cart");
            AssertJsonContains(evt.propertiesJson, "product_id", "sku_1");
            AssertJsonContains(evt.propertiesJson, "product_name", "Widget");
        }

        [Test]
        public void TrackAddToCart_CalculatesValue()
        {
            Commerce.TrackAddToCart("sku_1", "Widget", 10.0, quantity: 3);

            var evt = FindLastEvent("add_to_cart");
            // value = price * quantity = 10 * 3 = 30
            AssertJsonContainsNumber(evt.propertiesJson, "value", 30);
        }

        [Test]
        public void TrackAddToCart_WithCategory()
        {
            Commerce.TrackAddToCart("sku_1", "Widget", 19.99, category: "gadgets");

            var evt = FindLastEvent("add_to_cart");
            AssertJsonContains(evt.propertiesJson, "category", "gadgets");
        }

        // ── TrackRemoveFromCart ───────────────────────────────────────────

        [Test]
        public void TrackRemoveFromCart_FiresRemoveFromCartEvent()
        {
            Commerce.TrackRemoveFromCart("sku_2", "Gizmo", 29.99);

            var evt = FindLastEvent("remove_from_cart");
            Assert.AreEqual("remove_from_cart", evt.eventName);
        }

        [Test]
        public void TrackRemoveFromCart_IncludesProductId()
        {
            Commerce.TrackRemoveFromCart("sku_2", "Gizmo", 29.99);

            var evt = FindLastEvent("remove_from_cart");
            AssertJsonContains(evt.propertiesJson, "product_id", "sku_2");
        }

        // ── TrackBeginCheckout ───────────────────────────────────────────

        [Test]
        public void TrackBeginCheckout_FiresBeginCheckoutEvent()
        {
            Commerce.TrackBeginCheckout(150.0);

            var evt = FindLastEvent("begin_checkout");
            Assert.AreEqual("begin_checkout", evt.eventName);
        }

        [Test]
        public void TrackBeginCheckout_WithItemCount()
        {
            Commerce.TrackBeginCheckout(150.0, itemCount: 5);

            var evt = FindLastEvent("begin_checkout");
            Assert.That(evt.propertiesJson, Does.Contain("\"item_count\":5"));
        }

        // ── TrackViewProduct ─────────────────────────────────────────────

        [Test]
        public void TrackViewProduct_FiresViewItemEvent()
        {
            Commerce.TrackViewProduct("sku_99", "Mega Widget", 49.99);

            var evt = FindLastEvent("view_item");
            Assert.AreEqual("view_item", evt.eventName);
        }

        [Test]
        public void TrackViewProduct_IncludesFields()
        {
            Commerce.TrackViewProduct("sku_99", "Mega Widget", 49.99, "GBP", "widgets");

            var evt = FindLastEvent("view_item");
            AssertJsonContains(evt.propertiesJson, "product_id", "sku_99");
            AssertJsonContains(evt.propertiesJson, "product_name", "Mega Widget");
            AssertJsonContains(evt.propertiesJson, "currency", "GBP");
            AssertJsonContains(evt.propertiesJson, "category", "widgets");
        }

        // ── TrackRefund ──────────────────────────────────────────────────

        [Test]
        public void TrackRefund_FiresRefundEvent()
        {
            Commerce.TrackRefund("txn_123", 9.99, "USD");

            var evt = FindLastEvent("refund");
            Assert.AreEqual("refund", evt.eventName);
        }

        [Test]
        public void TrackRefund_IncludesTransactionId()
        {
            Commerce.TrackRefund("txn_123", 9.99, "USD");

            var evt = FindLastEvent("refund");
            AssertJsonContains(evt.propertiesJson, "transaction_id", "txn_123");
        }

        [Test]
        public void TrackRefund_WithReason()
        {
            Commerce.TrackRefund("txn_123", 9.99, "USD", reason: "defective");

            var evt = FindLastEvent("refund");
            AssertJsonContains(evt.propertiesJson, "reason", "defective");
        }
    }
}

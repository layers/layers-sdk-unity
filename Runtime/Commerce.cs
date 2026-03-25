using System.Collections.Generic;

namespace Layers.Unity
{
    /// <summary>
    /// Commerce tracking module for the Layers Unity SDK.
    ///
    /// Provides typed helper methods for purchase, subscription, cart, checkout,
    /// product view, and refund events. All methods delegate to
    /// <see cref="LayersSDK.Track"/> with standardized event names and properties
    /// consistent with the iOS CommerceModule, Android CommerceModule, Web commerce
    /// module, and React Native commerce helpers.
    ///
    /// v2.0.0 note: uses <c>price</c> (not <c>revenue</c>) as the primary
    /// currency field, matching the cross-platform rename.
    ///
    /// Usage:
    /// <code>
    /// Commerce.TrackPurchase(9.99, "USD", productId: "premium_monthly", transactionId: "txn_123");
    /// Commerce.TrackSubscription(49.99, "USD", "pro_annual", period: "P1Y");
    /// Commerce.TrackAddToCart("sku_456", "Widget", 19.99);
    /// </code>
    /// </summary>
    public static class Commerce
    {
        // ── Purchase Tracking ───────────────────────────────────────────

        /// <summary>
        /// Track a successful purchase.
        /// </summary>
        /// <param name="price">Unit price of the item.</param>
        /// <param name="currency">Currency code (e.g. "USD").</param>
        /// <param name="productId">Optional product identifier.</param>
        /// <param name="transactionId">Optional transaction identifier.</param>
        /// <param name="quantity">Quantity purchased. Default: 1.</param>
        /// <param name="isRestored">Whether this is a restored purchase. Default: false.</param>
        /// <param name="store">Optional store name (e.g. "apple", "google", "steam").</param>
        public static void TrackPurchase(
            double price,
            string currency,
            string productId = null,
            string transactionId = null,
            int quantity = 1,
            bool isRestored = false,
            string store = null)
        {
            var props = new Dictionary<string, object>
            {
                ["price"] = price,
                ["currency"] = currency,
                ["quantity"] = quantity,
                ["revenue"] = price * quantity
            };

            if (productId != null) props["product_id"] = productId;
            if (transactionId != null) props["transaction_id"] = transactionId;
            if (isRestored) props["is_restored"] = true;
            if (store != null) props["store"] = store;

            LayersSDK.Track("purchase_success", props);
        }

        /// <summary>
        /// Track a failed purchase attempt.
        /// </summary>
        /// <param name="productId">The product identifier.</param>
        /// <param name="currency">Currency code.</param>
        /// <param name="errorCode">Optional error code.</param>
        /// <param name="errorMessage">Optional human-readable error message.</param>
        public static void TrackPurchaseFailed(
            string productId,
            string currency,
            string errorCode = null,
            string errorMessage = null)
        {
            var props = new Dictionary<string, object>
            {
                ["product_id"] = productId,
                ["currency"] = currency
            };

            if (errorCode != null) props["error_code"] = errorCode;
            if (errorMessage != null) props["error_message"] = errorMessage;

            LayersSDK.Track("purchase_failed", props);
        }

        // ── Subscription Tracking ───────────────────────────────────────

        /// <summary>
        /// Track a subscription purchase or renewal.
        /// </summary>
        /// <param name="price">Subscription price.</param>
        /// <param name="currency">Currency code.</param>
        /// <param name="productId">Product/plan identifier.</param>
        /// <param name="period">Optional subscription period (e.g. "P1M", "P1Y").</param>
        /// <param name="transactionId">Optional transaction identifier.</param>
        /// <param name="isRenewal">Whether this is a renewal. Default: false.</param>
        /// <param name="isTrial">Whether this is a trial subscription. Default: false.</param>
        /// <param name="subscriptionGroupId">Optional subscription group identifier.</param>
        /// <param name="originalTransactionId">Optional original transaction ID (for renewals).</param>
        public static void TrackSubscription(
            double price,
            string currency,
            string productId,
            string period = null,
            string transactionId = null,
            bool isRenewal = false,
            bool isTrial = false,
            string subscriptionGroupId = null,
            string originalTransactionId = null)
        {
            var props = new Dictionary<string, object>
            {
                ["product_id"] = productId,
                ["price"] = price,
                ["currency"] = currency,
                ["quantity"] = 1,
                ["revenue"] = price
            };

            if (transactionId != null) props["transaction_id"] = transactionId;
            if (period != null) props["period"] = period;
            if (isRenewal) props["is_renewal"] = true;
            if (isTrial) props["is_trial"] = true;
            if (subscriptionGroupId != null) props["subscription_group_id"] = subscriptionGroupId;
            if (originalTransactionId != null) props["original_transaction_id"] = originalTransactionId;

            LayersSDK.Track("subscribe", props);
        }

        // ── Order Tracking ──────────────────────────────────────────────

        /// <summary>
        /// Track a completed order with a total value.
        /// </summary>
        /// <param name="orderId">Order identifier.</param>
        /// <param name="subtotal">Subtotal before tax/shipping/discounts.</param>
        /// <param name="currency">Currency code. Default: "USD".</param>
        /// <param name="tax">Optional tax amount.</param>
        /// <param name="shipping">Optional shipping cost.</param>
        /// <param name="discount">Optional discount amount.</param>
        /// <param name="couponCode">Optional coupon/promo code.</param>
        /// <param name="itemCount">Optional number of items in the order.</param>
        public static void TrackOrder(
            string orderId,
            double subtotal,
            string currency = "USD",
            double? tax = null,
            double? shipping = null,
            double? discount = null,
            string couponCode = null,
            int? itemCount = null)
        {
            double total = subtotal;
            if (tax.HasValue) total += tax.Value;
            if (shipping.HasValue) total += shipping.Value;
            if (discount.HasValue) total -= discount.Value;

            var props = new Dictionary<string, object>
            {
                ["order_id"] = orderId,
                ["subtotal"] = subtotal,
                ["total"] = total,
                ["currency"] = currency,
                ["revenue"] = total
            };

            if (tax.HasValue) props["tax"] = tax.Value;
            if (shipping.HasValue) props["shipping"] = shipping.Value;
            if (discount.HasValue) props["discount"] = discount.Value;
            if (couponCode != null) props["coupon_code"] = couponCode;
            if (itemCount.HasValue) props["item_count"] = itemCount.Value;

            LayersSDK.Track("purchase_success", props);
        }

        // ── Cart Tracking ───────────────────────────────────────────────

        /// <summary>
        /// Track an item being added to the cart.
        /// </summary>
        /// <param name="productId">Product identifier.</param>
        /// <param name="productName">Product name.</param>
        /// <param name="price">Unit price.</param>
        /// <param name="quantity">Quantity added. Default: 1.</param>
        /// <param name="category">Optional product category.</param>
        public static void TrackAddToCart(
            string productId,
            string productName,
            double price,
            int quantity = 1,
            string category = null)
        {
            var props = new Dictionary<string, object>
            {
                ["product_id"] = productId,
                ["product_name"] = productName,
                ["price"] = price,
                ["quantity"] = quantity,
                ["value"] = price * quantity
            };

            if (category != null) props["category"] = category;

            LayersSDK.Track("add_to_cart", props);
        }

        /// <summary>
        /// Track an item being removed from the cart.
        /// </summary>
        /// <param name="productId">Product identifier.</param>
        /// <param name="productName">Product name.</param>
        /// <param name="price">Unit price.</param>
        /// <param name="quantity">Quantity removed. Default: 1.</param>
        /// <param name="category">Optional product category.</param>
        public static void TrackRemoveFromCart(
            string productId,
            string productName,
            double price,
            int quantity = 1,
            string category = null)
        {
            var props = new Dictionary<string, object>
            {
                ["product_id"] = productId,
                ["product_name"] = productName,
                ["price"] = price,
                ["quantity"] = quantity
            };

            if (category != null) props["category"] = category;

            LayersSDK.Track("remove_from_cart", props);
        }

        // ── Checkout Tracking ───────────────────────────────────────────

        /// <summary>
        /// Track beginning the checkout flow.
        /// </summary>
        /// <param name="value">Total checkout value.</param>
        /// <param name="currency">Currency code. Default: "USD".</param>
        /// <param name="itemCount">Optional number of items being checked out.</param>
        public static void TrackBeginCheckout(
            double value,
            string currency = "USD",
            int? itemCount = null)
        {
            var props = new Dictionary<string, object>
            {
                ["value"] = value,
                ["currency"] = currency
            };

            if (itemCount.HasValue) props["item_count"] = itemCount.Value;

            LayersSDK.Track("begin_checkout", props);
        }

        // ── Product View Tracking ───────────────────────────────────────

        /// <summary>
        /// Track viewing a product detail page.
        /// </summary>
        /// <param name="productId">Product identifier.</param>
        /// <param name="productName">Product name.</param>
        /// <param name="price">Product price.</param>
        /// <param name="currency">Currency code. Default: "USD".</param>
        /// <param name="category">Optional product category.</param>
        public static void TrackViewProduct(
            string productId,
            string productName,
            double price,
            string currency = "USD",
            string category = null)
        {
            var props = new Dictionary<string, object>
            {
                ["product_id"] = productId,
                ["product_name"] = productName,
                ["price"] = price,
                ["currency"] = currency
            };

            if (category != null) props["category"] = category;

            LayersSDK.Track("view_item", props);
        }

        // ── Refund Tracking ─────────────────────────────────────────────

        /// <summary>
        /// Track a refund.
        /// </summary>
        /// <param name="transactionId">Original transaction identifier.</param>
        /// <param name="amount">Refund amount.</param>
        /// <param name="currency">Currency code.</param>
        /// <param name="reason">Optional refund reason.</param>
        public static void TrackRefund(
            string transactionId,
            double amount,
            string currency,
            string reason = null)
        {
            var props = new Dictionary<string, object>
            {
                ["transaction_id"] = transactionId,
                ["amount"] = amount,
                ["currency"] = currency
            };

            if (reason != null) props["reason"] = reason;

            LayersSDK.Track("refund", props);
        }
    }
}

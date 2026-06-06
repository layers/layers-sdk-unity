using System.Collections.Generic;

namespace Layers.Unity
{
    /// <summary>
    /// Standard event types for Layers Analytics.
    ///
    /// Two layers:
    ///
    /// 1. <b>System events</b> (<c>Layers*</c> with <c>$</c> prefix on the wire):
    ///    auto-emitted by the SDK for app lifecycle, deep links, sessions, and
    ///    install detection. These are the canonical analytics primitives.
    ///
    /// 2. <b>Conversion events</b> (no <c>$</c> prefix): legacy/business events
    ///    consumers fire explicitly. The 50+ named constants below cover the
    ///    common e-commerce, gaming, content, and engagement taxonomies that
    ///    Mixpanel, GA4, Amplitude, and TikTok / Meta CAPI all share.
    ///
    /// Usage:
    /// <code>
    /// LayersSDK.Track(StandardEvents.Purchase, StandardEvents.PurchaseEvent(9.99, "USD", "premium"));
    /// LayersSDK.Track(StandardEvents.Login, StandardEvents.LoginEvent("google"));
    /// </code>
    /// </summary>
    public static class StandardEvents
    {
        // ── System Events ($-prefixed, auto-emitted by the SDK) ─────────
        // These are the Tier 2 lifecycle events introduced for parity with
        // PostHog / Mixpanel / Amplitude. The SDK auto-fires them — consumers
        // generally do NOT need to track these manually.

        /// <summary>App became foreground (cold launch or resume).</summary>
        public const string LayersAppOpen = "$app_open";
        /// <summary>App entered background (OnApplicationPause(true)).</summary>
        public const string LayersAppBackground = "$app_background";
        /// <summary>App is about to terminate (Application.quitting).</summary>
        public const string LayersAppTerminate = "$app_terminate";
        /// <summary>First launch on this install.</summary>
        public const string LayersFirstOpen = "$first_open";
        /// <summary>App version changed since last launch.</summary>
        public const string LayersAppUpdate = "$app_update";
        /// <summary>Deep link / Universal Link / App Link opened.</summary>
        public const string LayersDeepLinkOpened = "$deep_link_opened";
        /// <summary>Session started.</summary>
        public const string LayersSessionStart = "$session_start";
        /// <summary>Session ended (timed out or terminated).</summary>
        public const string LayersSessionEnd = "$session_end";
        /// <summary>Screen view (auto-tracked by Tier 2 screen capture, when enabled).</summary>
        public const string LayersScreenView = "$screen_view";

        // ── Identity / Lifecycle ───────────────────────────────────────
        public const string Login = "login";
        public const string Logout = "logout";
        public const string SignUp = "sign_up";
        public const string Register = "register";
        public const string AppInstall = "app_install";
        public const string AppOpen = "app_open";

        // ── Engagement ─────────────────────────────────────────────────
        public const string TutorialBegin = "tutorial_begin";
        public const string TutorialComplete = "tutorial_complete";
        public const string OnboardingComplete = "onboarding_complete";
        public const string ScreenView = "screen_view";
        public const string Search = "search";
        public const string ViewContent = "view_content";
        public const string ViewItem = "view_item";
        public const string ViewItemList = "view_item_list";
        public const string SelectContent = "select_content";
        public const string SelectItem = "select_item";
        public const string Share = "share";
        public const string Rate = "rate";
        public const string Feedback = "feedback";
        public const string Notification = "notification_received";
        public const string NotificationOpened = "notification_opened";
        public const string DeepLink = "deep_link_opened";

        // ── Commerce / Subscription ────────────────────────────────────
        public const string AddToCart = "add_to_cart";
        public const string RemoveFromCart = "remove_from_cart";
        public const string AddToWishlist = "add_to_wishlist";
        public const string ViewCart = "view_cart";
        public const string InitiateCheckout = "initiate_checkout";
        public const string BeginCheckout = "begin_checkout";
        public const string AddPaymentInfo = "add_payment_info";
        public const string AddShippingInfo = "add_shipping_info";
        public const string Purchase = "purchase_success";
        public const string PaywallPurchased = "paywall_purchased";
        public const string Refund = "refund";
        public const string StartTrial = "start_trial";
        public const string TrialStart = "trial_start";
        public const string TrialStarted = "trial_started";
        public const string TrialConvert = "trial_convert";
        public const string Subscribe = "subscribe";
        public const string SubscriptionStart = "subscription_start";
        public const string SubscriptionCancel = "subscription_cancel";
        public const string SubscriptionRenew = "subscription_renew";
        public const string PurchaseAttempt = "purchase_attempt";
        public const string IapPurchaseStarted = "iap_purchase_started";

        // ── Gaming ─────────────────────────────────────────────────────
        public const string LevelStart = "level_start";
        public const string LevelComplete = "level_complete";
        public const string LevelFail = "level_fail";
        public const string LevelUp = "level_up";
        public const string AchievementUnlocked = "achievement_unlocked";
        public const string PostScore = "post_score";
        public const string SpendVirtualCurrency = "spend_virtual_currency";
        public const string EarnVirtualCurrency = "earn_virtual_currency";
        public const string UnlockReward = "unlock_reward";

        // ── Ads / Monetization ─────────────────────────────────────────
        public const string AdImpression = "ad_impression";
        public const string AdClick = "ad_click";
        public const string AdReward = "ad_reward";

        // ── Errors / Support ───────────────────────────────────────────
        public const string Error = "error";
        public const string ContactSupport = "contact_support";

        /// <summary>
        /// All system event names, useful for filtering / validation. Updated
        /// for Tier 2 ($-prefixed lifecycle).
        /// </summary>
        public static readonly string[] SystemEvents =
        {
            LayersAppOpen, LayersAppBackground, LayersAppTerminate,
            LayersFirstOpen, LayersAppUpdate, LayersDeepLinkOpened,
            LayersSessionStart, LayersSessionEnd, LayersScreenView
        };

        /// <summary>
        /// All standard (non-system) event names, useful for validation and
        /// IDE auto-completion. 47 entries.
        /// </summary>
        public static readonly string[] All =
        {
            // Identity / lifecycle
            Login, Logout, SignUp, Register, AppInstall, AppOpen,
            // Engagement
            TutorialBegin, TutorialComplete, OnboardingComplete,
            ScreenView, Search,
            ViewContent, ViewItem, ViewItemList, SelectContent, SelectItem,
            Share, Rate, Feedback, Notification, NotificationOpened, DeepLink,
            // Commerce
            AddToCart, RemoveFromCart, AddToWishlist, ViewCart,
            InitiateCheckout, BeginCheckout, AddPaymentInfo, AddShippingInfo,
            Purchase, PaywallPurchased, Refund,
            StartTrial, TrialStart, TrialStarted, TrialConvert,
            Subscribe, SubscriptionStart, SubscriptionCancel, SubscriptionRenew,
            PurchaseAttempt, IapPurchaseStarted,
            // Gaming
            LevelStart, LevelComplete, LevelFail, LevelUp,
            AchievementUnlocked, PostScore,
            SpendVirtualCurrency, EarnVirtualCurrency, UnlockReward,
            // Ads / monetization
            AdImpression, AdClick, AdReward,
            // Errors / support
            Error, ContactSupport
        };

        // ── Typed Helper Methods ────────────────────────────────────────

        /// <summary>
        /// Build properties for a login event.
        /// </summary>
        /// <param name="method">Login method (e.g. "google", "email", "apple").</param>
        public static Dictionary<string, object> LoginEvent(string method = null)
        {
            var props = new Dictionary<string, object>();
            if (method != null) props["method"] = method;
            return props;
        }

        /// <summary>
        /// Build properties for a sign-up event.
        /// </summary>
        /// <param name="method">Sign-up method (e.g. "google", "email").</param>
        public static Dictionary<string, object> SignUpEvent(string method = null)
        {
            var props = new Dictionary<string, object>();
            if (method != null) props["method"] = method;
            return props;
        }

        /// <summary>
        /// Build properties for a register event.
        /// </summary>
        /// <param name="method">Registration method.</param>
        public static Dictionary<string, object> RegisterEvent(string method = null)
        {
            var props = new Dictionary<string, object>();
            if (method != null) props["method"] = method;
            return props;
        }

        /// <summary>
        /// Build properties for a purchase event.
        /// </summary>
        /// <param name="price">Unit price of the item.</param>
        /// <param name="currency">Currency code (e.g. "USD").</param>
        /// <param name="productId">Optional product identifier.</param>
        public static Dictionary<string, object> PurchaseEvent(
            double price, string currency = "USD", string productId = null)
        {
            var props = new Dictionary<string, object>
            {
                ["price"] = price,
                ["currency"] = currency
            };
            if (productId != null) props["product_id"] = productId;
            return props;
        }

        /// <summary>
        /// Build properties for a refund event.
        /// </summary>
        public static Dictionary<string, object> RefundEvent(
            double amount, string currency = "USD", string orderId = null)
        {
            var props = new Dictionary<string, object>
            {
                ["amount"] = amount,
                ["currency"] = currency
            };
            if (orderId != null) props["order_id"] = orderId;
            return props;
        }

        /// <summary>
        /// Build properties for an add-to-cart event.
        /// </summary>
        /// <param name="itemId">The item/product identifier.</param>
        /// <param name="price">Unit price of the item.</param>
        /// <param name="quantity">Quantity added to cart.</param>
        public static Dictionary<string, object> AddToCartEvent(
            string itemId, double price, int quantity = 1)
        {
            return new Dictionary<string, object>
            {
                ["item_id"] = itemId,
                ["price"] = price,
                ["quantity"] = quantity
            };
        }

        /// <summary>
        /// Build properties for an add-to-wishlist event.
        /// </summary>
        public static Dictionary<string, object> AddToWishlistEvent(
            string itemId, string name = null, double? price = null)
        {
            var props = new Dictionary<string, object> { ["item_id"] = itemId };
            if (name != null) props["name"] = name;
            if (price.HasValue) props["price"] = price.Value;
            return props;
        }

        /// <summary>
        /// Build properties for an initiate-checkout event.
        /// </summary>
        public static Dictionary<string, object> InitiateCheckoutEvent(
            double value, string currency = "USD", int? itemCount = null)
        {
            var props = new Dictionary<string, object>
            {
                ["value"] = value,
                ["currency"] = currency
            };
            if (itemCount.HasValue) props["item_count"] = itemCount.Value;
            return props;
        }

        /// <summary>
        /// Build properties for a start-trial event.
        /// </summary>
        public static Dictionary<string, object> StartTrialEvent(
            string plan = null, int? durationDays = null)
        {
            var props = new Dictionary<string, object>();
            if (plan != null) props["plan"] = plan;
            if (durationDays.HasValue) props["duration_days"] = durationDays.Value;
            return props;
        }

        /// <summary>
        /// Build properties for a subscribe event.
        /// </summary>
        public static Dictionary<string, object> SubscribeEvent(
            string plan, double price, string currency = "USD")
        {
            return new Dictionary<string, object>
            {
                ["plan"] = plan,
                ["price"] = price,
                ["currency"] = currency
            };
        }

        /// <summary>
        /// Build properties for a level-start event.
        /// </summary>
        public static Dictionary<string, object> LevelStartEvent(string level)
        {
            return new Dictionary<string, object> { ["level"] = level };
        }

        /// <summary>
        /// Build properties for a level-complete event.
        /// </summary>
        public static Dictionary<string, object> LevelCompleteEvent(
            string level, int? score = null)
        {
            var props = new Dictionary<string, object> { ["level"] = level };
            if (score.HasValue) props["score"] = score.Value;
            return props;
        }

        /// <summary>
        /// Build properties for a level-fail event.
        /// </summary>
        public static Dictionary<string, object> LevelFailEvent(string level, string reason = null)
        {
            var props = new Dictionary<string, object> { ["level"] = level };
            if (reason != null) props["reason"] = reason;
            return props;
        }

        /// <summary>
        /// Build properties for a tutorial-complete event.
        /// </summary>
        public static Dictionary<string, object> TutorialCompleteEvent(string name = null)
        {
            var props = new Dictionary<string, object>();
            if (name != null) props["name"] = name;
            return props;
        }

        /// <summary>
        /// Build properties for an achievement-unlocked event.
        /// </summary>
        public static Dictionary<string, object> AchievementUnlockedEvent(
            string achievementId, string name = null)
        {
            var props = new Dictionary<string, object> { ["achievement_id"] = achievementId };
            if (name != null) props["name"] = name;
            return props;
        }

        /// <summary>
        /// Build properties for a search event.
        /// </summary>
        public static Dictionary<string, object> SearchEvent(
            string query, int? resultCount = null)
        {
            var props = new Dictionary<string, object> { ["query"] = query };
            if (resultCount.HasValue) props["result_count"] = resultCount.Value;
            return props;
        }

        /// <summary>
        /// Build properties for a view-item event.
        /// </summary>
        public static Dictionary<string, object> ViewItemEvent(
            string itemId, string name = null, string category = null)
        {
            var props = new Dictionary<string, object> { ["item_id"] = itemId };
            if (name != null) props["name"] = name;
            if (category != null) props["category"] = category;
            return props;
        }

        /// <summary>
        /// Build properties for a view-content event.
        /// </summary>
        public static Dictionary<string, object> ViewContentEvent(
            string contentId, string contentType = null, string name = null)
        {
            var props = new Dictionary<string, object> { ["content_id"] = contentId };
            if (contentType != null) props["content_type"] = contentType;
            if (name != null) props["name"] = name;
            return props;
        }

        /// <summary>
        /// Build properties for a share event.
        /// </summary>
        public static Dictionary<string, object> ShareEvent(
            string contentType, string method = null, string contentId = null)
        {
            var props = new Dictionary<string, object> { ["content_type"] = contentType };
            if (method != null) props["method"] = method;
            if (contentId != null) props["content_id"] = contentId;
            return props;
        }

        /// <summary>
        /// Build properties for a screen-view event.
        /// </summary>
        public static Dictionary<string, object> ScreenViewEvent(
            string screenName, string screenClass = null)
        {
            var props = new Dictionary<string, object> { ["screen_name"] = screenName };
            if (screenClass != null) props["screen_class"] = screenClass;
            return props;
        }

        /// <summary>
        /// Build properties for an ad-impression event.
        /// </summary>
        public static Dictionary<string, object> AdImpressionEvent(
            string adUnitId, string adFormat = null, string adNetwork = null)
        {
            var props = new Dictionary<string, object> { ["ad_unit_id"] = adUnitId };
            if (adFormat != null) props["ad_format"] = adFormat;
            if (adNetwork != null) props["ad_network"] = adNetwork;
            return props;
        }

        /// <summary>
        /// Build properties for an error event.
        /// </summary>
        public static Dictionary<string, object> ErrorEvent(
            string code, string message = null, string source = null)
        {
            var props = new Dictionary<string, object> { ["code"] = code };
            if (message != null) props["message"] = message;
            if (source != null) props["source"] = source;
            return props;
        }
    }
}

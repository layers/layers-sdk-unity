using System.Collections.Generic;

namespace Layers.Unity
{
    /// <summary>
    /// Standard event types for Layers Analytics.
    ///
    /// These 20 events match the canonical Layers event taxonomy
    /// and are consistent across all SDK platforms (Swift, Kotlin, Flutter,
    /// Web, React Native, Expo).
    ///
    /// Usage:
    /// <code>
    /// Layers.Track(StandardEvents.Purchase, StandardEvents.PurchaseEvent(9.99, "USD", "premium"));
    /// Layers.Track(StandardEvents.Login, StandardEvents.LoginEvent("google"));
    /// </code>
    /// </summary>
    public static class StandardEvents
    {
        // ── Event Name Constants ────────────────────────────────────────

        public const string AppOpen = "app_open";
        public const string Login = "login";
        public const string SignUp = "sign_up";
        public const string Register = "register";
        public const string Purchase = "purchase_success";
        public const string AddToCart = "add_to_cart";
        public const string AddToWishlist = "add_to_wishlist";
        public const string InitiateCheckout = "initiate_checkout";
        public const string BeginCheckout = "begin_checkout";
        public const string StartTrial = "start_trial";
        public const string Subscribe = "subscribe";
        public const string LevelStart = "level_start";
        public const string LevelComplete = "level_complete";
        public const string TutorialComplete = "tutorial_complete";
        public const string Search = "search";
        public const string ViewItem = "view_item";
        public const string ViewContent = "view_content";
        public const string Share = "share";
        public const string DeepLink = "deep_link_opened";
        public const string ScreenView = "screen_view";

        /// <summary>
        /// All standard event names, useful for validation.
        /// </summary>
        public static readonly string[] All =
        {
            AppOpen, Login, SignUp, Register, Purchase, AddToCart, AddToWishlist,
            InitiateCheckout, BeginCheckout, StartTrial, Subscribe, LevelStart,
            LevelComplete, TutorialComplete, Search, ViewItem, ViewContent,
            Share, DeepLink, ScreenView
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
        /// <param name="itemId">The item/product identifier.</param>
        /// <param name="name">Optional item name.</param>
        /// <param name="price">Optional item price.</param>
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
        /// <param name="value">Total checkout value.</param>
        /// <param name="currency">Currency code.</param>
        /// <param name="itemCount">Optional number of items.</param>
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
        /// <param name="plan">Trial plan name.</param>
        /// <param name="durationDays">Trial duration in days.</param>
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
        /// <param name="plan">Subscription plan name.</param>
        /// <param name="price">Subscription price.</param>
        /// <param name="currency">Currency code.</param>
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
        /// <param name="level">Level name or number.</param>
        public static Dictionary<string, object> LevelStartEvent(string level)
        {
            return new Dictionary<string, object> { ["level"] = level };
        }

        /// <summary>
        /// Build properties for a level-complete event.
        /// </summary>
        /// <param name="level">Level name or number.</param>
        /// <param name="score">Optional score achieved.</param>
        public static Dictionary<string, object> LevelCompleteEvent(
            string level, int? score = null)
        {
            var props = new Dictionary<string, object> { ["level"] = level };
            if (score.HasValue) props["score"] = score.Value;
            return props;
        }

        /// <summary>
        /// Build properties for a tutorial-complete event.
        /// </summary>
        /// <param name="name">Optional tutorial name.</param>
        public static Dictionary<string, object> TutorialCompleteEvent(string name = null)
        {
            var props = new Dictionary<string, object>();
            if (name != null) props["name"] = name;
            return props;
        }

        /// <summary>
        /// Build properties for a search event.
        /// </summary>
        /// <param name="query">The search query string.</param>
        /// <param name="resultCount">Optional number of results returned.</param>
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
        /// <param name="itemId">The item/product identifier.</param>
        /// <param name="name">Optional item name.</param>
        /// <param name="category">Optional item category.</param>
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
        /// <param name="contentId">The content identifier.</param>
        /// <param name="contentType">Optional content type.</param>
        /// <param name="name">Optional content name.</param>
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
        /// <param name="contentType">Type of shared content.</param>
        /// <param name="method">Optional sharing method (e.g. "twitter", "copy_link").</param>
        /// <param name="contentId">Optional content identifier.</param>
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
        /// <param name="screenName">The screen name.</param>
        /// <param name="screenClass">Optional screen class.</param>
        public static Dictionary<string, object> ScreenViewEvent(
            string screenName, string screenClass = null)
        {
            var props = new Dictionary<string, object> { ["screen_name"] = screenName };
            if (screenClass != null) props["screen_class"] = screenClass;
            return props;
        }
    }
}

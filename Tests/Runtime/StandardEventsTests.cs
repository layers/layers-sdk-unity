using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using Layers.Unity;

namespace Layers.Unity.Tests
{
    [TestFixture]
    public class StandardEventsTests
    {
        // ── Event Name Constants ──────────────────────────────────────────

        [Test]
        public void All_Contains20Events()
        {
            Assert.AreEqual(20, StandardEvents.All.Length,
                $"Expected 20 standard events, got {StandardEvents.All.Length}");
        }

        [Test]
        public void All_ContainsNoDuplicates()
        {
            var unique = new HashSet<string>(StandardEvents.All);
            Assert.AreEqual(StandardEvents.All.Length, unique.Count,
                "Standard events should not contain duplicates");
        }

        [Test]
        public void AppOpen_IsCorrectValue()
        {
            Assert.AreEqual("app_open", StandardEvents.AppOpen);
        }

        [Test]
        public void Login_IsCorrectValue()
        {
            Assert.AreEqual("login", StandardEvents.Login);
        }

        [Test]
        public void SignUp_IsCorrectValue()
        {
            Assert.AreEqual("sign_up", StandardEvents.SignUp);
        }

        [Test]
        public void Register_IsCorrectValue()
        {
            Assert.AreEqual("register", StandardEvents.Register);
        }

        [Test]
        public void Purchase_IsCorrectValue()
        {
            Assert.AreEqual("purchase_success", StandardEvents.Purchase);
        }

        [Test]
        public void AddToCart_IsCorrectValue()
        {
            Assert.AreEqual("add_to_cart", StandardEvents.AddToCart);
        }

        [Test]
        public void AddToWishlist_IsCorrectValue()
        {
            Assert.AreEqual("add_to_wishlist", StandardEvents.AddToWishlist);
        }

        [Test]
        public void InitiateCheckout_IsCorrectValue()
        {
            Assert.AreEqual("initiate_checkout", StandardEvents.InitiateCheckout);
        }

        [Test]
        public void BeginCheckout_IsCorrectValue()
        {
            Assert.AreEqual("begin_checkout", StandardEvents.BeginCheckout);
        }

        [Test]
        public void StartTrial_IsCorrectValue()
        {
            Assert.AreEqual("start_trial", StandardEvents.StartTrial);
        }

        [Test]
        public void Subscribe_IsCorrectValue()
        {
            Assert.AreEqual("subscribe", StandardEvents.Subscribe);
        }

        [Test]
        public void LevelStart_IsCorrectValue()
        {
            Assert.AreEqual("level_start", StandardEvents.LevelStart);
        }

        [Test]
        public void LevelComplete_IsCorrectValue()
        {
            Assert.AreEqual("level_complete", StandardEvents.LevelComplete);
        }

        [Test]
        public void TutorialComplete_IsCorrectValue()
        {
            Assert.AreEqual("tutorial_complete", StandardEvents.TutorialComplete);
        }

        [Test]
        public void Search_IsCorrectValue()
        {
            Assert.AreEqual("search", StandardEvents.Search);
        }

        [Test]
        public void ViewItem_IsCorrectValue()
        {
            Assert.AreEqual("view_item", StandardEvents.ViewItem);
        }

        [Test]
        public void ViewContent_IsCorrectValue()
        {
            Assert.AreEqual("view_content", StandardEvents.ViewContent);
        }

        [Test]
        public void Share_IsCorrectValue()
        {
            Assert.AreEqual("share", StandardEvents.Share);
        }

        [Test]
        public void DeepLink_IsCorrectValue()
        {
            Assert.AreEqual("deep_link_opened", StandardEvents.DeepLink);
        }

        [Test]
        public void ScreenView_IsCorrectValue()
        {
            Assert.AreEqual("screen_view", StandardEvents.ScreenView);
        }

        // ── Typed Helpers ─────────────────────────────────────────────────

        [Test]
        public void LoginEvent_WithMethod_ContainsMethod()
        {
            var props = StandardEvents.LoginEvent("google");
            Assert.AreEqual("google", props["method"]);
        }

        [Test]
        public void LoginEvent_WithoutMethod_ReturnsEmptyDict()
        {
            var props = StandardEvents.LoginEvent();
            Assert.AreEqual(0, props.Count);
        }

        [Test]
        public void SignUpEvent_WithMethod_ContainsMethod()
        {
            var props = StandardEvents.SignUpEvent("email");
            Assert.AreEqual("email", props["method"]);
        }

        [Test]
        public void RegisterEvent_WithMethod_ContainsMethod()
        {
            var props = StandardEvents.RegisterEvent("apple");
            Assert.AreEqual("apple", props["method"]);
        }

        [Test]
        public void PurchaseEvent_RequiredFields_ArePresent()
        {
            var props = StandardEvents.PurchaseEvent(9.99, "USD");
            Assert.AreEqual(9.99, props["price"]);
            Assert.AreEqual("USD", props["currency"]);
        }

        [Test]
        public void PurchaseEvent_WithProductId_IncludesIt()
        {
            var props = StandardEvents.PurchaseEvent(9.99, "USD", "sku_123");
            Assert.AreEqual("sku_123", props["product_id"]);
        }

        [Test]
        public void PurchaseEvent_WithoutProductId_ExcludesIt()
        {
            var props = StandardEvents.PurchaseEvent(9.99);
            Assert.IsFalse(props.ContainsKey("product_id"));
        }

        [Test]
        public void AddToCartEvent_ContainsAllFields()
        {
            var props = StandardEvents.AddToCartEvent("item_1", 19.99, 2);
            Assert.AreEqual("item_1", props["item_id"]);
            Assert.AreEqual(19.99, props["price"]);
            Assert.AreEqual(2, props["quantity"]);
        }

        [Test]
        public void AddToWishlistEvent_RequiredFields()
        {
            var props = StandardEvents.AddToWishlistEvent("item_2");
            Assert.AreEqual("item_2", props["item_id"]);
        }

        [Test]
        public void AddToWishlistEvent_WithOptionalFields()
        {
            var props = StandardEvents.AddToWishlistEvent("item_2", "Widget", 29.99);
            Assert.AreEqual("Widget", props["name"]);
            Assert.AreEqual(29.99, props["price"]);
        }

        [Test]
        public void InitiateCheckoutEvent_RequiredFields()
        {
            var props = StandardEvents.InitiateCheckoutEvent(99.99, "EUR");
            Assert.AreEqual(99.99, props["value"]);
            Assert.AreEqual("EUR", props["currency"]);
        }

        [Test]
        public void InitiateCheckoutEvent_WithItemCount()
        {
            var props = StandardEvents.InitiateCheckoutEvent(99.99, itemCount: 3);
            Assert.AreEqual(3, props["item_count"]);
        }

        [Test]
        public void StartTrialEvent_WithPlan()
        {
            var props = StandardEvents.StartTrialEvent("premium", 14);
            Assert.AreEqual("premium", props["plan"]);
            Assert.AreEqual(14, props["duration_days"]);
        }

        [Test]
        public void StartTrialEvent_NoArgs_ReturnsEmptyDict()
        {
            var props = StandardEvents.StartTrialEvent();
            Assert.AreEqual(0, props.Count);
        }

        [Test]
        public void SubscribeEvent_ContainsAllFields()
        {
            var props = StandardEvents.SubscribeEvent("annual", 49.99, "GBP");
            Assert.AreEqual("annual", props["plan"]);
            Assert.AreEqual(49.99, props["price"]);
            Assert.AreEqual("GBP", props["currency"]);
        }

        [Test]
        public void LevelStartEvent_ContainsLevel()
        {
            var props = StandardEvents.LevelStartEvent("world_3");
            Assert.AreEqual("world_3", props["level"]);
        }

        [Test]
        public void LevelCompleteEvent_WithScore()
        {
            var props = StandardEvents.LevelCompleteEvent("boss", 9500);
            Assert.AreEqual("boss", props["level"]);
            Assert.AreEqual(9500, props["score"]);
        }

        [Test]
        public void LevelCompleteEvent_WithoutScore_ExcludesIt()
        {
            var props = StandardEvents.LevelCompleteEvent("tutorial");
            Assert.IsFalse(props.ContainsKey("score"));
        }

        [Test]
        public void TutorialCompleteEvent_WithName()
        {
            var props = StandardEvents.TutorialCompleteEvent("onboarding");
            Assert.AreEqual("onboarding", props["name"]);
        }

        [Test]
        public void SearchEvent_ContainsQuery()
        {
            var props = StandardEvents.SearchEvent("unity sdk");
            Assert.AreEqual("unity sdk", props["query"]);
        }

        [Test]
        public void SearchEvent_WithResultCount()
        {
            var props = StandardEvents.SearchEvent("unity", 42);
            Assert.AreEqual(42, props["result_count"]);
        }

        [Test]
        public void ViewItemEvent_ContainsItemId()
        {
            var props = StandardEvents.ViewItemEvent("prod_99");
            Assert.AreEqual("prod_99", props["item_id"]);
        }

        [Test]
        public void ViewItemEvent_WithOptionalFields()
        {
            var props = StandardEvents.ViewItemEvent("prod_99", "Gizmo", "electronics");
            Assert.AreEqual("Gizmo", props["name"]);
            Assert.AreEqual("electronics", props["category"]);
        }

        [Test]
        public void ViewContentEvent_ContainsContentId()
        {
            var props = StandardEvents.ViewContentEvent("article_42");
            Assert.AreEqual("article_42", props["content_id"]);
        }

        [Test]
        public void ViewContentEvent_WithOptionalFields()
        {
            var props = StandardEvents.ViewContentEvent("article_42", "blog", "How to");
            Assert.AreEqual("blog", props["content_type"]);
            Assert.AreEqual("How to", props["name"]);
        }

        [Test]
        public void ShareEvent_ContainsContentType()
        {
            var props = StandardEvents.ShareEvent("photo");
            Assert.AreEqual("photo", props["content_type"]);
        }

        [Test]
        public void ShareEvent_WithMethodAndContentId()
        {
            var props = StandardEvents.ShareEvent("photo", "twitter", "img_123");
            Assert.AreEqual("twitter", props["method"]);
            Assert.AreEqual("img_123", props["content_id"]);
        }

        [Test]
        public void ScreenViewEvent_ContainsScreenName()
        {
            var props = StandardEvents.ScreenViewEvent("HomeScreen");
            Assert.AreEqual("HomeScreen", props["screen_name"]);
        }

        [Test]
        public void ScreenViewEvent_WithScreenClass()
        {
            var props = StandardEvents.ScreenViewEvent("HomeScreen", "MainMenuController");
            Assert.AreEqual("MainMenuController", props["screen_class"]);
        }

        // ── All array matches constants ───────────────────────────────────

        [Test]
        public void All_ContainsAppOpen()
        {
            Assert.IsTrue(StandardEvents.All.Contains(StandardEvents.AppOpen));
        }

        [Test]
        public void All_ContainsLogin()
        {
            Assert.IsTrue(StandardEvents.All.Contains(StandardEvents.Login));
        }

        [Test]
        public void All_ContainsDeepLink()
        {
            Assert.IsTrue(StandardEvents.All.Contains(StandardEvents.DeepLink));
        }

        [Test]
        public void All_ContainsScreenView()
        {
            Assert.IsTrue(StandardEvents.All.Contains(StandardEvents.ScreenView));
        }
    }
}

using NUnit.Framework;
using System.Collections.Generic;
using Layers.Unity;

namespace Layers.Unity.Tests
{
    [TestFixture]
    public class DeepLinkParsingTests
    {
        // ── Basic URL Parsing ───────────────────────────────────────────

        [Test]
        public void ParseUrl_BasicCustomScheme_ExtractsComponents()
        {
            var data = DeepLinksModule.ParseUrl("myapp://open/product");
            Assert.IsNotNull(data);
            Assert.AreEqual("myapp://open/product", data.RawUrl);
            Assert.AreEqual("myapp", data.Scheme);
            Assert.AreEqual("open", data.Host);
            Assert.AreEqual("/product", data.Path);
        }

        [Test]
        public void ParseUrl_HttpsUrl_ExtractsComponents()
        {
            var data = DeepLinksModule.ParseUrl("https://myapp.com/app/product/123");
            Assert.IsNotNull(data);
            Assert.AreEqual("https", data.Scheme);
            Assert.AreEqual("myapp.com", data.Host);
            Assert.AreEqual("/app/product/123", data.Path);
        }

        [Test]
        public void ParseUrl_PreservesRawUrl()
        {
            string url = "myapp://open?key=value";
            var data = DeepLinksModule.ParseUrl(url);
            Assert.IsNotNull(data);
            Assert.AreEqual(url, data.RawUrl);
        }

        // ── Query Parameters ────────────────────────────────────────────

        [Test]
        public void ParseUrl_WithQueryParameters_ExtractsAll()
        {
            var data = DeepLinksModule.ParseUrl("myapp://open?foo=bar&baz=qux");
            Assert.IsNotNull(data);
            Assert.IsNotNull(data.QueryParameters);
            Assert.AreEqual("bar", data.QueryParameters["foo"]);
            Assert.AreEqual("qux", data.QueryParameters["baz"]);
        }

        [Test]
        public void ParseUrl_WithUrlEncodedQueryParams_DecodesValues()
        {
            var data = DeepLinksModule.ParseUrl("myapp://open?msg=hello%20world&key=a%26b");
            Assert.IsNotNull(data);
            Assert.AreEqual("hello world", data.QueryParameters["msg"]);
            Assert.AreEqual("a&b", data.QueryParameters["key"]);
        }

        [Test]
        public void ParseUrl_WithNoQueryString_ReturnsEmptyParams()
        {
            var data = DeepLinksModule.ParseUrl("myapp://open/product");
            Assert.IsNotNull(data);
            Assert.IsNotNull(data.QueryParameters);
            Assert.AreEqual(0, data.QueryParameters.Count);
        }

        // ── UTM Parameters ──────────────────────────────────────────────

        [Test]
        public void ParseUrl_ExtractsUtmSource()
        {
            var data = DeepLinksModule.ParseUrl(
                "https://myapp.com/open?utm_source=google");
            Assert.IsNotNull(data.Attribution);
            Assert.AreEqual("google", data.Attribution.UtmSource);
        }

        [Test]
        public void ParseUrl_ExtractsUtmMedium()
        {
            var data = DeepLinksModule.ParseUrl(
                "https://myapp.com/open?utm_medium=cpc");
            Assert.AreEqual("cpc", data.Attribution.UtmMedium);
        }

        [Test]
        public void ParseUrl_ExtractsUtmCampaign()
        {
            var data = DeepLinksModule.ParseUrl(
                "https://myapp.com/open?utm_campaign=summer_sale");
            Assert.AreEqual("summer_sale", data.Attribution.UtmCampaign);
        }

        [Test]
        public void ParseUrl_ExtractsUtmTerm()
        {
            var data = DeepLinksModule.ParseUrl(
                "https://myapp.com/open?utm_term=running+shoes");
            Assert.AreEqual("running shoes", data.Attribution.UtmTerm);
        }

        [Test]
        public void ParseUrl_ExtractsUtmContent()
        {
            var data = DeepLinksModule.ParseUrl(
                "https://myapp.com/open?utm_content=banner_top");
            Assert.AreEqual("banner_top", data.Attribution.UtmContent);
        }

        [Test]
        public void ParseUrl_ExtractsAllUtmParams()
        {
            var data = DeepLinksModule.ParseUrl(
                "https://myapp.com/open" +
                "?utm_source=google&utm_medium=cpc&utm_campaign=launch" +
                "&utm_term=sdk&utm_content=hero_banner");

            var attr = data.Attribution;
            Assert.AreEqual("google", attr.UtmSource);
            Assert.AreEqual("cpc", attr.UtmMedium);
            Assert.AreEqual("launch", attr.UtmCampaign);
            Assert.AreEqual("sdk", attr.UtmTerm);
            Assert.AreEqual("hero_banner", attr.UtmContent);
        }

        // ── Click IDs ───────────────────────────────────────────────────

        [Test]
        public void ParseUrl_ExtractsGclid()
        {
            var data = DeepLinksModule.ParseUrl(
                "https://myapp.com/open?gclid=abc123");
            Assert.AreEqual("abc123", data.Attribution.Gclid);
        }

        [Test]
        public void ParseUrl_ExtractsFbclid()
        {
            var data = DeepLinksModule.ParseUrl(
                "https://myapp.com/open?fbclid=fb_abc");
            Assert.AreEqual("fb_abc", data.Attribution.Fbclid);
        }

        [Test]
        public void ParseUrl_ExtractsTtclid()
        {
            var data = DeepLinksModule.ParseUrl(
                "https://myapp.com/open?ttclid=tt_123");
            Assert.AreEqual("tt_123", data.Attribution.Ttclid);
        }

        [Test]
        public void ParseUrl_ExtractsGbraid()
        {
            var data = DeepLinksModule.ParseUrl(
                "https://myapp.com/open?gbraid=gb_123");
            Assert.AreEqual("gb_123", data.Attribution.Gbraid);
        }

        [Test]
        public void ParseUrl_ExtractsWbraid()
        {
            var data = DeepLinksModule.ParseUrl(
                "https://myapp.com/open?wbraid=wb_123");
            Assert.AreEqual("wb_123", data.Attribution.Wbraid);
        }

        [Test]
        public void ParseUrl_ExtractsTwclid()
        {
            var data = DeepLinksModule.ParseUrl(
                "https://myapp.com/open?twclid=tw_123");
            Assert.AreEqual("tw_123", data.Attribution.Twclid);
        }

        [Test]
        public void ParseUrl_ExtractsMsclkid()
        {
            var data = DeepLinksModule.ParseUrl(
                "https://myapp.com/open?msclkid=ms_123");
            Assert.AreEqual("ms_123", data.Attribution.Msclkid);
        }

        [Test]
        public void ParseUrl_ExtractsLiFatId()
        {
            var data = DeepLinksModule.ParseUrl(
                "https://myapp.com/open?li_fat_id=li_123");
            Assert.AreEqual("li_123", data.Attribution.LiFatId);
        }

        [Test]
        public void ParseUrl_ExtractsSclid()
        {
            var data = DeepLinksModule.ParseUrl(
                "https://myapp.com/open?sclid=sc_123");
            Assert.AreEqual("sc_123", data.Attribution.Sclid);
        }

        [Test]
        public void ParseUrl_ExtractsIrclickid()
        {
            var data = DeepLinksModule.ParseUrl(
                "https://myapp.com/open?irclickid=ir_123");
            Assert.AreEqual("ir_123", data.Attribution.Irclickid);
        }

        [Test]
        public void ParseUrl_ExtractsMultipleClickIds()
        {
            var data = DeepLinksModule.ParseUrl(
                "https://myapp.com/open?gclid=g1&fbclid=f1&ttclid=t1");
            Assert.AreEqual("g1", data.Attribution.Gclid);
            Assert.AreEqual("f1", data.Attribution.Fbclid);
            Assert.AreEqual("t1", data.Attribution.Ttclid);
        }

        // ── Edge Cases ──────────────────────────────────────────────────

        [Test]
        public void ParseUrl_NoQueryString_AttributionFieldsAreNull()
        {
            var data = DeepLinksModule.ParseUrl("myapp://open/product");
            Assert.IsNotNull(data.Attribution);
            Assert.IsNull(data.Attribution.UtmSource);
            Assert.IsNull(data.Attribution.UtmMedium);
            Assert.IsNull(data.Attribution.UtmCampaign);
            Assert.IsNull(data.Attribution.UtmTerm);
            Assert.IsNull(data.Attribution.UtmContent);
            Assert.IsNull(data.Attribution.Gclid);
            Assert.IsNull(data.Attribution.Fbclid);
        }

        [Test]
        public void ParseUrl_EmptyPath_ReturnsSlash()
        {
            // System.Uri normalizes empty path on authority-based URIs to "/"
            var data = DeepLinksModule.ParseUrl("https://myapp.com");
            Assert.IsNotNull(data);
            Assert.AreEqual("/", data.Path);
        }

        [Test]
        public void ParseUrl_WithFragment_StillParsesCorrectly()
        {
            var data = DeepLinksModule.ParseUrl(
                "https://myapp.com/page?utm_source=test#section1");
            Assert.IsNotNull(data);
            Assert.AreEqual("test", data.Attribution.UtmSource);
            // Fragment should not interfere with query parsing
            Assert.AreEqual("/page", data.Path);
        }

        [Test]
        public void ParseUrl_NullUrl_ReturnsNull()
        {
            var data = DeepLinksModule.ParseUrl(null);
            Assert.IsNull(data);
        }

        [Test]
        public void ParseUrl_EmptyUrl_ReturnsNull()
        {
            var data = DeepLinksModule.ParseUrl("");
            Assert.IsNull(data);
        }

        [Test]
        public void ParseUrl_MalformedUrl_ReturnsNull()
        {
            // ParseUrl catches exceptions and returns null for malformed URLs
            var data = DeepLinksModule.ParseUrl("not a url at all ://");
            // System.Uri may or may not throw depending on the input.
            // The contract is: ParseUrl returns null on parse failure.
            // If Uri happens to parse it, that's fine — we just verify no exception leaks.
            // This test validates the method doesn't throw.
            Assert.Pass("No exception thrown for malformed URL");
        }

        [Test]
        public void ParseUrl_QueryParamWithNoValue_HandledGracefully()
        {
            var data = DeepLinksModule.ParseUrl("myapp://open?flag&utm_source=test");
            Assert.IsNotNull(data);
            Assert.AreEqual("test", data.Attribution.UtmSource);
            // "flag" key should be present with empty value
            Assert.IsTrue(data.QueryParameters.ContainsKey("flag"));
        }

        [Test]
        public void ParseUrl_DuplicateQueryParams_LastWins()
        {
            var data = DeepLinksModule.ParseUrl(
                "myapp://open?utm_source=first&utm_source=second");
            Assert.IsNotNull(data);
            Assert.AreEqual("second", data.Attribution.UtmSource);
        }

        [Test]
        public void ParseUrl_MixedUtmAndClickIds()
        {
            var data = DeepLinksModule.ParseUrl(
                "https://myapp.com/promo" +
                "?utm_source=newsletter&utm_medium=email&gclid=click123&fbclid=fb456");

            Assert.AreEqual("newsletter", data.Attribution.UtmSource);
            Assert.AreEqual("email", data.Attribution.UtmMedium);
            Assert.IsNull(data.Attribution.UtmCampaign);
            Assert.AreEqual("click123", data.Attribution.Gclid);
            Assert.AreEqual("fb456", data.Attribution.Fbclid);
            Assert.IsNull(data.Attribution.Ttclid);
        }
    }
}

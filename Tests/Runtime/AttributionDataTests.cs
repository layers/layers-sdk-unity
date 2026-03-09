using NUnit.Framework;
using Layers.Unity;

namespace Layers.Unity.Tests
{
    [TestFixture]
    public class AttributionDataTests
    {
        // ── Default Values ──────────────────────────────────────────────

        [Test]
        public void DefaultUtmSource_IsNull()
        {
            var attr = new AttributionData();
            Assert.IsNull(attr.UtmSource);
        }

        [Test]
        public void DefaultUtmMedium_IsNull()
        {
            var attr = new AttributionData();
            Assert.IsNull(attr.UtmMedium);
        }

        [Test]
        public void DefaultUtmCampaign_IsNull()
        {
            var attr = new AttributionData();
            Assert.IsNull(attr.UtmCampaign);
        }

        [Test]
        public void DefaultUtmTerm_IsNull()
        {
            var attr = new AttributionData();
            Assert.IsNull(attr.UtmTerm);
        }

        [Test]
        public void DefaultUtmContent_IsNull()
        {
            var attr = new AttributionData();
            Assert.IsNull(attr.UtmContent);
        }

        // ── Click ID Defaults ───────────────────────────────────────────

        [Test]
        public void DefaultGclid_IsNull()
        {
            var attr = new AttributionData();
            Assert.IsNull(attr.Gclid);
        }

        [Test]
        public void DefaultGbraid_IsNull()
        {
            var attr = new AttributionData();
            Assert.IsNull(attr.Gbraid);
        }

        [Test]
        public void DefaultWbraid_IsNull()
        {
            var attr = new AttributionData();
            Assert.IsNull(attr.Wbraid);
        }

        [Test]
        public void DefaultFbclid_IsNull()
        {
            var attr = new AttributionData();
            Assert.IsNull(attr.Fbclid);
        }

        [Test]
        public void DefaultTtclid_IsNull()
        {
            var attr = new AttributionData();
            Assert.IsNull(attr.Ttclid);
        }

        [Test]
        public void DefaultTwclid_IsNull()
        {
            var attr = new AttributionData();
            Assert.IsNull(attr.Twclid);
        }

        [Test]
        public void DefaultMsclkid_IsNull()
        {
            var attr = new AttributionData();
            Assert.IsNull(attr.Msclkid);
        }

        [Test]
        public void DefaultLiFatId_IsNull()
        {
            var attr = new AttributionData();
            Assert.IsNull(attr.LiFatId);
        }

        [Test]
        public void DefaultSclid_IsNull()
        {
            var attr = new AttributionData();
            Assert.IsNull(attr.Sclid);
        }

        [Test]
        public void DefaultIrclickid_IsNull()
        {
            var attr = new AttributionData();
            Assert.IsNull(attr.Irclickid);
        }

        // ── All Fields Null By Default ──────────────────────────────────

        [Test]
        public void AllFieldsNullByDefault()
        {
            var attr = new AttributionData();

            // UTM fields
            Assert.IsNull(attr.UtmSource, "UtmSource should be null by default");
            Assert.IsNull(attr.UtmMedium, "UtmMedium should be null by default");
            Assert.IsNull(attr.UtmCampaign, "UtmCampaign should be null by default");
            Assert.IsNull(attr.UtmTerm, "UtmTerm should be null by default");
            Assert.IsNull(attr.UtmContent, "UtmContent should be null by default");

            // Click IDs
            Assert.IsNull(attr.Gclid, "Gclid should be null by default");
            Assert.IsNull(attr.Gbraid, "Gbraid should be null by default");
            Assert.IsNull(attr.Wbraid, "Wbraid should be null by default");
            Assert.IsNull(attr.Fbclid, "Fbclid should be null by default");
            Assert.IsNull(attr.Ttclid, "Ttclid should be null by default");
            Assert.IsNull(attr.Twclid, "Twclid should be null by default");
            Assert.IsNull(attr.Msclkid, "Msclkid should be null by default");
            Assert.IsNull(attr.LiFatId, "LiFatId should be null by default");
            Assert.IsNull(attr.Sclid, "Sclid should be null by default");
            Assert.IsNull(attr.Irclickid, "Irclickid should be null by default");
        }

        // ── Setters Work ────────────────────────────────────────────────

        [Test]
        public void SetUtmSource_IsRetained()
        {
            var attr = new AttributionData { UtmSource = "google" };
            Assert.AreEqual("google", attr.UtmSource);
        }

        [Test]
        public void SetUtmMedium_IsRetained()
        {
            var attr = new AttributionData { UtmMedium = "cpc" };
            Assert.AreEqual("cpc", attr.UtmMedium);
        }

        [Test]
        public void SetUtmCampaign_IsRetained()
        {
            var attr = new AttributionData { UtmCampaign = "summer" };
            Assert.AreEqual("summer", attr.UtmCampaign);
        }

        [Test]
        public void SetUtmTerm_IsRetained()
        {
            var attr = new AttributionData { UtmTerm = "shoes" };
            Assert.AreEqual("shoes", attr.UtmTerm);
        }

        [Test]
        public void SetUtmContent_IsRetained()
        {
            var attr = new AttributionData { UtmContent = "banner" };
            Assert.AreEqual("banner", attr.UtmContent);
        }

        [Test]
        public void SetGclid_IsRetained()
        {
            var attr = new AttributionData { Gclid = "abc123" };
            Assert.AreEqual("abc123", attr.Gclid);
        }

        [Test]
        public void SetFbclid_IsRetained()
        {
            var attr = new AttributionData { Fbclid = "fb_456" };
            Assert.AreEqual("fb_456", attr.Fbclid);
        }

        [Test]
        public void SetTtclid_IsRetained()
        {
            var attr = new AttributionData { Ttclid = "tt_789" };
            Assert.AreEqual("tt_789", attr.Ttclid);
        }

        [Test]
        public void SetAllClickIds_AreIndependent()
        {
            var attr = new AttributionData
            {
                Gclid = "g",
                Gbraid = "gb",
                Wbraid = "wb",
                Fbclid = "fb",
                Ttclid = "tt",
                Twclid = "tw",
                Msclkid = "ms",
                LiFatId = "li",
                Sclid = "sc",
                Irclickid = "ir"
            };

            Assert.AreEqual("g", attr.Gclid);
            Assert.AreEqual("gb", attr.Gbraid);
            Assert.AreEqual("wb", attr.Wbraid);
            Assert.AreEqual("fb", attr.Fbclid);
            Assert.AreEqual("tt", attr.Ttclid);
            Assert.AreEqual("tw", attr.Twclid);
            Assert.AreEqual("ms", attr.Msclkid);
            Assert.AreEqual("li", attr.LiFatId);
            Assert.AreEqual("sc", attr.Sclid);
            Assert.AreEqual("ir", attr.Irclickid);
        }
    }
}

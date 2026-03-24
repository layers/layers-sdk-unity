using NUnit.Framework;
using System.Collections.Generic;
using Layers.Unity;
using Layers.Unity.Internal;

namespace Layers.Unity.Tests
{
    [TestFixture]
    public class GroupTests
    {
        [SetUp]
        public void SetUp()
        {
            LayersTestMode.Enable();
            Layers.Initialize(new LayersConfig { AppId = "test-group" });
            LayersTestMode.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            Layers.Shutdown();
            LayersTestMode.Disable();
        }

        // ── Basic Group Call ──────────────────────────────────────────────

        [Test]
        public void Group_CallsGroupOnPlatform()
        {
            Layers.Group("company_123");

            Assert.AreEqual(1, LayersTestMode.GroupCalls.Count);
            Assert.AreEqual("company_123", LayersTestMode.GroupCalls[0].groupId);
        }

        [Test]
        public void Group_WithProperties_PassesPropertiesJson()
        {
            Layers.Group("company_123", new Dictionary<string, object>
            {
                ["name"] = "Acme Corp",
                ["plan"] = "enterprise"
            });

            Assert.AreEqual(1, LayersTestMode.GroupCalls.Count);
            string json = LayersTestMode.GroupCalls[0].propertiesJson;
            Assert.IsNotNull(json);
            Assert.That(json, Does.Contain("\"name\":\"Acme Corp\""));
            Assert.That(json, Does.Contain("\"plan\":\"enterprise\""));
        }

        [Test]
        public void Group_WithoutProperties_PassesNullPropertiesJson()
        {
            Layers.Group("company_123");

            Assert.AreEqual(1, LayersTestMode.GroupCalls.Count);
            Assert.IsNull(LayersTestMode.GroupCalls[0].propertiesJson);
        }

        // ── Validation ───────────────────────────────────────────────────

        [Test]
        public void Group_NullGroupId_DoesNotCallPlatform()
        {
            Layers.Group(null);

            Assert.AreEqual(0, LayersTestMode.GroupCalls.Count);
        }

        [Test]
        public void Group_EmptyGroupId_DoesNotCallPlatform()
        {
            Layers.Group("");

            Assert.AreEqual(0, LayersTestMode.GroupCalls.Count);
        }

        // ── Multiple Groups ──────────────────────────────────────────────

        [Test]
        public void Group_MultipleGroups_AllForwarded()
        {
            Layers.Group("group_a");
            Layers.Group("group_b", new Dictionary<string, object>
            {
                ["industry"] = "tech"
            });

            Assert.AreEqual(2, LayersTestMode.GroupCalls.Count);
            Assert.AreEqual("group_a", LayersTestMode.GroupCalls[0].groupId);
            Assert.AreEqual("group_b", LayersTestMode.GroupCalls[1].groupId);
        }
    }
}

using NUnit.Framework;
using System.Collections.Generic;
using Layers.Unity.Internal;

namespace Layers.Unity.Tests
{
    [TestFixture]
    public class JsonHelperTests
    {
        // ── Empty / Null ────────────────────────────────────────────────

        [Test]
        public void Serialize_EmptyDictionary_ReturnsEmptyObject()
        {
            var dict = new Dictionary<string, object>();
            Assert.AreEqual("{}", JsonHelper.Serialize(dict));
        }

        [Test]
        public void Serialize_NullDictionary_ReturnsEmptyObject()
        {
            Assert.AreEqual("{}", JsonHelper.Serialize(null));
        }

        // ── String Values ───────────────────────────────────────────────

        [Test]
        public void Serialize_SingleStringValue()
        {
            var dict = new Dictionary<string, object> { ["name"] = "alice" };
            Assert.AreEqual("{\"name\":\"alice\"}", JsonHelper.Serialize(dict));
        }

        [Test]
        public void Serialize_MultipleStringValues()
        {
            var dict = new Dictionary<string, object>
            {
                ["first"] = "hello",
                ["second"] = "world"
            };
            string json = JsonHelper.Serialize(dict);
            Assert.That(json, Does.Contain("\"first\":\"hello\""));
            Assert.That(json, Does.Contain("\"second\":\"world\""));
            Assert.That(json, Does.StartWith("{"));
            Assert.That(json, Does.EndWith("}"));
        }

        // ── Numeric Values ──────────────────────────────────────────────

        [Test]
        public void Serialize_IntValue()
        {
            var dict = new Dictionary<string, object> { ["count"] = 42 };
            Assert.AreEqual("{\"count\":42}", JsonHelper.Serialize(dict));
        }

        [Test]
        public void Serialize_NegativeIntValue()
        {
            var dict = new Dictionary<string, object> { ["offset"] = -10 };
            Assert.AreEqual("{\"offset\":-10}", JsonHelper.Serialize(dict));
        }

        [Test]
        public void Serialize_LongValue()
        {
            var dict = new Dictionary<string, object> { ["ts"] = 1700000000000L };
            Assert.AreEqual("{\"ts\":1700000000000}", JsonHelper.Serialize(dict));
        }

        [Test]
        public void Serialize_FloatValue()
        {
            var dict = new Dictionary<string, object> { ["price"] = 9.99f };
            string json = JsonHelper.Serialize(dict);
            // Float "R" format may produce "9.99" or "9.99000072" depending on precision
            Assert.That(json, Does.StartWith("{\"price\":9.99"));
            Assert.That(json, Does.EndWith("}"));
        }

        [Test]
        public void Serialize_DoubleValue()
        {
            var dict = new Dictionary<string, object> { ["lat"] = 37.7749 };
            string json = JsonHelper.Serialize(dict);
            Assert.That(json, Does.Contain("\"lat\":37.7749"));
        }

        [Test]
        public void Serialize_ZeroInt()
        {
            var dict = new Dictionary<string, object> { ["zero"] = 0 };
            Assert.AreEqual("{\"zero\":0}", JsonHelper.Serialize(dict));
        }

        // ── Bool Values ─────────────────────────────────────────────────

        [Test]
        public void Serialize_BoolTrue()
        {
            var dict = new Dictionary<string, object> { ["active"] = true };
            Assert.AreEqual("{\"active\":true}", JsonHelper.Serialize(dict));
        }

        [Test]
        public void Serialize_BoolFalse()
        {
            var dict = new Dictionary<string, object> { ["active"] = false };
            Assert.AreEqual("{\"active\":false}", JsonHelper.Serialize(dict));
        }

        // ── Null Values ─────────────────────────────────────────────────

        [Test]
        public void Serialize_NullValue()
        {
            var dict = new Dictionary<string, object> { ["nothing"] = null };
            Assert.AreEqual("{\"nothing\":null}", JsonHelper.Serialize(dict));
        }

        // ── Nested Dictionaries ─────────────────────────────────────────

        [Test]
        public void Serialize_NestedDictionary()
        {
            var dict = new Dictionary<string, object>
            {
                ["user"] = new Dictionary<string, object>
                {
                    ["name"] = "bob",
                    ["age"] = 30
                }
            };
            string json = JsonHelper.Serialize(dict);
            Assert.AreEqual("{\"user\":{\"name\":\"bob\",\"age\":30}}", json);
        }

        [Test]
        public void Serialize_DeeplyNestedDictionary()
        {
            var dict = new Dictionary<string, object>
            {
                ["level1"] = new Dictionary<string, object>
                {
                    ["level2"] = new Dictionary<string, object>
                    {
                        ["value"] = "deep"
                    }
                }
            };
            string json = JsonHelper.Serialize(dict);
            Assert.AreEqual("{\"level1\":{\"level2\":{\"value\":\"deep\"}}}", json);
        }

        [Test]
        public void Serialize_EmptyNestedDictionary()
        {
            var dict = new Dictionary<string, object>
            {
                ["empty"] = new Dictionary<string, object>()
            };
            // Empty nested dict serializes via SerializeObject as "{}"
            Assert.AreEqual("{\"empty\":{}}", JsonHelper.Serialize(dict));
        }

        // ── Arrays / Lists ──────────────────────────────────────────────

        [Test]
        public void Serialize_StringArray()
        {
            var dict = new Dictionary<string, object>
            {
                ["tags"] = new List<object> { "a", "b", "c" }
            };
            Assert.AreEqual("{\"tags\":[\"a\",\"b\",\"c\"]}", JsonHelper.Serialize(dict));
        }

        [Test]
        public void Serialize_IntArray()
        {
            var dict = new Dictionary<string, object>
            {
                ["ids"] = new List<object> { 1, 2, 3 }
            };
            Assert.AreEqual("{\"ids\":[1,2,3]}", JsonHelper.Serialize(dict));
        }

        [Test]
        public void Serialize_EmptyArray()
        {
            var dict = new Dictionary<string, object>
            {
                ["items"] = new List<object>()
            };
            Assert.AreEqual("{\"items\":[]}", JsonHelper.Serialize(dict));
        }

        [Test]
        public void Serialize_MixedArray()
        {
            var dict = new Dictionary<string, object>
            {
                ["mixed"] = new List<object> { "hello", 42, true, null }
            };
            Assert.AreEqual("{\"mixed\":[\"hello\",42,true,null]}", JsonHelper.Serialize(dict));
        }

        [Test]
        public void Serialize_NestedArrayWithDictionary()
        {
            var dict = new Dictionary<string, object>
            {
                ["items"] = new List<object>
                {
                    new Dictionary<string, object> { ["id"] = 1 },
                    new Dictionary<string, object> { ["id"] = 2 }
                }
            };
            Assert.AreEqual("{\"items\":[{\"id\":1},{\"id\":2}]}", JsonHelper.Serialize(dict));
        }

        // ── Special Characters ──────────────────────────────────────────

        [Test]
        public void Serialize_StringWithQuotes()
        {
            var dict = new Dictionary<string, object> { ["msg"] = "say \"hello\"" };
            Assert.AreEqual("{\"msg\":\"say \\\"hello\\\"\"}", JsonHelper.Serialize(dict));
        }

        [Test]
        public void Serialize_StringWithBackslashes()
        {
            var dict = new Dictionary<string, object> { ["path"] = "C:\\Users\\test" };
            Assert.AreEqual("{\"path\":\"C:\\\\Users\\\\test\"}", JsonHelper.Serialize(dict));
        }

        [Test]
        public void Serialize_StringWithNewline()
        {
            var dict = new Dictionary<string, object> { ["text"] = "line1\nline2" };
            Assert.AreEqual("{\"text\":\"line1\\nline2\"}", JsonHelper.Serialize(dict));
        }

        [Test]
        public void Serialize_StringWithTab()
        {
            var dict = new Dictionary<string, object> { ["text"] = "col1\tcol2" };
            Assert.AreEqual("{\"text\":\"col1\\tcol2\"}", JsonHelper.Serialize(dict));
        }

        [Test]
        public void Serialize_StringWithCarriageReturn()
        {
            var dict = new Dictionary<string, object> { ["text"] = "line1\rline2" };
            Assert.AreEqual("{\"text\":\"line1\\rline2\"}", JsonHelper.Serialize(dict));
        }

        [Test]
        public void Serialize_StringWithBackspace()
        {
            var dict = new Dictionary<string, object> { ["text"] = "ab\bc" };
            Assert.AreEqual("{\"text\":\"ab\\bc\"}", JsonHelper.Serialize(dict));
        }

        [Test]
        public void Serialize_StringWithFormFeed()
        {
            var dict = new Dictionary<string, object> { ["text"] = "ab\fc" };
            Assert.AreEqual("{\"text\":\"ab\\fc\"}", JsonHelper.Serialize(dict));
        }

        [Test]
        public void Serialize_StringWithUnicode()
        {
            var dict = new Dictionary<string, object> { ["emoji"] = "hello \u00e9" };
            // Non-ASCII characters above 0x1F are passed through as-is
            string json = JsonHelper.Serialize(dict);
            Assert.That(json, Does.Contain("\u00e9"));
        }

        [Test]
        public void Serialize_StringWithControlCharacter()
        {
            // Control character \x01 should be escaped as \u0001
            var dict = new Dictionary<string, object> { ["ctrl"] = "a\x01b" };
            Assert.AreEqual("{\"ctrl\":\"a\\u0001b\"}", JsonHelper.Serialize(dict));
        }

        [Test]
        public void Serialize_KeyWithSpecialCharacters()
        {
            var dict = new Dictionary<string, object> { ["key \"with\" quotes"] = "val" };
            Assert.AreEqual("{\"key \\\"with\\\" quotes\":\"val\"}", JsonHelper.Serialize(dict));
        }

        // ── Fallback Type ───────────────────────────────────────────────

        [Test]
        public void Serialize_UnknownType_FallsBackToString()
        {
            // A System.DateTime is not a directly handled type — it falls back to ToString()
            var dt = new System.DateTime(2024, 1, 15, 0, 0, 0, System.DateTimeKind.Utc);
            var dict = new Dictionary<string, object> { ["date"] = dt };
            string json = JsonHelper.Serialize(dict);
            // Should contain the DateTime's ToString() output wrapped in quotes
            Assert.That(json, Does.StartWith("{\"date\":\""));
            Assert.That(json, Does.EndWith("\"}"));
        }

        // ── Complex Combinations ────────────────────────────────────────

        [Test]
        public void Serialize_MixedValueTypes()
        {
            var dict = new Dictionary<string, object>
            {
                ["name"] = "test",
                ["count"] = 42,
                ["active"] = true,
                ["score"] = 3.14,
                ["tags"] = new List<object> { "a", "b" },
                ["meta"] = new Dictionary<string, object> { ["key"] = "val" },
                ["empty"] = null
            };
            string json = JsonHelper.Serialize(dict);

            Assert.That(json, Does.Contain("\"name\":\"test\""));
            Assert.That(json, Does.Contain("\"count\":42"));
            Assert.That(json, Does.Contain("\"active\":true"));
            Assert.That(json, Does.Contain("\"score\":3.14"));
            Assert.That(json, Does.Contain("\"tags\":[\"a\",\"b\"]"));
            Assert.That(json, Does.Contain("\"meta\":{\"key\":\"val\"}"));
            Assert.That(json, Does.Contain("\"empty\":null"));
        }
    }
}

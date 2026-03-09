using NUnit.Framework;
using System.Text.RegularExpressions;
using Layers.Unity.Internal;

namespace Layers.Unity.Tests
{
    [TestFixture]
    public class InstallIdTests
    {
        // UUID v4 format: 8-4-4-4-12 hex chars with dashes
        private static readonly Regex UuidRegex = new Regex(
            @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
            RegexOptions.IgnoreCase);

        [Test]
        public void GetOrCreate_ReturnsNonNullString()
        {
            string id = InstallIdProvider.GetOrCreate();
            Assert.IsNotNull(id);
            Assert.IsNotEmpty(id);
        }

        [Test]
        public void GetOrCreate_ReturnsValidUuidFormat()
        {
            string id = InstallIdProvider.GetOrCreate();
            Assert.IsTrue(
                UuidRegex.IsMatch(id),
                $"Expected UUID format (8-4-4-4-12), got: {id}");
        }

        [Test]
        public void GetOrCreate_ReturnsSameIdOnRepeatedCalls()
        {
            // InstallIdProvider caches the ID in memory after the first call.
            string first = InstallIdProvider.GetOrCreate();
            string second = InstallIdProvider.GetOrCreate();
            string third = InstallIdProvider.GetOrCreate();

            Assert.AreEqual(first, second, "Second call should return cached ID");
            Assert.AreEqual(first, third, "Third call should return cached ID");
        }

        [Test]
        public void GetOrCreate_IdIsExactly36Characters()
        {
            // Standard UUID string length: 32 hex digits + 4 dashes = 36
            string id = InstallIdProvider.GetOrCreate();
            Assert.AreEqual(36, id.Length, $"UUID should be 36 chars, got {id.Length}: {id}");
        }
    }
}

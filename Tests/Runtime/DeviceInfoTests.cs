using NUnit.Framework;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Layers.Unity.Internal;

namespace Layers.Unity.Tests
{
    [TestFixture]
    public class DeviceInfoTests
    {
        private Dictionary<string, object> _deviceInfo;

        [SetUp]
        public void SetUp()
        {
            _deviceInfo = DeviceInfoCollector.Collect();
        }

        // ── Non-null Result ─────────────────────────────────────────────

        [Test]
        public void Collect_ReturnsNonNullDictionary()
        {
            Assert.IsNotNull(_deviceInfo);
        }

        [Test]
        public void Collect_ReturnsNonEmptyDictionary()
        {
            Assert.IsTrue(_deviceInfo.Count > 0,
                "Device info dictionary should not be empty");
        }

        // ── Required Keys ───────────────────────────────────────────────

        [Test]
        public void Collect_ContainsPlatformKey()
        {
            Assert.IsTrue(_deviceInfo.ContainsKey("platform"),
                "Device info must contain 'platform' key");
        }

        [Test]
        public void Collect_ContainsOsVersionKey()
        {
            Assert.IsTrue(_deviceInfo.ContainsKey("os_version"),
                "Device info must contain 'os_version' key");
        }

        [Test]
        public void Collect_ContainsDeviceModelKey()
        {
            Assert.IsTrue(_deviceInfo.ContainsKey("device_model"),
                "Device info must contain 'device_model' key");
        }

        [Test]
        public void Collect_ContainsAppVersionKey()
        {
            Assert.IsTrue(_deviceInfo.ContainsKey("app_version"),
                "Device info must contain 'app_version' key");
        }

        [Test]
        public void Collect_ContainsLocaleKey()
        {
            Assert.IsTrue(_deviceInfo.ContainsKey("locale"),
                "Device info must contain 'locale' key");
        }

        [Test]
        public void Collect_ContainsBuildNumberKey()
        {
            Assert.IsTrue(_deviceInfo.ContainsKey("build_number"),
                "Device info must contain 'build_number' key");
        }

        [Test]
        public void Collect_ContainsScreenSizeKey()
        {
            Assert.IsTrue(_deviceInfo.ContainsKey("screen_size"),
                "Device info must contain 'screen_size' key");
        }

        [Test]
        public void Collect_ContainsInstallIdKey()
        {
            Assert.IsTrue(_deviceInfo.ContainsKey("install_id"),
                "Device info must contain 'install_id' key");
        }

        [Test]
        public void Collect_ContainsTimezoneKey()
        {
            Assert.IsTrue(_deviceInfo.ContainsKey("timezone"),
                "Device info must contain 'timezone' key");
        }

        // ── Value Validation ────────────────────────────────────────────

        [Test]
        public void Collect_PlatformIsUnity()
        {
            Assert.AreEqual("unity", _deviceInfo["platform"]);
        }

        [Test]
        public void Collect_OsVersionIsNonEmpty()
        {
            string osVersion = _deviceInfo["os_version"] as string;
            Assert.IsNotNull(osVersion);
            Assert.IsNotEmpty(osVersion);
        }

        [Test]
        public void Collect_DeviceModelIsNonEmpty()
        {
            string model = _deviceInfo["device_model"] as string;
            Assert.IsNotNull(model);
            Assert.IsNotEmpty(model);
        }

        [Test]
        public void Collect_ScreenSizeFormat_IsWxH()
        {
            string screenSize = _deviceInfo["screen_size"] as string;
            Assert.IsNotNull(screenSize);
            // Expected format: "<width>x<height>" where both are integers
            Assert.IsTrue(
                Regex.IsMatch(screenSize, @"^\d+x\d+$"),
                $"Screen size should match 'WxH' format, got: {screenSize}");
        }

        [Test]
        public void Collect_InstallIdIsValidUuid()
        {
            string installId = _deviceInfo["install_id"] as string;
            Assert.IsNotNull(installId);
            Assert.IsTrue(
                Regex.IsMatch(installId,
                    @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
                    RegexOptions.IgnoreCase),
                $"Install ID should be a UUID, got: {installId}");
        }

        [Test]
        public void Collect_TimezoneIsNonEmpty()
        {
            string timezone = _deviceInfo["timezone"] as string;
            Assert.IsNotNull(timezone);
            Assert.IsNotEmpty(timezone);
        }

        [Test]
        public void Collect_AppVersionIsNotNull()
        {
            string appVersion = _deviceInfo["app_version"] as string;
            Assert.IsNotNull(appVersion);
        }

        [Test]
        public void Collect_LocaleIsNotNull()
        {
            string locale = _deviceInfo["locale"] as string;
            Assert.IsNotNull(locale);
        }

        // ── Consistency ─────────────────────────────────────────────────

        [Test]
        public void Collect_MultipleCalls_ReturnConsistentPlatform()
        {
            var first = DeviceInfoCollector.Collect();
            var second = DeviceInfoCollector.Collect();
            Assert.AreEqual(first["platform"], second["platform"]);
        }

        [Test]
        public void Collect_MultipleCalls_ReturnSameInstallId()
        {
            var first = DeviceInfoCollector.Collect();
            var second = DeviceInfoCollector.Collect();
            Assert.AreEqual(first["install_id"], second["install_id"],
                "Install ID should be stable across calls");
        }

        [Test]
        public void Collect_ExactlyNineKeys()
        {
            // DeviceInfoCollector returns exactly 9 keys:
            // platform, os_version, device_model, app_version, locale,
            // build_number, screen_size, install_id, timezone
            Assert.AreEqual(9, _deviceInfo.Count,
                $"Expected 9 device info keys, got {_deviceInfo.Count}");
        }
    }
}

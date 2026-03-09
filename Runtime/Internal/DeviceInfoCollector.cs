using System.Collections.Generic;
using UnityEngine;

namespace Layers.Unity.Internal
{
    /// <summary>
    /// Collects device information matching the Rust core's DeviceContext JSON schema.
    /// Fields: platform, os_version, device_model, app_version, locale, build_number,
    /// screen_size, install_id, timezone.
    /// </summary>
    internal static class DeviceInfoCollector
    {
        internal static Dictionary<string, object> Collect()
        {
            return new Dictionary<string, object>
            {
                ["platform"] = "unity",
                ["os_version"] = SystemInfo.operatingSystem,
                ["device_model"] = SystemInfo.deviceModel,
                ["app_version"] = Application.version,
                ["locale"] = System.Globalization.CultureInfo.CurrentCulture.Name,
                ["build_number"] = Application.version,
                ["screen_size"] = $"{Screen.width}x{Screen.height}",
                ["install_id"] = InstallIdProvider.GetOrCreate(),
                ["timezone"] = System.TimeZoneInfo.Local.Id
            };
        }
    }
}

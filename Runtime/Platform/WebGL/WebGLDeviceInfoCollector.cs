using System.Collections.Generic;
using UnityEngine;

namespace Layers.Unity.Internal
{
    /// <summary>
    /// Collects device information for WebGL builds using browser APIs via the jslib.
    /// Falls back to Unity's SystemInfo where browser APIs are unavailable.
    ///
    /// On WebGL, some SystemInfo values (like deviceModel) return generic strings
    /// like "WebGL". The jslib can provide more accurate values from navigator.userAgent
    /// and other browser APIs.
    ///
    /// Sentinel hygiene from <see cref="DeviceInfoCollector"/> is applied to
    /// os_version / device_model / app_version / build_number so invalid or
    /// unknown values become JSON null rather than literal "unknown" strings.
    /// </summary>
#if UNITY_WEBGL && !UNITY_EDITOR
    internal static class WebGLDeviceInfoCollector
    {
        internal static Dictionary<string, object> Collect()
        {
            // Use jslib browser APIs for more accurate device info
            string locale = WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetLanguage())
                            ?? System.Globalization.CultureInfo.CurrentCulture.Name;
            string screenSize = WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetScreenSize())
                                ?? $"{Screen.width}x{Screen.height}";
            string timezone = WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetTimezone())
                              ?? System.TimeZoneInfo.Local.Id;
            string osVersionRaw = WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetPlatformOS())
                                  ?? SystemInfo.operatingSystem;

            return new Dictionary<string, object>
            {
                ["platform"] = "web",
                ["os_version"] = DeviceInfoCollector.SanitizeOsVersion(osVersionRaw),
                ["device_model"] = DeviceInfoCollector.SanitizeDeviceModel(SystemInfo.deviceModel),
                ["app_version"] = DeviceInfoCollector.SanitizeAppVersion(Application.version),
                ["locale"] = locale,
                // No separate build-number concept on WebGL — emit null rather
                // than duplicating Application.version, which would conflate
                // version_name and build_code downstream.
                ["build_number"] = null,
                ["screen_size"] = screenSize,
                ["install_id"] = InstallIdProvider.GetOrCreate(),
                ["timezone"] = timezone
            };
        }
    }
#endif
}

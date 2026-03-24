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
            string osVersion = WebGLStringHelper.ReadAndFree(WebGLBindings.LayersWebGL_GetPlatformOS())
                               ?? SystemInfo.operatingSystem;

            return new Dictionary<string, object>
            {
                ["platform"] = "unity",
                ["os_version"] = osVersion,
                ["device_model"] = SystemInfo.deviceModel,
                ["app_version"] = Application.version,
                ["locale"] = locale,
                ["build_number"] = Application.version,
                ["screen_size"] = screenSize,
                ["install_id"] = InstallIdProvider.GetOrCreate(),
                ["timezone"] = timezone
            };
        }
    }
#endif
}

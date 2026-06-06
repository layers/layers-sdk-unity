using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Layers.Unity.Internal
{
    /// <summary>
    /// Collects device information matching the Rust core's DeviceContext JSON schema.
    /// Fields: platform, os_version, device_model, app_version, locale, build_number,
    /// screen_size, install_id, timezone.
    ///
    /// Unity's <c>SystemInfo.operatingSystem</c> and <c>SystemInfo.deviceModel</c> are
    /// NOT used on iOS or Android because they have known data-quality issues:
    ///  - On iOS, SystemInfo can return the literal string "unknown" if read before
    ///    UIDevice is fully booted, and that placeholder then leaks downstream into
    ///    Meta CAPI / TikTok CAPI requests as a real value.
    ///  - On Android, SystemInfo.operatingSystem returns the verbose
    ///    "Android OS 13 / API-33 (TP1A.220624.014/...)" string built from
    ///    Build.FINGERPRINT — not the dotted-decimal version other SDKs send.
    ///
    /// Instead we call directly into UIKit (iOS) or android.os.Build / PackageManager
    /// (Android) via P/Invoke / JNI. If a native API returns nil/empty/error, we emit
    /// JSON <c>null</c> over the wire — NEVER substitute a sentinel like "unknown".
    /// Downstream pipelines treat <c>null</c> as "not collected, skip"; sentinels get
    /// forwarded as real values and break validation.
    /// </summary>
    internal static class DeviceInfoCollector
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern IntPtr layers_ios_get_os_version();

        [DllImport("__Internal")]
        private static extern IntPtr layers_ios_get_device_model();

        [DllImport("__Internal")]
        private static extern IntPtr layers_ios_get_app_version();

        [DllImport("__Internal")]
        private static extern IntPtr layers_ios_get_build_number();

        [DllImport("__Internal")]
        private static extern void layers_devinfo_free(IntPtr ptr);

        private static string ReadNativeString(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            try
            {
                string value = Marshal.PtrToStringAnsi(ptr);
                return string.IsNullOrEmpty(value) ? null : value;
            }
            finally
            {
                layers_devinfo_free(ptr);
            }
        }
#endif

        // Sentinel-hygiene regexes. A value is dropped (replaced with null) if
        // it doesn't match the expected shape — this prevents bad upstream
        // values from being forwarded as if they were real data.
        private static readonly Regex OsVersionPattern = new Regex(@"^[\d.]+$", RegexOptions.Compiled);
        private static readonly Regex DeviceModelPattern = new Regex(@"^[\w\s.,()'\-/]{1,64}$", RegexOptions.Compiled);
        private static readonly Regex AppVersionPattern = new Regex(@"^[\d.]+$", RegexOptions.Compiled);
        private static readonly Regex BuildNumberPattern = new Regex(@"^[\w.\-]+$", RegexOptions.Compiled);

        internal static Dictionary<string, object> Collect()
        {
            string osVersion = SanitizeOsVersion(GetRawOsVersion());
            string deviceModel = SanitizeDeviceModel(GetRawDeviceModel());
            string appVersion = SanitizeAppVersion(GetRawAppVersion());
            string buildNumber = SanitizeBuildNumber(GetRawBuildNumber());

            return new Dictionary<string, object>
            {
                ["platform"] = RuntimePlatform,
                ["os_version"] = osVersion,
                ["device_model"] = deviceModel,
                ["app_version"] = appVersion,
                ["locale"] = System.Globalization.CultureInfo.CurrentCulture.Name,
                ["build_number"] = buildNumber,
                ["screen_size"] = $"{Screen.width}x{Screen.height}",
                ["install_id"] = InstallIdProvider.GetOrCreate(),
                ["timezone"] = System.TimeZoneInfo.Local.Id
            };
        }

        // ── Raw platform reads ──────────────────────────────────────────

        private static string GetRawOsVersion()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return ReadNativeString(layers_ios_get_os_version());
#elif UNITY_ANDROID && !UNITY_EDITOR
            return AndroidModule.GetOsVersion();
#else
            // Editor / desktop / WebGL fallback. SystemInfo is reliable here
            // because there is no UIDevice race and no Build.FINGERPRINT
            // concatenation. The sanitizer still drops it if it's "unknown".
            return SystemInfo.operatingSystem;
#endif
        }

        private static string GetRawDeviceModel()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return ReadNativeString(layers_ios_get_device_model());
#elif UNITY_ANDROID && !UNITY_EDITOR
            return AndroidModule.GetDeviceModel();
#else
            return SystemInfo.deviceModel;
#endif
        }

        private static string GetRawAppVersion()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return ReadNativeString(layers_ios_get_app_version());
#elif UNITY_ANDROID && !UNITY_EDITOR
            return AndroidModule.GetAppVersion();
#else
            return Application.version;
#endif
        }

        private static string GetRawBuildNumber()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return ReadNativeString(layers_ios_get_build_number());
#elif UNITY_ANDROID && !UNITY_EDITOR
            return AndroidModule.GetBuildNumber();
#else
            // Editor / desktop / WebGL: no separate build-number concept is
            // exposed by Unity's managed API, so we emit null rather than
            // duplicating Application.version.
            return null;
#endif
        }

        // ── Sentinel hygiene ────────────────────────────────────────────
        //
        // Each platform-specific accessor may return an unwanted value:
        //   - iOS native bridge returns NULL on failure (already null here).
        //   - Android JNI returns null on exception.
        //   - Editor SystemInfo returns "unknown" / "n/a" / empty on some hosts.
        // We collapse all of those to null so the wire payload carries JSON
        // null instead of a misleading sentinel string.

        internal static string SanitizeOsVersion(string raw) =>
            MatchOrNull(raw, OsVersionPattern);

        internal static string SanitizeDeviceModel(string raw) =>
            MatchOrNull(raw, DeviceModelPattern);

        internal static string SanitizeAppVersion(string raw) =>
            MatchOrNull(raw, AppVersionPattern);

        internal static string SanitizeBuildNumber(string raw) =>
            MatchOrNull(raw, BuildNumberPattern);

        private static string MatchOrNull(string raw, Regex pattern)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string trimmed = raw.Trim();
            return pattern.IsMatch(trimmed) ? trimmed : null;
        }

        internal static string RuntimePlatform
        {
            get
            {
#if UNITY_IOS
                return "ios";
#elif UNITY_ANDROID
                return "android";
#elif UNITY_WEBGL
                return "web";
#elif UNITY_STANDALONE_OSX
                return "macos";
#else
                return "unity";
#endif
            }
        }
    }
}

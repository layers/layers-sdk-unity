// InstallEventGate.cs
// Layers Unity SDK
//
// Prevents false `is_first_launch` events when the SDK is added to an
// existing app. If the app was installed more than 24 hours ago and the
// SDK has never run before, suppresses `is_first_launch: true` on the
// first `app_open` event.
//
// iOS: reads the app bundle's creation date via native plugin.
// Android: reads PackageInfo.firstInstallTime via AndroidJavaClass.
// Editor / other: falls back to trusting the PlayerPrefs flag.

using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Layers.Unity.Internal
{
    /// <summary>
    /// Install event gating logic.
    ///
    /// Prevents false <c>is_first_launch</c> events when the SDK is added to an
    /// existing app. If the app was installed more than 24 hours ago and the SDK
    /// has never run before (no <c>layers_install_id</c> in PlayerPrefs), this
    /// class suppresses <c>is_first_launch = true</c> on the first <c>app_open</c> event.
    ///
    /// This mirrors the Flutter SDK's <c>InstallEventGate</c> logic.
    /// </summary>
    internal static class InstallEventGate
    {
        private const string FirstLaunchKey = "layers_first_launch_tracked";

        /// <summary>
        /// Maximum age of an app installation (in milliseconds) for which the SDK
        /// will consider the first launch as a genuine new install.
        /// If the app was installed more than 24 hours ago AND no prior SDK state
        /// exists, the SDK suppresses <c>is_first_launch = true</c>.
        /// </summary>
        internal const long InstallEventMaxDiffMs = 24L * 60 * 60 * 1000;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern long layers_get_first_install_time_ms();
#endif

        /// <summary>
        /// Determine the <c>is_first_launch</c> value for the <c>app_open</c> event.
        ///
        /// Manages the first-launch flag in PlayerPrefs and applies install event gating.
        ///
        /// Returns <c>true</c> if this is a genuine first launch, <c>false</c> otherwise.
        /// </summary>
        internal static bool DetermineIsFirstLaunch()
        {
            bool isFirstLaunchByFlag;

            if (PlayerPrefs.GetInt(FirstLaunchKey, 0) == 1)
            {
                // Flag already set -- this is NOT the first launch
                isFirstLaunchByFlag = false;
            }
            else
            {
                // Flag not set -- this IS the first launch (by flag)
                isFirstLaunchByFlag = true;
                // Persist immediately so subsequent launches are not treated as first launch
                PlayerPrefs.SetInt(FirstLaunchKey, 1);
                PlayerPrefs.Save();
            }

            return ShouldTreatAsNewInstall(isFirstLaunchByFlag, _hadInstallIdBeforeInit);
        }

        // ── Pre-init snapshot ──────────────────────────────────────────────
        // Must be captured before DeviceInfoCollector.Collect() creates the
        // install_id key, otherwise the install-event gating check always
        // sees the key as present and skips the 24-hour threshold.
        private static bool _hadInstallIdBeforeInit;

        /// <summary>
        /// Snapshot whether <c>layers_install_id</c> already exists in PlayerPrefs.
        /// Must be called BEFORE <see cref="InstallIdProvider.GetOrCreate"/>.
        /// </summary>
        internal static void CapturePreInitState()
        {
            _hadInstallIdBeforeInit = PlayerPrefs.HasKey("layers_install_id");
        }

        /// <summary>
        /// Determine whether this is a genuine new install or an existing app
        /// that just added the Layers SDK.
        ///
        /// Logic:
        /// 1. If the flag says this is NOT the first launch, respect that.
        /// 2. If the SDK had prior state (layers_install_id already existed in
        ///    PlayerPrefs), trust the flag -- this is a returning user.
        /// 3. If the SDK had NO prior state AND the app was installed more than
        ///    24 hours ago, suppress is_first_launch.
        /// 4. If the SDK had no prior state AND the app was installed within 24
        ///    hours, this is a genuine new install.
        /// </summary>
        internal static bool ShouldTreatAsNewInstall(bool isFirstLaunchByFlag, bool hadPriorState)
        {
            // If the flag says this isn't the first launch, respect that.
            if (!isFirstLaunchByFlag) return false;

            // If the SDK had prior state (install_id already in PlayerPrefs
            // BEFORE this session created it), trust the flag -- this is a
            // real returning-user first-launch scenario.
            if (hadPriorState) return true;

            // SDK has no prior state -- check whether the app itself is a recent install.
            try
            {
                long firstInstallTimeMs = GetFirstInstallTimeMs();
                if (firstInstallTimeMs <= 0)
                {
                    // Couldn't read install time -- trust the flag as safe default
                    return true;
                }

                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                long elapsed = nowMs - firstInstallTimeMs;
                bool isRecentInstall = elapsed <= InstallEventMaxDiffMs;

                if (!isRecentInstall)
                {
                    LayersLogger.Log(
                        $"Install event gated: app installed {elapsed / 1000}s ago " +
                        $"(threshold={InstallEventMaxDiffMs / 1000}s), " +
                        "no prior SDK state -- suppressing is_first_launch");
                }

                return isRecentInstall;
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"Failed to read firstInstallTime: {e.Message}");
                // If we can't read the install time, default to trusting the flag
                return true;
            }
        }

        /// <summary>
        /// Get the app's first install time in milliseconds since epoch.
        ///
        /// - iOS: reads the app bundle's creation date via a native Objective-C plugin.
        /// - Android: reads <c>PackageInfo.firstInstallTime</c> via <c>AndroidJavaClass</c>.
        /// - Editor/other: returns 0 (unknown).
        /// </summary>
        private static long GetFirstInstallTimeMs()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return layers_get_first_install_time_ms();
#elif UNITY_ANDROID && !UNITY_EDITOR
            return GetAndroidFirstInstallTime();
#else
            return 0;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static long GetAndroidFirstInstallTime()
        {
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var context = activity.Call<AndroidJavaObject>("getApplicationContext"))
                {
                    string packageName = context.Call<string>("getPackageName");
                    using (var pm = context.Call<AndroidJavaObject>("getPackageManager"))
                    using (var packageInfo = pm.Call<AndroidJavaObject>("getPackageInfo", packageName, 0))
                    {
                        // PackageInfo.firstInstallTime is in milliseconds since epoch
                        return packageInfo.Get<long>("firstInstallTime");
                    }
                }
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"Android firstInstallTime failed: {e.Message}");
                return 0;
            }
        }
#endif
    }
}

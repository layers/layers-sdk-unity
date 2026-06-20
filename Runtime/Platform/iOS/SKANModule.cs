// SKANModule.cs
// Layers Unity SDK
//
// C# wrapper for SKAdNetwork (SKAN) on iOS.
//
// The Rust core (core/src/skan.rs) is the single source of truth for rule
// evaluation, presets, the monotonic conversion floor, and persistence. This
// module bridges to the native Objective-C StoreKit APIs (which the core cannot
// call) and reports the native apply outcome back to the core. There is no
// parallel C# rule engine.
//
// On non-iOS platforms (Android, Editor), all methods are safe no-ops.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Layers.Unity.Internal;

namespace Layers.Unity
{
    /// <summary>
    /// SKAN coarse conversion values for SKAdNetwork 4.0 (iOS 16.1+).
    /// </summary>
    public enum SKANCoarseValue
    {
        Low,
        Medium,
        High
    }

    /// <summary>
    /// SKAdNetwork module for iOS install attribution.
    /// Provides access to SKAdNetwork APIs for registering attribution,
    /// updating conversion values, and querying SKAN version support.
    /// On non-iOS platforms (Android, Editor), all methods are safe no-ops.
    /// </summary>
    public static class SKANModule
    {
#if UNITY_IOS && !UNITY_EDITOR
        // Objective-C StoreKit bridge (Plugins/iOS/LayersSKANBridge.m).
        [DllImport("__Internal")]
        private static extern bool layers_skan_is_supported();

        [DllImport("__Internal")]
        private static extern IntPtr layers_skan_get_version();

        [DllImport("__Internal")]
        private static extern void layers_skan_register();

        [DllImport("__Internal")]
        private static extern void layers_skan_update_conversion_value(int fineValue);

        [DllImport("__Internal")]
        private static extern void layers_skan_update_postback(
            int fineValue, string coarseValue, bool lockWindow);
#endif

        /// <summary>
        /// Check if SKAdNetwork is supported on this device (iOS 14.0+).
        /// </summary>
        /// <returns>True if SKAN APIs are available.</returns>
        public static bool IsSupported
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                return layers_skan_is_supported();
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Get the highest SKAN version supported by the current OS.
        /// Possible values: "4.0", "3.0", "2.2", "2.1", "2.0", "unsupported".
        /// </summary>
        /// <returns>The SKAN version string.</returns>
        public static string GetVersion()
        {
#if UNITY_IOS && !UNITY_EDITOR
            IntPtr ptr = layers_skan_get_version();
            if (ptr == IntPtr.Zero)
                return "unsupported";

            // The bridge returns a static literal — read-only, never freed.
            string version = Marshal.PtrToStringUTF8(ptr);
            return string.IsNullOrEmpty(version) ? "unsupported" : version;
#else
            return "unsupported";
#endif
        }

        /// <summary>
        /// Register the app for ad network attribution.
        /// Uses the best available API for the OS version:
        /// iOS 15.4+: updatePostbackConversionValue(0),
        /// iOS 14.0+: registerAppForAdNetworkAttribution().
        /// Should be called early in the app lifecycle (e.g., during initialization).
        /// </summary>
        public static void Register()
        {
#if UNITY_IOS && !UNITY_EDITOR
            layers_skan_register();
#endif
        }

        /// <summary>
        /// Update the fine conversion value (0-63).
        /// Uses the best available API for the OS version:
        /// iOS 15.4+: updatePostbackConversionValue:completionHandler:,
        /// iOS 14.0+: updateConversionValue: (deprecated but functional).
        /// </summary>
        /// <param name="fineValue">The fine conversion value (0-63).</param>
        public static void UpdateConversionValue(int fineValue)
        {
            if (fineValue < 0 || fineValue > 63)
            {
                UnityEngine.Debug.LogWarning(
                    $"[Layers] SKANModule.UpdateConversionValue: fineValue {fineValue} is outside valid range 0-63. Clamping.");
                fineValue = Math.Clamp(fineValue, 0, 63);
            }

#if UNITY_IOS && !UNITY_EDITOR
            layers_skan_update_conversion_value(fineValue);
#endif
        }

        /// <summary>
        /// Update the postback conversion value with coarse value and lock window (SKAN 4.0).
        /// Requires iOS 16.1+. Falls back to fine-value-only update on older versions.
        /// </summary>
        /// <param name="fineValue">The fine conversion value (0-63).</param>
        /// <param name="coarseValue">The coarse conversion value (Low, Medium, High).</param>
        /// <param name="lockWindow">Whether to lock the current postback window.</param>
        public static void UpdatePostbackConversionValue(
            int fineValue, SKANCoarseValue coarseValue, bool lockWindow)
        {
            if (fineValue < 0 || fineValue > 63)
            {
                UnityEngine.Debug.LogWarning(
                    $"[Layers] SKANModule.UpdatePostbackConversionValue: fineValue {fineValue} is outside valid range 0-63. Clamping.");
                fineValue = Math.Clamp(fineValue, 0, 63);
            }

#if UNITY_IOS && !UNITY_EDITOR
            string coarseStr = CoarseValueToString(coarseValue);
            layers_skan_update_postback(fineValue, coarseStr, lockWindow);
#endif
        }

        /// <summary>
        /// Check if SKAN 4.0 features (coarse values, multiple postbacks) are available.
        /// Equivalent to checking GetVersion() returns "4.0".
        /// </summary>
        /// <returns>True if SKAN 4.0 is supported (iOS 16.1+).</returns>
        public static bool SupportsSKAN4()
        {
            return GetVersion() == "4.0";
        }

        private static string CoarseValueToString(SKANCoarseValue value)
        {
            switch (value)
            {
                case SKANCoarseValue.High:
                    return "high";
                case SKANCoarseValue.Medium:
                    return "medium";
                case SKANCoarseValue.Low:
                default:
                    return "low";
            }
        }

        // ── SKAN Auto-Config & Rule Engine (delegated to the Rust core) ─────────

        /// <summary>
        /// A conversion value rule that maps events (with optional conditions)
        /// to SKAN fine conversion values, coarse values, and lock window flags.
        /// </summary>
        public struct SKANConversionRule
        {
            public string EventName;
            public int ConversionValue;
            public int Priority;
            public SKANCoarseValue? CoarseValue;
            public bool LockWindow;
            /// <summary>
            /// Optional conditions: keys are property names, values are either
            /// direct match values (string/double/bool) or operator dictionaries
            /// (e.g., { ">": 10, "<": 100 }).
            /// </summary>
            public Dictionary<string, object> Conditions;
        }

        /// <summary>Whether Apple's postback window has been armed this launch.
        /// One-shot: re-registering would reset the OS conversion value.</summary>
        private static bool _skanArmed;

        /// <summary>
        /// The highest fine conversion value posted so far (the Rust core's
        /// install-scoped monotonic floor).
        /// </summary>
        public static int CurrentValue
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                int v = NativeBindings.layers_skan_current_value();
                return v < 0 ? 0 : v;
#else
                return 0;
#endif
            }
        }

        /// <summary>
        /// The name of the currently active preset ("subscriptions", "engagement",
        /// "iap", "custom"), or null if no rules are configured.
        /// </summary>
        public static string CurrentPreset
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                return NativeStringHelper.ReadAndFree(NativeBindings.layers_skan_current_preset());
#else
                return null;
#endif
            }
        }

        /// <summary>
        /// Whether SKAN is enabled (a non-disabled skan config applied). Gates
        /// whether tracked events are forwarded to the SKAN engine.
        /// </summary>
        public static bool IsAutoConfigured
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                return NativeBindings.layers_skan_is_enabled() != 0;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Apply a named preset in the Rust core, replacing any existing rules.
        /// Presets match the React Native and Swift SDK presets.
        /// </summary>
        /// <param name="preset">One of "subscriptions", "engagement", "iap"
        /// (case-insensitive; "ecommerce" aliases "iap").</param>
        public static void SetPreset(string preset)
        {
#if UNITY_IOS && !UNITY_EDITOR
            string err = NativeStringHelper.ReadAndFree(
                NativeBindings.layers_skan_set_preset(preset));
            if (!string.IsNullOrEmpty(err))
            {
                LayersLogger.Warn($"SKAN setPreset failed: {err}");
            }
#endif
        }

        /// <summary>
        /// Replace the active conversion rules in the Rust core with a custom set.
        /// </summary>
        public static void SetCustomRules(List<SKANConversionRule> rules)
        {
#if UNITY_IOS && !UNITY_EDITOR
            string json = SerializeRules(rules);
            string err = NativeStringHelper.ReadAndFree(
                NativeBindings.layers_skan_set_rules(json));
            if (!string.IsNullOrEmpty(err))
            {
                LayersLogger.Warn($"SKAN setCustomRules failed: {err}");
            }
#endif
        }

        /// <summary>
        /// Forward an event to the Rust core's SKAN engine. If the core returns an
        /// update, apply it via StoreKit and report the outcome so the core commits
        /// its monotonic floor only on success (a failed apply is re-issued on the
        /// next event). Call after every Track/Screen when <see cref="IsAutoConfigured"/>.
        /// </summary>
        public static void ProcessEvent(string eventName, Dictionary<string, object> properties)
        {
#if UNITY_IOS && !UNITY_EDITOR
            string propsJson = JsonHelper.Serialize(properties ?? new Dictionary<string, object>());
            string decisionJson = NativeStringHelper.ReadAndFree(
                NativeBindings.layers_skan_process_event(eventName, propsJson));
            if (string.IsNullOrEmpty(decisionJson))
            {
                return;
            }

            if (!TryParseDecision(decisionJson, out int fineValue, out string coarse, out bool lockWindow))
            {
                return;
            }

            // Apply via StoreKit, then report the outcome so the core advances its
            // floor only on success. A synchronous bridge failure rolls back via
            // record(success: false); the void bridge doesn't surface async failures.
            bool applied = true;
            try
            {
                if (!string.IsNullOrEmpty(coarse))
                {
                    layers_skan_update_postback(fineValue, coarse, lockWindow);
                }
                else
                {
                    layers_skan_update_conversion_value(fineValue);
                }
            }
            catch (Exception e)
            {
                applied = false;
                LayersLogger.Warn($"SKAN native update failed: {e.Message}");
            }

            NativeStringHelper.ReadAndFree(
                NativeBindings.layers_skan_record_conversion_result(
                    (byte)Math.Clamp(fineValue, 0, 63), applied));
#endif
        }

        /// <summary>
        /// Configure SKAN from the cached remote config. The Rust core already holds
        /// the config (fed via layers_update_remote_config) and owns all parsing
        /// (preset / rules / ecommerce alias / unknown-preset clearing / disable).
        /// Arms Apple's window once if SKAN is enabled. The <paramref name="configJson"/>
        /// argument is accepted for back-compat but ignored — the core reads its own.
        /// </summary>
        public static void ConfigureFromRemoteConfig(string configJson)
        {
#if UNITY_IOS && !UNITY_EDITOR
            // A non-null error (e.g. unknown preset → rules cleared) is non-fatal.
            NativeStringHelper.ReadAndFree(
                NativeBindings.layers_skan_configure_from_remote_config());

            if (NativeBindings.layers_skan_is_enabled() != 0)
            {
                ArmOnce();
            }
#endif
        }

        /// <summary>Reset SKAN arming state. Primarily for testing / shutdown.</summary>
        internal static void ResetAutoConfig()
        {
            _skanArmed = false;
        }

        /// <summary>Arm Apple's postback window exactly once per launch (re-arming
        /// would reset the OS conversion value mid-session).</summary>
        private static void ArmOnce()
        {
            if (_skanArmed)
            {
                return;
            }
            _skanArmed = true;
            Register();
        }

        // --- JSON helpers (bridge between C# rule structs and the core's wire format) ---

        private static string SerializeRules(List<SKANConversionRule> rules)
        {
            var list = new List<object>();
            if (rules != null)
            {
                foreach (var r in rules)
                {
                    var map = new Dictionary<string, object>
                    {
                        ["eventName"] = r.EventName,
                        ["conversionValue"] = r.ConversionValue,
                        ["priority"] = r.Priority,
                        ["lockWindow"] = r.LockWindow,
                    };
                    if (r.CoarseValue.HasValue)
                    {
                        map["coarseValue"] = CoarseValueToString(r.CoarseValue.Value);
                    }
                    if (r.Conditions != null)
                    {
                        map["conditions"] = r.Conditions;
                    }
                    list.Add(map);
                }
            }
            return JsonHelper.SerializeAny(list);
        }

        private static bool TryParseDecision(
            string json, out int fineValue, out string coarse, out bool lockWindow)
        {
            fineValue = 0;
            coarse = null;
            lockWindow = false;
            try
            {
                var map = JsonHelper.Deserialize(json);
                if (map == null)
                {
                    return false;
                }
                if (map.TryGetValue("fineValue", out object fv) && fv != null)
                {
                    fineValue = Convert.ToInt32(fv, CultureInfo.InvariantCulture);
                }
                if (map.TryGetValue("coarseValue", out object cv))
                {
                    coarse = cv as string;
                }
                if (map.TryGetValue("lockWindow", out object lw) && lw is bool b)
                {
                    lockWindow = b;
                }
                return true;
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"SKAN decision parse failed: {e.Message}");
                return false;
            }
        }
    }
}

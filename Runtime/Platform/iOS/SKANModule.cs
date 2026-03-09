// SKANModule.cs
// Layers Unity SDK
//
// C# wrapper for SKAdNetwork (SKAN) on iOS.
// Delegates to native Objective-C bridge via P/Invoke.
// On non-iOS platforms, all methods are safe no-ops.
//
// SKAN auto-config: reads the "skan" section from remote config to automatically
// configure presets or custom conversion value rules, matching the Swift and
// React Native SDK behavior.

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

            string version = Marshal.PtrToStringAnsi(ptr);
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

        // ── SKAN Auto-Config & Rule Engine ──────────────────────────────

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

        /// <summary>The active conversion rules, sorted by priority (highest first).</summary>
        private static List<SKANConversionRule> _rules = new List<SKANConversionRule>();

        /// <summary>The current fine conversion value reported to SKAN.</summary>
        private static int _currentValue;

        /// <summary>Name of the active preset, or null if none / custom rules.</summary>
        private static string _currentPreset;

        /// <summary>Whether auto-config has been applied at least once.</summary>
        private static bool _autoConfigured;

        /// <summary>
        /// The current fine conversion value tracked by the rule engine.
        /// </summary>
        public static int CurrentValue => _currentValue;

        /// <summary>
        /// The name of the currently active preset ("subscriptions", "engagement",
        /// "iap", "custom"), or null if no rules are configured.
        /// </summary>
        public static string CurrentPreset => _currentPreset;

        /// <summary>
        /// Whether SKAN auto-config from remote config has been applied.
        /// </summary>
        public static bool IsAutoConfigured => _autoConfigured;

        /// <summary>
        /// Apply a named preset, replacing any existing rules.
        /// Presets match the React Native and Swift SDK presets.
        /// </summary>
        /// <param name="preset">One of "subscriptions", "engagement", "iap".</param>
        public static void SetPreset(string preset)
        {
            var rules = GetPresetRules(preset);
            if (rules == null || rules.Count == 0)
            {
                LayersLogger.Warn($"SKAN: unknown preset '{preset}'");
                return;
            }
            _rules = rules;
            _currentPreset = preset;
            SortRules();
        }

        /// <summary>
        /// Set custom conversion value rules, replacing any existing rules.
        /// </summary>
        /// <param name="rules">The list of conversion rules.</param>
        public static void SetCustomRules(List<SKANConversionRule> rules)
        {
            _rules = rules ?? new List<SKANConversionRule>();
            _currentPreset = "custom";
            SortRules();
        }

        /// <summary>
        /// Process a tracked event against the active rules. If a matching rule has
        /// a higher conversion value than the current value, SKAN is updated.
        /// Conversion values only increase (never decrease), matching SKAN semantics.
        ///
        /// Call this after every <see cref="Layers.Track"/> call when SKAN rules are
        /// active. The SDK wires this up automatically when auto-config is enabled.
        /// </summary>
        /// <param name="eventName">The event name.</param>
        /// <param name="properties">The event properties, or null.</param>
        public static void ProcessEvent(string eventName, Dictionary<string, object> properties)
        {
            if (_rules == null || _rules.Count == 0) return;

            var props = properties ?? new Dictionary<string, object>();

            foreach (var rule in _rules)
            {
                if (EvaluateRule(rule, eventName, props))
                {
                    // Only update if the new value is strictly higher (SKAN semantics)
                    if (rule.ConversionValue > _currentValue)
                    {
                        int previousValue = _currentValue;
                        _currentValue = rule.ConversionValue;

                        if (rule.CoarseValue.HasValue)
                        {
                            UpdatePostbackConversionValue(
                                rule.ConversionValue, rule.CoarseValue.Value, rule.LockWindow);
                        }
                        else
                        {
                            UpdateConversionValue(rule.ConversionValue);
                        }

                        LayersLogger.Log(
                            $"SKAN conversion value updated: {previousValue} -> {rule.ConversionValue} (event: {eventName})");
                    }

                    // First match (highest priority) wins
                    break;
                }
            }
        }

        /// <summary>
        /// Configure SKAN from the remote config JSON. Reads the "skan" section
        /// and applies either a preset or custom rules. Auto-registers for
        /// attribution if rules are configured.
        ///
        /// This is called automatically when the remote config is fetched.
        /// The expected JSON structure mirrors the Swift and React Native SDKs:
        /// <code>
        /// {
        ///   "skan": {
        ///     "enabled": true,
        ///     "preset": "subscriptions",        // OR
        ///     "customRules": [
        ///       {
        ///         "eventName": "purchase",
        ///         "conversionValue": 10,
        ///         "priority": 5,
        ///         "conditions": { "revenue": { ">=": 10 } },
        ///         "coarseValue": "high",
        ///         "lockWindow": true
        ///       }
        ///     ]
        ///   }
        /// }
        /// </code>
        /// </summary>
        /// <param name="configJson">The full remote config JSON string.</param>
        public static void ConfigureFromRemoteConfig(string configJson)
        {
            if (string.IsNullOrEmpty(configJson)) return;

            Dictionary<string, object> config;
            try
            {
                config = JsonHelper.Deserialize(configJson);
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"SKAN auto-config: failed to parse config JSON: {e.Message}");
                return;
            }

            if (config == null) return;

            // Extract the "skan" section
            if (!config.ContainsKey("skan")) return;
            var skanObj = config["skan"] as Dictionary<string, object>;
            if (skanObj == null) return;

            // Respect explicit "enabled": false
            if (skanObj.ContainsKey("enabled"))
            {
                var enabled = skanObj["enabled"];
                if (enabled is bool b && !b) return;
                if (enabled is double d && d == 0) return;
            }

            // Option 1: preset name
            if (skanObj.ContainsKey("preset") && skanObj["preset"] is string preset)
            {
                string presetLower = preset.ToLowerInvariant();
                // Map "ecommerce" to "iap" to match Swift SDK behavior
                if (presetLower == "ecommerce") presetLower = "iap";

                var presetRules = GetPresetRules(presetLower);
                if (presetRules != null && presetRules.Count > 0)
                {
                    _rules = presetRules;
                    _currentPreset = presetLower;
                    SortRules();
                    Register();
                    _autoConfigured = true;
                    LayersLogger.Log($"SKAN auto-configured from remote config: preset={presetLower}");
                }
                else
                {
                    LayersLogger.Warn($"SKAN auto-config: unknown preset '{preset}'");
                }
                return;
            }

            // Option 2: custom rules array
            if (skanObj.ContainsKey("customRules") && skanObj["customRules"] is List<object> rulesArray)
            {
                var parsedRules = ParseCustomRules(rulesArray);
                if (parsedRules.Count > 0)
                {
                    _rules = parsedRules;
                    _currentPreset = "custom";
                    SortRules();
                    Register();
                    _autoConfigured = true;
                    LayersLogger.Log(
                        $"SKAN auto-configured from remote config: {parsedRules.Count} custom rules");
                }
            }
        }

        /// <summary>
        /// Reset the SKAN rule engine state. Primarily useful for testing.
        /// </summary>
        internal static void ResetAutoConfig()
        {
            _rules = new List<SKANConversionRule>();
            _currentValue = 0;
            _currentPreset = null;
            _autoConfigured = false;
        }

        // ── Private: Rule Evaluation ────────────────────────────────────

        private static bool EvaluateRule(
            SKANConversionRule rule, string eventName, Dictionary<string, object> properties)
        {
            if (rule.EventName != eventName) return false;
            if (rule.Conditions == null || rule.Conditions.Count == 0) return true;

            foreach (var kvp in rule.Conditions)
            {
                string key = kvp.Key;
                object expected = kvp.Value;

                object actual = properties.ContainsKey(key) ? properties[key] : null;

                if (expected is Dictionary<string, object> operators)
                {
                    // Operator-based condition: { ">": 10, "<": 100 }
                    foreach (var op in operators)
                    {
                        if (!EvaluateOperator(actual, op.Key, op.Value))
                            return false;
                    }
                }
                else
                {
                    // Direct equality check
                    if (!ValuesEqual(actual, expected))
                        return false;
                }
            }

            return true;
        }

        private static bool EvaluateOperator(object actual, string op, object expected)
        {
            double a = ToDouble(actual);
            double b = ToDouble(expected);

            switch (op)
            {
                case ">": return a > b;
                case ">=": return a >= b;
                case "<": return a < b;
                case "<=": return a <= b;
                case "==":
                case "=": return ValuesEqual(actual, expected);
                case "!=": return !ValuesEqual(actual, expected);
                default: return false;
            }
        }

        private static double ToDouble(object value)
        {
            if (value == null) return 0;
            if (value is double d) return d;
            if (value is int i) return i;
            if (value is long l) return l;
            if (value is float f) return f;
            if (value is string s && double.TryParse(
                    s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                return parsed;
            return 0;
        }

        private static bool ValuesEqual(object a, object b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;

            // Compare numerically if both are numeric types
            if (IsNumeric(a) && IsNumeric(b))
                return Math.Abs(ToDouble(a) - ToDouble(b)) < 0.0001;

            return string.Equals(a.ToString(), b.ToString(), StringComparison.Ordinal);
        }

        private static bool IsNumeric(object value)
        {
            return value is double || value is int || value is long || value is float;
        }

        private static void SortRules()
        {
            _rules.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        // ── Private: Custom Rules Parsing ───────────────────────────────

        private static List<SKANConversionRule> ParseCustomRules(List<object> rulesArray)
        {
            var result = new List<SKANConversionRule>();

            foreach (var item in rulesArray)
            {
                if (!(item is Dictionary<string, object> ruleDict)) continue;

                if (!ruleDict.ContainsKey("eventName") || !(ruleDict["eventName"] is string eventName))
                    continue;

                int conversionValue = 0;
                if (ruleDict.ContainsKey("conversionValue"))
                    conversionValue = (int)ToDouble(ruleDict["conversionValue"]);

                int priority = 0;
                if (ruleDict.ContainsKey("priority"))
                    priority = (int)ToDouble(ruleDict["priority"]);

                SKANCoarseValue? coarseValue = null;
                if (ruleDict.ContainsKey("coarseValue") && ruleDict["coarseValue"] is string coarseStr)
                {
                    switch (coarseStr.ToLowerInvariant())
                    {
                        case "high": coarseValue = SKANCoarseValue.High; break;
                        case "medium": coarseValue = SKANCoarseValue.Medium; break;
                        case "low": coarseValue = SKANCoarseValue.Low; break;
                    }
                }

                bool lockWindow = false;
                if (ruleDict.ContainsKey("lockWindow"))
                {
                    var lw = ruleDict["lockWindow"];
                    if (lw is bool lwBool) lockWindow = lwBool;
                    else if (lw is double lwDouble) lockWindow = lwDouble != 0;
                }

                Dictionary<string, object> conditions = null;
                if (ruleDict.ContainsKey("conditions") &&
                    ruleDict["conditions"] is Dictionary<string, object> cond)
                {
                    conditions = cond;
                }

                result.Add(new SKANConversionRule
                {
                    EventName = eventName,
                    ConversionValue = conversionValue,
                    Priority = priority,
                    CoarseValue = coarseValue,
                    LockWindow = lockWindow,
                    Conditions = conditions
                });
            }

            return result;
        }

        // ── Private: Preset Configurations ──────────────────────────────

        /// <summary>
        /// Get the preset rules matching the React Native and Swift SDK presets.
        /// Returns null for unknown preset names.
        /// </summary>
        private static List<SKANConversionRule> GetPresetRules(string preset)
        {
            switch (preset)
            {
                case "subscriptions": return GetSubscriptionsPreset();
                case "engagement": return GetEngagementPreset();
                case "iap": return GetIapPreset();
                default: return null;
            }
        }

        private static List<SKANConversionRule> GetSubscriptionsPreset()
        {
            return new List<SKANConversionRule>
            {
                new SKANConversionRule
                {
                    EventName = "app_open", ConversionValue = 1, Priority = 1
                },
                new SKANConversionRule
                {
                    EventName = "screen_view", ConversionValue = 8, Priority = 3,
                    Conditions = new Dictionary<string, object> { ["screen_name"] = "onboarding_complete" }
                },
                new SKANConversionRule
                {
                    EventName = "trial_start", ConversionValue = 20, Priority = 5
                },
                new SKANConversionRule
                {
                    EventName = "purchase_success", ConversionValue = 35, Priority = 7,
                    Conditions = new Dictionary<string, object>
                    {
                        ["revenue"] = new Dictionary<string, object> { ["<"] = 5.0 }
                    }
                },
                new SKANConversionRule
                {
                    EventName = "subscription_start", ConversionValue = 50, Priority = 10
                },
                new SKANConversionRule
                {
                    EventName = "subscription_renew", ConversionValue = 63, Priority = 15
                }
            };
        }

        private static List<SKANConversionRule> GetEngagementPreset()
        {
            return new List<SKANConversionRule>
            {
                new SKANConversionRule
                {
                    EventName = "app_open", ConversionValue = 1, Priority = 1
                },
                new SKANConversionRule
                {
                    EventName = "content_open", ConversionValue = 5, Priority = 2
                },
                new SKANConversionRule
                {
                    EventName = "search", ConversionValue = 12, Priority = 4
                },
                new SKANConversionRule
                {
                    EventName = "bookmark_add", ConversionValue = 25, Priority = 6
                },
                new SKANConversionRule
                {
                    EventName = "app_open", ConversionValue = 40, Priority = 8,
                    Conditions = new Dictionary<string, object>
                    {
                        ["session_count"] = new Dictionary<string, object> { [">"] = 3.0 }
                    }
                },
                new SKANConversionRule
                {
                    EventName = "app_open", ConversionValue = 63, Priority = 12,
                    Conditions = new Dictionary<string, object>
                    {
                        ["session_count"] = new Dictionary<string, object> { [">"] = 10.0 }
                    }
                }
            };
        }

        private static List<SKANConversionRule> GetIapPreset()
        {
            return new List<SKANConversionRule>
            {
                new SKANConversionRule
                {
                    EventName = "app_open", ConversionValue = 1, Priority = 1
                },
                new SKANConversionRule
                {
                    EventName = "paywall_show", ConversionValue = 8, Priority = 2
                },
                new SKANConversionRule
                {
                    EventName = "purchase_attempt", ConversionValue = 15, Priority = 3
                },
                new SKANConversionRule
                {
                    EventName = "purchase_success", ConversionValue = 25, Priority = 5,
                    Conditions = new Dictionary<string, object>
                    {
                        ["revenue"] = new Dictionary<string, object> { ["<"] = 1.0 }
                    }
                },
                new SKANConversionRule
                {
                    EventName = "purchase_success", ConversionValue = 40, Priority = 7,
                    Conditions = new Dictionary<string, object>
                    {
                        ["revenue"] = new Dictionary<string, object> { [">="] = 1.0, ["<"] = 10.0 }
                    }
                },
                new SKANConversionRule
                {
                    EventName = "purchase_success", ConversionValue = 63, Priority = 10,
                    Conditions = new Dictionary<string, object>
                    {
                        ["revenue"] = new Dictionary<string, object> { [">="] = 10.0 }
                    }
                }
            };
        }
    }
}

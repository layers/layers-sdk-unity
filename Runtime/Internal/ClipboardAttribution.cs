// ClipboardAttribution.cs
// Layers Unity SDK
//
// Reads the system clipboard on first install for a Layers click URL.
// This enables deferred deep link attribution via clipboard.
//
// Behavior:
//   - Only runs once per install (checked via PlayerPrefs flag).
//   - Only runs when remote config has clipboard_attribution_enabled = true.
//   - Reads clipboard via GUIUtility.systemCopyBuffer (Unity API).
//   - Checks for Layers click URL patterns: in.layers.com/c/ or link.layers.com/c/
//   - Returns the full URL and extracted click ID if found.

using System.Text.RegularExpressions;
using UnityEngine;

namespace Layers.Unity.Internal
{
    /// <summary>
    /// Result of a clipboard attribution check.
    /// Contains the full click URL and extracted click ID.
    /// </summary>
    internal class ClipboardAttributionData
    {
        /// <summary>
        /// The full Layers click URL found on the clipboard.
        /// </summary>
        internal string ClickUrl { get; set; }

        /// <summary>
        /// The click ID extracted from the URL path (the segment after /c/).
        /// </summary>
        internal string ClickId { get; set; }
    }

    /// <summary>
    /// Reads the system clipboard for Layers attribution URLs on first install.
    /// Gated by a PlayerPrefs flag so the clipboard is only read once per install,
    /// and by remote config <c>clipboard_attribution_enabled</c>.
    ///
    /// On iOS 16+, reading the clipboard triggers a system paste consent dialog.
    /// </summary>
    internal static class ClipboardAttribution
    {
        private const string CheckedKey = "layers_clipboard_checked";

        /// <summary>
        /// Regex pattern matching Layers click URLs.
        /// Matches: https://in.layers.com/c/{click_id} or https://link.layers.com/c/{click_id}
        /// </summary>
        private static readonly Regex ClickUrlPattern = new Regex(
            @"https?://(in\.layers\.com|link\.layers\.com)/c/([^?\s]+)",
            RegexOptions.Compiled);

        private static ClipboardAttributionData _cachedResult;
        private static bool _hasChecked;

        /// <summary>
        /// Check the clipboard for a Layers attribution URL.
        /// Only reads once per install (subsequent calls return cached result).
        /// Returns null if no Layers URL is found, or if already checked.
        /// </summary>
        /// <returns>Attribution data if a Layers click URL was found, null otherwise.</returns>
        internal static ClipboardAttributionData Check()
        {
            if (_hasChecked)
                return _cachedResult;

            _hasChecked = true;

            // Check if we've already read clipboard in a previous session
            if (PlayerPrefs.GetInt(CheckedKey, 0) == 1)
            {
                LayersLogger.Log("Clipboard already checked (previous session)");
                return null;
            }

            // Mark as checked immediately so we never read again
            PlayerPrefs.SetInt(CheckedKey, 1);
            PlayerPrefs.Save();

            // Read system clipboard
            string clipboardText = null;
            try
            {
                clipboardText = GUIUtility.systemCopyBuffer;
            }
            catch (System.Exception e)
            {
                LayersLogger.Warn($"Failed to read clipboard: {e.Message}");
                return null;
            }

            if (string.IsNullOrEmpty(clipboardText))
            {
                LayersLogger.Log("Clipboard empty, no attribution URL");
                return null;
            }

            // Check for Layers click URL pattern
            var match = ClickUrlPattern.Match(clipboardText);
            if (!match.Success)
            {
                LayersLogger.Log("No Layers attribution URL on clipboard");
                return null;
            }

            _cachedResult = new ClipboardAttributionData
            {
                ClickUrl = clipboardText,
                ClickId = match.Groups[2].Value
            };

            LayersLogger.Log($"Clipboard attribution URL found: {_cachedResult.ClickUrl}");
            return _cachedResult;
        }

        /// <summary>
        /// The cached result, if previously checked. Does not trigger a new read.
        /// </summary>
        internal static ClipboardAttributionData CachedResult => _cachedResult;
    }
}

// DebugOverlay.cs
// Layers Unity SDK
//
// IMGUI-based debug overlay that displays real-time SDK state.
// Uses Unity's OnGUI() system for maximum compatibility (works in
// all render pipelines, no Canvas or EventSystem required).
//
// Toggle via Layers.ShowDebugOverlay() / Layers.HideDebugOverlay().

using System;
using System.Collections.Generic;
using Layers.Unity.Internal;
using UnityEngine;

namespace Layers.Unity
{
    /// <summary>
    /// IMGUI debug overlay that displays real-time SDK state including:
    /// SDK version, queue depth, session ID, install ID, app ID, environment,
    /// consent status, and recent events.
    ///
    /// Provides a "Flush Now" button for manual event delivery.
    /// Auto-refreshes every 1.5 seconds.
    ///
    /// This component is added to the [Layers] hidden GameObject and is
    /// toggled via <see cref="Layers.ShowDebugOverlay"/> and
    /// <see cref="Layers.HideDebugOverlay"/>.
    /// </summary>
    internal class DebugOverlay : MonoBehaviour
    {
        // ── Constants ────────────────────────────────────────────────────

        private const int MaxRecentEvents = 10;
        private const float RefreshIntervalSec = 1.5f;
        private const float WindowWidth = 380f;
        private const float WindowMinHeight = 100f;

        // ── State ────────────────────────────────────────────────────────

        private Rect _windowRect = new Rect(16, 80, WindowWidth, WindowMinHeight);
        private bool _isCollapsed;
        private float _lastRefreshTime;

        // Snapshot of SDK state, refreshed periodically.
        private bool _sdkInitialized;
        private string _environment = "--";
        private string _appId = "--";
        private string _sessionId = "--";
        private string _userId = "(anonymous)";
        private int _queueDepth;
        private string _installId = "--";
        private string _consent = "--";
        private string _lastFlush = "never";

        // Recent events log, shared across the SDK.
        private static readonly List<string> _recentEvents = new List<string>();
        private static string _lastFlushInfo;

        private GUIStyle _headerStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _eventStyle;
        private GUIStyle _buttonStyle;
        private bool _stylesInitialized;

        // ── Static Event Recording (called by Layers.Track) ─────────────

        /// <summary>
        /// Record a tracked event for display in the debug overlay.
        /// Called internally by the SDK when debug mode is enabled.
        /// </summary>
        internal static void RecordEvent(string eventName, int propertyCount)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            string entry = propertyCount > 0
                ? $"{time}  {eventName} ({propertyCount}p)"
                : $"{time}  {eventName}";
            _recentEvents.Insert(0, entry);
            while (_recentEvents.Count > MaxRecentEvents)
                _recentEvents.RemoveAt(_recentEvents.Count - 1);
        }

        /// <summary>
        /// Record a flush completion for display in the debug overlay.
        /// </summary>
        internal static void RecordFlush(bool success, string message = null)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            if (success)
                _lastFlushInfo = message != null ? $"{time} ok ({message})" : $"{time} ok";
            else
                _lastFlushInfo = message != null ? $"{time} fail ({message})" : $"{time} fail";
        }

        /// <summary>
        /// Clear all debug overlay state.
        /// </summary>
        internal static void ResetState()
        {
            _recentEvents.Clear();
            _lastFlushInfo = null;
        }

        // ── Unity Lifecycle ──────────────────────────────────────────────

        private void OnEnable()
        {
            _lastRefreshTime = 0;
            RefreshData();
        }

        private void OnGUI()
        {
            if (!_stylesInitialized)
                InitStyles();

            // Auto-refresh at interval
            if (!_isCollapsed && Time.realtimeSinceStartup - _lastRefreshTime > RefreshIntervalSec)
            {
                RefreshData();
                _lastRefreshTime = Time.realtimeSinceStartup;
            }

            // Use a unique window ID to avoid conflicts with game UI
            _windowRect = GUILayout.Window(
                928374, // arbitrary unique ID
                _windowRect,
                DrawWindow,
                "",
                GUIStyle.none,
                GUILayout.Width(WindowWidth));
        }

        // ── Window Drawing ───────────────────────────────────────────────

        private void DrawWindow(int windowId)
        {
            // Dark semi-transparent background
            var bgRect = new Rect(0, 0, _windowRect.width, _windowRect.height);
            GUI.color = new Color(0.11f, 0.11f, 0.12f, 0.9f);
            GUI.DrawTexture(bgRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginVertical();

            DrawHeader();

            if (!_isCollapsed)
            {
                DrawSeparator();
                DrawDataRows();
                DrawSeparator();
                DrawRecentEvents();
                DrawSeparator();
                DrawFlushButton();
            }

            GUILayout.EndVertical();

            // Make the window draggable by its entire area
            GUI.DragWindow();
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label("Layers SDK Debug", _headerStyle);
            GUILayout.FlexibleSpace();

            string toggleChar = _isCollapsed ? "\u25B6" : "\u25BC";
            if (GUILayout.Button(toggleChar, _headerStyle, GUILayout.Width(20)))
            {
                _isCollapsed = !_isCollapsed;
                if (!_isCollapsed) RefreshData();
            }

            GUILayout.EndHorizontal();
        }

        private void DrawDataRows()
        {
            DrawRow("SDK Version", $"unity/0.1.0");
            DrawRow("Status", _sdkInitialized ? "Initialized" : "Not initialized");
            DrawRow("Environment", _environment);
            DrawRow("App ID", _appId);
            DrawRow("User ID", _userId);
            DrawRow("Session ID", _sessionId);
            DrawRow("Queue Depth", _queueDepth.ToString());
            DrawRow("Install ID", _installId);
            DrawRow("Consent", _consent);
            DrawRow("Last Flush", _lastFlush);
        }

        private void DrawRow(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _labelStyle, GUILayout.Width(100));
            GUILayout.Label(value, _valueStyle);
            GUILayout.EndHorizontal();
        }

        private void DrawRecentEvents()
        {
            GUILayout.Label("Recent Events", _labelStyle);

            if (_recentEvents.Count == 0)
            {
                GUILayout.Label("  (no events tracked)", _eventStyle);
            }
            else
            {
                int count = Mathf.Min(_recentEvents.Count, MaxRecentEvents);
                for (int i = 0; i < count; i++)
                {
                    GUILayout.Label(_recentEvents[i], _eventStyle);
                }
            }
        }

        private void DrawFlushButton()
        {
            GUILayout.Space(4);
            if (GUILayout.Button("Flush Now", _buttonStyle, GUILayout.Height(28)))
            {
                if (Layers.IsInitialized)
                {
                    Layers.Flush();
                    RefreshData();
                }
            }
            GUILayout.Space(4);
        }

        private void DrawSeparator()
        {
            GUILayout.Space(2);
            var rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            GUI.color = new Color(1f, 1f, 1f, 0.2f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUILayout.Space(2);
        }

        // ── Data Refresh ─────────────────────────────────────────────────

        private void RefreshData()
        {
            _sdkInitialized = Layers.IsInitialized;

            if (_sdkInitialized)
            {
                string sessionId = Layers.SessionId;
                _sessionId = TruncateId(sessionId);
                _queueDepth = Layers.QueueDepth;
            }
            else
            {
                _sessionId = "--";
                _queueDepth = 0;
            }

            _userId = string.IsNullOrEmpty(Layers.UserId) ? "(anonymous)" : Layers.UserId;
            _installId = TruncateId(InstallIdProvider.GetOrCreate());
            _lastFlush = _lastFlushInfo ?? "never";

            // Read config state via internal accessors on the Layers facade
            _environment = Layers.Environment ?? "--";
            _appId = Layers.AppId ?? "--";

            // Read consent from the Rust core's remote config if available
            string remoteConfig = Layers.RemoteConfig;
            if (!string.IsNullOrEmpty(remoteConfig))
            {
                try
                {
                    var configDict = JsonHelper.Deserialize(remoteConfig);
                    if (configDict != null && configDict.TryGetValue("consent", out object consentObj))
                        _consent = consentObj.ToString();
                    else
                        _consent = "granted";
                }
                catch
                {
                    _consent = "unknown";
                }
            }
            else
            {
                _consent = _sdkInitialized ? "granted" : "--";
            }
        }

        private static string TruncateId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "--";
            if (id.Length > 8) return id.Substring(0, 8) + "...";
            return id;
        }

        // ── Style Initialization ─────────────────────────────────────────

        private void InitStyles()
        {
            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                normal = { textColor = new Color(1f, 1f, 1f, 0.6f) }
            };

            _valueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                normal = { textColor = new Color(0f, 1f, 0.53f, 0.9f) } // green
            };

            _eventStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                normal = { textColor = new Color(1f, 1f, 1f, 0.75f) }
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            _stylesInitialized = true;
        }
    }
}

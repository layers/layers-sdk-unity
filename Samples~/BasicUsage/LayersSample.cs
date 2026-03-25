using UnityEngine;
using System.Collections.Generic;
using Layers.Unity;

/// <summary>
/// Sample MonoBehaviour demonstrating basic usage of the Layers Unity SDK.
///
/// Attach this script to a GameObject in your scene to see the SDK in action.
/// Set the App ID in the Inspector or directly in the field below.
///
/// This sample covers:
/// - SDK initialization with configuration
/// - Error handling via OnError event
/// - Deep link listener registration
/// - Custom event tracking with properties
/// - Screen view tracking
/// - User identification
/// - User properties (set and set-once)
/// - Consent management
/// - ATT request (iOS only, no-op on other platforms)
/// - Graceful shutdown
/// </summary>
public class LayersSample : MonoBehaviour
{
    [Header("Layers SDK Configuration")]
    [Tooltip("Your app ID from the Layers dashboard")]
    [SerializeField] private string appId = "your-app-id";

    [Tooltip("Override the ingest URL for local testing (e.g. http://localhost:3333)")]
    [SerializeField] private string baseUrl = "";

    [Tooltip("Enable verbose SDK logging")]
    [SerializeField] private bool enableDebug = true;

    void Start()
    {
        // ── Initialize the SDK ──────────────────────────────────────
        var config = new LayersConfig
        {
            AppId = appId,
            Environment = LayersEnvironment.Development,
            EnableDebug = enableDebug,
            AutoTrackAppOpen = true,
            AutoTrackDeepLinks = true
        };

        // Point to mock server for local testing
        if (!string.IsNullOrEmpty(baseUrl))
            config.BaseUrl = baseUrl;

        LayersSDK.Initialize(config);

        // ── Error handling ──────────────────────────────────────────
        LayersSDK.OnError += (method, error) =>
            Debug.LogWarning($"[LayersSample] Layers error in {method}: {error}");

        // ── Deep link listener ──────────────────────────────────────
        DeepLinksModule.OnDeepLinkReceived += (data) =>
        {
            Debug.Log($"[LayersSample] Deep link received: {data.RawUrl}");
            Debug.Log($"[LayersSample]   Scheme: {data.Scheme}, Host: {data.Host}, Path: {data.Path}");

            if (data.Attribution != null)
            {
                if (!string.IsNullOrEmpty(data.Attribution.UtmSource))
                    Debug.Log($"[LayersSample]   UTM Source: {data.Attribution.UtmSource}");
                if (!string.IsNullOrEmpty(data.Attribution.UtmCampaign))
                    Debug.Log($"[LayersSample]   UTM Campaign: {data.Attribution.UtmCampaign}");
            }
        };

        // ── Track a custom event ────────────────────────────────────
        LayersSDK.Track("game_started", new Dictionary<string, object>
        {
            ["level"] = 1,
            ["difficulty"] = "normal",
            ["tutorial_complete"] = false
        });

        // ── Track a screen view ─────────────────────────────────────
        LayersSDK.Screen("MainMenu");

        // ── Identify user ───────────────────────────────────────────
        LayersSDK.Identify("user-123");

        // ── Set user properties ─────────────────────────────────────
        LayersSDK.SetUserProperties(new Dictionary<string, object>
        {
            ["plan"] = "premium",
            ["signup_date"] = "2024-01-15",
            ["level"] = 42
        });

        // ── Set user properties once (only set if not already set) ──
        LayersSDK.SetUserPropertiesOnce(new Dictionary<string, object>
        {
            ["first_seen"] = System.DateTime.UtcNow.ToString("o"),
            ["initial_platform"] = "unity"
        });

        // ── Set consent ─────────────────────────────────────────────
        LayersSDK.SetConsent(analytics: true, advertising: false);

        // ── Request ATT (iOS only - no-op on Android/Editor) ────────
        ATTModule.RequestTracking((status) =>
        {
            Debug.Log($"[LayersSample] ATT status: {status}");
            if (status == LayersATTStatus.Authorized)
            {
                LayersSDK.SetConsent(advertising: true);
                Debug.Log("[LayersSample] Advertising consent granted via ATT");
            }
        });

        Debug.Log("[LayersSample] SDK initialized and sample events tracked");
    }

    /// <summary>
    /// Example: track an in-game purchase event.
    /// Call this from a UI button or game logic.
    /// </summary>
    public void TrackPurchase(string itemId, double price, string currency)
    {
        LayersSDK.Track("purchase", new Dictionary<string, object>
        {
            ["item_id"] = itemId,
            ["price"] = price,
            ["currency"] = currency
        });
    }

    /// <summary>
    /// Example: track a level completion event.
    /// </summary>
    public void TrackLevelComplete(int level, float timeSeconds)
    {
        LayersSDK.Track("level_complete", new Dictionary<string, object>
        {
            ["level"] = level,
            ["time_seconds"] = timeSeconds,
            ["user_id"] = LayersSDK.UserId
        });
    }

    /// <summary>
    /// Example: manually flush events (e.g. before a loading screen).
    /// </summary>
    public void FlushEvents()
    {
        LayersSDK.Flush();
    }

    /// <summary>
    /// Example: reset user state on logout.
    /// </summary>
    public void OnLogout()
    {
        LayersSDK.Reset();
        Debug.Log("[LayersSample] User state reset");
    }

    void OnDestroy()
    {
        LayersSDK.Shutdown();
    }
}

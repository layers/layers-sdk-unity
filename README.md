# Layers Analytics SDK for Unity

Rust-powered analytics SDK for Unity — iOS, Android, and WebGL.

## Installation

### Unity Package Manager (UPM)

Add to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.layers.analytics": "https://github.com/layers/layers-sdk-unity.git#v2.1.5"
  }
}
```

Or via Unity Editor: **Window > Package Manager > + > Add package from git URL**:

```
https://github.com/layers/layers-sdk-unity.git#v2.1.5
```

## Quick Start

```csharp
using Layers.Unity;
using System.Collections.Generic;

// Initialize (once, e.g. in your first scene's Awake)
LayersSDK.Initialize(new LayersConfig
{
    AppId = "your-app-id",
    Environment = LayersEnvironment.Production,
    AutoTrackAppOpen = true,
    AutoTrackDeepLinks = true
});

// Track events
LayersSDK.Track("button_clicked", new Dictionary<string, object>
{
    ["button"] = "signup",
    ["screen"] = "onboarding"
});

// Screen views
LayersSDK.Screen("MainMenu");

// Identify users
LayersSDK.Identify("user-123");

// Set user properties
LayersSDK.SetUserProperties(new Dictionary<string, object>
{
    ["plan"] = "premium",
    ["level"] = 42
});

// Set user properties (only if not already set)
LayersSDK.SetUserPropertiesOnce(new Dictionary<string, object>
{
    ["first_seen"] = "2026-03-24"
});

// Group association
LayersSDK.Group("org-456", new Dictionary<string, object>
{
    ["name"] = "Acme Corp"
});
```

## Standard Events

Use the `StandardEvents` class for canonical event names and typed helpers:

```csharp
// Purchase
LayersSDK.Track(StandardEvents.Purchase,
    StandardEvents.PurchaseEvent(9.99, "USD", "premium_upgrade"));

// Level complete
LayersSDK.Track(StandardEvents.LevelComplete,
    StandardEvents.LevelCompleteEvent("world_3", 42, 185.5));

// Search
LayersSDK.Track(StandardEvents.Search,
    StandardEvents.SearchEvent("blue sword", resultCount: 12));

// Login
LayersSDK.Track(StandardEvents.Login,
    StandardEvents.LoginEvent("google"));
```

## Commerce

The `Commerce` class provides typed helpers for e-commerce events:

```csharp
// Track a purchase
Commerce.TrackPurchase(
    price: 9.99,
    currency: "USD",
    productId: "premium_monthly",
    transactionId: "txn_abc123"
);

// Track a subscription
Commerce.TrackSubscription(
    price: 4.99,
    currency: "USD",
    productId: "premium_monthly",
    period: "monthly",
    transactionId: "sub_xyz789",
    isTrial: true
);

// Track add to cart
Commerce.TrackAddToCart("sword_01", "Blue Sword", 2.99, 1, "weapons");
```

## Deep Links

Deep links are auto-tracked by default. To handle them manually:

```csharp
DeepLinksModule.OnDeepLinkReceived += (DeepLinkData data) =>
{
    Debug.Log($"Deep link: {data.RawUrl}");
    Debug.Log($"Path: {data.Path}");

    // Attribution data is auto-extracted
    if (data.Attribution?.UtmSource != null)
        Debug.Log($"Campaign: {data.Attribution.UtmSource}");
};
```

Parse a URL without tracking:

```csharp
var data = DeepLinksModule.ParseUrl("myapp://shop/item?id=123&utm_source=meta");
```

## iOS: App Tracking Transparency (ATT)

```csharp
#if UNITY_IOS
if (ATTModule.IsAvailable())
{
    ATTModule.RequestTracking((status) =>
    {
        if (status == LayersATTStatus.Authorized)
        {
            var idfa = ATTModule.GetIDFA();
            LayersSDK.SetConsent(analytics: true, advertising: true);
        }
        else
        {
            LayersSDK.SetConsent(analytics: true, advertising: false);
        }
    });
}
#endif
```

### iOS Build Setup

Add a `LayersSettings` asset via **Assets > Create > Layers > Settings** to configure:

- ATT usage description (the prompt shown to users)
- SKAdNetwork IDs (17 defaults included)
- URL schemes for deep linking
- Associated domains for Universal Links

The `LayersPostBuildProcessor` automatically modifies Info.plist and links required frameworks (AppTrackingTransparency, AdSupport, AdServices, StoreKit) during build.

## iOS: SKAdNetwork (SKAN)

SKAN conversion values are auto-configured from remote config. Manual usage:

```csharp
#if UNITY_IOS
SKANModule.Register();
SKANModule.UpdateConversionValue(42);

// SKAN 4.0
SKANModule.UpdatePostbackConversionValue(
    fineValue: 42,
    coarseValue: SKANCoarseValue.High,
    lockWindow: false
);
#endif
```

## Android

### Google Advertising ID

Auto-collected on init. Respects limit-ad-tracking. Manual access:

```csharp
#if UNITY_ANDROID
AndroidModule.GetAdvertisingId((gaid, isLimited) =>
{
    Debug.Log($"GAID: {gaid}, limited: {isLimited}");
});
#endif
```

### Install Referrer

Auto-collected on first launch. Manual access:

```csharp
#if UNITY_ANDROID
AndroidModule.GetInstallReferrer((result) =>
{
    Debug.Log($"Source: {result.UtmSource}");
    Debug.Log($"Campaign: {result.UtmCampaign}");

    // Track as event properties
    LayersSDK.Track("install_referrer", result.ToEventProperties());
});
#endif
```

## Consent Management

```csharp
// Grant all
LayersSDK.SetConsent(analytics: true, advertising: true, thirdPartySharing: true);

// Analytics only (no ads, no sharing)
LayersSDK.SetConsent(analytics: true, advertising: false, thirdPartySharing: false);
```

## Debug Overlay

Enable an in-game overlay showing SDK state, queue depth, and recent events:

```csharp
// Toggle via code
LayersSDK.EnableDebugOverlay();
LayersSDK.DisableDebugOverlay();
```

## Flush and Shutdown

Events are flushed automatically on a timer and on app background. Manual control:

```csharp
// Flush now
LayersSDK.Flush();

// Shutdown (also called automatically on Application.quitting)
LayersSDK.Shutdown();
```

## Error Handling

```csharp
LayersSDK.OnError += (message) =>
{
    Debug.LogWarning($"Layers SDK error: {message}");
};
```

## Configuration Reference

| Property             | Default                 | Description                               |
| -------------------- | ----------------------- | ----------------------------------------- |
| `AppId`              | required                | Your Layers app ID                        |
| `Environment`        | `Development`           | `Development`, `Staging`, or `Production` |
| `BaseUrl`            | `https://in.layers.com` | Ingest endpoint override                  |
| `EnableDebug`        | `false`                 | Verbose console logging                   |
| `FlushIntervalMs`    | `30000`                 | Auto-flush interval (ms)                  |
| `FlushThreshold`     | `20`                    | Events queued before auto-flush           |
| `MaxQueueSize`       | `10000`                 | Max events before dropping                |
| `MaxBatchSize`       | `20`                    | Events per HTTP batch                     |
| `AutoTrackAppOpen`   | `true`                  | Auto-fire `app_open` on init              |
| `AutoTrackDeepLinks` | `true`                  | Auto-fire `deep_link_opened`              |

## Requirements

- Unity 2021.3 LTS or later
- iOS 13.0+ / Android API 21+
- IL2CPP build (iOS requires it; Android recommended)

## Architecture

This SDK uses a shared Rust core compiled to native libraries:

- **iOS**: Static library (`.a`) linked via `__Internal` P/Invoke
- **Android**: Shared library (`.so`) per ABI via P/Invoke
- **WebGL**: WASM binary loaded via JavaScript bridge (`LayersWebGL.jslib`)

The Rust core handles event queuing, serialization, persistence, retry, and batching. The C# wrapper provides Unity-specific integrations (lifecycle, coroutine-based networking, platform APIs).

# Layers Analytics SDK for Unity

Analytics SDK for Unity — iOS and Android.

> WebGL is not supported. For web games, integrate the `@layers/client` web SDK from the hosting page instead.

## Installation

### Unity Package Manager (UPM)

Add to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.layers.analytics": "https://github.com/layers/layers-sdk-unity.git#v3.2.7"
  }
}
```

Or via Unity Editor: **Window > Package Manager > + > Add package from git URL**:

```
https://github.com/layers/layers-sdk-unity.git#v3.2.7
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
- SKAdNetwork IDs (23 defaults included)
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

### Required: External Dependency Manager for Unity (EDM4U)

The advertising ID and install referrer are provided by two Google Android
libraries, which this package declares in `Editor/LayersDependencies.xml`.
Resolving that file requires [EDM4U](https://github.com/googlesamples/unity-jar-resolver)
in your project — the same resolver Firebase, AppsFlyer and Adjust use.

Without EDM4U the libraries never reach your Android classpath. The SDK looks
them up by JNI class name and degrades quietly rather than crashing, so the
symptom is not an error: **the advertising ID and install referrer simply
resolve to null forever.** After installing EDM4U, run
`Assets > External Dependency Manager > Android Resolver > Resolve`.

The `com.google.android.gms.permission.AD_ID` permission that Android 13+
requires to read the advertising ID merges into your app automatically with
the resolved library — you do not need to declare it, and should not if you
exclude the dependency, since Play Console data-safety answers must match what
the app actually collects.

### Also required if you enable Minify

EDM4U puts the libraries on your classpath; it does not stop R8 from removing
them again. The SDK reaches both by JNI class name, so R8 has no static
reference to keep them alive, and `play-services-ads-identifier` ships no keep
rules of its own. **With Minify enabled they can be stripped and both features
silently return null** — in release builds only, since minification is off in
development builds, so the first sign is production attribution data that is
quietly empty.

Copy `Plugins/Android/layers-proguard-rules.txt` from this package into your
custom ProGuard file (Player Settings → Publishing Settings → Minify → _Custom
Proguard File_, which creates `Assets/Plugins/Android/proguard-user.txt`), or
append its contents to the one you already have.

This step is manual for now, and deliberately so rather than by necessity. It
_can_ be automated with a `Plugins/Android/*.androidlib` module carrying
`consumerProguardFiles`, exactly as the Flutter and React Native SDKs do. That
module would join the Gradle build of every project using this package, and
nothing in the Layers repo currently exercises a Unity Gradle build — CI runs
EditMode tests and an Android scripts-only compile, neither of which invokes
Gradle. Shipping it unverified would risk breaking every consumer's Android
build, which is worse than the null values it prevents. It will be automated
once there is a build gate that can prove it.

### Google Advertising ID

Auto-collected on init (requires EDM4U, above). Respects limit-ad-tracking.
Manual access:

```csharp
#if UNITY_ANDROID
AndroidModule.GetAdvertisingId((gaid, isLimited) =>
{
    Debug.Log($"GAID: {gaid}, limited: {isLimited}");
});
#endif
```

### Install Referrer

Auto-collected on first launch (requires EDM4U, above). Manual access:

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

## How Delivery Works

Events are queued locally, serialized, and persisted to disk so nothing is lost across app restarts or crashes. A coroutine-based flush loop batches queued events and delivers them over HTTP, retrying automatically on transient failures. On top of this, the C# layer adds Unity-specific integrations: lifecycle handling (`Application.quitting`, pause/resume), coroutine-based networking, and platform APIs for iOS and Android.

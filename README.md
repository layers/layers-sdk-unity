# Layers Analytics SDK for Unity

Rust-powered analytics SDK for Unity — iOS and Android.

## Installation

### Unity Package Manager (UPM)

Add to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.layers.analytics": "https://github.com/layers/layers-sdk-unity.git#v1.3.0"
  }
}
```

Or via Unity Editor: **Window → Package Manager → + → Add package from git URL**:
```
https://github.com/layers/layers-sdk-unity.git#v1.3.0
```

## Quick Start

```csharp
using Layers.Unity;

// Initialize
Layers.Initialize(new LayersConfig
{
    AppId = "your-app-id",
    Environment = LayersEnvironment.Production,
    AutoTrackAppOpen = true,
    AutoTrackDeepLinks = true
});

// Track events
Layers.Track("purchase_complete", new Dictionary<string, object>
{
    ["item"] = "premium_upgrade",
    ["price"] = 9.99
});

// Screen views
Layers.Screen("MainMenu");

// Identify users
Layers.Identify("user-123");

// Set user properties
Layers.SetUserProperties(new Dictionary<string, object>
{
    ["plan"] = "premium"
});
```

## iOS Features

### App Tracking Transparency (ATT)

```csharp
ATTModule.RequestTracking((status) =>
{
    if (status == ATTStatus.Authorized)
        Layers.SetConsent(advertising: true);
});
```

### SKAdNetwork (SKAN)

Auto-configured from remote config. Manual usage:

```csharp
SKANModule.Register();
SKANModule.UpdateConversionValue(42);
```

### Build Setup

Add a `LayersSettings` asset via **Assets → Create → Layers → Settings** to configure:
- ATT usage description
- SKAdNetwork IDs (16 defaults included)
- URL schemes for deep linking
- Associated domains for Universal Links

## Android Features

### Google Advertising ID (GAID)

Auto-collected on init. Respects limit-ad-tracking.

### Install Referrer

Auto-collected on first launch from Google Play Install Referrer API.

### Deep Links

Configure intent filters via the `LayersSettings` asset.

## Requirements

- Unity 2021.3 LTS or later
- iOS 13.0+ / Android API 21+
- IL2CPP build (iOS requires it; Android recommended)

## Architecture

This SDK uses a shared Rust core compiled to native libraries:
- **iOS**: Static library (`.a`) linked via `__Internal` P/Invoke
- **Android**: Shared library (`.so`) per ABI via P/Invoke

The Rust core handles event queuing, serialization, persistence, and batching. The C# wrapper provides Unity-specific integrations (lifecycle, networking, platform APIs).

## License

See [LICENSE](LICENSE).

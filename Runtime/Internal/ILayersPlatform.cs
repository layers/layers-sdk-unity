using System;
using System.Collections.Generic;

namespace Layers.Unity.Internal
{
    /// <summary>
    /// Platform abstraction for the Layers SDK. Separates native (iOS/Android/desktop)
    /// from WebGL targets. Each implementation delegates to the appropriate bridge:
    ///
    /// - <see cref="NativePlatform"/>: P/Invoke via <see cref="NativeBindings"/> → Rust FFI (C ABI)
    /// - <see cref="WebGLPlatform"/>: [DllImport("__Internal")] via <see cref="WebGLBindings"/> → jslib → Rust WASM
    ///
    /// The <see cref="Layers"/> facade uses <see cref="LayersPlatformFactory.Create"/>
    /// to select the correct implementation at runtime.
    /// </summary>
    internal interface ILayersPlatform
    {
        // ── Lifecycle ──────────────────────────────────────────────────

        /// <summary>
        /// Initialize the SDK with a JSON config string.
        /// Returns null on success, error message on failure.
        /// </summary>
        string Init(string configJson);

        /// <summary>
        /// Shut down the SDK, persisting remaining events.
        /// Returns null on success, error message on failure.
        /// </summary>
        string Shutdown();

        // ── Event Tracking ─────────────────────────────────────────────

        /// <summary>
        /// Track a custom event with optional properties JSON.
        /// Returns null on success, error message on failure.
        /// </summary>
        string Track(string eventName, string propertiesJson);

        /// <summary>
        /// Track a screen view with optional properties JSON.
        /// Returns null on success, error message on failure.
        /// </summary>
        string Screen(string screenName, string propertiesJson);

        // ── User Identity ──────────────────────────────────────────────

        /// <summary>
        /// Identify the current user by user ID.
        /// Returns null on success, error message on failure.
        /// </summary>
        string Identify(string userId);

        /// <summary>
        /// Set user properties (upsert semantics).
        /// Returns null on success, error message on failure.
        /// </summary>
        string SetUserProperties(string propertiesJson);

        /// <summary>
        /// Set user properties with "set once" semantics.
        /// Returns null on success, error message on failure.
        /// </summary>
        string SetUserPropertiesOnce(string propertiesJson);

        // ── Group ──────────────────────────────────────────────────────

        /// <summary>
        /// Associate subsequent events with a group (company, team, organization).
        /// Returns null on success, error message on failure.
        /// </summary>
        string Group(string groupId, string propertiesJson);

        // ── Consent ────────────────────────────────────────────────────

        /// <summary>
        /// Set consent state from a JSON string.
        /// Returns null on success, error message on failure.
        /// </summary>
        string SetConsent(string consentJson);

        // ── Device Context ─────────────────────────────────────────────

        /// <summary>
        /// Set device context from a JSON string.
        /// Returns null on success, error message on failure.
        /// </summary>
        string SetDeviceContext(string contextJson);

        // ── Flush / Drain ──────────────────────────────────────────────

        /// <summary>
        /// Flush queued events (synchronous persistence or sendBeacon).
        /// Returns null on success, error message on failure.
        /// </summary>
        string Flush();

        /// <summary>
        /// Drain up to count events from the queue as a serialized EventBatch JSON.
        /// Returns null if queue is empty, or the batch JSON string.
        /// </summary>
        string DrainBatch(uint count);

        /// <summary>
        /// Re-enqueue events after a failed HTTP delivery.
        /// Returns null on success, error message on failure.
        /// </summary>
        string RequeueEvents(string eventsJson);

        /// <summary>
        /// Return flush headers as a JSON string.
        /// </summary>
        string FlushHeadersJson();

        /// <summary>
        /// Return the events ingest URL.
        /// </summary>
        string EventsUrl();

        // ── Queue State ────────────────────────────────────────────────

        /// <summary>
        /// Get the number of queued events. Returns -1 if not initialized.
        /// </summary>
        int QueueDepth();

        // ── Session ────────────────────────────────────────────────────

        /// <summary>
        /// Get the current session ID.
        /// </summary>
        string GetSessionId();

        // ── Remote Config ──────────────────────────────────────────────

        /// <summary>
        /// Get the cached remote config as a JSON string.
        /// Returns null if no config has been fetched yet.
        /// </summary>
        string GetRemoteConfigJson();

        /// <summary>
        /// Update the cached remote config from a fetched JSON response body.
        /// Returns null on success, error message on failure.
        /// </summary>
        string UpdateRemoteConfig(string configJson, string etag);
    }
}

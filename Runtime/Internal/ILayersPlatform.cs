using System;
using System.Collections.Generic;

namespace Layers.Unity.Internal
{
    /// <summary>
    /// Platform abstraction for the Layers SDK.
    ///
    /// - <see cref="NativePlatform"/>: P/Invoke via <see cref="NativeBindings"/> → Rust FFI (C ABI)
    /// - Mock implementations via <see cref="LayersTestMode"/> for tests
    ///
    /// The <see cref="LayersSDK"/> facade uses <see cref="LayersPlatformFactory.Create"/>
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

        /// <summary>
        /// Get the per-device DebugView token, or null if debug mode is off.
        /// </summary>
        string GetDebugToken();

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

        // ── Tier 1: Super-properties ───────────────────────────────────

        /// <summary>
        /// Register super-properties merged into every subsequent track/screen call.
        /// </summary>
        string SetSuperProperties(string propertiesJson);

        /// <summary>
        /// Register super-properties only for keys not previously registered.
        /// </summary>
        string SetSuperPropertiesOnce(string propertiesJson);

        /// <summary>
        /// Remove a single super-property by key.
        /// </summary>
        string UnregisterSuperProperty(string key);

        /// <summary>
        /// Clear all super-properties and the once-keys history.
        /// </summary>
        string ClearSuperProperties();

        /// <summary>
        /// Snapshot of current super-properties as a JSON string ("{}" if none).
        /// </summary>
        string GetSuperPropertiesJson();

        // ── Tier 1: Timed events ───────────────────────────────────────

        /// <summary>
        /// Start a duration timer for the next track(name, ...) call.
        /// </summary>
        string TimeEvent(string eventName);

        /// <summary>
        /// Cancel a timed event without emitting it. Returns elapsed milliseconds, or 0.
        /// </summary>
        ulong CancelTimedEvent(string eventName);

        // ── Tier 1: Multi-group ────────────────────────────────────────

        /// <summary>
        /// Set membership for a single groupType (overwrites). Empty groupId removes.
        /// </summary>
        string SetGroup(string groupType, string groupId);

        /// <summary>
        /// Add a group membership without overwriting other types.
        /// </summary>
        string AddGroup(string groupType, string groupId);

        /// <summary>
        /// Remove a single groupType from the membership map.
        /// </summary>
        string RemoveGroup(string groupType);

        /// <summary>
        /// Snapshot of current $groups as a JSON object ("{}" if none).
        /// </summary>
        string GetGroupsJson();

        // ── Tier 1: User-property mutators ─────────────────────────────

        /// <summary>
        /// Increment a numeric user property by delta (negative decrements).
        /// </summary>
        string Increment(string key, double delta);

        /// <summary>
        /// Append a value (JSON-encoded) to a list user property.
        /// </summary>
        string Append(string key, string valueJson);

        /// <summary>
        /// Union an array (JSON-encoded) into a list user property.
        /// </summary>
        string Union(string key, string valuesJson);

        /// <summary>
        /// Remove a user property. Maps to $unset.
        /// </summary>
        string Unset(string key);

        // ── Tier 1: Identity reset + ID accessors ──────────────────────

        /// <summary>
        /// Clear identity, rotate device_id/anonymous_id, clear super-properties + groups.
        /// </summary>
        string Reset();

        /// <summary>
        /// Get the stable per-install device ID (or null if not set).
        /// </summary>
        string GetDeviceId();

        /// <summary>
        /// Get the anonymous ID (or null if not set).
        /// </summary>
        string GetAnonymousId();

        /// <summary>
        /// Get the monotonic session counter (0 if not initialized).
        /// </summary>
        uint GetSessionNumber();

        /// <summary>
        /// Get the SDK first-open timestamp as ISO-8601 (or null).
        /// </summary>
        string GetFirstOpenTime();

        // ── Tier 1: before_send filter hook ────────────────────────────

        /// <summary>
        /// Register a before_send filter callback. Receives event JSON, returns
        /// the (possibly mutated) JSON to keep, or null to drop the event.
        ///
        /// Note: WebGL implementation is a no-op (the WASM bridge handles
        /// before_send through the JS layer separately).
        /// </summary>
        string SetBeforeSend(Func<string, string> callback);

        /// <summary>
        /// Clear a previously-registered before_send callback.
        /// </summary>
        string ClearBeforeSend();

        // ── Tier 4: Feature flags ──────────────────────────────────────

        /// <summary>
        /// Evaluate a feature flag. Returns the value as a JSON-encoded
        /// string ("true"/"false" for binary, "\"variant_key\"" for
        /// multivariate, "null" if unknown). Side-effect: emits one
        /// $feature_flag_called event per (flag_key, response) per session.
        /// </summary>
        string GetFeatureFlagJson(string flagKey);

        /// <summary>
        /// Returns 1 (true), 0 (false), or -1 (error / not initialized).
        /// </summary>
        int IsFeatureEnabled(string flagKey);

        /// <summary>
        /// Look up the JSON payload attached to a flag. Returns "null" if
        /// the flag has no payload. Does NOT emit exposure.
        /// </summary>
        string GetFeatureFlagPayloadJson(string flagKey);

        /// <summary>
        /// Snapshot every known flag's current value as a JSON object.
        /// Does NOT emit exposure events.
        /// </summary>
        string GetAllFlagsJson();

        /// <summary>
        /// Drop cached flag definitions. Returns 1 if any were dropped, 0
        /// if none, -1 on error.
        /// </summary>
        int ReloadFeatureFlags();

        /// <summary>
        /// Override person properties for flag evaluation. Clears the
        /// exposure dedup cache so flipped responses re-emit.
        /// </summary>
        string SetPersonPropertiesForFlags(string propertiesJson);

        /// <summary>
        /// Seed bootstrap flag values + payloads (SSR pattern). JSON
        /// matches the Rust BootstrapData shape:
        /// <c>{"feature_flags": {...}, "feature_flag_payloads": {...}}</c>.
        /// </summary>
        string SetFeatureFlagBootstrap(string bootstrapJson);
    }
}

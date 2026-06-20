using System;
using System.Runtime.InteropServices;

namespace Layers.Unity.Internal
{
    /// <summary>
    /// P/Invoke declarations for the Rust core FFI (C ABI).
    /// Maps 1:1 to the exported functions in core/src/ffi.rs.
    ///
    /// Convention:
    /// - Success: returns IntPtr.Zero (null pointer)
    /// - Error: returns a heap-allocated C string with the error message
    /// - String outputs (session ID, URLs, JSON): return heap-allocated C strings
    /// - All returned non-null strings MUST be freed via layers_free_string()
    /// - layers_queue_depth returns i32 directly (-1 if not initialized)
    /// </summary>
    internal static class NativeBindings
    {
#if UNITY_IOS && !UNITY_EDITOR
        private const string LibName = "__Internal";
#else
        private const string LibName = "layers_core";
#endif

        // ── Lifecycle ──────────────────────────────────────────────────

        /// <summary>
        /// Initialize the SDK with a JSON config string.
        /// Returns null on success, error string on failure.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_init(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string config_json);

        /// <summary>
        /// Shut down the SDK, persisting remaining events.
        /// Returns null on success, error string on failure.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_shutdown();

        // ── Event Tracking ─────────────────────────────────────────────

        /// <summary>
        /// Track a custom event with optional properties JSON.
        /// Returns null on success, error string on failure.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_track(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string event_name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string properties_json);

        /// <summary>
        /// Track a screen view with optional properties JSON.
        /// Returns null on success, error string on failure.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_screen(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string screen_name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string properties_json);

        // ── User Identity ──────────────────────────────────────────────

        /// <summary>
        /// Identify the current user by user ID.
        /// Returns null on success, error string on failure.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_identify(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string user_id);

        /// <summary>
        /// Set user properties (upsert semantics).
        /// Returns null on success, error string on failure.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_set_user_properties(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string properties_json);

        /// <summary>
        /// Set user properties with "set once" semantics.
        /// Only properties whose keys have not been previously set are forwarded.
        /// Returns null on success, error string on failure.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_set_user_properties_once(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string properties_json);

        // ── Group ────────────────────────────────────────────────────

        /// <summary>
        /// Associate subsequent events with a group (company, team, organization).
        /// Returns null on success, error string on failure.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_group(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string group_id,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string properties_json);

        // ── Consent ────────────────────────────────────────────────────

        /// <summary>
        /// Set consent state from a JSON string like {"analytics": true, "advertising": false}.
        /// Returns null on success, error string on failure.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_set_consent(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string consent_json);

        // ── Device Context ─────────────────────────────────────────────

        /// <summary>
        /// Set device context from a JSON string with platform, os_version, device_model, etc.
        /// Returns null on success, error string on failure.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_set_device_context(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string context_json);

        // ── Flush / Drain ──────────────────────────────────────────────

        /// <summary>
        /// Flush queued events to persistence for later delivery.
        /// Returns null on success, error string on failure.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_flush();

        /// <summary>
        /// Drain up to count events from the queue as a serialized EventBatch JSON.
        /// Returns null if queue is empty, or a heap-allocated JSON string.
        /// Caller MUST free via layers_free_string.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_drain_batch(uint count);

        /// <summary>
        /// Re-enqueue events after a failed HTTP delivery.
        /// Returns the number of re-enqueued events as a string, or an error string.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_requeue_events(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string events_json);

        /// <summary>
        /// Single delivery gate owned by the Rust core (ADR 0001): privacy
        /// (consent/DNT), server-requested Retry-After delay, and the circuit
        /// breaker. Returns 1 if a flush should be attempted now, 0 otherwise.
        /// May claim the breaker's half-open probe slot — call only when there
        /// are events to send, and always follow a 1 with
        /// <see cref="layers_record_flush_result"/>.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte layers_should_attempt_flush();

        /// <summary>
        /// Report the outcome of a delivery attempt (status 0 = no response /
        /// network error) and get the verdict for the in-flight batch:
        /// 1 = Delivered (done), 2 = RetryLater (requeue the batch),
        /// 3 = Drop (discard the batch, surface the error).
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte layers_record_flush_result(
            ushort status,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string retry_after_header);

        /// <summary>
        /// Abort a flush attempt that never reached the wire (queue raced
        /// empty, URL/header setup failed). Releases a claimed half-open
        /// breaker probe without counting a delivery failure — use instead
        /// of <see cref="layers_record_flush_result"/> when NO request was
        /// made.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void layers_abort_flush_attempt();

        /// <summary>
        /// Return flush headers as a JSON string of [key, value] pairs.
        /// Caller MUST free via layers_free_string.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_flush_headers_json();

        /// <summary>
        /// Return the events ingest URL.
        /// Caller MUST free via layers_free_string.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_events_url();

        // ── Queue State ────────────────────────────────────────────────

        /// <summary>
        /// Get the number of queued events. Returns -1 if SDK is not initialized.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int layers_queue_depth();

        // ── Session ────────────────────────────────────────────────────

        /// <summary>
        /// Get the current session ID as a heap-allocated C string.
        /// Caller MUST free via layers_free_string.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_get_session_id();

        /// <summary>
        /// Get the per-device DebugView token as a heap-allocated C string,
        /// or IntPtr.Zero if `debug` was not enabled at init. Caller MUST
        /// free via layers_free_string.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_get_debug_token();

        // ── Remote Config ──────────────────────────────────────────────

        /// <summary>
        /// Get the cached remote config as a JSON string.
        /// Returns null if no config has been fetched yet.
        /// Caller MUST free via layers_free_string.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_get_remote_config_json();

        /// <summary>
        /// Update the cached remote config from a fetched JSON response body.
        /// etag may be IntPtr.Zero (null) if no ETag header was present.
        /// Returns null on success, error string on failure.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_update_remote_config(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string config_json,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string etag);

        // ── SKAN (SKAdNetwork) — the Rust core owns rule evaluation, presets, and
        // the monotonic floor. The wrapper bridges to native StoreKit separately. ──

        /// <summary>Apply the `skan` block from the cached remote config. Returns
        /// null on success or an error string (e.g. unknown preset). MUST free.</summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_skan_configure_from_remote_config();

        /// <summary>Load a built-in SKAN preset. Returns null/error. MUST free.</summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_skan_set_preset(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string preset);

        /// <summary>Replace the active SKAN rules with a JSON array. MUST free.</summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_skan_set_rules(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string rules_json);

        /// <summary>Evaluate an event. Returns a JSON decision
        /// {"fineValue":N,"coarseValue":"...","lockWindow":bool} or null. MUST free.</summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_skan_process_event(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string event_name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string properties_json);

        /// <summary>Report the outcome of applying a SKAN update. MUST free.</summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_skan_record_conversion_result(
            byte value, [MarshalAs(UnmanagedType.I1)] bool success);

        /// <summary>The highest SKAN conversion value posted so far, or -1.</summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int layers_skan_current_value();

        /// <summary>1 if SKAN is currently enabled, 0 otherwise.</summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte layers_skan_is_enabled();

        /// <summary>The active SKAN preset name, or null. MUST free.</summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_skan_current_preset();

        // ── Tier 1: Super-properties ───────────────────────────────────

        /// <summary>
        /// Register super-properties merged into every subsequent track/screen call.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_set_super_properties(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string properties_json);

        /// <summary>
        /// Register super-properties only for keys not previously registered.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_set_super_properties_once(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string properties_json);

        /// <summary>
        /// Remove a single super-property by key.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_unregister_super_property(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string key);

        /// <summary>
        /// Clear all super-properties and the once-keys history.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_clear_super_properties();

        /// <summary>
        /// Snapshot of current super-properties as a heap-allocated JSON C string.
        /// Caller MUST free via layers_free_string.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_get_super_properties_json();

        // ── Tier 1: Timed events ───────────────────────────────────────

        /// <summary>
        /// Start a duration timer for the next track(name, ...) call.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_time_event(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string event_name);

        /// <summary>
        /// Cancel a timed event without emitting it. Returns elapsed milliseconds, or 0.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong layers_cancel_timed_event(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string event_name);

        // ── Tier 1: Multi-group ────────────────────────────────────────

        /// <summary>
        /// Set membership for a single group_type. Pass empty group_id to remove.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_set_group(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string group_type,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string group_id);

        /// <summary>
        /// Add a group membership without overwriting other types.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_add_group(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string group_type,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string group_id);

        /// <summary>
        /// Remove a single group_type from the membership map.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_remove_group(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string group_type);

        /// <summary>
        /// Snapshot of $groups as a heap-allocated JSON C string.
        /// Caller MUST free via layers_free_string.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_get_groups_json();

        // ── Tier 1: User-property mutators ─────────────────────────────

        /// <summary>
        /// Increment a numeric user property by delta (negative decrements).
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_increment(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
            double delta);

        /// <summary>
        /// Append a value (JSON-encoded) to a list user property.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_append(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value_json);

        /// <summary>
        /// Union an array (JSON-encoded) into a list user property.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_union(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string values_json);

        /// <summary>
        /// Remove a user property. Maps to $unset.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_unset(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string key);

        // ── Tier 1: Identity reset + ID accessors ──────────────────────

        /// <summary>
        /// Clear identity, rotate device_id/anonymous_id, clear super-properties + groups.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_reset();

        /// <summary>
        /// Get the stable per-install device ID. Caller MUST free via layers_free_string.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_get_device_id();

        /// <summary>
        /// Get the anonymous ID. Caller MUST free via layers_free_string.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_get_anonymous_id();

        /// <summary>
        /// Get the monotonic session counter. Returns 0 if not initialized.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint layers_get_session_number();

        /// <summary>
        /// Get the SDK first-open timestamp as ISO-8601, or null if not set.
        /// Caller MUST free via layers_free_string.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_get_first_open_time();

        // ── Tier 1: before_send filter hook ────────────────────────────

        /// <summary>
        /// C ABI signature for the `before_send` filter callback.
        ///
        /// Receives the event JSON as a C string. Return:
        /// - IntPtr.Zero to drop the event
        /// - A heap-allocated C string (e.g. <c>Marshal.StringToCoTaskMemUTF8</c>)
        ///   with the (possibly mutated) event JSON to keep the event. The
        ///   Rust core copies the contents and then invokes the registered
        ///   <see cref="BeforeSendFreeCallbackDelegate"/> to release the buffer.
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate IntPtr BeforeSendCallbackDelegate(IntPtr eventJsonPtr);

        /// <summary>
        /// C ABI signature for freeing buffers returned by
        /// <see cref="BeforeSendCallbackDelegate"/>.
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void BeforeSendFreeCallbackDelegate(IntPtr ptr);

        /// <summary>
        /// Register a before_send filter callback. Pass null/null to clear.
        /// Returns null on success, error string on failure.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_set_before_send(
            BeforeSendCallbackDelegate callback,
            BeforeSendFreeCallbackDelegate free_callback);

        /// <summary>
        /// Clear a previously-registered before_send callback.
        /// Returns null on success, error string on failure.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_clear_before_send();

        // ── Tier 4: Feature flags ──────────────────────────────────────

        /// <summary>
        /// Evaluate a feature flag. Returns a heap-allocated JSON-encoded
        /// C string ("true", "false", "\"variant_key\"", "null").
        /// Caller MUST free via <see cref="layers_free_string"/>.
        /// Side-effect: emits one <c>$feature_flag_called</c> event per
        /// (flag_key, response) pair per session.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_get_feature_flag(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string flag_key);

        /// <summary>
        /// Truthy-check shortcut. Returns 1 (true), 0 (false), or -1
        /// (error / not initialized).
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int layers_is_feature_enabled(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string flag_key);

        /// <summary>
        /// Look up the JSON payload attached to a flag. Returns "null" if
        /// the flag has no payload. Does NOT emit exposure. Caller MUST free.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_get_feature_flag_payload(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string flag_key);

        /// <summary>
        /// Snapshot every known flag's current value as a JSON object string.
        /// Does NOT emit exposure events. Caller MUST free.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_get_all_flags_json();

        /// <summary>
        /// Drop cached flag definitions. Returns 1 if any were dropped, 0
        /// if none, -1 on error.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int layers_reload_feature_flags();

        /// <summary>
        /// Override person properties for flag evaluation. JSON object.
        /// Clears the exposure dedup cache so flipped responses re-emit.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_set_person_properties_for_flags(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string properties_json);

        /// <summary>
        /// Seed bootstrap flag values + payloads (SSR pattern). JSON object
        /// matching <c>BootstrapData</c>: <c>{ "feature_flags": {...},
        /// "feature_flag_payloads": {...} }</c>.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr layers_set_feature_flag_bootstrap(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string bootstrap_json);

        // ── Memory Management ──────────────────────────────────────────

        /// <summary>
        /// Free a string that was returned by one of the layers_* functions.
        /// Safe to call with IntPtr.Zero (no-op).
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void layers_free_string(IntPtr ptr);
    }
}

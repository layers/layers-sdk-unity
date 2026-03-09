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

        // ── Memory Management ──────────────────────────────────────────

        /// <summary>
        /// Free a string that was returned by one of the layers_* functions.
        /// Safe to call with IntPtr.Zero (no-op).
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void layers_free_string(IntPtr ptr);
    }
}

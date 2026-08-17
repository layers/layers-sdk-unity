using System;
using System.Runtime.InteropServices;

namespace Layers.Unity.Internal
{
    /// <summary>
    /// Native platform implementation of <see cref="ILayersPlatform"/>.
    /// Delegates to the Rust core via C ABI P/Invoke (<see cref="NativeBindings"/>).
    ///
    /// Used on iOS, Android, macOS, Windows, and Linux — any platform where the
    /// Rust core is compiled as a native library (.dylib/.so/.dll/.a).
    /// </summary>
    internal class NativePlatform : ILayersPlatform
    {
        // Persistent delegate references — required to keep the JIT-compiled trampolines
        // alive across the FFI boundary. Without these, Mono's GC may collect the
        // delegate while Rust still holds a function pointer to it.
        private static NativeBindings.BeforeSendCallbackDelegate _beforeSendCb;
        private static NativeBindings.BeforeSendFreeCallbackDelegate _beforeSendFreeCb;
        private static Func<string, string> _userBeforeSend;
        private static readonly object _beforeSendLock = new object();

        public string Init(string configJson)
        {
            return NativeStringHelper.ProcessResult(NativeBindings.layers_init(configJson));
        }

        public string Shutdown()
        {
            return NativeStringHelper.ProcessResult(NativeBindings.layers_shutdown());
        }

        public string Track(string eventName, string propertiesJson)
        {
            return NativeStringHelper.ProcessResult(
                NativeBindings.layers_track(eventName, propertiesJson));
        }

        public string Screen(string screenName, string propertiesJson)
        {
            return NativeStringHelper.ProcessResult(
                NativeBindings.layers_screen(screenName, propertiesJson));
        }

        public string Identify(string userId)
        {
            return NativeStringHelper.ProcessResult(NativeBindings.layers_identify(userId));
        }

        public string SetUserProperties(string propertiesJson)
        {
            return NativeStringHelper.ProcessResult(
                NativeBindings.layers_set_user_properties(propertiesJson));
        }

        public string SetUserPropertiesOnce(string propertiesJson)
        {
            return NativeStringHelper.ProcessResult(
                NativeBindings.layers_set_user_properties_once(propertiesJson));
        }

        public string Group(string groupId, string propertiesJson)
        {
            return NativeStringHelper.ProcessResult(
                NativeBindings.layers_group(groupId, propertiesJson));
        }

        public string SetConsent(string consentJson)
        {
            return NativeStringHelper.ProcessResult(
                NativeBindings.layers_set_consent(consentJson));
        }

        public string SetDeviceContext(string contextJson)
        {
            return NativeStringHelper.ProcessResult(
                NativeBindings.layers_set_device_context(contextJson));
        }

        public string Flush()
        {
            return NativeStringHelper.ProcessResult(NativeBindings.layers_flush());
        }

        public string DrainBatch(uint count)
        {
            return NativeStringHelper.ReadAndFree(NativeBindings.layers_drain_batch(count));
        }

        public string RequeueEvents(string eventsJson)
        {
            return NativeStringHelper.ProcessResult(
                NativeBindings.layers_requeue_events(eventsJson));
        }

        public string FlushHeadersJson()
        {
            return NativeStringHelper.ReadAndFree(NativeBindings.layers_flush_headers_json());
        }

        public string ConfigHeadersJson()
        {
            return NativeStringHelper.ReadAndFree(NativeBindings.layers_config_headers_json());
        }

        public string EventsUrl()
        {
            return NativeStringHelper.ReadAndFree(NativeBindings.layers_events_url());
        }

        public int QueueDepth()
        {
            return NativeBindings.layers_queue_depth();
        }

        public string GetSessionId()
        {
            return NativeStringHelper.ReadAndFree(NativeBindings.layers_get_session_id());
        }

        public string GetDebugToken()
        {
            // ReadAndFree returns null when the FFI returns IntPtr.Zero, which
            // is exactly what we want for "debug mode off".
            return NativeStringHelper.ReadAndFree(NativeBindings.layers_get_debug_token());
        }

        public string GetRemoteConfigJson()
        {
            return NativeStringHelper.ReadAndFree(NativeBindings.layers_get_remote_config_json());
        }

        public string UpdateRemoteConfig(string configJson, string etag)
        {
            return NativeStringHelper.ProcessResult(
                NativeBindings.layers_update_remote_config(configJson, etag));
        }

        // ── Tier 1: Super-properties ───────────────────────────────────

        public string SetSuperProperties(string propertiesJson)
        {
            return NativeStringHelper.ProcessResult(
                NativeBindings.layers_set_super_properties(propertiesJson));
        }

        public string SetSuperPropertiesOnce(string propertiesJson)
        {
            return NativeStringHelper.ProcessResult(
                NativeBindings.layers_set_super_properties_once(propertiesJson));
        }

        public string UnregisterSuperProperty(string key)
        {
            return NativeStringHelper.ProcessResult(
                NativeBindings.layers_unregister_super_property(key));
        }

        public string ClearSuperProperties()
        {
            return NativeStringHelper.ProcessResult(NativeBindings.layers_clear_super_properties());
        }

        public string GetSuperPropertiesJson()
        {
            return NativeStringHelper.ReadAndFree(NativeBindings.layers_get_super_properties_json())
                ?? "{}";
        }

        // ── Tier 1: Timed events ───────────────────────────────────────

        public string TimeEvent(string eventName)
        {
            return NativeStringHelper.ProcessResult(NativeBindings.layers_time_event(eventName));
        }

        public ulong CancelTimedEvent(string eventName)
        {
            return NativeBindings.layers_cancel_timed_event(eventName);
        }

        // ── Tier 1: Multi-group ────────────────────────────────────────

        public string SetGroup(string groupType, string groupId)
        {
            return NativeStringHelper.ProcessResult(
                NativeBindings.layers_set_group(groupType, groupId ?? string.Empty));
        }

        public string AddGroup(string groupType, string groupId)
        {
            return NativeStringHelper.ProcessResult(
                NativeBindings.layers_add_group(groupType, groupId ?? string.Empty));
        }

        public string RemoveGroup(string groupType)
        {
            return NativeStringHelper.ProcessResult(NativeBindings.layers_remove_group(groupType));
        }

        public string GetGroupsJson()
        {
            return NativeStringHelper.ReadAndFree(NativeBindings.layers_get_groups_json()) ?? "{}";
        }

        // ── Tier 1: User-property mutators ─────────────────────────────

        public string Increment(string key, double delta)
        {
            return NativeStringHelper.ProcessResult(NativeBindings.layers_increment(key, delta));
        }

        public string Append(string key, string valueJson)
        {
            return NativeStringHelper.ProcessResult(NativeBindings.layers_append(key, valueJson));
        }

        public string Union(string key, string valuesJson)
        {
            return NativeStringHelper.ProcessResult(NativeBindings.layers_union(key, valuesJson));
        }

        public string Unset(string key)
        {
            return NativeStringHelper.ProcessResult(NativeBindings.layers_unset(key));
        }

        // ── Tier 1: Identity reset + ID accessors ──────────────────────

        public string Reset()
        {
            return NativeStringHelper.ProcessResult(NativeBindings.layers_reset());
        }

        public string GetDeviceId()
        {
            return NativeStringHelper.ReadAndFree(NativeBindings.layers_get_device_id());
        }

        public string GetAnonymousId()
        {
            return NativeStringHelper.ReadAndFree(NativeBindings.layers_get_anonymous_id());
        }

        public uint GetSessionNumber()
        {
            return NativeBindings.layers_get_session_number();
        }

        public string GetFirstOpenTime()
        {
            return NativeStringHelper.ReadAndFree(NativeBindings.layers_get_first_open_time());
        }

        // ── Tier 1: before_send filter hook ────────────────────────────

        /// <summary>
        /// Register a before_send filter callback.
        ///
        /// The user provides a managed Func&lt;string, string&gt; that takes
        /// event JSON and returns the (possibly mutated) JSON to keep, or
        /// null to drop the event. We wrap it in a static delegate that
        /// matches the C ABI signature.
        ///
        /// Both the trampoline delegate and the free-callback delegate must
        /// be kept alive for as long as the Rust core holds the function
        /// pointer — we store them in static fields above.
        /// </summary>
        public string SetBeforeSend(Func<string, string> callback)
        {
            if (callback == null)
                return ClearBeforeSend();

            lock (_beforeSendLock)
            {
                _userBeforeSend = callback;

                // (Re)build the trampolines. Reusing the same instance is
                // important on iOS/AOT — Mono allocates a new thunk per
                // assignment, so we keep a single delegate alive across calls.
                if (_beforeSendCb == null)
                    _beforeSendCb = BeforeSendTrampoline;
                if (_beforeSendFreeCb == null)
                    _beforeSendFreeCb = BeforeSendFreeTrampoline;

                return NativeStringHelper.ProcessResult(
                    NativeBindings.layers_set_before_send(_beforeSendCb, _beforeSendFreeCb));
            }
        }

        public string ClearBeforeSend()
        {
            lock (_beforeSendLock)
            {
                _userBeforeSend = null;
                return NativeStringHelper.ProcessResult(NativeBindings.layers_clear_before_send());
            }
        }

#if ENABLE_IL2CPP
        [AOT.MonoPInvokeCallback(typeof(NativeBindings.BeforeSendCallbackDelegate))]
#endif
        private static IntPtr BeforeSendTrampoline(IntPtr eventJsonPtr)
        {
            try
            {
                Func<string, string> cb;
                lock (_beforeSendLock) { cb = _userBeforeSend; }
                if (cb == null) return IntPtr.Zero;

                string json = Marshal.PtrToStringUTF8(eventJsonPtr);
                if (json == null) return IntPtr.Zero;

                string result;
                try
                {
                    result = cb(json);
                }
                catch (Exception ex)
                {
                    LayersLogger.Warn($"before_send callback threw, dropping event: {ex.Message}");
                    return IntPtr.Zero;
                }

                if (result == null) return IntPtr.Zero;

                // Allocate via CoTaskMem so Rust can free it via the matching
                // free trampoline below.
                return Marshal.StringToCoTaskMemUTF8(result);
            }
            catch (Exception ex)
            {
                LayersLogger.Warn($"before_send trampoline failed: {ex.Message}");
                return IntPtr.Zero;
            }
        }

#if ENABLE_IL2CPP
        [AOT.MonoPInvokeCallback(typeof(NativeBindings.BeforeSendFreeCallbackDelegate))]
#endif
        private static void BeforeSendFreeTrampoline(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return;
            try
            {
                Marshal.FreeCoTaskMem(ptr);
            }
            catch (Exception ex)
            {
                LayersLogger.Warn($"before_send free trampoline failed: {ex.Message}");
            }
        }

        // ── Tier 4: Feature flags ──────────────────────────────────────

        public string GetFeatureFlagJson(string flagKey)
        {
            return NativeStringHelper.ReadAndFree(
                NativeBindings.layers_get_feature_flag(flagKey));
        }

        public int IsFeatureEnabled(string flagKey)
        {
            return NativeBindings.layers_is_feature_enabled(flagKey);
        }

        public string GetFeatureFlagPayloadJson(string flagKey)
        {
            return NativeStringHelper.ReadAndFree(
                NativeBindings.layers_get_feature_flag_payload(flagKey));
        }

        public string GetAllFlagsJson()
        {
            return NativeStringHelper.ReadAndFree(
                NativeBindings.layers_get_all_flags_json());
        }

        public int ReloadFeatureFlags()
        {
            return NativeBindings.layers_reload_feature_flags();
        }

        public string SetPersonPropertiesForFlags(string propertiesJson)
        {
            return NativeStringHelper.ProcessResult(
                NativeBindings.layers_set_person_properties_for_flags(propertiesJson));
        }

        public string SetFeatureFlagBootstrap(string bootstrapJson)
        {
            return NativeStringHelper.ProcessResult(
                NativeBindings.layers_set_feature_flag_bootstrap(bootstrapJson));
        }
    }
}

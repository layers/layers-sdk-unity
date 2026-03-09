// AdServicesModule.cs
// Layers Unity SDK
//
// C# wrapper for AdServices attribution token on iOS (14.3+).
// Delegates to native Objective-C bridge via P/Invoke.
// On non-iOS platforms, all methods return safe defaults.
//
// The AdServices token does NOT require ATT consent. It is used
// for Apple Search Ads attribution and is cached after the first request.

using System;
using System.Runtime.InteropServices;

namespace Layers.Unity
{
    /// <summary>
    /// AdServices module for iOS. Provides access to the Apple AdServices
    /// attribution token (iOS 14.3+) for Apple Search Ads attribution.
    ///
    /// The token does not require ATT consent and is safe to request at any time.
    /// The result is cached after the first successful request.
    ///
    /// On non-iOS platforms (Android, Editor), all methods return safe no-op defaults.
    /// </summary>
    public static class AdServicesModule
    {
        private static string _cachedToken;
        private static bool _hasRequested;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern bool layers_adservices_is_available();

        [DllImport("__Internal")]
        private static extern IntPtr layers_adservices_get_token();
#endif

        /// <summary>
        /// Check if AdServices is available on this device (iOS 14.3+).
        /// </summary>
        /// <returns>True if AdServices APIs are available, false otherwise.</returns>
        public static bool IsAvailable()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return layers_adservices_is_available();
#else
            return false;
#endif
        }

        /// <summary>
        /// Request the AdServices attribution token. Safe to call on any platform;
        /// returns null on non-iOS or iOS &lt; 14.3.
        ///
        /// The token is cached after the first successful request. Subsequent calls
        /// return the cached value without hitting the OS API again.
        /// </summary>
        /// <returns>The attribution token string, or null if unavailable.</returns>
        public static string GetToken()
        {
            if (_hasRequested)
                return _cachedToken;

            _hasRequested = true;

#if UNITY_IOS && !UNITY_EDITOR
            IntPtr ptr = layers_adservices_get_token();
            if (ptr == IntPtr.Zero)
            {
                _cachedToken = null;
                return null;
            }

            string token = Marshal.PtrToStringAnsi(ptr);
            // Native side returns empty string when not available.
            if (string.IsNullOrEmpty(token))
            {
                _cachedToken = null;
                return null;
            }

            _cachedToken = token;
            return _cachedToken;
#else
            _cachedToken = null;
            return null;
#endif
        }

        /// <summary>
        /// The cached token, if previously requested. Does not trigger a new request.
        /// </summary>
        public static string CachedToken => _cachedToken;
    }
}

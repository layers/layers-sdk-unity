// RevenueCatIntegration.cs
// Layers Unity SDK
//
// Duck-typed integration with the RevenueCat Unity SDK.
// Tracks purchase and subscription events and syncs user properties.
// No hard dependency on the RevenueCat Unity SDK package.
//
// Usage:
//   // After a successful purchase:
//   RevenueCatIntegration.TrackPurchase(productId, price, currency, "app_store");
//
//   // When customer info updates (e.g., new subscription):
//   RevenueCatIntegration.OnCustomerInfoUpdated(activeProductIds, originalAppUserId);
//
//   // Sync subscriber status:
//   RevenueCatIntegration.SyncAttributes(isSubscriber, originalAppUserId);

using System;
using System.Collections.Generic;
using Layers.Unity.Internal;
using UnityEngine;

namespace Layers.Unity
{
    /// <summary>
    /// RevenueCat integration for the Layers Unity SDK.
    ///
    /// Provides static methods to forward purchase/subscription events to Layers
    /// without requiring a hard dependency on the RevenueCat Unity SDK.
    ///
    /// Tracks: <c>purchase_success</c>, <c>subscription_start</c> events.
    /// Sets: <c>is_subscriber</c>, <c>revenuecat_original_app_user_id</c> user properties.
    /// </summary>
    public static class RevenueCatIntegration
    {
        private static readonly HashSet<string> _activeSubscriptions = new HashSet<string>();
        private static bool _isInitialized;

        /// <summary>
        /// Track a purchase completed via RevenueCat.
        /// Fires a <c>purchase_success</c> event.
        /// </summary>
        /// <param name="productId">The product identifier (SKU or App Store product ID).</param>
        /// <param name="price">The product price.</param>
        /// <param name="currency">The currency code (e.g., "USD").</param>
        /// <param name="store">The store name (e.g., "app_store", "play_store").</param>
        public static void TrackPurchase(
            string productId,
            double price,
            string currency = "USD",
            string store = null)
        {
            try
            {
                if (string.IsNullOrEmpty(productId)) return;

                // Auto-detect store if not provided
                if (string.IsNullOrEmpty(store))
                {
#if UNITY_IOS && !UNITY_EDITOR
                    store = "app_store";
#elif UNITY_ANDROID && !UNITY_EDITOR
                    store = "play_store";
#else
                    store = "unknown";
#endif
                }

                LayersSDK.Track("purchase_success", new Dictionary<string, object>
                {
                    ["product_id"] = productId,
                    ["price"] = price,
                    ["currency"] = currency ?? "USD",
                    ["store"] = store,
                    ["source"] = "revenuecat"
                });
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"RevenueCatIntegration.TrackPurchase error: {e.Message}");
            }
        }

        /// <summary>
        /// Sync user attributes (subscriber status, original app user ID) to Layers.
        /// Calls <see cref="LayersSDK.SetUserProperties"/> with <c>is_subscriber</c> and
        /// optionally <c>revenuecat_original_app_user_id</c>.
        /// </summary>
        /// <param name="isSubscriber">Whether the user has any active subscriptions.</param>
        /// <param name="originalAppUserId">The RevenueCat original app user ID, or null.</param>
        public static void SyncAttributes(bool isSubscriber, string originalAppUserId = null)
        {
            try
            {
                var props = new Dictionary<string, object>
                {
                    ["is_subscriber"] = isSubscriber
                };

                if (!string.IsNullOrEmpty(originalAppUserId))
                    props["revenuecat_original_app_user_id"] = originalAppUserId;

                LayersSDK.SetUserProperties(props);
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"RevenueCatIntegration.SyncAttributes error: {e.Message}");
            }
        }

        /// <summary>
        /// Handle a customer info update from RevenueCat.
        ///
        /// Compares the provided active subscription product IDs against the
        /// previously known set. For each new subscription, fires a
        /// <c>subscription_start</c> event. Also syncs the <c>is_subscriber</c>
        /// user property.
        ///
        /// Call this from your RevenueCat <c>CustomerInfoUpdateListener</c>.
        /// </summary>
        /// <param name="activeProductIds">
        /// List of currently active subscription product IDs.
        /// Pass an empty list or null if the user has no active subscriptions.
        /// </param>
        /// <param name="originalAppUserId">
        /// The RevenueCat original app user ID, or null.
        /// </param>
        public static void OnCustomerInfoUpdated(
            IEnumerable<string> activeProductIds,
            string originalAppUserId = null)
        {
            try
            {
                var current = new HashSet<string>();
                if (activeProductIds != null)
                {
                    foreach (string id in activeProductIds)
                    {
                        if (!string.IsNullOrEmpty(id))
                            current.Add(id);
                    }
                }

                // On subsequent updates, track new subscriptions
                if (_isInitialized)
                {
                    foreach (string productId in current)
                    {
                        if (!_activeSubscriptions.Contains(productId))
                        {
                            LayersSDK.Track("subscription_start", new Dictionary<string, object>
                            {
                                ["product_id"] = productId,
                                ["source"] = "revenuecat"
                            });
                        }
                    }
                }

                // Update tracked subscriptions
                _activeSubscriptions.Clear();
                foreach (string id in current)
                    _activeSubscriptions.Add(id);

                _isInitialized = true;

                // Sync user properties
                SyncAttributes(current.Count > 0, originalAppUserId);
            }
            catch (Exception e)
            {
                LayersLogger.Warn($"RevenueCatIntegration.OnCustomerInfoUpdated error: {e.Message}");
            }
        }

        /// <summary>
        /// Reset all integration state. Useful for testing or logout flows.
        /// </summary>
        public static void Reset()
        {
            _activeSubscriptions.Clear();
            _isInitialized = false;
        }
    }
}

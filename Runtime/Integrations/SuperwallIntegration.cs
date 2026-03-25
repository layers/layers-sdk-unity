// SuperwallIntegration.cs
// Layers Unity SDK
//
// Duck-typed integration with the Superwall Unity SDK.
// Tracks paywall presentation, dismiss, purchase, and skip events.
// No hard dependency on the Superwall Unity SDK package.
//
// Usage:
//   // In your Superwall delegate/handler:
//   SuperwallIntegration.TrackPresentation(paywallId, placementName);
//   SuperwallIntegration.TrackDismiss(paywallId);
//   SuperwallIntegration.TrackPurchase(paywallId, productId, price, currency);
//   SuperwallIntegration.TrackSkip(paywallId, "no_rule_match");

using System.Collections.Generic;
using Layers.Unity.Internal;
using UnityEngine;

namespace Layers.Unity
{
    /// <summary>
    /// Superwall integration for the Layers Unity SDK.
    ///
    /// Provides static methods to forward paywall lifecycle events to Layers
    /// without requiring a hard dependency on the Superwall Unity SDK.
    ///
    /// Each method calls <see cref="LayersSDK.Track"/> with appropriate event names:
    /// <c>paywall_show</c>, <c>paywall_dismiss</c>, <c>paywall_purchase</c>, <c>paywall_skip</c>.
    /// </summary>
    public static class SuperwallIntegration
    {
        /// <summary>
        /// Track a generic Superwall event. Forwards to <see cref="LayersSDK.Track"/>
        /// with the given event name and optional properties.
        /// </summary>
        /// <param name="eventName">The Superwall event name (e.g., "paywall_open").</param>
        /// <param name="properties">Optional event properties.</param>
        public static void OnEvent(string eventName, Dictionary<string, object> properties = null)
        {
            if (string.IsNullOrEmpty(eventName)) return;

            try
            {
                LayersSDK.Track(eventName, properties);
            }
            catch (System.Exception e)
            {
                LayersLogger.Warn($"SuperwallIntegration.OnEvent error: {e.Message}");
            }
        }

        /// <summary>
        /// Track that a paywall was presented to the user.
        /// Fires a <c>paywall_show</c> event.
        /// </summary>
        /// <param name="paywallId">The paywall identifier (e.g., from PaywallInfo.identifier).</param>
        /// <param name="placementName">The placement name (e.g., from PaywallInfo.name).</param>
        /// <param name="experimentId">Optional A/B test experiment ID.</param>
        /// <param name="variantId">Optional A/B test variant ID.</param>
        public static void TrackPresentation(
            string paywallId,
            string placementName = null,
            string experimentId = null,
            string variantId = null)
        {
            try
            {
                var props = new Dictionary<string, object>
                {
                    ["paywall_id"] = paywallId ?? "unknown",
                    ["placement"] = placementName ?? "unknown"
                };

                if (!string.IsNullOrEmpty(experimentId) || !string.IsNullOrEmpty(variantId))
                {
                    var abTest = new Dictionary<string, object>();
                    if (!string.IsNullOrEmpty(experimentId))
                        abTest["id"] = experimentId;
                    if (!string.IsNullOrEmpty(variantId))
                        abTest["variant"] = variantId;
                    props["ab_test"] = abTest;
                }

                LayersSDK.Track("paywall_show", props);
            }
            catch (System.Exception e)
            {
                LayersLogger.Warn($"SuperwallIntegration.TrackPresentation error: {e.Message}");
            }
        }

        /// <summary>
        /// Track that a paywall was dismissed.
        /// Fires a <c>paywall_dismiss</c> event.
        /// </summary>
        /// <param name="paywallId">The paywall identifier.</param>
        public static void TrackDismiss(string paywallId)
        {
            try
            {
                LayersSDK.Track("paywall_dismiss", new Dictionary<string, object>
                {
                    ["paywall_id"] = paywallId ?? "unknown"
                });
            }
            catch (System.Exception e)
            {
                LayersLogger.Warn($"SuperwallIntegration.TrackDismiss error: {e.Message}");
            }
        }

        /// <summary>
        /// Track a purchase initiated from a Superwall paywall.
        /// Fires a <c>paywall_purchase</c> event.
        /// </summary>
        /// <param name="paywallId">The paywall identifier.</param>
        /// <param name="productId">The product identifier (e.g., SKU or App Store product ID).</param>
        /// <param name="price">The product price. Pass 0 if unknown.</param>
        /// <param name="currency">The currency code (e.g., "USD"). Pass null if unknown.</param>
        public static void TrackPurchase(
            string paywallId,
            string productId = null,
            double price = 0,
            string currency = null)
        {
            try
            {
                var props = new Dictionary<string, object>
                {
                    ["paywall_id"] = paywallId ?? "unknown",
                    ["source"] = "superwall"
                };

                if (!string.IsNullOrEmpty(productId))
                    props["product_id"] = productId;
                if (price > 0)
                    props["price"] = price;
                if (!string.IsNullOrEmpty(currency))
                    props["currency"] = currency;

                LayersSDK.Track("paywall_purchase", props);
            }
            catch (System.Exception e)
            {
                LayersLogger.Warn($"SuperwallIntegration.TrackPurchase error: {e.Message}");
            }
        }

        /// <summary>
        /// Track that a paywall was skipped (not shown).
        /// Fires a <c>paywall_skip</c> event.
        /// </summary>
        /// <param name="paywallId">The paywall identifier, or null if not available.</param>
        /// <param name="reason">The reason the paywall was skipped (e.g., "no_rule_match", "holdout").</param>
        public static void TrackSkip(string paywallId, string reason)
        {
            try
            {
                LayersSDK.Track("paywall_skip", new Dictionary<string, object>
                {
                    ["paywall_id"] = paywallId ?? "unknown",
                    ["reason"] = reason ?? "unknown"
                });
            }
            catch (System.Exception e)
            {
                LayersLogger.Warn($"SuperwallIntegration.TrackSkip error: {e.Message}");
            }
        }

        /// <summary>
        /// Get user attributes for Superwall's user attribute API.
        /// Returns a dictionary with <c>layers_session_id</c> and <c>layers_user_id</c>.
        ///
        /// Usage:
        /// <code>
        /// Superwall.Instance.SetUserAttributes(SuperwallIntegration.UserAttributes());
        /// </code>
        /// </summary>
        public static Dictionary<string, object> UserAttributes()
        {
            var attrs = new Dictionary<string, object>();

            try
            {
                string sessionId = LayersSDK.SessionId;
                if (!string.IsNullOrEmpty(sessionId))
                    attrs["layers_session_id"] = sessionId;

                string userId = LayersSDK.UserId;
                if (!string.IsNullOrEmpty(userId))
                    attrs["layers_user_id"] = userId;
            }
            catch (System.Exception e)
            {
                LayersLogger.Warn($"SuperwallIntegration.UserAttributes error: {e.Message}");
            }

            return attrs;
        }
    }
}

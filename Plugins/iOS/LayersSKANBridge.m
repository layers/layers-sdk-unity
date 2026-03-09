//
//  LayersSKANBridge.m
//  Layers Unity SDK
//
//  Objective-C bridge for SKAdNetwork (SKAN).
//  Exports C functions callable from Unity via P/Invoke.
//
//  SKAN 2.0 requires iOS 14.0+. SKAN 4.0 features require iOS 16.1+.
//

#import <Foundation/Foundation.h>

#if __has_include(<StoreKit/SKAdNetwork.h>)
#import <StoreKit/SKAdNetwork.h>
#define LAYERS_HAS_SKAN 1
#else
#define LAYERS_HAS_SKAN 0
#endif

/// Check if SKAdNetwork is supported on this device (iOS 14.0+).
bool layers_skan_is_supported(void) {
#if LAYERS_HAS_SKAN
    if (@available(iOS 14, *)) {
        return true;
    }
#endif
    return false;
}

/// Get the highest SKAN version supported by this OS.
/// Returns a strdup'd C string. Unity marshals and frees it.
/// Possible values: "4.0", "3.0", "2.2", "2.1", "2.0", "unsupported"
const char* layers_skan_get_version(void) {
#if LAYERS_HAS_SKAN
    if (@available(iOS 16.1, *)) { return strdup("4.0"); }
    if (@available(iOS 15.4, *)) { return strdup("3.0"); }
    if (@available(iOS 14.6, *)) { return strdup("2.2"); }
    if (@available(iOS 14.5, *)) { return strdup("2.1"); }
    if (@available(iOS 14.0, *)) { return strdup("2.0"); }
#endif
    return strdup("unsupported");
}

/// Register app for ad network attribution.
/// Uses the best available API for the OS version:
///   - iOS 15.4+: updatePostbackConversionValue(0) (async)
///   - iOS 14.0+: registerAppForAdNetworkAttribution() (legacy)
void layers_skan_register(void) {
#if LAYERS_HAS_SKAN
    if (@available(iOS 15.4, *)) {
        [SKAdNetwork updatePostbackConversionValue:0
                                 completionHandler:^(NSError * _Nullable error) {
            if (error) {
                NSLog(@"[Layers] SKAdNetwork.updatePostbackConversionValue(0) failed: %@", error.localizedDescription);
            }
        }];
        return;
    }
    if (@available(iOS 14, *)) {
        [SKAdNetwork registerAppForAdNetworkAttribution];
        return;
    }
#endif
}

/// Update the fine conversion value (SKAN 3.0+ with fallback).
/// - iOS 15.4+: updatePostbackConversionValue:completionHandler:
/// - iOS 14.0+: updateConversionValue: (deprecated but functional)
/// fineValue must be 0-63.
void layers_skan_update_conversion_value(int fineValue) {
#if LAYERS_HAS_SKAN
    if (@available(iOS 15.4, *)) {
        [SKAdNetwork updatePostbackConversionValue:fineValue
                                 completionHandler:^(NSError * _Nullable error) {
            if (error) {
                NSLog(@"[Layers] SKAdNetwork.updatePostbackConversionValue(%d) failed: %@", fineValue, error.localizedDescription);
            }
        }];
        return;
    }
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
    if (@available(iOS 14, *)) {
        [SKAdNetwork updateConversionValue:fineValue];
        return;
    }
#pragma clang diagnostic pop
#endif
}

/// Update postback conversion value with coarse value and lock window (SKAN 4.0).
/// fineValue: 0-63
/// coarseValue: C string — "low", "medium", or "high"
/// lockWindow: whether to lock the current postback window
///
/// Falls back to fine-only update on iOS 15.4-16.0, or legacy update on iOS 14.0-15.3.
void layers_skan_update_postback(int fineValue, const char* coarseValue, bool lockWindow) {
#if LAYERS_HAS_SKAN
    if (@available(iOS 16.1, *)) {
        // Map string to SKAdNetwork.CoarseConversionValue
        SKAdNetworkCoarseConversionValue coarse = SKAdNetworkCoarseConversionValueLow;
        if (coarseValue != NULL) {
            NSString *coarseStr = [NSString stringWithUTF8String:coarseValue];
            NSString *lower = coarseStr.lowercaseString;
            if ([lower isEqualToString:@"high"]) {
                coarse = SKAdNetworkCoarseConversionValueHigh;
            } else if ([lower isEqualToString:@"medium"]) {
                coarse = SKAdNetworkCoarseConversionValueMedium;
            }
            // Default is low
        }

        [SKAdNetwork updatePostbackConversionValue:fineValue
                                      coarseValue:coarse
                                       lockWindow:lockWindow
                                completionHandler:^(NSError * _Nullable error) {
            if (error) {
                NSLog(@"[Layers] SKAdNetwork.updatePostbackConversionValue(%d, %@, %d) failed: %@",
                      fineValue, coarseValue ? [NSString stringWithUTF8String:coarseValue] : @"low", lockWindow, error.localizedDescription);
            }
        }];
        return;
    }
    // Fall back to fine-value-only update on older iOS versions.
    layers_skan_update_conversion_value(fineValue);
#endif
}

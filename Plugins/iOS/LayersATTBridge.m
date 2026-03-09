//
//  LayersATTBridge.m
//  Layers Unity SDK
//
//  Objective-C bridge for App Tracking Transparency (ATT).
//  Exports C functions callable from Unity via P/Invoke.
//
//  ATT requires iOS 14.0+. All functions degrade gracefully on older versions.
//

#import <Foundation/Foundation.h>

#if __has_include(<AppTrackingTransparency/AppTrackingTransparency.h>)
#import <AppTrackingTransparency/AppTrackingTransparency.h>
#define LAYERS_HAS_ATT 1
#else
#define LAYERS_HAS_ATT 0
#endif

#if __has_include(<AdSupport/ASIdentifierManager.h>)
#import <AdSupport/ASIdentifierManager.h>
#define LAYERS_HAS_ADSUPPORT 1
#else
#define LAYERS_HAS_ADSUPPORT 0
#endif

#import <UIKit/UIKit.h>

// Callback type for async ATT result.
// Status values: 0=notDetermined, 1=restricted, 2=denied, 3=authorized
typedef void (*LayersATTCallback)(int status);

/// Check if ATT is available (iOS 14.0+).
bool layers_att_is_available(void) {
#if LAYERS_HAS_ATT
    if (@available(iOS 14, *)) {
        return true;
    }
#endif
    return false;
}

/// Get the current ATT authorization status.
/// Returns: 0=notDetermined, 1=restricted, 2=denied, 3=authorized
int layers_att_get_status(void) {
#if LAYERS_HAS_ATT
    if (@available(iOS 14, *)) {
        return (int)ATTrackingManager.trackingAuthorizationStatus;
    }
#endif
    return 0; // notDetermined
}

/// Request tracking authorization from the user.
/// Shows the system ATT dialog if status is notDetermined.
/// Calls the callback on the main thread with the resulting status.
void layers_att_request_tracking(LayersATTCallback callback) {
#if LAYERS_HAS_ATT
    if (@available(iOS 14, *)) {
        [ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:^(ATTrackingManagerAuthorizationStatus status) {
            if (callback) {
                // Dispatch to main thread for Unity safety.
                // Unity's UnitySendMessage and most Unity APIs must be called from the main thread.
                dispatch_async(dispatch_get_main_queue(), ^{
                    callback((int)status);
                });
            }
        }];
        return;
    }
#endif
    // ATT not available — return notDetermined immediately.
    if (callback) {
        dispatch_async(dispatch_get_main_queue(), ^{
            callback(0);
        });
    }
}

/// Get the IDFA (advertising identifier).
/// Returns a strdup'd C string. Unity marshals and frees it.
/// Returns empty string if ATT is not authorized or IDFA is zeroed out.
const char* layers_att_get_idfa(void) {
#if LAYERS_HAS_ATT && LAYERS_HAS_ADSUPPORT
    if (@available(iOS 14, *)) {
        ATTrackingManagerAuthorizationStatus status = ATTrackingManager.trackingAuthorizationStatus;
        if (status != ATTrackingManagerAuthorizationStatusAuthorized) {
            return strdup("");
        }
        NSString *idfa = ASIdentifierManager.sharedManager.advertisingIdentifier.UUIDString;
        // Check for zeroed-out IDFA (tracking limited or simulator).
        if ([idfa isEqualToString:@"00000000-0000-0000-0000-000000000000"]) {
            return strdup("");
        }
        return strdup(idfa.UTF8String);
    }
#endif
    return strdup("");
}

/// Get the IDFV (vendor identifier).
/// Always available on iOS, does not require ATT authorization.
/// Returns a strdup'd C string. Unity marshals and frees it.
const char* layers_att_get_idfv(void) {
    NSUUID *idfv = UIDevice.currentDevice.identifierForVendor;
    if (idfv) {
        return strdup(idfv.UUIDString.UTF8String);
    }
    return strdup("");
}

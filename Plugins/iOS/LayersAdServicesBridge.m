//
//  LayersAdServicesBridge.m
//  Layers Unity SDK
//
//  Objective-C bridge for AdServices attribution token (iOS 14.3+).
//  Exports C functions callable from Unity via P/Invoke.
//
//  The AdServices attribution token does NOT require ATT consent.
//  It is used for Apple Search Ads attribution.
//

#import <Foundation/Foundation.h>

#if __has_include(<AdServices/AAAttribution.h>)
#import <AdServices/AAAttribution.h>
#define LAYERS_HAS_ADSERVICES 1
#else
#define LAYERS_HAS_ADSERVICES 0
#endif

/// Check if AdServices is available (iOS 14.3+).
bool layers_adservices_is_available(void) {
#if LAYERS_HAS_ADSERVICES
    if (@available(iOS 14.3, *)) {
        return true;
    }
#endif
    return false;
}

/// Free a C string previously returned by layers_adservices_get_token.
/// P/Invoke marshaling COPIES the string into managed memory — it never
/// frees the native allocation, so the C# side must call this after
/// reading (the old "Unity marshals and frees it" comment was wrong and
/// every call leaked).
void layers_adservices_free_string(char* ptr) {
    if (ptr) {
        free(ptr);
    }
}

/// Get the AdServices attribution token.
/// Returns a strdup'd C string — caller must release it via
/// layers_adservices_free_string after reading.
/// Returns empty string if AdServices is not available or the token cannot be obtained.
const char* layers_adservices_get_token(void) {
#if LAYERS_HAS_ADSERVICES
    if (@available(iOS 14.3, *)) {
        NSError *error = nil;
        NSString *token = [AAAttribution attributionTokenWithError:&error];
        if (token && !error) {
            return strdup(token.UTF8String);
        }
        if (error) {
            NSLog(@"[Layers] AAAttribution.attributionToken() failed: %@", error.localizedDescription);
        }
    }
#endif
    return strdup("");
}

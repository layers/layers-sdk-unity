//
//  LayersDeviceInfoBridge.m
//  Layers Unity SDK
//
//  Objective-C bridge for collecting device-info fields directly from UIKit /
//  Foundation, bypassing Unity's SystemInfo / Application APIs which can return
//  the literal string "unknown" or stale defaults when read during early init.
//
//  Each function returns a strdup'd C string OR NULL. Unity's P/Invoke
//  marshaller copies the bytes into a managed string and the caller calls
//  layers_devinfo_free() to release the buffer. NULL is mapped to a C#
//  null reference — the collector then emits JSON null over the wire (the
//  caller MUST NOT substitute a sentinel like "unknown").
//

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <sys/utsname.h>

static const char *layers_devinfo_strdup_or_null(NSString *value) {
    if (value == nil) {
        return NULL;
    }
    const char *utf8 = [value UTF8String];
    if (utf8 == NULL || utf8[0] == '\0') {
        return NULL;
    }
    return strdup(utf8);
}

/// Return UIDevice.currentDevice.systemVersion as a strdup'd C string,
/// or NULL if it is not yet available. e.g. "18.2.1".
const char *layers_ios_get_os_version(void) {
    @try {
        NSString *version = [UIDevice currentDevice].systemVersion;
        return layers_devinfo_strdup_or_null(version);
    }
    @catch (NSException *exception) {
        return NULL;
    }
}

/// Return the hardware model identifier (e.g. "iPhone14,5") via uname(),
/// or NULL if it cannot be read. This is the raw model identifier, not
/// the marketing name — matching what the native iOS SDK already sends.
const char *layers_ios_get_device_model(void) {
    @try {
        struct utsname systemInfo;
        if (uname(&systemInfo) != 0) {
            return NULL;
        }
        NSString *model = [NSString stringWithCString:systemInfo.machine
                                             encoding:NSUTF8StringEncoding];
        return layers_devinfo_strdup_or_null(model);
    }
    @catch (NSException *exception) {
        return NULL;
    }
}

/// Return CFBundleShortVersionString (the user-facing app version, e.g.
/// "1.0.5"), or NULL if the Info.plist key is missing.
const char *layers_ios_get_app_version(void) {
    @try {
        NSString *version = [[NSBundle mainBundle] objectForInfoDictionaryKey:@"CFBundleShortVersionString"];
        return layers_devinfo_strdup_or_null(version);
    }
    @catch (NSException *exception) {
        return NULL;
    }
}

/// Return CFBundleVersion (the internal build number, e.g. "147"), or NULL
/// if the Info.plist key is missing. This is distinct from
/// CFBundleShortVersionString — sending the same value for both fields is a
/// bug that loses the build-code dimension downstream.
const char *layers_ios_get_build_number(void) {
    @try {
        NSString *build = [[NSBundle mainBundle] objectForInfoDictionaryKey:@"CFBundleVersion"];
        return layers_devinfo_strdup_or_null(build);
    }
    @catch (NSException *exception) {
        return NULL;
    }
}

/// Free a string previously returned by one of the layers_ios_get_*
/// functions. Safe to call with NULL (no-op).
void layers_devinfo_free(const char *ptr) {
    if (ptr != NULL) {
        free((void *)ptr);
    }
}

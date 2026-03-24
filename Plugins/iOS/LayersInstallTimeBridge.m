//
//  LayersInstallTimeBridge.m
//  Layers Unity SDK
//
//  Objective-C bridge for retrieving the app's first install time on iOS.
//  Reads the creation date of the app's Documents directory, which is set
//  when the app is first installed.
//
//  Exports a C function callable from Unity via P/Invoke.
//

#import <Foundation/Foundation.h>

/// Get the app's first install time in milliseconds since epoch.
///
/// Reads the creation date of the app's Documents directory. This date
/// is set when the app is first installed and does not change across
/// app updates.
///
/// Returns 0 if the creation date cannot be determined.
long long layers_get_first_install_time_ms(void) {
    @try {
        NSFileManager *fileManager = [NSFileManager defaultManager];
        // Use the Documents directory -- its creation date corresponds to
        // the initial app installation.
        NSURL *documentsURL = [fileManager URLsForDirectory:NSDocumentDirectory
                                                  inDomains:NSUserDomainMask].firstObject;
        if (!documentsURL) {
            return 0;
        }

        NSDictionary *attributes = [fileManager attributesOfItemAtPath:documentsURL.path
                                                                error:nil];
        if (!attributes) {
            return 0;
        }

        NSDate *creationDate = attributes[NSFileCreationDate];
        if (!creationDate) {
            return 0;
        }

        // Convert to milliseconds since epoch
        return (long long)([creationDate timeIntervalSince1970] * 1000.0);
    }
    @catch (NSException *exception) {
        NSLog(@"[Layers] Failed to get first install time: %@", exception.reason);
        return 0;
    }
}

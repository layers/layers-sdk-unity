//
//  LayersBackgroundFlush.mm
//  Layers Unity SDK
//
//  Objective-C++ bridge for iOS background flush via BGAppRefreshTask.
//  Registers a BGAppRefreshTask with identifier "com.layers.sdk.background-flush"
//  and schedules it with a 15-minute minimum interval.
//
//  On execution, calls back to Unity via UnitySendMessage to trigger a flush.
//
//  IMPORTANT: The task MUST be registered during application launch.
//  In UnityAppController+Layers.mm or your custom AppDelegate,
//  call layers_background_flush_register() in
//  application:didFinishLaunchingWithOptions:.
//
//  Setup: Add "com.layers.sdk.background-flush" to Info.plist under
//  BGTaskSchedulerPermittedIdentifiers.
//

#import <Foundation/Foundation.h>
#include <stdatomic.h>

#if __has_include(<BackgroundTasks/BackgroundTasks.h>)
#import <BackgroundTasks/BackgroundTasks.h>
#define LAYERS_HAS_BGTASKS 1
#else
#define LAYERS_HAS_BGTASKS 0
#endif

static NSString *const kTaskIdentifier = @"com.layers.sdk.background-flush";
static const NSTimeInterval kMinimumInterval = 15 * 60; // 15 minutes
static BOOL sIsRegistered = NO;
static atomic_bool sFlushCompleted = false;

// Forward declarations
static void layers_bg_schedule_next_flush(void);
static void layers_bg_handle_task(id task);

#pragma mark - Internal helpers

static void layers_bg_schedule_next_flush(void) {
#if LAYERS_HAS_BGTASKS
    if (@available(iOS 13.0, *)) {
        BGAppRefreshTaskRequest *request = [[BGAppRefreshTaskRequest alloc]
            initWithIdentifier:kTaskIdentifier];
        request.earliestBeginDate = [NSDate dateWithTimeIntervalSinceNow:kMinimumInterval];
        NSError *error = nil;
        [BGTaskScheduler.sharedScheduler submitTaskRequest:request error:&error];
        if (error) {
            NSLog(@"[Layers] BGAppRefreshTask scheduling failed: %@", error.localizedDescription);
        }
    }
#endif
}

#if LAYERS_HAS_BGTASKS
API_AVAILABLE(ios(13.0))
static void layers_bg_handle_task(BGTask *task) {
    // Schedule the next execution before starting work
    layers_bg_schedule_next_flush();

    __block atomic_bool expired = false;
    task.expirationHandler = ^{
        atomic_store(&expired, true);
        [task setTaskCompletedWithSuccess:NO];
    };

    // Notify Unity to flush events. UnitySendMessage is asynchronous (posts to
    // Unity's main thread message queue), so we must wait for the flush to
    // complete before marking the task done. The C# side calls
    // layers_background_flush_completed() when done.
    atomic_store(&sFlushCompleted, false);
    UnitySendMessage("_LayersBackgroundFlush", "OnBackgroundFlush", "");

    // Poll for completion with a timeout (max 25 seconds — BGAppRefreshTask
    // typically allows ~30 seconds). Polling on the background thread is safe
    // since the task handler runs on a serial queue.
    NSDate *deadline = [NSDate dateWithTimeIntervalSinceNow:25.0];
    while (!atomic_load(&sFlushCompleted) && !atomic_load(&expired) && [[NSDate date] compare:deadline] == NSOrderedAscending) {
        [NSThread sleepForTimeInterval:0.25];
    }

    if (!atomic_load(&expired)) {
        [task setTaskCompletedWithSuccess:atomic_load(&sFlushCompleted)];
    }
}
#endif

#pragma mark - Exported C functions

/// Register the background flush task with the system.
/// MUST be called during application launch (in application:didFinishLaunchingWithOptions:).
/// Calling this after the app has finished launching will fail silently.
extern "C" void layers_background_flush_register(void) {
#if LAYERS_HAS_BGTASKS
    if (@available(iOS 13.0, *)) {
        sIsRegistered = [BGTaskScheduler.sharedScheduler
            registerForTaskWithIdentifier:kTaskIdentifier
            usingQueue:nil
            launchHandler:^(BGTask * _Nonnull task) {
                layers_bg_handle_task(task);
            }];
        if (!sIsRegistered) {
            NSLog(@"[Layers] BGTaskScheduler.register failed -- ensure "
                  @"layers_background_flush_register() is called in "
                  @"application:didFinishLaunchingWithOptions: and "
                  @"com.layers.sdk.background-flush is in Info.plist "
                  @"BGTaskSchedulerPermittedIdentifiers");
        }
    }
#endif
}

/// Enable periodic background flush. Schedules the BGAppRefreshTask.
/// Returns true if scheduling succeeded, false otherwise.
extern "C" bool layers_background_flush_enable(void) {
#if LAYERS_HAS_BGTASKS
    if (@available(iOS 13.0, *)) {
        if (!sIsRegistered) {
            layers_background_flush_register();
        }
        if (!sIsRegistered) {
            return false;
        }
        layers_bg_schedule_next_flush();
        return true;
    }
#endif
    return false;
}

/// Signal that the background flush has completed.
/// Called by Unity C# after the flush finishes, so the BGTask can
/// be marked complete and iOS knows not to suspend the app prematurely.
extern "C" void layers_background_flush_completed(void) {
    atomic_store(&sFlushCompleted, true);
}

/// Disable periodic background flush. Cancels any pending task requests.
extern "C" void layers_background_flush_disable(void) {
#if LAYERS_HAS_BGTASKS
    if (@available(iOS 13.0, *)) {
        [BGTaskScheduler.sharedScheduler cancelTaskRequestWithIdentifier:kTaskIdentifier];
    }
#endif
}

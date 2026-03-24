// LayersWebGL.jslib — JavaScript bridge between Unity C# (via IL2CPP WASM) and
// the Layers Rust WASM core. Unity WebGL builds compile C# to WASM via IL2CPP,
// but the Rust WASM binary is a separate module loaded via this bridge.
//
// Architecture:
//   C# → [DllImport("__Internal")] → this jslib → Rust WASM core
//   HTTP delivery: Rust WASM drain() → JS fetch() (same pattern as @layers/client)
//
// The Rust WASM core is the SINGLE SOURCE OF TRUTH for event acceptance, building,
// queuing, consent, rate limiting, sampling, and serialization. This bridge only
// handles: WASM loading, HTTP delivery (fetch/sendBeacon), browser APIs (cookies,
// localStorage, navigator), and lifecycle listeners.

var LayersWebGLPlugin = {
  // ── Internal State ────────────────────────────────────────────────────

  $LayersState: {
    wasm: null, // WasmLayersCore instance (from Rust WASM)
    wasmModule: null, // Raw WASM module reference
    wasmReady: false,
    config: null, // Parsed config object
    baseUrl: 'https://in.layers.com',
    flushTimer: null,
    isFlushing: false,
    isShutDown: false,
    circuitFailures: 0,
    circuitState: 'closed', // 'closed' | 'open' | 'half-open'
    circuitOpenedAt: 0,
    circuitThreshold: 5,
    circuitResetMs: 60000,
    maxBatchSize: 20,

    // Retry-After gate (mirrors WASM/Rust behavior)
    retryAfterUntilMs: 0,
    RETRY_AFTER_MAX_SECS: 300,

    // Pre-init event queue: buffers calls made before WASM is ready.
    // Each entry is { method: string, args: array }.
    // Replayed in order once WASM init completes.
    preInitQueue: [],
    PRE_INIT_MAX: 1000,

    // Listeners for cleanup
    onlineListener: null,
    offlineListener: null,
    visibilityListener: null,
    beforeUnloadListener: null,

    // SPA deep link tracking
    popstateListener: null,
    hashchangeListener: null,
    lastDeepLinkUrl: null,
    lastDeepLinkTimestamp: 0,
    DEEP_LINK_DEDUP_MS: 2000
  },

  // ── WASM Loading ──────────────────────────────────────────────────────

  // Load the Rust WASM binary. Looks for it in StreamingAssets first,
  // then falls back to a relative path. Returns a promise.
  $LayersLoadWasm: function () {
    return new Promise(function (resolve, reject) {
      // Try StreamingAssets path first (standard Unity WebGL pattern)
      var paths = [
        'StreamingAssets/layers_core_bg.wasm',
        'layers_core_bg.wasm',
        './layers_core_bg.wasm'
      ];

      function tryLoad(index) {
        if (index >= paths.length) {
          reject(new Error('Failed to load Layers WASM binary from any path'));
          return;
        }

        fetch(paths[index])
          .then(function (response) {
            if (!response.ok) {
              tryLoad(index + 1);
              return;
            }
            return response.arrayBuffer();
          })
          .then(function (buffer) {
            if (!buffer) return;
            return WebAssembly.instantiate(buffer);
          })
          .then(function (result) {
            if (!result) return;
            LayersState.wasmModule = result.instance;
            LayersState.wasmReady = true;
            resolve(result.instance);
          })
          .catch(function () {
            tryLoad(index + 1);
          });
      }

      tryLoad(0);
    });
  },

  // ── String Helpers ────────────────────────────────────────────────────

  // Allocate a C string on the Unity heap and return its pointer.
  // The caller (C#) is responsible for freeing via Marshal.FreeHGlobal.
  $LayersAllocString: function (str) {
    if (str === null || str === undefined) return 0;
    var bufferSize = lengthBytesUTF8(str) + 1;
    var buffer = _malloc(bufferSize);
    stringToUTF8(str, buffer, bufferSize);
    return buffer;
  },

  // ── Circuit Breaker ───────────────────────────────────────────────────

  $LayersCheckCircuit: function () {
    if (LayersState.circuitState === 'open') {
      if (Date.now() - LayersState.circuitOpenedAt >= LayersState.circuitResetMs) {
        LayersState.circuitState = 'half-open';
        return true;
      }
      return false;
    }
    return true;
  },

  $LayersRecordSuccess: function () {
    LayersState.circuitFailures = 0;
    if (LayersState.circuitState === 'half-open') {
      LayersState.circuitState = 'closed';
    }
  },

  $LayersRecordFailure: function () {
    LayersState.circuitFailures++;
    if (LayersState.circuitFailures >= LayersState.circuitThreshold) {
      LayersState.circuitState = 'open';
      LayersState.circuitOpenedAt = Date.now();
    }
  },

  // ── Retry-After Helpers ───────────────────────────────────────────────

  $LayersUpdateRetryAfter: function (headerValue) {
    if (!headerValue) {
      LayersState.retryAfterUntilMs = Date.now() + 60000;
      return;
    }
    var deltaSecs = parseInt(headerValue, 10);
    if (!isNaN(deltaSecs) && deltaSecs > 0) {
      var capped = Math.min(deltaSecs, LayersState.RETRY_AFTER_MAX_SECS);
      LayersState.retryAfterUntilMs = Date.now() + capped * 1000;
      return;
    }
    var dateMs = Date.parse(headerValue);
    if (!isNaN(dateMs)) {
      var delaySecs = Math.max(0, (dateMs - Date.now()) / 1000);
      var cappedSecs = Math.min(delaySecs, LayersState.RETRY_AFTER_MAX_SECS);
      LayersState.retryAfterUntilMs = Date.now() + cappedSecs * 1000;
      return;
    }
    LayersState.retryAfterUntilMs = Date.now() + 60000;
  },

  $LayersIsRetryAfterActive: function () {
    return LayersState.retryAfterUntilMs > 0 && Date.now() < LayersState.retryAfterUntilMs;
  },

  // ── HTTP Delivery ─────────────────────────────────────────────────────

  // Drain events from the Rust WASM core and send via fetch.
  // On failure, requeue events back into the Rust queue.
  // This mirrors the drain+fetch pattern from @layers/client.
  $LayersFlushViaFetch: function () {
    if (LayersState.isFlushing || LayersState.isShutDown) return;
    if (!LayersState.wasm) return;
    if (!LayersCheckCircuit()) return;
    if (LayersIsRetryAfterActive()) return;

    LayersState.isFlushing = true;

    try {
      var batchJson = LayersState.wasm.drain(LayersState.maxBatchSize);
      if (batchJson === null || batchJson === undefined) {
        LayersState.isFlushing = false;
        return;
      }

      var headers = LayersState.wasm.flushHeaders();
      var url = LayersState.wasm.eventsUrl();

      // Parse events for potential requeue
      var eventsJson = null;
      try {
        var parsed = JSON.parse(batchJson);
        eventsJson = JSON.stringify(parsed.events);
      } catch (e) {
        // Can't parse — won't be able to requeue
      }

      var headerObj = { 'Content-Type': 'application/json' };
      if (Array.isArray(headers)) {
        for (var i = 0; i < headers.length; i++) {
          headerObj[headers[i][0]] = headers[i][1];
        }
      }

      var maxRetries = 3;
      var attempt = 0;

      function doAttempt() {
        fetch(url, {
          method: 'POST',
          headers: headerObj,
          body: batchJson,
          keepalive: true
        })
          .then(function (response) {
            if (response.ok) {
              LayersRecordSuccess();
              LayersState.retryAfterUntilMs = 0;
              LayersState.isFlushing = false;
              // Try to drain more if events remain
              if (LayersState.wasm && LayersState.wasm.queueDepth() > 0) {
                LayersFlushViaFetch();
              }
              return;
            }

            if (response.status === 429 || response.status >= 500) {
              LayersRecordFailure();
              var retryAfterHeader = response.headers.get('retry-after');
              LayersUpdateRetryAfter(retryAfterHeader);

              attempt++;
              if (attempt < maxRetries) {
                var delay = Math.min(1000 * Math.pow(2, attempt) + Math.random() * 250, 30000);
                setTimeout(doAttempt, delay);
                return;
              }

              // Retries exhausted for retryable error — requeue for later
              if (eventsJson && LayersState.wasm) {
                try {
                  LayersState.wasm.requeue(eventsJson);
                } catch (e) {}
              }
            } else {
              // Non-retryable error (400, 401, 403, etc.) — drop events to
              // avoid an infinite requeue loop. Record failure so circuit
              // breaker can open if the server keeps rejecting requests.
              LayersRecordFailure();
              if (LayersState.config && LayersState.config.enable_debug) {
                console.warn('[Layers] Non-retryable HTTP ' + response.status + ', dropping batch');
              }
            }

            LayersState.isFlushing = false;
          })
          .catch(function () {
            LayersRecordFailure();
            attempt++;
            if (attempt < maxRetries) {
              var delay = Math.min(1000 * Math.pow(2, attempt) + Math.random() * 250, 30000);
              setTimeout(doAttempt, delay);
              return;
            }
            // Retries exhausted — requeue
            if (eventsJson && LayersState.wasm) {
              try {
                LayersState.wasm.requeue(eventsJson);
              } catch (e) {}
            }
            LayersState.isFlushing = false;
          });
      }

      doAttempt();
    } catch (e) {
      LayersState.isFlushing = false;
    }
  },

  // ── Lifecycle Listeners ───────────────────────────────────────────────

  $LayersSetupListeners: function () {
    if (typeof window === 'undefined') return;

    // Online/offline detection — flush on reconnect
    LayersState.onlineListener = function () {
      if (!LayersState.isShutDown && LayersState.wasm) {
        LayersFlushViaFetch();
      }
    };
    LayersState.offlineListener = function () {
      // No-op, just track state
    };
    window.addEventListener('online', LayersState.onlineListener);
    window.addEventListener('offline', LayersState.offlineListener);

    // visibilitychange — flush on page hide using sendBeacon
    if (typeof document !== 'undefined') {
      LayersState.visibilityListener = function () {
        if (document.visibilityState === 'hidden' && LayersState.wasm && !LayersState.isShutDown) {
          // Drain ALL events via sendBeacon for reliability on page hide.
          // On mobile browsers beforeunload is unreliable, so visibilitychange
          // with 'hidden' is the primary last-chance flush point.
          try {
            var batchJson = LayersState.wasm.drain(10000);
            if (batchJson !== null && batchJson !== undefined) {
              var url = LayersState.wasm.eventsUrl();
              var blob = new Blob([batchJson], { type: 'application/json' });
              var sent = navigator.sendBeacon(url, blob);
              if (!sent) {
                // sendBeacon failed — requeue
                try {
                  var parsed = JSON.parse(batchJson);
                  LayersState.wasm.requeue(JSON.stringify(parsed.events));
                } catch (e) {}
              }
            }
          } catch (e) {
            // Best effort
          }
        }
      };
      document.addEventListener('visibilitychange', LayersState.visibilityListener);
    }

    // beforeunload — last-chance flush
    LayersState.beforeUnloadListener = function () {
      if (LayersState.wasm && !LayersState.isShutDown) {
        try {
          var batchJson = LayersState.wasm.drain(10000); // drain all
          if (batchJson !== null && batchJson !== undefined) {
            var url = LayersState.wasm.eventsUrl();
            var blob = new Blob([batchJson], { type: 'application/json' });
            navigator.sendBeacon(url, blob);
          }
        } catch (e) {}
      }
    };
    window.addEventListener('beforeunload', LayersState.beforeUnloadListener);
  },

  $LayersCleanupListeners: function () {
    if (typeof window !== 'undefined') {
      if (LayersState.onlineListener) {
        window.removeEventListener('online', LayersState.onlineListener);
        LayersState.onlineListener = null;
      }
      if (LayersState.offlineListener) {
        window.removeEventListener('offline', LayersState.offlineListener);
        LayersState.offlineListener = null;
      }
      if (LayersState.beforeUnloadListener) {
        window.removeEventListener('beforeunload', LayersState.beforeUnloadListener);
        LayersState.beforeUnloadListener = null;
      }
    }
    if (typeof document !== 'undefined' && LayersState.visibilityListener) {
      document.removeEventListener('visibilitychange', LayersState.visibilityListener);
      LayersState.visibilityListener = null;
    }

    // Cleanup SPA deep link listeners
    LayersCleanupSpaListeners();
  },

  // ── Remote Config Polling ─────────────────────────────────────────────

  // Fetch remote config from /config via fetch() and feed to the WASM core.
  // Supports ETag / 304 Not Modified to avoid unnecessary re-downloads.
  $LayersConfigEtag: '',
  $LayersConfigTimer: null,

  $LayersFetchConfig: function () {
    if (!LayersState.wasm || LayersState.isShutDown) return;

    var url =
      LayersState.baseUrl +
      '/config?app_id=' +
      encodeURIComponent(LayersState.config.app_id) +
      '&platform=unity';
    var headers = {
      'X-App-Id': LayersState.config.app_id,
      Accept: 'application/json'
    };
    if (LayersConfigEtag) {
      headers['If-None-Match'] = LayersConfigEtag;
    }

    fetch(url, { method: 'GET', headers: headers })
      .then(function (response) {
        if (response.status === 200) {
          var newEtag = response.headers.get('ETag') || '';
          LayersConfigEtag = newEtag;
          return response.text().then(function (body) {
            if (body && LayersState.wasm) {
              try {
                LayersState.wasm.updateRemoteConfig(body, newEtag);
                if (LayersState.config && LayersState.config.enable_debug) {
                  console.log('[Layers] Remote config updated');
                }
              } catch (e) {
                if (LayersState.config && LayersState.config.enable_debug) {
                  console.warn('[Layers] Remote config update failed:', e);
                }
              }
            }
          });
        } else if (response.status === 304) {
          if (LayersState.config && LayersState.config.enable_debug) {
            console.log('[Layers] Remote config not modified');
          }
        }
      })
      .catch(function (e) {
        if (LayersState.config && LayersState.config.enable_debug) {
          console.warn('[Layers] Remote config fetch failed:', e);
        }
      });
  },

  // ── Pre-Init Queue Replay ─────────────────────────────────────────────

  $LayersReplayPreInitQueue: function () {
    var queue = LayersState.preInitQueue;
    LayersState.preInitQueue = [];
    if (queue.length === 0) return;

    if (LayersState.config && LayersState.config.enable_debug) {
      console.log('[Layers] Replaying ' + queue.length + ' pre-init queued calls');
    }

    for (var i = 0; i < queue.length; i++) {
      var entry = queue[i];
      try {
        if (entry.method === 'track') {
          LayersState.wasm.track(entry.args[0], entry.args[1], null, null);
        } else if (entry.method === 'screen') {
          LayersState.wasm.screen(entry.args[0], entry.args[1], null, null);
        } else if (entry.method === 'identify') {
          LayersState.wasm.identify(entry.args[0]);
        } else if (entry.method === 'group') {
          LayersState.wasm.group(entry.args[0], entry.args[1]);
        } else if (entry.method === 'setUserProperties') {
          LayersState.wasm.setUserProperties(entry.args[0]);
        } else if (entry.method === 'setUserPropertiesOnce') {
          LayersState.wasm.setUserPropertiesOnce(entry.args[0]);
        } else if (entry.method === 'setConsent') {
          LayersState.wasm.setConsent(entry.args[0]);
        } else if (entry.method === 'setDeviceContext') {
          LayersState.wasm.setDeviceContext(entry.args[0]);
        }
      } catch (e) {
        if (LayersState.config && LayersState.config.enable_debug) {
          console.warn('[Layers] Pre-init replay failed for ' + entry.method + ':', e);
        }
      }
    }
  },

  // ── Cookie Reading ────────────────────────────────────────────────────

  $LayersGetCookie: function (name) {
    if (typeof document === 'undefined' || !document.cookie) return null;
    var prefix = name + '=';
    var cookies = document.cookie.split('; ');
    for (var i = 0; i < cookies.length; i++) {
      if (cookies[i].indexOf(prefix) === 0) {
        return decodeURIComponent(cookies[i].substring(prefix.length));
      }
    }
    return null;
  },

  // ── SPA Deep Link Tracking ──────────────────────────────────────

  // Attribution parameter names to look for in URLs
  $LayersAttributionParams: [
    'fbclid',
    'gclid',
    'gbraid',
    'wbraid',
    'ttclid',
    'msclkid',
    'rclid',
    'twclid',
    'li_fat_id',
    'sclid',
    'irclickid',
    'utm_source',
    'utm_medium',
    'utm_campaign',
    'utm_content',
    'utm_term'
  ],

  // Build deep_link_opened properties from a URL string.
  // Returns null if the URL has no attribution params.
  $LayersBuildDeepLinkProps: function (url) {
    try {
      var parsed = new URL(url);
      var params = parsed.searchParams;
      var hasAttribution = false;
      var props = {
        url: url,
        scheme: parsed.protocol.replace(':', ''),
        host: parsed.hostname,
        path: parsed.pathname
      };
      for (var i = 0; i < LayersAttributionParams.length; i++) {
        var name = LayersAttributionParams[i];
        var val = params.get(name);
        if (val) {
          props[name] = val;
          hasAttribution = true;
        }
      }
      if (!hasAttribution) return null;
      return props;
    } catch (e) {
      return null;
    }
  },

  // Track a deep_link_opened event via the WASM core, with 2-second deduplication.
  $LayersTrackDeepLink: function (url) {
    if (!LayersState.wasm || LayersState.isShutDown) return;

    // Deduplicate: same URL within DEEP_LINK_DEDUP_MS window
    var now = Date.now();
    if (
      url === LayersState.lastDeepLinkUrl &&
      now - LayersState.lastDeepLinkTimestamp < LayersState.DEEP_LINK_DEDUP_MS
    ) {
      return;
    }

    var props = LayersBuildDeepLinkProps(url);
    if (!props) return;

    LayersState.lastDeepLinkUrl = url;
    LayersState.lastDeepLinkTimestamp = now;

    try {
      LayersState.wasm.track('deep_link_opened', props, null, null);

      // Auto-flush if threshold reached
      var config = LayersState.config;
      var threshold = (config && config.flush_threshold) || 20;
      if (LayersState.wasm.queueDepth() >= threshold) {
        LayersFlushViaFetch();
      }
    } catch (e) {
      if (LayersState.config && LayersState.config.enable_debug) {
        console.warn('[Layers] deep_link_opened track failed:', e);
      }
    }
  },

  // Setup popstate and hashchange listeners for SPA navigation tracking
  $LayersSetupSpaListeners: function () {
    if (typeof window === 'undefined') return;

    LayersState.popstateListener = function () {
      LayersTrackDeepLink(window.location.href);
    };
    window.addEventListener('popstate', LayersState.popstateListener);

    LayersState.hashchangeListener = function () {
      LayersTrackDeepLink(window.location.href);
    };
    window.addEventListener('hashchange', LayersState.hashchangeListener);
  },

  $LayersCleanupSpaListeners: function () {
    if (typeof window === 'undefined') return;
    if (LayersState.popstateListener) {
      window.removeEventListener('popstate', LayersState.popstateListener);
      LayersState.popstateListener = null;
    }
    if (LayersState.hashchangeListener) {
      window.removeEventListener('hashchange', LayersState.hashchangeListener);
      LayersState.hashchangeListener = null;
    }
  },

  // ══════════════════════════════════════════════════════════════════════
  // EXPORTED FUNCTIONS (called from C# via [DllImport("__Internal")])
  // ══════════════════════════════════════════════════════════════════════

  // ── Initialization ────────────────────────────────────────────────────

  LayersWebGL_Init: function (configJsonPtr) {
    var configJson = UTF8ToString(configJsonPtr);
    var config;
    try {
      config = JSON.parse(configJson);
    } catch (e) {
      console.error('[Layers] Failed to parse config JSON:', e);
      return;
    }

    LayersState.config = config;
    LayersState.isShutDown = false;
    LayersState.baseUrl = (config.base_url || 'https://in.layers.com').replace(/\/$/, '');
    LayersState.maxBatchSize = config.max_batch_size || 20;

    // Initialize the WASM core.
    // The WASM binary must be loaded asynchronously, so we start the load
    // and the wasm instance becomes available when ready.
    LayersLoadWasm()
      .then(function () {
        // If shutdown was called while WASM was loading, bail out.
        // Otherwise we'd leak timers and listeners that can never be cleaned up.
        if (LayersState.isShutDown) {
          return;
        }

        // Try to initialize via the global layers_core WASM bindings
        if (typeof Module !== 'undefined' && Module.LayersWasm) {
          try {
            LayersState.wasm = Module.LayersWasm.init({
              appId: config.app_id,
              environment: config.environment || 'production',
              baseUrl: config.base_url,
              flushThreshold: config.flush_threshold,
              maxQueueSize: config.max_queue_size,
              maxBatchSize: config.max_batch_size,
              enableDebug: config.enable_debug,
              sdkVersion: config.sdk_version
            });
            LayersState.wasmReady = true;

            // Replay any events queued before WASM was ready
            LayersReplayPreInitQueue();

            // Fetch remote config now that WASM is ready (the initial call
            // outside the .then() fires too early — WASM hasn't loaded yet).
            LayersFetchConfig();
          } catch (e) {
            console.warn('[Layers] WASM core initialization failed:', e);
          }
        }

        // Only start timers and listeners if WASM init succeeded
        if (!LayersState.wasmReady) {
          console.error('[Layers] WASM core not available, skipping timer/listener setup');
          return;
        }

        // Fire deep_link_opened for initial URL if it contains attribution params.
        // This matches iOS/Android DeepLinksModule behavior on cold start.
        if (typeof window !== 'undefined') {
          LayersTrackDeepLink(window.location.href);
        }

        // Setup SPA navigation listeners for popstate/hashchange
        LayersSetupSpaListeners();

        // Start periodic flush timer only after WASM is ready (avoids
        // wasteful no-op ticks before the core can accept events).
        var intervalMs = (LayersState.config && LayersState.config.flush_interval_ms) || 30000;
        if (LayersState.flushTimer) clearInterval(LayersState.flushTimer);
        LayersState.flushTimer = setInterval(function () {
          if (!LayersState.isShutDown && LayersState.wasm && LayersState.wasm.queueDepth() > 0) {
            LayersFlushViaFetch();
          }
        }, intervalMs);

        // Start remote config polling (5 minute interval) after WASM is ready.
        if (typeof LayersConfigTimer !== 'undefined' && LayersConfigTimer)
          clearInterval(LayersConfigTimer);
        LayersConfigTimer = setInterval(function () {
          LayersFetchConfig();
        }, 300000);
      })
      .catch(function (e) {
        console.warn('[Layers] WASM binary load failed, using JS fallback:', e);
      });

    // Setup lifecycle listeners (these don't require WASM — they handle
    // visibility/online events and are needed from the start)
    LayersSetupListeners();
  },

  // ── Event Tracking ────────────────────────────────────────────────────

  LayersWebGL_Track: function (eventNamePtr, propertiesJsonPtr) {
    if (LayersState.isShutDown) return;

    var eventName = UTF8ToString(eventNamePtr);
    var propsJson = propertiesJsonPtr ? UTF8ToString(propertiesJsonPtr) : null;

    // Queue events that arrive before WASM is ready
    if (!LayersState.wasm) {
      if (LayersState.preInitQueue.length < LayersState.PRE_INIT_MAX) {
        var props = null;
        try {
          props = propsJson ? JSON.parse(propsJson) : null;
        } catch (e) {
          if (LayersState.config && LayersState.config.enable_debug) {
            console.warn('[Layers] Pre-init track: failed to parse properties JSON:', e);
          }
        }
        LayersState.preInitQueue.push({ method: 'track', args: [eventName, props] });
      } else if (LayersState.config && LayersState.config.enable_debug) {
        console.warn('[Layers] Pre-init queue full, dropping event: ' + eventName);
      }
      return;
    }

    try {
      var props = propsJson ? JSON.parse(propsJson) : null;
      LayersState.wasm.track(eventName, props, null, null);

      // Auto-flush if threshold reached
      var config = LayersState.config;
      var threshold = (config && config.flush_threshold) || 20;
      if (LayersState.wasm.queueDepth() >= threshold) {
        LayersFlushViaFetch();
      }
    } catch (e) {
      if (LayersState.config && LayersState.config.enable_debug) {
        console.warn('[Layers] WebGL track failed:', e);
      }
    }
  },

  LayersWebGL_Screen: function (screenNamePtr, propertiesJsonPtr) {
    if (LayersState.isShutDown) return;

    var screenName = UTF8ToString(screenNamePtr);
    var propsJson = propertiesJsonPtr ? UTF8ToString(propertiesJsonPtr) : null;

    // Queue screen calls that arrive before WASM is ready
    if (!LayersState.wasm) {
      if (LayersState.preInitQueue.length < LayersState.PRE_INIT_MAX) {
        var props = null;
        try {
          props = propsJson ? JSON.parse(propsJson) : null;
        } catch (e) {
          if (LayersState.config && LayersState.config.enable_debug) {
            console.warn('[Layers] Pre-init screen: failed to parse properties JSON:', e);
          }
        }
        LayersState.preInitQueue.push({ method: 'screen', args: [screenName, props] });
      } else if (LayersState.config && LayersState.config.enable_debug) {
        console.warn('[Layers] Pre-init queue full, dropping screen: ' + screenName);
      }
      return;
    }

    try {
      var props = propsJson ? JSON.parse(propsJson) : null;
      LayersState.wasm.screen(screenName, props, null, null);

      var config = LayersState.config;
      var threshold = (config && config.flush_threshold) || 20;
      if (LayersState.wasm.queueDepth() >= threshold) {
        LayersFlushViaFetch();
      }
    } catch (e) {
      if (LayersState.config && LayersState.config.enable_debug) {
        console.warn('[Layers] WebGL screen failed:', e);
      }
    }
  },

  // ── User Identity ─────────────────────────────────────────────────────

  LayersWebGL_Identify: function (userIdPtr) {
    if (LayersState.isShutDown) return;

    var userId = UTF8ToString(userIdPtr);

    if (!LayersState.wasm) {
      if (LayersState.preInitQueue.length < LayersState.PRE_INIT_MAX) {
        LayersState.preInitQueue.push({ method: 'identify', args: [userId] });
      }
      return;
    }

    try {
      LayersState.wasm.identify(userId);
    } catch (e) {
      if (LayersState.config && LayersState.config.enable_debug) {
        console.warn('[Layers] WebGL identify failed:', e);
      }
    }
  },

  LayersWebGL_Group: function (groupIdPtr, propertiesJsonPtr) {
    if (LayersState.isShutDown) return;

    var groupId = UTF8ToString(groupIdPtr);
    var propsJson = propertiesJsonPtr ? UTF8ToString(propertiesJsonPtr) : null;

    if (!LayersState.wasm) {
      if (LayersState.preInitQueue.length < LayersState.PRE_INIT_MAX) {
        var props = null;
        try {
          props = propsJson ? JSON.parse(propsJson) : null;
        } catch (e) {
          if (LayersState.config && LayersState.config.enable_debug) {
            console.warn('[Layers] Pre-init group: failed to parse properties JSON:', e);
          }
        }
        LayersState.preInitQueue.push({ method: 'group', args: [groupId, props] });
      }
      return;
    }

    try {
      var props = propsJson ? JSON.parse(propsJson) : null;
      LayersState.wasm.group(groupId, props);
    } catch (e) {
      if (LayersState.config && LayersState.config.enable_debug) {
        console.warn('[Layers] WebGL group failed:', e);
      }
    }
  },

  LayersWebGL_SetUserProperties: function (propertiesJsonPtr) {
    if (LayersState.isShutDown) return;

    var propsJson = UTF8ToString(propertiesJsonPtr);

    if (!LayersState.wasm) {
      if (LayersState.preInitQueue.length < LayersState.PRE_INIT_MAX) {
        var props = {};
        try {
          props = JSON.parse(propsJson);
        } catch (e) {
          if (LayersState.config && LayersState.config.enable_debug) {
            console.warn('[Layers] Pre-init setUserProperties: failed to parse JSON:', e);
          }
        }
        LayersState.preInitQueue.push({ method: 'setUserProperties', args: [props] });
      }
      return;
    }

    try {
      var props = JSON.parse(propsJson);
      LayersState.wasm.setUserProperties(props);
    } catch (e) {
      if (LayersState.config && LayersState.config.enable_debug) {
        console.warn('[Layers] WebGL setUserProperties failed:', e);
      }
    }
  },

  LayersWebGL_SetUserPropertiesOnce: function (propertiesJsonPtr) {
    if (LayersState.isShutDown) return;

    var propsJson = UTF8ToString(propertiesJsonPtr);

    if (!LayersState.wasm) {
      if (LayersState.preInitQueue.length < LayersState.PRE_INIT_MAX) {
        var props = {};
        try {
          props = JSON.parse(propsJson);
        } catch (e) {
          if (LayersState.config && LayersState.config.enable_debug) {
            console.warn('[Layers] Pre-init setUserPropertiesOnce: failed to parse JSON:', e);
          }
        }
        LayersState.preInitQueue.push({ method: 'setUserPropertiesOnce', args: [props] });
      }
      return;
    }

    try {
      var props = JSON.parse(propsJson);
      LayersState.wasm.setUserPropertiesOnce(props);
    } catch (e) {
      if (LayersState.config && LayersState.config.enable_debug) {
        console.warn('[Layers] WebGL setUserPropertiesOnce failed:', e);
      }
    }
  },

  // ── Consent ───────────────────────────────────────────────────────────

  LayersWebGL_SetConsent: function (consentJsonPtr) {
    if (LayersState.isShutDown) return;

    var consentJson = UTF8ToString(consentJsonPtr);

    if (!LayersState.wasm) {
      if (LayersState.preInitQueue.length < LayersState.PRE_INIT_MAX) {
        var consent = {};
        try {
          consent = JSON.parse(consentJson);
        } catch (e) {
          if (LayersState.config && LayersState.config.enable_debug) {
            console.warn('[Layers] Pre-init setConsent: failed to parse JSON:', e);
          }
        }
        LayersState.preInitQueue.push({ method: 'setConsent', args: [consent] });
      }
      return;
    }

    try {
      var consent = JSON.parse(consentJson);
      LayersState.wasm.setConsent(consent);
    } catch (e) {
      if (LayersState.config && LayersState.config.enable_debug) {
        console.warn('[Layers] WebGL setConsent failed:', e);
      }
    }
  },

  // ── Device Context ────────────────────────────────────────────────────

  LayersWebGL_SetDeviceContext: function (contextJsonPtr) {
    if (LayersState.isShutDown) return;

    var contextJson = UTF8ToString(contextJsonPtr);

    if (!LayersState.wasm) {
      if (LayersState.preInitQueue.length < LayersState.PRE_INIT_MAX) {
        var context = {};
        try {
          context = JSON.parse(contextJson);
        } catch (e) {
          if (LayersState.config && LayersState.config.enable_debug) {
            console.warn('[Layers] Pre-init setDeviceContext: failed to parse JSON:', e);
          }
        }
        LayersState.preInitQueue.push({ method: 'setDeviceContext', args: [context] });
      }
      return;
    }

    try {
      var context = JSON.parse(contextJson);
      LayersState.wasm.setDeviceContext(context);
    } catch (e) {
      if (LayersState.config && LayersState.config.enable_debug) {
        console.warn('[Layers] WebGL setDeviceContext failed:', e);
      }
    }
  },

  // ── Flush / Drain ─────────────────────────────────────────────────────

  LayersWebGL_Flush: function () {
    if (LayersState.isShutDown) return;
    LayersFlushViaFetch();
  },

  LayersWebGL_FlushBlocking: function () {
    // In WebGL, we can't do a truly blocking flush.
    // Use sendBeacon for best-effort delivery during shutdown.
    if (!LayersState.wasm || LayersState.isShutDown) return;

    try {
      var batchJson = LayersState.wasm.drain(10000);
      if (batchJson !== null && batchJson !== undefined) {
        var url = LayersState.wasm.eventsUrl();
        var blob = new Blob([batchJson], { type: 'application/json' });
        navigator.sendBeacon(url, blob);
      }
    } catch (e) {
      // Best effort
    }
  },

  LayersWebGL_DrainBatch: function (count) {
    if (!LayersState.wasm) return 0;

    try {
      var batchJson = LayersState.wasm.drain(count);
      if (batchJson === null || batchJson === undefined) return 0;
      return LayersAllocString(batchJson);
    } catch (e) {
      return 0;
    }
  },

  LayersWebGL_RequeueEvents: function (eventsJsonPtr) {
    if (!LayersState.wasm) return;

    var eventsJson = UTF8ToString(eventsJsonPtr);
    try {
      LayersState.wasm.requeue(eventsJson);
    } catch (e) {
      // Requeue failed — events lost
    }
  },

  LayersWebGL_FlushHeaders: function () {
    if (!LayersState.wasm) return 0;

    try {
      var headers = LayersState.wasm.flushHeaders();
      return LayersAllocString(JSON.stringify(headers));
    } catch (e) {
      return 0;
    }
  },

  LayersWebGL_EventsUrl: function () {
    if (!LayersState.wasm) return 0;

    try {
      var url = LayersState.wasm.eventsUrl();
      return LayersAllocString(url);
    } catch (e) {
      return 0;
    }
  },

  // ── Queue State ───────────────────────────────────────────────────────

  LayersWebGL_QueueDepth: function () {
    if (!LayersState.wasm) return -1;
    try {
      return LayersState.wasm.queueDepth();
    } catch (e) {
      return -1;
    }
  },

  LayersWebGL_IsInitialized: function () {
    return LayersState.wasm !== null && !LayersState.isShutDown ? 1 : 0;
  },

  // ── Session ───────────────────────────────────────────────────────────

  LayersWebGL_GetSessionId: function () {
    if (!LayersState.wasm) return 0;
    try {
      var sessionId = LayersState.wasm.getSessionId();
      return LayersAllocString(sessionId);
    } catch (e) {
      return 0;
    }
  },

  // ── Remote Config ─────────────────────────────────────────────────────

  LayersWebGL_GetRemoteConfigJson: function () {
    if (!LayersState.wasm) return 0;
    try {
      var json = LayersState.wasm.getRemoteConfigJson();
      if (json === null || json === undefined) return 0;
      return LayersAllocString(json);
    } catch (e) {
      return 0;
    }
  },

  LayersWebGL_UpdateRemoteConfig: function (configJsonPtr, etagPtr) {
    if (!LayersState.wasm) return;

    var configJson = UTF8ToString(configJsonPtr);
    var etag = etagPtr ? UTF8ToString(etagPtr) : null;

    try {
      LayersState.wasm.updateRemoteConfig(configJson, etag);
    } catch (e) {
      // Best effort
    }
  },

  // ── Shutdown ──────────────────────────────────────────────────────────

  LayersWebGL_Shutdown: function () {
    LayersState.isShutDown = true;

    // Stop periodic flush
    if (LayersState.flushTimer) {
      clearInterval(LayersState.flushTimer);
      LayersState.flushTimer = null;
    }

    // Stop remote config polling
    if (LayersConfigTimer) {
      clearInterval(LayersConfigTimer);
      LayersConfigTimer = null;
    }

    // Cleanup listeners
    LayersCleanupListeners();

    // Last-chance flush via sendBeacon
    if (LayersState.wasm) {
      try {
        var batchJson = LayersState.wasm.drain(10000);
        if (batchJson !== null && batchJson !== undefined) {
          var url = LayersState.wasm.eventsUrl();
          var blob = new Blob([batchJson], { type: 'application/json' });
          navigator.sendBeacon(url, blob);
        }
      } catch (e) {}

      try {
        LayersState.wasm.shutdown();
      } catch (e) {}
    }

    LayersState.wasm = null;
    LayersState.wasmReady = false;
  },

  // ── CAPI Properties ───────────────────────────────────────────────────
  // Meta _fbp, TikTok _ttp, page URL, fbc — same as @layers/client/capi.ts

  LayersWebGL_GetFbpCookie: function () {
    var value = LayersGetCookie('_fbp');
    return LayersAllocString(value);
  },

  LayersWebGL_GetTtpCookie: function () {
    var value = LayersGetCookie('_ttp');
    return LayersAllocString(value);
  },

  LayersWebGL_GetPageUrl: function () {
    if (typeof window === 'undefined') return 0;
    try {
      return LayersAllocString(window.location.href);
    } catch (e) {
      return 0;
    }
  },

  LayersWebGL_GetFbc: function () {
    // Check for fbclid in URL params first
    if (typeof window !== 'undefined') {
      try {
        var params = new URLSearchParams(window.location.search);
        var fbclid = params.get('fbclid');
        if (fbclid) {
          var fbc = 'fb.1.' + Date.now() + '.' + fbclid;
          return LayersAllocString(fbc);
        }
      } catch (e) {}
    }
    // Fall back to _fbc cookie
    var value = LayersGetCookie('_fbc');
    return LayersAllocString(value);
  },

  // ── Attribution URL Parameters ────────────────────────────────────────

  LayersWebGL_GetUrlParameters: function () {
    if (typeof window === 'undefined') return 0;
    try {
      var params = new URLSearchParams(window.location.search);
      var result = {};
      var clickIdParams = [
        'fbclid',
        'gclid',
        'gbraid',
        'wbraid',
        'ttclid',
        'msclkid',
        'rclid',
        'twclid',
        'li_fat_id',
        'sclid',
        'irclickid'
      ];
      var utmParams = ['utm_source', 'utm_medium', 'utm_campaign', 'utm_content', 'utm_term'];
      var allParams = clickIdParams.concat(utmParams);

      for (var i = 0; i < allParams.length; i++) {
        var val = params.get(allParams[i]);
        if (val) result[allParams[i]] = val;
      }

      if (typeof document !== 'undefined' && document.referrer) {
        result['referrer_url'] = document.referrer;
      }

      if (Object.keys(result).length === 0) return 0;
      return LayersAllocString(JSON.stringify(result));
    } catch (e) {
      return 0;
    }
  },

  // ── Online/Offline ────────────────────────────────────────────────────

  LayersWebGL_IsOnline: function () {
    return navigator.onLine ? 1 : 0;
  },

  // ── localStorage Persistence ──────────────────────────────────────────

  LayersWebGL_SetItem: function (keyPtr, valuePtr) {
    try {
      var key = UTF8ToString(keyPtr);
      var value = UTF8ToString(valuePtr);
      localStorage.setItem(key, value);
    } catch (e) {
      // localStorage unavailable or full
    }
  },

  LayersWebGL_GetItem: function (keyPtr) {
    try {
      var key = UTF8ToString(keyPtr);
      var value = localStorage.getItem(key);
      return LayersAllocString(value);
    } catch (e) {
      return 0;
    }
  },

  LayersWebGL_RemoveItem: function (keyPtr) {
    try {
      var key = UTF8ToString(keyPtr);
      localStorage.removeItem(key);
    } catch (e) {
      // Best effort
    }
  },

  // ── Browser Info ──────────────────────────────────────────────────────

  LayersWebGL_GetUserAgent: function () {
    if (typeof navigator === 'undefined') return 0;
    return LayersAllocString(navigator.userAgent);
  },

  LayersWebGL_GetLanguage: function () {
    if (typeof navigator === 'undefined') return 0;
    return LayersAllocString(navigator.language || 'en-US');
  },

  LayersWebGL_GetScreenSize: function () {
    if (typeof screen === 'undefined') return 0;
    return LayersAllocString(screen.width + 'x' + screen.height);
  },

  LayersWebGL_GetTimezone: function () {
    try {
      return LayersAllocString(Intl.DateTimeFormat().resolvedOptions().timeZone);
    } catch (e) {
      return 0;
    }
  },

  LayersWebGL_GetPlatformOS: function () {
    if (typeof navigator === 'undefined') return 0;
    var ua = navigator.userAgent;
    if (ua.indexOf('Windows') >= 0) return LayersAllocString('Windows');
    if (ua.indexOf('Mac OS') >= 0) return LayersAllocString('macOS');
    if (ua.indexOf('Android') >= 0) return LayersAllocString('Android');
    if (ua.indexOf('iPhone') >= 0 || ua.indexOf('iPad') >= 0) return LayersAllocString('iOS');
    if (ua.indexOf('Linux') >= 0) return LayersAllocString('Linux');
    return LayersAllocString('WebGL');
  }
};

// Wire up dependencies between $ functions and exported functions
autoAddDeps(LayersWebGLPlugin, '$LayersState');
autoAddDeps(LayersWebGLPlugin, '$LayersLoadWasm');
autoAddDeps(LayersWebGLPlugin, '$LayersAllocString');
autoAddDeps(LayersWebGLPlugin, '$LayersCheckCircuit');
autoAddDeps(LayersWebGLPlugin, '$LayersRecordSuccess');
autoAddDeps(LayersWebGLPlugin, '$LayersRecordFailure');
autoAddDeps(LayersWebGLPlugin, '$LayersUpdateRetryAfter');
autoAddDeps(LayersWebGLPlugin, '$LayersIsRetryAfterActive');
autoAddDeps(LayersWebGLPlugin, '$LayersFlushViaFetch');
autoAddDeps(LayersWebGLPlugin, '$LayersSetupListeners');
autoAddDeps(LayersWebGLPlugin, '$LayersCleanupListeners');
autoAddDeps(LayersWebGLPlugin, '$LayersReplayPreInitQueue');
autoAddDeps(LayersWebGLPlugin, '$LayersConfigEtag');
autoAddDeps(LayersWebGLPlugin, '$LayersConfigTimer');
autoAddDeps(LayersWebGLPlugin, '$LayersFetchConfig');
autoAddDeps(LayersWebGLPlugin, '$LayersGetCookie');
autoAddDeps(LayersWebGLPlugin, '$LayersAttributionParams');
autoAddDeps(LayersWebGLPlugin, '$LayersBuildDeepLinkProps');
autoAddDeps(LayersWebGLPlugin, '$LayersTrackDeepLink');
autoAddDeps(LayersWebGLPlugin, '$LayersSetupSpaListeners');
autoAddDeps(LayersWebGLPlugin, '$LayersCleanupSpaListeners');

mergeInto(LibraryManager.library, LayersWebGLPlugin);

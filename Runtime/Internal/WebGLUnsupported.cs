// The Layers SDK does not support the Unity WebGL target.
//
// The previous WebGL bridge (LayersWebGL.jslib + WebGLPlatform) never
// functioned end-to-end: the wasm-bindgen JS glue was never shipped, the
// loader fetched the binary from a path nothing staged, and raw
// WebAssembly.instantiate cannot link a wasm-bindgen module. Rather than
// ship a platform that silently drops every event, WebGL was removed from
// the support matrix (June 2026 audit). Web games should integrate
// @layers/client from the page side instead.
//
// This guard turns an unsupported-platform build into a clear compile-time
// message instead of a mysterious missing-symbol failure.
#if UNITY_WEBGL && !UNITY_EDITOR
#error The Layers SDK does not support the Unity WebGL target. Use the @layers/client web SDK from the hosting page instead.
#endif

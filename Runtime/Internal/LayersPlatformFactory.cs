namespace Layers.Unity.Internal
{
    /// <summary>
    /// Factory that selects the correct <see cref="ILayersPlatform"/> implementation
    /// based on the build target:
    ///
    /// - WebGL: <see cref="WebGLPlatform"/> (jslib → Rust WASM)
    /// - Everything else: <see cref="NativePlatform"/> (P/Invoke → Rust native lib)
    ///
    /// Platform selection is compile-time via #if directives, so no runtime
    /// overhead or reflection is involved.
    /// </summary>
    internal static class LayersPlatformFactory
    {
        internal static ILayersPlatform Create()
        {
            // Test mode: return mock platform for unit testing without native lib
            if (LayersTestMode.IsEnabled)
            {
                var mock = LayersTestMode.GetMockPlatform();
                if (mock != null) return mock;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            return new WebGLPlatform();
#else
            return new NativePlatform();
#endif
        }
    }
}

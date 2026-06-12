namespace Layers.Unity.Internal
{
    /// <summary>
    /// Factory that selects the <see cref="ILayersPlatform"/> implementation.
    ///
    /// All supported targets (iOS, Android, desktop/Editor) use
    /// <see cref="NativePlatform"/> (P/Invoke → Rust native lib). WebGL is
    /// not supported — see Runtime/Internal/WebGLUnsupported.cs.
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

            return new NativePlatform();
        }
    }
}

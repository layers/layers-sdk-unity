using System;
using System.Runtime.InteropServices;

namespace Layers.Unity.Internal
{
    /// <summary>
    /// Helper for reading and freeing heap-allocated C strings returned by
    /// the WebGL JavaScript bridge (LayersWebGL.jslib).
    ///
    /// Unlike the native Rust FFI (which uses <see cref="NativeStringHelper"/>
    /// with <c>layers_free_string</c>), the jslib allocates strings via
    /// Emscripten's <c>_malloc</c>. These must be freed with
    /// <see cref="Marshal.FreeHGlobal"/> which maps to <c>_free</c> in
    /// the WebGL runtime.
    /// </summary>
#if UNITY_WEBGL && !UNITY_EDITOR
    internal static class WebGLStringHelper
    {
        /// <summary>
        /// Read a UTF-8 C string from a native pointer and free the memory.
        /// Returns null if the pointer is IntPtr.Zero (0).
        /// </summary>
        internal static string ReadAndFree(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            try
            {
                return Marshal.PtrToStringUTF8(ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }
#endif
}

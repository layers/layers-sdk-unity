using System;
using System.Runtime.InteropServices;

namespace Layers.Unity.Internal
{
    /// <summary>
    /// Helper for reading and freeing heap-allocated C strings returned by the Rust FFI.
    /// Every non-null IntPtr returned by a layers_* function MUST be freed exactly once.
    /// </summary>
    internal static class NativeStringHelper
    {
        /// <summary>
        /// Read a UTF-8 C string from a native pointer and free the native memory.
        /// Returns null if the pointer is IntPtr.Zero.
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
                NativeBindings.layers_free_string(ptr);
            }
        }

        /// <summary>
        /// Process a result pointer from a layers_* call that uses the success/error convention.
        /// Returns null on success (ptr is IntPtr.Zero), or the error message string on failure.
        /// </summary>
        internal static string ProcessResult(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            return ReadAndFree(ptr);
        }
    }
}

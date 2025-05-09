using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace KcpTransport.Fallbacks
{
    internal static class MemoryMarshalFallback
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write<T>(Span<byte> destination, in T value) where T : struct
        {
#if NET6_0_OR_GREATER
            MemoryMarshal.Write(destination, value);
#else
            MemoryMarshal.Write(destination, ref Unsafe.AsRef(in value));
#endif
        }
    }
}

namespace KcpTransport
{
    using System.Runtime.InteropServices;
    internal static class MemoryMarshalPolyfill
    {
        internal static void Write<T>(Span<byte> destination, in T value) where T : struct
        {
#if NET8_0_OR_GREATER
            MemoryMarshal.Write(destination, value);
#else
            var refValeu = value;
            MemoryMarshal.Write(destination, ref refValeu);
#endif
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static ref T GetArrayDataReference<T>(T[] array)
        {
#if NET5_0_OR_GREATER
            return ref MemoryMarshal.GetArrayDataReference(array);
#else
            return ref MemoryMarshal.GetReference(array.AsSpan());
#endif
        }
    }
}
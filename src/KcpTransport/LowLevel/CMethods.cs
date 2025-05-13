#pragma warning disable CS8500
#pragma warning disable CS8981

using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace KcpTransport.LowLevel
{
    internal static unsafe class CMethods
    {
        public static void* malloc(nuint size)
        {
#if NET6_0_OR_GREATER
            return NativeMemory.Alloc((nuint)size);
#else
            return (void*)Marshal.AllocHGlobal((nint)size);
#endif
        }

        public static void free(void* memory)
        {
#if NET6_0_OR_GREATER
            NativeMemory.Free(memory);
#else
            Marshal.FreeHGlobal((nint)memory);
#endif
        }

        public static void memcpy(void* dst, void* src, nuint size) => Unsafe.CopyBlockUnaligned(dst, src, (uint)size);

        public static void memset(void* dst, byte val, nuint size) => Unsafe.InitBlockUnaligned(dst, val, (uint)size);

        [Conditional("DEBUG")]
        public static void assert<T>(T _)
        {
        }

        [Conditional("DEBUG")]
        public static void assert(IKCPCB* _)
        {
        }

        [Conditional("DEBUG")]
        public static void assert(IKCPSEG* _)
        {
        }

        public static void abort()
        {
            Environment.Exit(0);
        }
    }
}

#pragma warning disable CS8500
#pragma warning disable CS8981

using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace KcpTransport.LowLevel
{
    internal static unsafe class CMethods
    {
        public static void memcpy(void* dest, void* src, nuint n)
        {
            Unsafe.CopyBlockUnaligned(dest, src, (uint)n);
        }

        public static void* malloc(nuint size)
        {
            return NativeMemory.Alloc(size);
        }

        public static void free(void* ptr)
        {
            NativeMemory.Free(ptr);
        }

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

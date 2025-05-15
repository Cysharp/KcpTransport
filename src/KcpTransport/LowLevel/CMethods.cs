#pragma warning disable CS8500
#pragma warning disable CS8981

using IUINT32 = System.UInt32;
using IUINT16 = System.UInt16;
using IUINT8 = System.Byte;
using IINT32 = System.Int32;
using size_t = System.IntPtr;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace KcpTransport.LowLevel
{
    internal static unsafe class CMethods
    {
        public static void memcpy(void* dest, void* src, int n)
        {
            Buffer.MemoryCopy(src, dest, n, n);
        }

        public static void* malloc(size_t size)
        {
#if NET6_0_OR_GREATER
            return NativeMemory.AllocZeroed((nuint)size);
#else
             
            return (void*)Marshal.AllocHGlobal(size);
#endif
        }

        public static void free(void* ptr)
        {
#if NET6_0_OR_GREATER
            NativeMemory.Free(ptr);
#else
            Marshal.FreeHGlobal((IntPtr)ptr);
#endif
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
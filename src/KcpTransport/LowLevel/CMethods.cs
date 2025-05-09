#pragma warning disable CS8500
#pragma warning disable CS8981

using IUINT32 = uint;
using IUINT16 = ushort;
using IUINT8 = byte;
using IINT32 = int;
using size_t = nint;
using System.Runtime.InteropServices;
using System.Diagnostics;

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
            return NativeMemory.AllocZeroed((nuint)size);
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

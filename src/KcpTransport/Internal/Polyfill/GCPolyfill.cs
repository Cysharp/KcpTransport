using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace KcpTransport
{
    internal static class GCPolyfill
    {
        internal static T[] AllocateUninitializedArray<T>(int length, bool pinned = false)
        {
#if NET8_0_OR_GREATER
            return System.GC.AllocateUninitializedArray<T>(length, pinned);
#else
            return new T[length];
#endif
        }
    }
}

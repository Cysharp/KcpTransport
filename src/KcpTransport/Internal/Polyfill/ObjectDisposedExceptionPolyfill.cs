using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KcpTransport
{
    internal static class ObjectDisposedExceptionPolyfill
    {
        internal static void ThrowIf([DoesNotReturnIf(true)] bool condition, object @object)
        {
#if NET7_0_OR_GREATER
            ObjectDisposedException.ThrowIf(condition, @object);
#else
            if (condition)
            {
                throw new ObjectDisposedException(nameof(@object));
            }
#endif
        }
    }
}

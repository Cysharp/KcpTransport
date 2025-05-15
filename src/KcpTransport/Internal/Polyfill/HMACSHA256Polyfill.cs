using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace KcpTransport.Internal.Polyfill
{
    internal static class HMACSHA256Polyfill
    {


#if NET8_0_OR_GREATER
        public const int HashSizeInBits = HMACSHA256.HashSizeInBits;
        public const int HashSizeInBytes = HMACSHA256.HashSizeInBytes;
#else
        public const int HashSizeInBits = 256;
        public const int HashSizeInBytes = HashSizeInBits / 8;
#endif
        public static bool TryHashData(ReadOnlySpan<byte> key, ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
        {
#if NET8_0_OR_GREATER
            return HMACSHA256.TryHashData(key, source, destination, out bytesWritten);
#else
            using var hasher = new HMACSHA256(key.ToArray());

            var hash = hasher.ComputeHash(source.ToArray());

            if (destination.Length < hash.Length)
            {
                bytesWritten = 0;
                return false;
            }

            hash.CopyTo(destination);
            bytesWritten = hash.Length;
            return true;
#endif
        }
    }
}

using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace KcpTransport
{
    internal static class SynCookie
    {
        public static (uint Cookie, long Timestamp) Generate(ReadOnlySpan<byte> hashKey, SocketAddress remoteAddress)
        {
            var timestamp = Stopwatch.GetTimestamp();
            var hash = GenerateCore(hashKey, remoteAddress, timestamp);

            return (hash, timestamp);
        }

        public static bool Validate(ReadOnlySpan<byte> hashKey, TimeSpan timeout, uint cookie, SocketAddress remoteAddress, long timestamp)
        {
            var cookie2 = GenerateCore(hashKey, remoteAddress, timestamp);
            if (cookie != cookie2)
            {
                return false;
            }

            var elapsed = StopwatchPolyfiil.GetElapsedTime(timestamp);
            if (elapsed < timeout)
            {
                return true;
            }

            return false;
        }

        static uint GenerateCore(ReadOnlySpan<byte> hashKey, SocketAddress remoteAddress, long timestamp)
        {
            Span<byte> source = stackalloc byte[remoteAddress.Size + 8];
#if NET8_0_OR_GREATER
            remoteAddress.Buffer.Span.CopyTo(source);
#else
            for (int i = 0; i < remoteAddress.Size; i++)
            {
                source[i] = remoteAddress[i];
            }
#endif
            MemoryMarshalPolyfill.Write(source.Slice(remoteAddress.Size), timestamp);

            Span<byte> dest = stackalloc byte[HMACSHA256Polyfill.HashSizeInBytes];
            HMACSHA256Polyfill.TryHashData(hashKey, source, dest, out _);

            return MemoryMarshal.Read<uint>(dest);
        }
    }
}

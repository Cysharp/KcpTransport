using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KcpTransport.Fallbacks;

namespace KcpTransport
{
    internal static class SynCookie
    {
        public static (uint Cookie, long Timestamp) Generate(ReadOnlySpan<byte> hashKey, EndPoint remoteAddress)
        {
            return Generate(hashKey, remoteAddress.Serialize());
        }

        public static (uint Cookie, long Timestamp) Generate(ReadOnlySpan<byte> hashKey, SocketAddress remoteAddress)
        {
            var timestamp = Stopwatch.GetTimestamp();
            var hash = GenerateCore(hashKey, remoteAddress, timestamp);

            return (hash, timestamp);
        }

        public static bool Validate(ReadOnlySpan<byte> hashKey, TimeSpan timeout, uint cookie, EndPoint remoteAddress, long timestamp)
        {
            return Validate(hashKey, timeout, cookie, remoteAddress.Serialize(), timestamp);
        }

        public static bool Validate(ReadOnlySpan<byte> hashKey, TimeSpan timeout, uint cookie, SocketAddress remoteAddress, long timestamp)
        {
            var cookie2 = GenerateCore(hashKey, remoteAddress, timestamp);
            if (cookie != cookie2)
            {
                return false;
            }

            var elapsed = StopwatchFallback.GetElapsedTime(timestamp);
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
            for (int i = 0; i < remoteAddress.Size; ++i)
                source[i] = remoteAddress[i];
#endif
            MemoryMarshalFallback.Write(source.Slice(remoteAddress.Size), timestamp);

            Span<byte> dest = stackalloc byte[32];
            HMACSHA256Fallback.TryHashData(hashKey, source, dest, out _);

            return MemoryMarshal.Read<uint>(dest);
        }
    }
}

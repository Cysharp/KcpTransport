using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KcpTransport.Fallbacks;

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

            var elapsed = Stopwatch.GetElapsedTime(timestamp);
            if (elapsed < timeout)
            {
                return true;
            }

            return false;
        }

        static uint GenerateCore(ReadOnlySpan<byte> hashKey, SocketAddress remoteAddress, long timestamp)
        {
            Span<byte> source = stackalloc byte[remoteAddress.Size + 8];

            remoteAddress.Buffer.Span.CopyTo(source);
            MemoryMarshalFallback.Write(source.Slice(remoteAddress.Size), timestamp);

            Span<byte> dest = stackalloc byte[HMACSHA256.HashSizeInBytes];
            HMACSHA256Fallback.TryHashData(hashKey, source, dest, out _);

            return MemoryMarshal.Read<uint>(dest);
        }
    }
}

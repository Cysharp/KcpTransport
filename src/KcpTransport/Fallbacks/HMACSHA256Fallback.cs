using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace KcpTransport.Fallbacks
{
    public static unsafe class HMACSHA256Fallback
    {
        public static bool TryHashData(ReadOnlySpan<byte> key, ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
        {
#if NET6_0_OR_GREATER
            return HMACSHA256.TryHashData(key, source, destination, out bytesWritten);
#else
            if (destination.Length < 32)
            {
                bytesWritten = 0;
                return false;
            }

            fixed (byte* keyBuffer = &MemoryMarshal.GetReference(key))
            {
                fixed (byte* sourceBuffer = &MemoryMarshal.GetReference(source))
                {
                    fixed (byte* destinationBuffer = &MemoryMarshal.GetReference(destination))
                    {
                        hmac_sha256_hash(keyBuffer, key.Length, sourceBuffer, source.Length, destinationBuffer);
                    }
                }
            }

            bytesWritten = SHA256_BLOCK_SIZE;
            return true;
#endif
        }

#if !NET6_0_OR_GREATER
        private static void hmac_sha256_hash(byte* keyBuffer, int keyBufferLength, byte* sourceBuffer, int sourceBufferLength, byte* destinationBuffer)
        {
            var k1 = stackalloc byte[SHA256_BLOCK_SIZE];

            SHA256_CTX ctx;
            sha256_init(&ctx);

            if (keyBufferLength > 64)
            {
                sha256_update(&ctx, keyBuffer, (nuint)keyBufferLength);
                sha256_final(&ctx, k1);

                keyBuffer = k1;
                keyBufferLength = SHA256_BLOCK_SIZE;
            }

            var k2 = stackalloc byte[64];

            for (var i = 0; i < keyBufferLength; i++)
                k2[i] = (byte)(54 ^ keyBuffer[i]);

            for (var i = keyBufferLength; i < 64; i++)
                k2[i] = 54;

            sha256_init(&ctx);
            sha256_update(&ctx, k2, 64);
            sha256_update(&ctx, sourceBuffer, (nuint)sourceBufferLength);
            sha256_final(&ctx, destinationBuffer);

            for (var i = 0; i < keyBufferLength; i++)
                k2[i] = (byte)(92 ^ keyBuffer[i]);

            for (var i = keyBufferLength; i < 64; i++)
                k2[i] = 92;

            sha256_init(&ctx);
            sha256_update(&ctx, k2, 64);
            sha256_update(&ctx, destinationBuffer, SHA256_BLOCK_SIZE);
            sha256_final(&ctx, destinationBuffer);
        }

        private const int SHA256_BLOCK_SIZE = 32;

        private static uint ROTLEFT(uint a, int b) => (((a) << (b)) | ((a) >> (32 - (b))));
        private static uint ROTRIGHT(uint a, int b) => (((a) >> (b)) | ((a) << (32 - (b))));

        private static uint CH(uint x, uint y, uint z) => (((x) & (y)) ^ (~(x) & (z)));
        private static uint MAJ(uint x, uint y, uint z) => (((x) & (y)) ^ ((x) & (z)) ^ ((y) & (z)));
        private static uint EP0(uint x) => (ROTRIGHT(x, 2) ^ ROTRIGHT(x, 13) ^ ROTRIGHT(x, 22));
        private static uint EP1(uint x) => (ROTRIGHT(x, 6) ^ ROTRIGHT(x, 11) ^ ROTRIGHT(x, 25));
        private static uint SIG0(uint x) => (ROTRIGHT(x, 7) ^ ROTRIGHT(x, 18) ^ ((x) >> 3));
        private static uint SIG1(uint x) => (ROTRIGHT(x, 17) ^ ROTRIGHT(x, 19) ^ ((x) >> 10));

        private static ReadOnlySpan<uint> k => new uint[64] { 0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da, 0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070, 0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2 };

        private struct SHA256_CTX
        {
            public fixed byte data[64];
            public uint datalen;
            public ulong bitlen;
            public fixed uint state[8];
        }

        private static void sha256_transform(SHA256_CTX* ctx, byte* data)
        {
            uint a, b, c, d, e, f, g, h, i, j, t1, t2;
            uint* m = stackalloc uint[64];

            for (i = 0, j = 0; i < 16; ++i, j += 4)
            {
                m[i] = ((uint)data[j + 0] << 24) |
                       ((uint)data[j + 1] << 16) |
                       ((uint)data[j + 2] << 8) |
                       ((uint)data[j + 3]);
            }

            for (; i < 64; ++i)
                m[i] = SIG1(m[i - 2]) + m[i - 7] + SIG0(m[i - 15]) + m[i - 16];

            a = ctx->state[0];
            b = ctx->state[1];
            c = ctx->state[2];
            d = ctx->state[3];
            e = ctx->state[4];
            f = ctx->state[5];
            g = ctx->state[6];
            h = ctx->state[7];

            for (i = 0; i < 64; ++i)
            {
                t1 = h + EP1(e) + CH(e, f, g) + k[(int)i] + m[i];
                t2 = EP0(a) + MAJ(a, b, c);
                h = g;
                g = f;
                f = e;
                e = d + t1;
                d = c;
                c = b;
                b = a;
                a = t1 + t2;
            }

            ctx->state[0] += a;
            ctx->state[1] += b;
            ctx->state[2] += c;
            ctx->state[3] += d;
            ctx->state[4] += e;
            ctx->state[5] += f;
            ctx->state[6] += g;
            ctx->state[7] += h;
        }

        private static void sha256_init(SHA256_CTX* ctx)
        {
            ctx->datalen = 0;
            ctx->bitlen = 0;
            ctx->state[0] = 0x6a09e667;
            ctx->state[1] = 0xbb67ae85;
            ctx->state[2] = 0x3c6ef372;
            ctx->state[3] = 0xa54ff53a;
            ctx->state[4] = 0x510e527f;
            ctx->state[5] = 0x9b05688c;
            ctx->state[6] = 0x1f83d9ab;
            ctx->state[7] = 0x5be0cd19;
        }

        private static void sha256_update(SHA256_CTX* ctx, byte* data, nuint len)
        {
            uint i;

            for (i = 0; i < len; ++i)
            {
                ctx->data[ctx->datalen] = data[i];
                ctx->datalen++;
                if (ctx->datalen == 64)
                {
                    sha256_transform(ctx, ctx->data);
                    ctx->bitlen += 512;
                    ctx->datalen = 0;
                }
            }
        }

        private static void sha256_final(SHA256_CTX* ctx, byte* hash)
        {
            uint i;

            i = ctx->datalen;

            // Pad whatever data is left in the buffer.
            if (ctx->datalen < 56)
            {
                ctx->data[i++] = 0x80;
                while (i < 56)
                    ctx->data[i++] = 0x00;
            }
            else
            {
                ctx->data[i++] = 0x80;
                while (i < 64)
                    ctx->data[i++] = 0x00;
                sha256_transform(ctx, ctx->data);
                Unsafe.InitBlockUnaligned(ctx->data, 0, 56);
            }

            // Append to the padding the total message's length in bits and transform.
            ctx->bitlen += ctx->datalen * 8;
            ctx->data[63] = (byte)ctx->bitlen;
            ctx->data[62] = (byte)(ctx->bitlen >> 8);
            ctx->data[61] = (byte)(ctx->bitlen >> 16);
            ctx->data[60] = (byte)(ctx->bitlen >> 24);
            ctx->data[59] = (byte)(ctx->bitlen >> 32);
            ctx->data[58] = (byte)(ctx->bitlen >> 40);
            ctx->data[57] = (byte)(ctx->bitlen >> 48);
            ctx->data[56] = (byte)(ctx->bitlen >> 56);
            sha256_transform(ctx, ctx->data);

            // Since this implementation uses little endian byte ordering and SHA uses big endian,
            // reverse all the bytes when copying the final state to the output hash.
            for (i = 0; i < 4; ++i)
            {
                hash[i] = (byte)((ctx->state[0] >> (int)(24 - i * 8)) & 0x000000ff);
                hash[i + 4] = (byte)((ctx->state[1] >> (int)(24 - i * 8)) & 0x000000ff);
                hash[i + 8] = (byte)((ctx->state[2] >> (int)(24 - i * 8)) & 0x000000ff);
                hash[i + 12] = (byte)((ctx->state[3] >> (int)(24 - i * 8)) & 0x000000ff);
                hash[i + 16] = (byte)((ctx->state[4] >> (int)(24 - i * 8)) & 0x000000ff);
                hash[i + 20] = (byte)((ctx->state[5] >> (int)(24 - i * 8)) & 0x000000ff);
                hash[i + 24] = (byte)((ctx->state[6] >> (int)(24 - i * 8)) & 0x000000ff);
                hash[i + 28] = (byte)((ctx->state[7] >> (int)(24 - i * 8)) & 0x000000ff);
            }
        }
#endif
    }
}

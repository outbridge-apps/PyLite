using System;
using System.Security.Cryptography;

namespace Outbridge.PyLite.Modules.Support
{
    // MD5 (RFC 1321). The framework's MD5 is a CryptoAPI provider, which a FIPS-enforcing machine refuses
    // outright; in an ASP.NET host in .NET 4.5 compatibility mode the refusal is a throw, so hashlib.md5, hmac over
    // md5 and uuid.uuid3 were engine faults there. Checksums and UUIDs are not a security use.
    internal sealed class Md5Managed : HashAlgorithm
    {
        // Per-round shift amounts and the integer parts of abs(sin(i + 1)) * 2**32 (RFC 1321 §3.4).
        private static readonly int[] S =
        {
            7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
            5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
            4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
            6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
        };

        private static readonly uint[] K =
        {
            0xd76aa478, 0xe8c7b756, 0x242070db, 0xc1bdceee, 0xf57c0faf, 0x4787c62a, 0xa8304613, 0xfd469501,
            0x698098d8, 0x8b44f7af, 0xffff5bb1, 0x895cd7be, 0x6b901122, 0xfd987193, 0xa679438e, 0x49b40821,
            0xf61e2562, 0xc040b340, 0x265e5a51, 0xe9b6c7aa, 0xd62f105d, 0x02441453, 0xd8a1e681, 0xe7d3fbc8,
            0x21e1cde6, 0xc33707d6, 0xf4d50d87, 0x455a14ed, 0xa9e3e905, 0xfcefa3f8, 0x676f02d9, 0x8d2a4c8a,
            0xfffa3942, 0x8771f681, 0x6d9d6122, 0xfde5380c, 0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70,
            0x289b7ec6, 0xeaa127fa, 0xd4ef3085, 0x04881d05, 0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665,
            0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039, 0x655b59c3, 0x8f0ccc92, 0xffeff47d, 0x85845dd1,
            0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1, 0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391,
        };

        private readonly uint[] _h = new uint[4];
        private readonly uint[] _m = new uint[16];
        private readonly byte[] _block = new byte[64];
        private int _blockLen;
        private ulong _bitCount;

        internal Md5Managed()
        {
            HashSizeValue = 128;
            Initialize();
        }

        public override void Initialize()
        {
            _h[0] = 0x67452301; _h[1] = 0xefcdab89; _h[2] = 0x98badcfe; _h[3] = 0x10325476;
            _blockLen = 0;
            _bitCount = 0;
        }

        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            _bitCount += (ulong)cbSize * 8;
            int i = ibStart;
            int end = ibStart + cbSize;
            while (i < end)
            {
                int take = Math.Min(64 - _blockLen, end - i);
                Buffer.BlockCopy(array, i, _block, _blockLen, take);
                _blockLen += take;
                i += take;
                if (_blockLen == 64)
                {
                    Compress(_block, 0);
                    _blockLen = 0;
                }
            }
        }

        // Pad: 0x80, zeros, then the 64-bit little-endian bit length in the last 8 bytes.
        protected override byte[] HashFinal()
        {
            byte[] tail = new byte[_blockLen < 56 ? 64 : 128];
            Buffer.BlockCopy(_block, 0, tail, 0, _blockLen);
            tail[_blockLen] = 0x80;
            for (int i = 0; i < 8; i++)
                tail[tail.Length - 8 + i] = (byte)(_bitCount >> (8 * i));
            for (int off = 0; off < tail.Length; off += 64)
                Compress(tail, off);

            byte[] result = new byte[16];
            for (int i = 0; i < 4; i++)
            {
                result[i * 4] = (byte)_h[i];
                result[i * 4 + 1] = (byte)(_h[i] >> 8);
                result[i * 4 + 2] = (byte)(_h[i] >> 16);
                result[i * 4 + 3] = (byte)(_h[i] >> 24);
            }
            return result;
        }

        private void Compress(byte[] b, int off)
        {
            for (int i = 0; i < 16; i++)
                _m[i] = (uint)(b[off + i * 4] | (b[off + i * 4 + 1] << 8) | (b[off + i * 4 + 2] << 16) | (b[off + i * 4 + 3] << 24));
            uint a = _h[0], bb = _h[1], c = _h[2], d = _h[3];
            for (int i = 0; i < 64; i++)
            {
                uint f;
                int g;
                if (i < 16)
                {
                    f = (bb & c) | (~bb & d);
                    g = i;
                }
                else if (i < 32)
                {
                    f = (d & bb) | (~d & c);
                    g = (5 * i + 1) & 15;
                }
                else if (i < 48)
                {
                    f = bb ^ c ^ d;
                    g = (3 * i + 5) & 15;
                }
                else
                {
                    f = c ^ (bb | ~d);
                    g = (7 * i) & 15;
                }
                uint t = d;
                d = c;
                c = bb;
                uint x = a + f + K[i] + _m[g];
                bb = bb + ((x << S[i]) | (x >> (32 - S[i])));
                a = t;
            }
            _h[0] += a;
            _h[1] += bb;
            _h[2] += c;
            _h[3] += d;
        }
    }
}

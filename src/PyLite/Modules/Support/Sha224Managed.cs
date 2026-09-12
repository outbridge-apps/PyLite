using System;
using System.Security.Cryptography;

namespace Outbridge.PyLite.Modules.Support
{
    // SHA-224 (FIPS 180-4 §6.3): the SHA-256 compression function with SHA-224's initial state, output
    // truncated to the first 28 bytes. .NET Framework ships MD5/SHA1/SHA256/SHA384/SHA512 but no
    // SHA-224, so hashlib.sha224 (and hmac over it) need this managed implementation.
    internal sealed class Sha224Managed : HashAlgorithm
    {
        // SHA-256 round constants: the first 32 bits of the fractional parts of the cube roots of the
        // first 64 primes (FIPS 180-4 §4.2.2).
        private static readonly uint[] K =
        {
            0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
            0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
            0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
            0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
            0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
            0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
            0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
            0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
        };

        private readonly uint[] _h = new uint[8];
        private readonly uint[] _w = new uint[64];
        private readonly byte[] _block = new byte[64];
        private int _blockLen;
        private ulong _bitCount;

        internal Sha224Managed()
        {
            HashSizeValue = 224;
            Initialize();
        }

        public override void Initialize()
        {
            _h[0] = 0xc1059ed8; _h[1] = 0x367cd507; _h[2] = 0x3070dd17; _h[3] = 0xf70e5939;
            _h[4] = 0xffc00b31; _h[5] = 0x68581511; _h[6] = 0x64f98fa7; _h[7] = 0xbefa4fa4;
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

        // Pad: 0x80, zeros, then the 64-bit big-endian bit length in the last 8 bytes. One extra block
        // is needed when the 0x80 and the length no longer fit together.
        protected override byte[] HashFinal()
        {
            byte[] tail = new byte[_blockLen < 56 ? 64 : 128];
            Buffer.BlockCopy(_block, 0, tail, 0, _blockLen);
            tail[_blockLen] = 0x80;
            for (int i = 0; i < 8; i++)
                tail[tail.Length - 1 - i] = (byte)(_bitCount >> (8 * i));
            for (int off = 0; off < tail.Length; off += 64)
                Compress(tail, off);

            byte[] result = new byte[28];   // SHA-224 drops the eighth word
            for (int i = 0; i < 7; i++)
            {
                result[i * 4] = (byte)(_h[i] >> 24);
                result[i * 4 + 1] = (byte)(_h[i] >> 16);
                result[i * 4 + 2] = (byte)(_h[i] >> 8);
                result[i * 4 + 3] = (byte)_h[i];
            }
            return result;
        }

        private void Compress(byte[] buf, int off)
        {
            uint[] w = _w;
            for (int t = 0; t < 16; t++)
            {
                int p = off + t * 4;
                w[t] = ((uint)buf[p] << 24) | ((uint)buf[p + 1] << 16) | ((uint)buf[p + 2] << 8) | buf[p + 3];
            }
            for (int t = 16; t < 64; t++)
                w[t] = Sigma1(w[t - 2]) + w[t - 7] + Sigma0(w[t - 15]) + w[t - 16];

            uint a = _h[0], b = _h[1], c = _h[2], d = _h[3], e = _h[4], f = _h[5], g = _h[6], h = _h[7];
            for (int t = 0; t < 64; t++)
            {
                uint t1 = h + Sum1(e) + ((e & f) ^ (~e & g)) + K[t] + w[t];
                uint t2 = Sum0(a) + ((a & b) ^ (a & c) ^ (b & c));
                h = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
            }
            _h[0] += a; _h[1] += b; _h[2] += c; _h[3] += d;
            _h[4] += e; _h[5] += f; _h[6] += g; _h[7] += h;
        }

        private static uint Rotr(uint x, int n) { return (x >> n) | (x << (32 - n)); }
        private static uint Sigma0(uint x) { return Rotr(x, 7) ^ Rotr(x, 18) ^ (x >> 3); }
        private static uint Sigma1(uint x) { return Rotr(x, 17) ^ Rotr(x, 19) ^ (x >> 10); }
        private static uint Sum0(uint x) { return Rotr(x, 2) ^ Rotr(x, 13) ^ Rotr(x, 22); }
        private static uint Sum1(uint x) { return Rotr(x, 6) ^ Rotr(x, 11) ^ Rotr(x, 25); }
    }
}

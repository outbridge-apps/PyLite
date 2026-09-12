using System;
using System.Numerics;

namespace Outbridge.PyLite.Modules.Support
{
    // Numeric/string/tuple/frozenset hash primitives. Ports of CPython
    // Objects/longobject.c::long_hash and Python/pyhash.c::_Py_HashDouble. All wrap-around
    // arithmetic is intentional and kept in `unchecked`.
    public static class NumericHash
    {
        public const long P = 2305843009213693951L;   // 2^61 - 1 (Mersenne modulus)
        private const ulong PU = 2305843009213693951UL;

        public static long FoldMinusOne(long h) { return h == -1L ? -2L : h; }

        public static long HashBool(bool b) { return b ? 1L : 0L; }

        // Exact long twin of HashBigInteger (P1 fast path): |n| % P with the sign put back, -1 -> -2.
        public static long HashLong(long n)
        {
            if (n == 0)
                return 0L;
            ulong u = n > 0 ? (ulong)n : unchecked((ulong)(-n));   // long.MinValue wraps to its own |n|
            long r = (long)(u % PU);
            if (n < 0)
                r = -r;
            if (r == -1L)
                r = -2L;
            return r;
        }

        public static long HashBigInteger(BigInteger n)
        {
            if (n.IsZero)
                return 0L;
            long r = (long)(BigInteger.Abs(n) % P);
            if (n.Sign < 0)
                r = -r;
            if (r == -1L)
                r = -2L;
            return r;
        }

        // Hash of the rational num/den (den > 0), consistent with int/float. Port of
        // Fraction.__hash__ via the Mersenne modulus: hash(Fraction(7))==hash(7)==hash(7.0).
        public static long HashRational(BigInteger num, BigInteger den)
        {
            BigInteger pBig = P;
            BigInteger dinv = BigInteger.ModPow(den % pBig, pBig - 2, pBig);
            long h;
            if (dinv.IsZero)
                h = 314159L;   // den is a multiple of P (_PyHASH_INF equivalent)
            else
                h = (long)((BigInteger.Abs(num) % pBig) * dinv % pBig);
            if (num.Sign < 0)
                h = -h;
            return FoldMinusOne(h);
        }

        public static long HashDouble(double v)
        {
            if (double.IsNaN(v))
                return 0L;
            if (double.IsPositiveInfinity(v))
                return 314159L;
            if (double.IsNegativeInfinity(v))
                return -271828L;

            int e;
            double m = FrExp(v, out e);
            int sign = 1;
            if (m < 0)
            {
                sign = -1;
                m = -m;
            }

            ulong x = 0UL;
            unchecked
            {
                while (m != 0.0)
                {
                    x = ((x << 28) & PU) | (x >> 33);
                    m *= 268435456.0; // 2^28
                    e -= 28;
                    ulong y = (ulong)m; // floor: 0 <= m < 2^28, exact
                    m -= y;
                    x += y;
                    if (x >= PU)
                        x -= PU;
                }
                e %= 61;
                if (e < 0)
                    e += 61;
                if (e != 0)
                    x = ((x << e) & PU) | (x >> (61 - e));
            }
            long r = (long)x * sign;
            if (r == -1L)
                r = -2L;
            return r;
        }

        public static long HashString(string s, uint seed)
        {
            if (s.Length == 0)
                return 0L;
            unchecked
            {
                ulong h = 14695981039346656037UL ^ ((ulong)seed * 0x9E3779B97F4A7C15UL);
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    h = (h ^ (ulong)(c & 0xFF)) * 1099511628211UL;
                    h = (h ^ (ulong)(c >> 8)) * 1099511628211UL;
                }
                long r = (long)h;
                if (r == -1L)
                    r = -2L;
                if (r == long.MinValue)
                    r = long.MinValue + 1L;   // reserve the StrValue cache sentinel (R8)
                return r;
            }
        }

        // frexp via bit surgery: v = m * 2^e with 0.5 <= |m| < 1 (or m == 0 for v == 0).
        private static double FrExp(double d, out int e)
        {
            if (d == 0.0)
            {
                e = 0;
                return d;
            }
            long bits = BitConverter.DoubleToInt64Bits(d);
            int exp = (int)((bits >> 52) & 0x7FF);
            int eBias = 0;
            if (exp == 0) // subnormal: scale up by 2^64 and recompute
            {
                d *= 18446744073709551616.0;
                bits = BitConverter.DoubleToInt64Bits(d);
                exp = (int)((bits >> 52) & 0x7FF);
                eBias = -64;
            }
            e = exp - 1022 + eBias;
            long mBits = (bits & ~(0x7FFL << 52)) | (1022L << 52);
            return BitConverter.Int64BitsToDouble(mBits);
        }
    }
}

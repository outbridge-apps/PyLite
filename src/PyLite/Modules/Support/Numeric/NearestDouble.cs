using System;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Outbridge.PyLite.Modules.Support
{
    // The double nearest to a decimal text or an integer, ties to even: what CPython answers (dtoa.c for
    // text, PyLong_AsDouble for ints). .NET Framework misses by an ulp: double.Parse on a few texts in a
    // thousand of 16+ digits and on subnormals, (double)BigInteger by truncating past 64 bits.
    // A text takes one of three tiers: Clinger's exact fast path (up to 15 digits times 10^-22..10^22),
    // Eisel-Lemire on a 128-bit power of five (up to 19 digits; fast_float's compute_float, Lemire et al.,
    // MIT/Apache-2.0), or, when more than 19 digits leave the answer between two doubles, exact integers.
    internal static class NearestDouble
    {
        private const int MaxKeptDigits = 19;
        private const int MaxExactDigits = 800;   // 767 decide any halfway case; the rest is a sticky digit

        private static readonly double[] ExactPow10 =
        {
            1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11,
            1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
        };

        // [sign] digits [. digits] [(e|E) [sign] digits] spanning all of s[start..end), with a digit on at
        // least one side of the point. No underscores or spaces: callers strip what their grammar allows.
        // Past the float range the answer is an infinity (no exception), below it a zero.
        internal static bool TryParse(string s, int start, int end, out double value)
        {
            value = 0.0;
            int i = start;
            bool neg = false;
            if (i < end && (s[i] == '+' || s[i] == '-'))
            {
                neg = s[i] == '-';
                i++;
            }
            // w keeps the first 19 significant digits (leading zeros are not significant); q is the power of
            // ten of w's last digit; truncated says a non-zero digit followed
            ulong w = 0;
            int kept = 0, fracDigits = 0;
            long q = 0;
            bool truncated = false;
            int mantStart = i;
            for (; i < end; i++)
            {
                uint d = (uint)(s[i] - '0');
                if (d > 9)
                    break;
                if (kept < MaxKeptDigits)
                {
                    if (w != 0 || d != 0)
                    {
                        w = w * 10 + d;
                        kept++;
                    }
                }
                else
                {
                    truncated |= d != 0;
                    q++;
                }
            }
            int intDigits = i - mantStart;
            if (i < end && s[i] == '.')
            {
                int fracStart = ++i;
                for (; i < end; i++)
                {
                    uint d = (uint)(s[i] - '0');
                    if (d > 9)
                        break;
                    if (kept < MaxKeptDigits)
                    {
                        if (w != 0 || d != 0)
                        {
                            w = w * 10 + d;
                            kept++;
                        }
                        q--;
                    }
                    else
                        truncated |= d != 0;
                }
                fracDigits = i - fracStart;
            }
            if (intDigits + fracDigits == 0)
                return false;
            int mantEnd = i;
            long exp = 0;
            if (i < end && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                bool eneg = false;
                if (i < end && (s[i] == '+' || s[i] == '-'))
                {
                    eneg = s[i] == '-';
                    i++;
                }
                if (i >= end || !IsDigit(s[i]))
                    return false;
                for (; i < end && IsDigit(s[i]); i++)
                {
                    if (exp < 1000000000L)   // saturated: past +-10^9 the answer is 0 or inf whatever the digits
                        exp = exp * 10 + (s[i] - '0');
                }
                if (eneg)
                    exp = -exp;
            }
            if (i != end)
                return false;
            q += exp;

            long bits;
            if (w == 0)
                bits = 0;
            else if (!truncated)
                bits = Compute(w, q);
            else
            {
                // The digits past the 19th put the value between w and w + 1 at this power: when both ends
                // give the same double, so does the text. Otherwise only the full digits decide.
                bits = Compute(w, q);
                if (Compute(w + 1, q) != bits)
                    bits = Exact(s, mantStart, mantEnd, exp - fracDigits);
            }
            value = BitConverter.Int64BitsToDouble(bits);
            if (neg)
                value = -value;
            return true;
        }

        internal static bool TryParse(string s, out double value)
        {
            return TryParse(s, 0, s.Length, out value);
        }

        // float(Decimal): CPython converts the decimal's text, which rounds once.
        internal static double FromDecimal(decimal d)
        {
            if (d == 0m)
                return (decimal.GetBits(d)[3] & int.MinValue) != 0 ? -0.0 : 0.0;   // Decimal('-0') -> -0.0
            string text = d.ToString(CultureInfo.InvariantCulture);
            double v;
            return TryParse(text, out v) ? v : (double)d;
        }

        // int -> float, ties to even. Up to 64 bits the hardware conversion already rounds so.
        internal static double FromBigInteger(BigInteger v)
        {
            if (v >= long.MinValue && v <= long.MaxValue)
                return (long)v;
            bool neg = v.Sign < 0;
            BigInteger a = neg ? -v : v;
            int shift = BitLength(a) - 64;
            ulong top = (ulong)(a >> shift);                       // bit 63 set
            bool sticky = !(a & ((BigInteger.One << shift) - 1)).IsZero;
            ulong m = top >> 11;                                   // 53 bits
            ulong rest = top & 0x7FF;
            if (rest > 0x400 || (rest == 0x400 && (sticky || (m & 1) != 0)))
                m++;
            int e2 = shift + 11;                                   // value = m * 2^e2
            if (m == 1UL << 53)
            {
                m >>= 1;
                e2++;
            }
            long biased = e2 + 52 + 1023;
            double r = biased >= 2047
                ? double.PositiveInfinity
                : BitConverter.Int64BitsToDouble((biased << 52) | (long)(m & ((1UL << 52) - 1)));
            return neg ? -r : r;
        }

        private static bool IsDigit(char c) { return c >= '0' && c <= '9'; }

        // The bits of the double nearest w * 10^q (w > 0).
        internal static long Compute(ulong w, long q)
        {
            if (w <= 1UL << 53 && q >= -22 && q <= 22)
            {
                // Clinger: both operands are exact, so the one operation rounds once
                double d = q >= 0 ? (long)w * ExactPow10[q] : (long)w / ExactPow10[-q];
                return BitConverter.DoubleToInt64Bits(d);
            }
            return EiselLemire(w, q);
        }

        // ---- Eisel-Lemire (fast_float compute_float<binary64>) ----

        private const int SmallestPowerOfTen = -342;
        private const int LargestPowerOfTen = 308;
        private const int MantissaBits = 52;
        private const int MinimumExponent = -1023;
        private const int InfinitePower = 0x7FF;

        private static long EiselLemire(ulong w, long q)
        {
            if (q < SmallestPowerOfTen)
                return 0;
            if (q > LargestPowerOfTen)
                return (long)InfinitePower << MantissaBits;
            int lz = LeadingZeros(w);
            w <<= lz;
            ulong low;
            ulong high = ProductApproximation((int)q, w, out low);
            int upperbit = (int)(high >> 63);
            int shift = upperbit + 64 - MantissaBits - 3;
            ulong mantissa = high >> shift;
            int power2 = Power((int)q) + upperbit - lz - MinimumExponent;
            if (power2 <= 0)
            {
                // subnormal (with at most 19 digits a subnormal is never exactly halfway)
                if (-power2 + 1 >= 64)
                    return 0;
                mantissa >>= -power2 + 1;
                mantissa += mantissa & 1;
                mantissa >>= 1;
                // rounding may carry into the smallest normal
                power2 = mantissa < 1UL << MantissaBits ? 0 : 1;
                return ((long)power2 << MantissaBits) | (long)(mantissa & ((1UL << MantissaBits) - 1));
            }
            // exactly halfway is possible only where 5^q fits one word; then round to even
            if (low <= 1 && q >= -4 && q <= 23 && (mantissa & 3) == 1 && (mantissa << shift) == high)
                mantissa &= ~1UL;
            mantissa += mantissa & 1;
            mantissa >>= 1;
            if (mantissa >= 2UL << MantissaBits)
            {
                mantissa = 1UL << MantissaBits;
                power2++;
            }
            mantissa &= ~(1UL << MantissaBits);
            if (power2 >= InfinitePower)
                return (long)InfinitePower << MantissaBits;
            return ((long)power2 << MantissaBits) | (long)mantissa;
        }

        // floor(log2(5^q)) + q + 63 for q in (-400, 350); the bias keeps the shifted value non-negative.
        private static int Power(int q)
        {
            return (((152170 + 65536) * q + (2048 * 65536)) >> 16) - 2048 + 63;
        }

        // w * 5^q to 55 significant bits plus the bits below, from the 128-bit truncated power.
        private static ulong ProductApproximation(int q, ulong w, out ulong low)
        {
            ulong[] t = Pow5Table.Words;
            int index = 2 * (q - SmallestPowerOfTen);
            ulong high = Multiply(w, t[index], out low);
            const ulong precisionMask = 0xFFFFFFFFFFFFFFFFUL >> (MantissaBits + 3);
            if ((high & precisionMask) == precisionMask)
            {
                ulong secondLow;
                ulong secondHigh = Multiply(w, t[index + 1], out secondLow);
                low += secondHigh;
                if (secondHigh > low)
                    high++;
            }
            return high;
        }

        // The full 128-bit product of two 64-bit words.
        private static ulong Multiply(ulong a, ulong b, out ulong low)
        {
            ulong aLo = (uint)a, aHi = a >> 32, bLo = (uint)b, bHi = b >> 32;
            ulong ll = aLo * bLo, lh = aLo * bHi, hl = aHi * bLo, hh = aHi * bHi;
            ulong mid = (ll >> 32) + (uint)lh + (uint)hl;
            low = (mid << 32) | (uint)ll;
            return hh + (lh >> 32) + (hl >> 32) + (mid >> 32);
        }

        private static int LeadingZeros(ulong x)
        {
            int n = 0;
            if (x >> 32 == 0) { n += 32; x <<= 32; }
            if (x >> 48 == 0) { n += 16; x <<= 16; }
            if (x >> 56 == 0) { n += 8; x <<= 8; }
            if (x >> 60 == 0) { n += 4; x <<= 4; }
            if (x >> 62 == 0) { n += 2; x <<= 2; }
            if (x >> 63 == 0) { n += 1; }
            return n;
        }

        // fast_float's power_of_five_128: for q in [-342, 308] the high and low word of 5^q normalized to
        // 128 bits (truncated; a negative q as the reciprocal rounded up), built once from exact integers.
        private static class Pow5Table
        {
            internal static readonly ulong[] Words = Build();

            private static ulong[] Build()
            {
                var t = new ulong[2 * (LargestPowerOfTen - SmallestPowerOfTen + 1)];
                BigInteger two128 = BigInteger.One << 128;
                int at = 0;
                for (int q = SmallestPowerOfTen; q < 0; q++)
                {
                    BigInteger p5 = BigInteger.Pow(5, -q);
                    int z = BitLength(p5 - 1);   // the least z with 2^z >= 5^-q
                    BigInteger c = (BigInteger.One << (q >= -27 ? z + 127 : 2 * z + 128)) / p5 + 1;
                    while (c >= two128)
                        c >>= 1;
                    Put(t, ref at, c);
                }
                for (int q = 0; q <= LargestPowerOfTen; q++)
                {
                    BigInteger p5 = BigInteger.Pow(5, q);
                    int len = BitLength(p5);
                    Put(t, ref at, len <= 128 ? p5 << (128 - len) : p5 >> (len - 128));
                }
                return t;
            }

            private static void Put(ulong[] t, ref int at, BigInteger c)
            {
                t[at++] = (ulong)(c >> 64);
                t[at++] = (ulong)(c & ulong.MaxValue);
            }
        }

        // ---- exact: more than 19 digits and the answer still between two doubles ----

        private static long Exact(string s, int mantStart, int mantEnd, long exp10)
        {
            // the significant digits (at most MaxExactDigits, then a sticky 1) and the power of ten of the last
            var sb = new StringBuilder(Math.Min(mantEnd - mantStart, MaxExactDigits + 1));
            long dropped = 0;
            bool sticky = false;
            for (int i = mantStart; i < mantEnd; i++)
            {
                char c = s[i];
                if (c == '.' || (c == '0' && sb.Length == 0))
                    continue;
                if (sb.Length < MaxExactDigits)
                    sb.Append(c);
                else
                {
                    dropped++;
                    sticky |= c != '0';
                }
            }
            if (sticky)
            {
                sb.Append('1');
                dropped--;
            }
            // the decimal point sat after the integer digits; exp10 already counts every fraction digit
            long e = exp10 + dropped;
            int n = sb.Length;
            if (n + e > 310)
                return (long)InfinitePower << MantissaBits;
            if (n + e < -330)
                return 0;
            BigInteger num = BigInteger.Parse(sb.ToString(), CultureInfo.InvariantCulture);
            BigInteger den = BigInteger.One;
            if (e >= 0)
                num *= BigInteger.Pow(10, (int)e);
            else
                den = BigInteger.Pow(10, (int)-e);
            return Nearest(num, den);
        }

        // The bits of the double nearest num / den (both positive), ties to even.
        private static long Nearest(BigInteger num, BigInteger den)
        {
            int k = BitLength(num) - BitLength(den) - 53;   // num/den / 2^k lies in [2^52, 2^54)
            BigInteger r;
            BigInteger m = Divide(num, den, k, out r);
            if (m >= BigInteger.One << 53)
            {
                k++;
                m = Divide(num, den, k, out r);
            }
            if (k < -1074)   // subnormal: the last bit is worth 2^-1074
            {
                k = -1074;
                m = Divide(num, den, k, out r);
            }
            BigInteger divisor = k >= 0 ? den << k : den;
            int half = (r << 1).CompareTo(divisor);
            if (half > 0 || (half == 0 && !m.IsEven))
                m++;
            if (m == BigInteger.One << 53)
            {
                m >>= 1;
                k++;
            }
            if (k > 1023 - 52)
                return (long)InfinitePower << MantissaBits;
            ulong mant = (ulong)m;
            if (mant < 1UL << MantissaBits)
                return (long)mant;   // subnormal, k == -1074
            return ((long)(k + 52 + 1023) << MantissaBits) | (long)(mant & ((1UL << MantissaBits) - 1));
        }

        // floor(num / den / 2^k) and the remainder against the scaled divisor.
        private static BigInteger Divide(BigInteger num, BigInteger den, int k, out BigInteger rem)
        {
            return k >= 0
                ? BigInteger.DivRem(num, den << k, out rem)
                : BigInteger.DivRem(num << -k, den, out rem);
        }

        private static int BitLength(BigInteger v)
        {
            return (int)Runtime.Values.NumericOps.BitLength(v);
        }
    }
}

using System;
using System.Globalization;
using System.Numerics;

namespace Outbridge.PyLite.Modules.Support
{
    // The shortest decimal digits that read back as the double, the nearest of them when two of one length
    // do (half-way: the even last digit), as CPython's repr (dtoa mode 0). .NET Framework's 17-digit
    // rendering is exact, but it rounds that string again for fewer digits, so a 16-digit rendering is a
    // unit off a few times in a hundred. Each shorter length is therefore tried from the 17 digits both
    // ways, truncated and one unit up, and checked with NearestDouble; a normal double needs at least 15.
    internal static class ReprDigits
    {
        private static readonly ulong[] Pow10 =
        {
            1UL, 10UL, 100UL, 1000UL, 10000UL, 100000UL, 1000000UL, 10000000UL, 100000000UL, 1000000000UL,
            10000000000UL, 100000000000UL, 1000000000000UL, 10000000000000UL, 100000000000000UL,
            1000000000000000UL, 10000000000000000UL, 100000000000000000UL,
        };

        // d finite and non-zero. The digits (no trailing zeros) and the decimal exponent of the first.
        internal static string Shortest(double d, out bool neg, out int decExp)
        {
            return Shortest(d, out neg, out decExp, IntPtr.Size == 4);
        }

        // exact: the x86 path (tests run it on x64 too).
        internal static string Shortest(double d, out bool neg, out int decExp, bool exact)
        {
            long bits = BitConverter.DoubleToInt64Bits(d);
            neg = bits < 0;
            long abs = bits & long.MaxValue;
            string quick = ShortDecimal(abs, out decExp);
            if (quick != null)
                return quick;
            ulong d17;
            int e10;
            if (exact)
                Exact17(abs, out d17, out e10);   // x86 .NET Framework: "E16" was wrong in the 17th digit for 0.5% of doubles
            else
            {
                string sci = BitConverter.Int64BitsToDouble(abs).ToString("E16", CultureInfo.InvariantCulture);
                d17 = (ulong)(sci[0] - '0');
                for (int i = 2; i < 18; i++)   // "d.dddddddddddddddd" then "E+ddd"
                    d17 = d17 * 10 + (ulong)(sci[i] - '0');
                e10 = int.Parse(sci.Substring(19), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            }
            // a k <= 15 digit text of a normal double is its 15-digit one padded with zeros (half an ulp is far
            // below that spacing); a subnormal's ulp is not small against its value, so every length is tried
            for (int p = abs < 0x0010000000000000L ? 1 : 15; p <= 16; p++)
            {
                ulong unit = Pow10[17 - p];
                ulong t = d17 / unit;
                int q = e10 - (p - 1);
                bool down = NearestDouble.Compute(t, q) == abs;
                bool up = NearestDouble.Compute(t + 1, q) == abs;
                if (!down && !up)
                    continue;
                if (down && up)
                {
                    ulong tail = d17 % unit;
                    int c = tail.CompareTo(unit / 2);
                    if (c == 0)
                        c = CompareToMidpoint(abs, t, q);   // the 17 digits end in 5: they were rounded
                    up = c > 0 || (c == 0 && (t & 1) != 0);
                }
                return Render(up ? t + 1 : t, p, e10, out decExp);
            }
            if ((d17 & 1) != 0 && IsTieBelow(abs, d17, e10 - 16))
                d17--;   // .NET rounds a tie away from zero, CPython to the even digit
            return Render(d17, 17, e10, out decExp);
        }

        // What x64 .NET's "E16" gives: the 17 significant digits of the exact value, a tie rounded away from zero (the
        // caller corrects that one), and the decimal exponent of the first. abs is a finite, non-zero double's bits.
        internal static void Exact17(long abs, out ulong d17, out int e10)
        {
            int be = (int)(abs >> 52);
            BigInteger m = abs & 0xFFFFFFFFFFFFFL;
            if (be == 0)
                be = 1;   // subnormal
            else
                m += BigInteger.One << 52;
            be -= 1075;   // value = m * 2^be
            e10 = (int)Math.Floor(Math.Log10(BitConverter.Int64BitsToDouble(abs)));
            while (true)
            {
                BigInteger num = m, den = BigInteger.One;
                if (be >= 0)
                    num <<= be;
                else
                    den <<= -be;
                int k = 16 - e10;
                if (k >= 0)
                    num *= BigInteger.Pow(10, k);
                else
                    den *= BigInteger.Pow(10, -k);
                BigInteger rem;
                BigInteger q = BigInteger.DivRem(num, den, out rem);
                if (q < Pow10[16])
                {
                    e10--;   // the estimate was one too high
                    continue;
                }
                if (q >= Pow10[17])
                {
                    e10++;
                    continue;
                }
                if (rem * 2 >= den)
                    q += 1;
                if (q == Pow10[17])
                {
                    q = Pow10[16];
                    e10++;
                }
                d17 = (ulong)q;
                return;
            }
        }

        // A short decimal (12.5, 0.1, 99.99): the first k <= 8 where the integer nearest |d| * 10^k reads back as
        // the double, without the 17-digit rendering. At most 15 digits, so no other 15-digit text reads back and
        // the loop above would answer the same digits; null otherwise.
        private static string ShortDecimal(long abs, out int decExp)
        {
            decExp = 0;
            double a = BitConverter.Int64BitsToDouble(abs);
            if (a < 1e-4 || a >= 1e15)
                return null;
            for (int k = 0; k <= 8; k++)
            {
                double scaled = a * Pow10Double[k];
                if (scaled >= 1e15)
                    return null;
                double r = Math.Round(scaled);
                if (r < 1 || NearestDouble.Compute((ulong)r, -k) != abs)
                    continue;
                string s = ((ulong)r).ToString(CultureInfo.InvariantCulture);
                decExp = s.Length - 1 - k;
                return s.TrimEnd('0');
            }
            return null;
        }

        private static readonly double[] Pow10Double = { 1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8 };

        // Whether the double is exactly (d17 - 1/2) * 10^q: m * 2^(e+1) == (2*d17 - 1) * 10^q. The right side is
        // odd times 10^q, so q < 0 (an integer past 2^53 is even), and then m * 5^-q * 2^(e+1-q) == 2*d17 - 1
        // with 5^-q at most 2*d17 < 2^58.
        private static bool IsTieBelow(long abs, ulong d17, int q)
        {
            if (q >= 0 || q < -25)
                return false;
            long field = abs >> 52;
            ulong m = (ulong)(abs & 0xFFFFFFFFFFFFFL);
            if (field != 0)
                m |= 1UL << 52;
            int e = field == 0 ? -1074 : (int)field - 1075;
            int shift = e + 1 - q;
            if (shift > 0 || shift <= -64 || (m & ((1UL << -shift) - 1)) != 0)
                return false;   // an even left side, or bits below the point
            ulong odd = 2 * d17 - 1;
            ulong pow5 = Pow5(-q);
            return odd % pow5 == 0 && odd / pow5 == m >> -shift;
        }

        private static ulong Pow5(int k)
        {
            ulong r = 1;
            for (int i = 0; i < k; i++)
                r *= 5;
            return r;
        }

        private static string Render(ulong digits, int p, int e10, out int decExp)
        {
            string s = digits.ToString(CultureInfo.InvariantCulture);
            decExp = e10 + (s.Length - p);   // one unit up from 99..9 carries into a new first digit
            return s.TrimEnd('0');
        }

        // The sign of the double (bits abs) minus (t + 1/2) * 10^q, exactly: 2d against (2t + 1) * 10^q.
        private static int CompareToMidpoint(long abs, ulong t, int q)
        {
            long field = abs >> 52;
            long frac = abs & 0xFFFFFFFFFFFFFL;
            BigInteger left = field == 0 ? frac : frac | (1L << 52);
            int e = field == 0 ? -1074 : (int)field - 1075;
            BigInteger right = new BigInteger(t) * 2 + 1;
            if (q >= 0)
                right *= BigInteger.Pow(5, q);
            else
                left *= BigInteger.Pow(5, -q);
            int shift = e + 1 - q;
            if (shift >= 0)
                left <<= shift;
            else
                right <<= -shift;
            return left.CompareTo(right);
        }
    }
}

using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Modules.Support
{
    // System.Decimal <-> (unscaled mantissa, scale, sign). The scale is Python's
    // -exponent (always <= 0 in this dialect). All values are exact; no NaN/Inf.
    //
    // The arithmetic path reads the parts through an overlay rather than decimal.GetBits, which allocates
    // an array per call, and rounds them as three 32-bit words rather than through Math.Round - see
    // Read / FitsPrec / TryRound. The BigInteger members remain for the rare and the exact.
    internal static class DecimalBits
    {
        public static readonly BigInteger MaxMantissa = (BigInteger.One << 96) - 1;

        // The bit layout of System.Decimal is fixed by its OLE DECIMAL ancestry - flags, hi, lo, mid - so
        // an explicit-layout overlay yields the parts without a call or an allocation. The layout is
        // checked once at start-up; if it ever failed to match, every reader would use GetBits instead.
        [StructLayout(LayoutKind.Explicit)]
        private struct Overlay
        {
            [FieldOffset(0)] public decimal Value;
            [FieldOffset(0)] public int Flags;
            [FieldOffset(4)] public int Hi;
            [FieldOffset(8)] public int Lo;
            [FieldOffset(12)] public int Mid;
        }

        private static readonly bool OverlayOk = ProbeOverlay();

        internal static bool UsesOverlay { get { return OverlayOk; } }

        private static bool ProbeOverlay()
        {
            var u = new Overlay { Value = new decimal(7, 11, 13, true, 5) };
            return u.Lo == 7 && u.Mid == 11 && u.Hi == 13 && u.Flags < 0 && ((u.Flags >> 16) & 0xFF) == 5;
        }

        public static void Read(decimal d, out uint lo, out uint mid, out uint hi, out int scale, out bool negative)
        {
            if (OverlayOk)
            {
                var u = new Overlay { Value = d };
                lo = unchecked((uint)u.Lo);
                mid = unchecked((uint)u.Mid);
                hi = unchecked((uint)u.Hi);
                scale = (u.Flags >> 16) & 0xFF;
                negative = u.Flags < 0;
                return;
            }
            int[] b = decimal.GetBits(d);
            lo = unchecked((uint)b[0]);
            mid = unchecked((uint)b[1]);
            hi = unchecked((uint)b[2]);
            scale = (b[3] >> 16) & 0xFF;
            negative = b[3] < 0;
        }

        public static int ScaleOf(decimal d)
        {
            uint lo, mid, hi;
            int scale;
            bool neg;
            Read(d, out lo, out mid, out hi, out scale, out neg);
            return scale;
        }

        // How many powers of ten divide the mantissa, looking no further than `max` (the caller only
        // ever needs enough to raise an exponent to its ideal).
        public static int TrailingZeros(decimal d, int max)
        {
            uint lo, mid, hi;
            int scale;
            bool neg;
            Read(d, out lo, out mid, out hi, out scale, out neg);
            if ((lo | mid | hi) == 0)
                return max;
            int n = 0;
            while (n < max)
            {
                uint l = lo, m = mid, h = hi;
                if (DivRem(ref l, ref m, ref h, 10) != 0)
                    break;
                lo = l;
                mid = m;
                hi = h;
                n++;
            }
            return n;
        }

        public static bool IsZero(decimal d)
        {
            uint lo, mid, hi;
            int scale;
            bool neg;
            Read(d, out lo, out mid, out hi, out scale, out neg);
            return (lo | mid | hi) == 0;
        }

        public static void Unpack(decimal d, out BigInteger mantissa, out int scale, out bool negative)
        {
            uint lo, mid, hi;
            Read(d, out lo, out mid, out hi, out scale, out negative);
            mantissa = ((BigInteger)hi << 64) | ((BigInteger)mid << 32) | lo;
        }

        // 10^0 .. 10^28 as the three words of a mantissa; 10^28 is the largest power that fits.
        private static readonly uint[] P10Lo = new uint[29];
        private static readonly uint[] P10Mid = new uint[29];
        private static readonly uint[] P10Hi = new uint[29];

        // 10^0 .. 10^29 - a 96-bit mantissa never needs more.
        private static readonly BigInteger[] Pow10 = new BigInteger[30];

        static DecimalBits()
        {
            BigInteger p = BigInteger.One;
            for (int i = 0; i < 30; i++)
            {
                Pow10[i] = p;
                if (i < 29)
                {
                    P10Lo[i] = (uint)(p & 0xFFFFFFFF);
                    P10Mid[i] = (uint)((p >> 32) & 0xFFFFFFFF);
                    P10Hi[i] = (uint)((p >> 64) & 0xFFFFFFFF);
                }
                p *= 10;
            }
        }

        // A mantissa below 10^prec has at most prec significant digits: three word compares, nothing else.
        public static bool FitsPrec(uint lo, uint mid, uint hi, int prec)
        {
            return Less(lo, mid, hi, P10Lo[prec], P10Mid[prec], P10Hi[prec]);
        }

        public static bool FitsPrec(decimal v, int prec)
        {
            uint lo, mid, hi;
            int scale;
            bool neg;
            Read(v, out lo, out mid, out hi, out scale, out neg);
            return FitsPrec(lo, mid, hi, prec);
        }

        private static bool Less(uint lo, uint mid, uint hi, uint l, uint m, uint h)
        {
            if (hi != h)
                return hi < h;
            if (mid != m)
                return mid < m;
            return lo < l;
        }

        // Digit count of a mantissa already known to exceed prec digits, so the search starts above it.
        public static int Digits(uint lo, uint mid, uint hi, int prec)
        {
            int d = prec + 1;
            while (d < 29 && !Less(lo, mid, hi, P10Lo[d], P10Mid[d], P10Hi[d]))
                d++;
            return d;
        }

        // Drops `drop` digits from the mantissa with the given mode, in 32-bit steps. Covers every divisor
        // that fits a uint (drop <= 9, which is every default-precision case); the caller takes the
        // BigInteger route for wider drops and for a carry that spills past 96 bits.
        public static bool TryRound(ref uint lo, ref uint mid, ref uint hi, int drop, bool negative, RoundingMode mode)
        {
            if (drop > 9)
                return false;
            uint divisor = P10Lo[drop];
            uint rem = DivRem(ref lo, ref mid, ref hi, divisor);
            if (rem == 0)
                return true;
            bool up;
            switch (mode)
            {
                case RoundingMode.Down:
                    up = false;
                    break;
                case RoundingMode.Up:
                    up = true;
                    break;
                case RoundingMode.Floor:
                    up = negative;
                    break;
                case RoundingMode.Ceiling:
                    up = !negative;
                    break;
                case RoundingMode.HalfUp:
                    up = 2UL * rem >= divisor;
                    break;
                case RoundingMode.HalfDown:
                    up = 2UL * rem > divisor;
                    break;
                case RoundingMode.ZeroFiveUp:
                    uint last = LastDigit(lo, mid, hi);
                    up = last == 0 || last == 5;
                    break;
                default:   // HalfEven
                    ulong twice = 2UL * rem;
                    up = twice > divisor || (twice == divisor && (lo & 1) != 0);
                    break;
            }
            if (!up)
                return true;
            if (++lo == 0 && ++mid == 0 && ++hi == 0)
                return false;   // carried out of 96 bits
            return true;
        }

        // Quotient stays in the words, the remainder is returned; the divisor must fit a uint.
        public static uint DivRem(ref uint lo, ref uint mid, ref uint hi, uint divisor)
        {
            ulong t = hi;
            hi = (uint)(t / divisor);
            t = ((t % divisor) << 32) | mid;
            mid = (uint)(t / divisor);
            t = ((t % divisor) << 32) | lo;
            lo = (uint)(t / divisor);
            return (uint)(t % divisor);
        }

        private static uint LastDigit(uint lo, uint mid, uint hi)
        {
            return DivRem(ref lo, ref mid, ref hi, 10);
        }

        // The backend has no exponent below 1e-28, so a small enough value lands exactly on zero. CPython
        // has no floor there, so answering 0 would be silently wrong: every path that can reach it raises.
        public static ScriptException Underflow(EvalContext ctx)
        {
            return Raise.Make(ctx, PyExceptionTypes.DecimalUnderflow,
                "result is below the smallest magnitude this backend can hold (1e-28)");
        }

        public static bool TryPack(BigInteger mantissa, int scale, bool negative, out decimal result)
        {
            result = 0m;
            if (mantissa.Sign < 0 || mantissa > MaxMantissa || scale < 0 || scale > 28)
                return false;
            int lo = unchecked((int)(uint)(mantissa & 0xFFFFFFFF));
            int mid = unchecked((int)(uint)((mantissa >> 32) & 0xFFFFFFFF));
            int hi = unchecked((int)(uint)((mantissa >> 64) & 0xFFFFFFFF));
            int flags = (scale << 16) | (negative ? unchecked((int)0x80000000) : 0);
            result = new decimal(new[] { lo, mid, hi, flags });
            return true;
        }

        // Pack or raise decimal.Overflow if the mantissa does not fit into 96 bits.
        public static decimal Pack(EvalContext ctx, BigInteger mantissa, int scale, bool negative)
        {
            decimal r;
            if (!TryPack(mantissa, scale, negative, out r))
                throw Raise.Make(ctx, PyExceptionTypes.DecimalOverflow, "[<class 'decimal.Overflow'>]");
            return r;
        }

        // Half-even round of m (>=0) from fromScale down to toScale (toScale < fromScale).
        public static BigInteger RoundUnscaledToScale(BigInteger m, int fromScale, int toScale)
        {
            int drop = fromScale - toScale;
            BigInteger divisor = BigInteger.Pow(10, drop);
            BigInteger r;
            BigInteger q = BigInteger.DivRem(m, divisor, out r);
            BigInteger twice = r * 2;
            if (twice > divisor || (twice == divisor && !q.IsEven))
                q += 1;
            return q;
        }

        public static int SignificantDigits(BigInteger m)
        {
            if (m.IsZero)
                return 1;
            BigInteger a = BigInteger.Abs(m);
            int i = System.Array.BinarySearch(Pow10, a);
            if (i >= 0)
                return i + 1;
            i = ~i;
            return i < Pow10.Length ? i : a.ToString(CultureInfo.InvariantCulture).Length;
        }
    }
}

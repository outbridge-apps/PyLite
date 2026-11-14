using System;
using System.Numerics;

namespace Outbridge.PyLite.Runtime.Values
{
    // Big-int operations bounded by the deadline. net472's BigInteger runs a call to its end once started (a
    // 1M-bit gcd, division or modular squaring takes 1-2 s), so one past LongWork word products (~10 ms) goes to
    // BigKernel instead, which checks the deadline every few ms on the run's own thread: an abort ends the
    // operation with the run, and nothing computes on after it. Callers keep their own MaxIntBits gates and
    // charges.
    internal static class BigOps
    {
        internal const long LongWork = 1L << 22;

        // Below LongWork the kernel still takes what it computes faster than net472, its conversions included
        // (measured, in 64-bit words): a product from 64-word factors (Karatsuba), a square from 32 words (half
        // the products), a division by a 32-word divisor (algorithm D, about 3x), gcd and decimal text from
        // 128 words, a power from a 256-word result, a modular power from 1024 word products of work
        // (bits(e) * words(m)^2) on a modulus of 4 words or more (2-4x). Smaller ones lose to the conversions.
        private const long KernelFactorWords = 64;
        private const long KernelSquareWords = 32;
        internal const long KernelSquareBits = KernelSquareWords * 64;
        private const long KernelDivisorWords = 32;
        private const long KernelGcdWords = 128;
        private const long KernelTextWords = 128;
        private const long KernelModulusWords = 4;
        private const long KernelModPowWork = 1024;
        private const double KernelPowWords = 256;

        // 64-bit words of |x| in O(1). Not BigInteger.Log: net472's walks every bit, and each size check here
        // cost a 25,000-bit operand more than its division by one word (review 9, C-9).
        internal static long Words(BigInteger x)
        {
            return (NumericOps.BitLength(x) - 1) / 64 + 1;
        }

        internal static BigInteger Multiply(EvalContext ctx, BigInteger a, BigInteger b)
        {
            return InKernel(Words(a), Words(b), a, b) ? MultiplyLong(ctx, a, b) : AfterCall(ctx, a * b);
        }

        // For a caller that already holds both bit lengths (or upper bounds of them).
        internal static BigInteger Multiply(EvalContext ctx, BigInteger a, BigInteger b, long bitsA, long bitsB)
        {
            return InKernel(bitsA / 64 + 1, bitsB / 64 + 1, a, b) ? MultiplyLong(ctx, a, b) : AfterCall(ctx, a * b);
        }

        // Whether a * b of these word lengths goes to the kernel. A caller with only bit lengths in hand can
        // skip it, and the operands' values, below KernelSquareBits per factor and LongWork in all.
        internal static bool InKernel(long wa, long wb, BigInteger a, BigInteger b)
        {
            long min = Math.Min(wa, wb);
            if (wa * wb > LongWork || min >= KernelFactorWords)
                return true;
            return min >= KernelSquareWords && wa == wb && a.Equals(b);
        }

        // x * x takes the kernel's square, which does half the word products.
        internal static BigInteger MultiplyLong(EvalContext ctx, BigInteger a, BigInteger b)
        {
            ctx.Budget.CheckDeadlineNow();
            return AfterCall(ctx, a.Equals(b) ? BigKernel.Square(ctx, a) : BigKernel.Multiply(ctx, a, b));
        }

        internal static BigInteger Square(EvalContext ctx, BigInteger a)
        {
            long w = Words(a);
            return w * w <= LongWork && w < KernelSquareWords ? AfterCall(ctx, a * a) : AfterCall(ctx, BigKernel.Square(ctx, a));
        }

        internal static BigInteger Divide(EvalContext ctx, BigInteger a, BigInteger b)
        {
            if (!DivInKernel(a, b))
                return AfterCall(ctx, a / b);
            BigInteger r;
            return AfterCall(ctx, BigKernel.DivRem(ctx, a, b, out r));
        }

        // a % b (truncated, like the operator): the operator's small-value fast path stays.
        internal static BigInteger Remainder(EvalContext ctx, BigInteger a, BigInteger b)
        {
            if (!DivInKernel(a, b))
                return AfterCall(ctx, a % b);
            BigInteger r;
            BigKernel.DivRem(ctx, a, b, out r);
            return AfterCall(ctx, r);
        }

        internal static BigInteger DivRem(EvalContext ctx, BigInteger a, BigInteger b, out BigInteger r)
        {
            if (!DivInKernel(a, b))
                return AfterCall(ctx, BigInteger.DivRem(a, b, out r));
            return AfterCall(ctx, BigKernel.DivRem(ctx, a, b, out r));
        }

        // Every operation, native or kernel, is charged one step by its caller and may still take milliseconds
        // (net472 divides by one word at ~200 ns per word; the kernel divides a 1M-bit number by another in
        // ~20 ms): a loop of them is seen by a deadline check here, or a loop of big floor divisions leaked.
        private static BigInteger AfterCall(EvalContext ctx, BigInteger r)
        {
            ctx.Budget.CheckDeadlineAfterCall();
            return r;
        }

        private static bool DivInKernel(BigInteger a, BigInteger b)
        {
            long wa = Words(a), wb = Words(b);
            if (wa < wb)
                return false;   // the quotient is zero
            return wb * (wa - wb + 1) > LongWork || wb >= KernelDivisorWords;
        }

        // Python's floored quotient (b != 0).
        internal static BigInteger FloorDiv(EvalContext ctx, BigInteger a, BigInteger b)
        {
            BigInteger r;
            BigInteger q = DivRem(ctx, a, b, out r);
            return !r.IsZero && r.Sign != b.Sign ? q - 1 : q;
        }

        internal static BigInteger Gcd(EvalContext ctx, BigInteger a, BigInteger b)
        {
            if (a.IsZero || b.IsZero)
                return BigInteger.Abs(a.IsZero ? b : a);   // math.gcd starts from 0: no trip through the kernel's limbs
            long w = Math.Max(Words(a), Words(b));
            return w * w <= LongWork && w < KernelGcdWords ? AfterCall(ctx, BigInteger.GreatestCommonDivisor(a, b)) : AfterCall(ctx, BigKernel.Gcd(ctx, a, b));
        }

        // a**n with about resultBits bits (the caller has checked them against MaxIntBits).
        internal static BigInteger Pow(EvalContext ctx, BigInteger a, int n, double resultBits)
        {
            double w = resultBits / 64 + 1;
            return w * w <= LongWork && w < KernelPowWords ? AfterCall(ctx, BigInteger.Pow(a, n)) : PowLong(ctx, a, n);
        }

        // Square-and-multiply from the top bit; each step is short or goes to the kernel by its own size.
        private static BigInteger PowLong(EvalContext ctx, BigInteger a, int n)
        {
            if (n == 0)
                return BigInteger.One;
            BigInteger r = a;
            for (int bit = 30 - Nlz(n); bit >= 0; bit--)
            {
                ctx.Budget.CheckDeadlineNow();
                r = Square(ctx, r);
                if (((n >> bit) & 1) != 0)
                    r = Multiply(ctx, r, a);
            }
            return r;
        }

        // One ModPow of the given work (its exponent bits times the modulus's words squared).
        internal static BigInteger ModPow(EvalContext ctx, BigInteger b, BigInteger e, BigInteger m, long work)
        {
            return work <= LongWork && (work < KernelModPowWork || Words(m) < KernelModulusWords) ? AfterCall(ctx, BigInteger.ModPow(b, e, m)) : ModPowLong(ctx, b, e, m);
        }

        // BigInteger.ModPow's result: the sign of b**e, the magnitude |b|**e mod |m| from the kernel.
        private static BigInteger ModPowLong(EvalContext ctx, BigInteger b, BigInteger e, BigInteger m)
        {
            if (m.IsZero)
                throw new DivideByZeroException();
            if (e.Sign < 0)
                throw new ArgumentOutOfRangeException(nameof(e));
            ctx.Budget.CheckDeadlineNow();
            BigInteger r = AfterCall(ctx, BigKernel.ModPow(ctx, BigInteger.Abs(b), e, BigInteger.Abs(m)));
            return b.Sign < 0 && !e.IsEven ? -r : r;
        }

        // Decimal digits of v (net472's conversion is quadratic; the kernel's divides and conquers), within
        // CPython's int_max_str_digits: an int of more than 4300 digits has no str (CPython 3.11+).
        // Refused before any charge: a try around str() of a huge int must see the ValueError, not run out of steps.
        internal static void CheckDigitCap(EvalContext ctx, BigInteger v)
        {
            if (NumericOps.BitLength(v) > MaxStrDigitsBits)
                throw TooManyDigits(ctx);
        }

        internal static string ToDecimalString(EvalContext ctx, BigInteger v)
        {
            long w = Words(v);
            CheckDigitCap(ctx, v);
            string s = w * w <= LongWork && w < KernelTextWords ? v.ToString(System.Globalization.CultureInfo.InvariantCulture) : BigKernel.ToDecimalString(ctx, v);
            ctx.Budget.CheckDeadlineAfterCall();
            if (s.Length - (v.Sign < 0 ? 1 : 0) > MaxStrDigits)
                throw TooManyDigits(ctx);
            return s;
        }

        internal const int MaxStrDigits = 4300;
        // 10**4300 - 1 has 14,285 bits: an int of more has more digits. By whole words (14,400) an int of up to
        // 14,464 bits was converted, and charged for it, before it was refused (review 12, row 6).
        private const long MaxStrDigitsBits = 14285;

        private static Exception TooManyDigits(EvalContext ctx)
        {
            return Errors.Raise.ValueError(ctx, "Exceeds the limit (4300 digits) for integer string conversion; "
                + "use sys.set_int_max_str_digits() to increase the limit");
        }

        private static int Nlz(int n)
        {
            int z = 0;
            for (uint u = (uint)n; (u & 0x80000000u) == 0 && z < 32; u <<= 1)
                z++;
            return z;
        }
    }
}

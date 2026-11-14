using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // BigInteger helpers absent from net472 (the bit length is NumericOps.BitLength).
    internal static class BigIntMath
    {
        // 10**e, sized against MaxIntBits together with the bits of the factor it will scale, before it is built.
        public static BigInteger PowTen(EvalContext ctx, long e, long factorBits)
        {
            ctx.Values.EnsureIntBits(factorBits + (long)(e * Log2Of10));
            ctx.Budget.CheckDeadlineNow();
            BigInteger r = BigOps.Pow(ctx, 10, (int)e, e * Log2Of10);
            ctx.Budget.CheckDeadlineNow();
            return r;
        }

        private static readonly double Log2Of10 = System.Math.Log(10, 2);

        // floor(a / b) for any signs, as Python's //.
        public static BigInteger FloorDiv(BigInteger a, BigInteger b)
        {
            BigInteger r;
            return NumericOps.FloorDiv(a, b, out r, null);
        }

        // a / b to the nearest integer, a tie to the even one, for any signs (_PyLong_DivmodNear). ctx set: a long
        // division runs under BigOps (deadline-bounded).
        public static BigInteger DivNearHalfEven(BigInteger a, BigInteger b, EvalContext ctx = null)
        {
            BigInteger r;
            BigInteger q = NumericOps.FloorDiv(a, b, out r, ctx);   // a/b - q = r/b in [0, 1)
            int c = BigInteger.Abs(r * 2).CompareTo(BigInteger.Abs(b));
            return c > 0 || (c == 0 && !q.IsEven) ? q + 1 : q;
        }

        public static bool IsPowerOfTwo(BigInteger n)
        {
            if (n.Sign <= 0)
                return false;
            return n == (BigInteger.One << (int)(NumericOps.BitLength(n) - 1));
        }
    }
}

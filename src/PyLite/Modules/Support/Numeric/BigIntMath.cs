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

        public static bool IsPowerOfTwo(BigInteger n)
        {
            if (n.Sign <= 0)
                return false;
            return n == (BigInteger.One << (int)(NumericOps.BitLength(n) - 1));
        }
    }
}

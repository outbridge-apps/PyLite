using System;
using System.Numerics;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // Numeric-tower helpers. Arithmetic proper is added alongside the number hooks
    // this file also holds the shared bit-length and int->str charging primitives.
    internal static partial class NumericOps
    {
        internal static bool IsNumber(ScriptValue v)
        {
            ValueKind k = v.Kind;
            return k == ValueKind.Bool || k == ValueKind.Int || k == ValueKind.Float;
        }

        internal static BigInteger AsBigInteger(ScriptValue v)
        {
            BoolValue b = v as BoolValue;
            if (b != null)
                return b.Value ? BigInteger.One : BigInteger.Zero;
            return ((IntValue)v).Value;
        }

        // Exact bit length of |v|; BitLength(0) == 0. Byte-based: one O(n/8) copy instead of
        // BigInteger.Log plus shift-allocating correction loops (net472 has no GetBitLength).
        internal static long BitLength(BigInteger v)
        {
            if (v.IsZero)
                return 0;
            byte[] bytes = BigInteger.Abs(v).ToByteArray();   // little-endian magnitude, top byte may be a 0 sign byte
            int top = bytes.Length - 1;
            if (bytes[top] == 0)
                top--;
            return 8L * top + BitLengthLong(bytes[top]);
        }

        // Exact bit length of |v| without BigInteger (P1); BitLengthLong(0) == 0, long.MinValue -> 64.
        internal static int BitLengthLong(long v)
        {
            ulong u = v >= 0 ? (ulong)v : unchecked((ulong)(-v));   // MinValue wraps to 2^63, its own |v|
            int bits = 0;
            if ((u >> 32) != 0) { bits += 32; u >>= 32; }
            if ((u >> 16) != 0) { bits += 16; u >>= 16; }
            if ((u >> 8) != 0) { bits += 8; u >>= 8; }
            if ((u >> 4) != 0) { bits += 4; u >>= 4; }
            if ((u >> 2) != 0) { bits += 2; u >>= 2; }
            if ((u >> 1) != 0) { bits += 1; u >>= 1; }
            return bits + (int)u;
        }

        internal static double ToDoubleChecked(BigInteger v, EvalContext ctx)
        {
            if (BitLength(v) > 1024)
                throw Raise.Overflow(ctx, "int too large to convert to float");
            return (double)v;
        }

        // R10: pre-charge for the quadratic cost of BigInteger.ToString before printing an int.
        internal static void ChargeIntToStr(BigInteger v, EvalContext ctx)
        {
            ChargeIntToStrBits(BitLength(v), ctx);
        }

        // Long twin with identical budget accounting (P1).
        internal static void ChargeIntToStr(long v, EvalContext ctx)
        {
            ChargeIntToStrBits(BitLengthLong(v), ctx);
        }

        private static void ChargeIntToStrBits(long bits, EvalContext ctx)
        {
            long digits = (long)(bits / 3.321928094887362) + 1;
            long cost = digits + digits * digits / 256;
            ctx.Budget.Step((int)Math.Min(int.MaxValue, cost));
            ctx.Budget.PreCharge(2 * digits);
            ctx.Budget.CheckDeadlineNow();
        }
    }
}

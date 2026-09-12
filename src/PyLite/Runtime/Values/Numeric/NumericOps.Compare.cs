using System;
using System.Numerics;

namespace Outbridge.PyLite.Runtime.Values
{
    // Numeric equality/ordering with the exact int<->float comparison.
    internal static partial class NumericOps
    {
        internal static bool ApplyCmp(PyCmpOp op, int c)
        {
            switch (op)
            {
                case PyCmpOp.Lt: return c < 0;
                case PyCmpOp.Le: return c <= 0;
                case PyCmpOp.Gt: return c > 0;
                case PyCmpOp.Ge: return c >= 0;
                case PyCmpOp.Eq: return c == 0;
                default: return c != 0;   // Ne
            }
        }

        internal static bool NumericEquals(ScriptValue a, ScriptValue b)
        {
            // P1 fast path: both long-backed ints.
            IntValue ia = a as IntValue, ib = b as IntValue;
            if (ia != null && ib != null && ia.IsSmall && ib.IsSmall)
                return ia.Small == ib.Small;
            bool af = a.Kind == ValueKind.Float, bf = b.Kind == ValueKind.Float;
            if (!af && !bf)
                return AsBigInteger(a) == AsBigInteger(b);
            if (af && bf)
                return ((FloatValue)a).Value == ((FloatValue)b).Value;   // NaN!=NaN, -0.0==0.0
            double d; BigInteger i;
            if (af)
            {
                d = ((FloatValue)a).Value;
                i = AsBigInteger(b);
            }
            else
            {
                d = ((FloatValue)b).Value;
                i = AsBigInteger(a);
            }
            if (double.IsNaN(d) || double.IsInfinity(d))
                return false;
            return CompareIntFloat(i, d) == 0;
        }

        internal static bool NumericCompareOp(PyCmpOp op, ScriptValue a, ScriptValue b)
        {
            // P1 fast path: both long-backed ints.
            IntValue ia = a as IntValue, ib = b as IntValue;
            if (ia != null && ib != null && ia.IsSmall && ib.IsSmall)
                return ApplyCmp(op, ia.Small < ib.Small ? -1 : (ia.Small > ib.Small ? 1 : 0));
            bool af = a.Kind == ValueKind.Float, bf = b.Kind == ValueKind.Float;
            if (af && double.IsNaN(((FloatValue)a).Value))
                return false;
            if (bf && double.IsNaN(((FloatValue)b).Value))
                return false;
            int cmp;
            if (!af && !bf)
                cmp = BigInteger.Compare(AsBigInteger(a), AsBigInteger(b));
            else if (af && bf)
            {
                double x = ((FloatValue)a).Value, y = ((FloatValue)b).Value;
                cmp = x < y ? -1 : (x > y ? 1 : 0);
            }
            else if (af)
                cmp = -CompareIntFloat(AsBigInteger(b), ((FloatValue)a).Value);
            else
                cmp = CompareIntFloat(AsBigInteger(a), ((FloatValue)b).Value);
            return ApplyCmp(op, cmp);
        }

        // Port of the int branch of Objects/floatobject.c::float_richcompare. Handles infinity.
        internal static int CompareIntFloat(BigInteger i, double d)
        {
            int si = i.Sign;
            int sd = d > 0 ? 1 : (d < 0 ? -1 : 0);
            if (si != sd)
                return si < sd ? -1 : 1;
            if (si == 0)
                return 0;
            if (double.IsInfinity(d))
                return si > 0 ? -1 : 1;   // |d| = inf dominates any finite |i|

            long bl = BitLength(i);
            if (bl <= 53)
                return ((double)i).CompareTo(d);
            if (bl > 1024)
                return si;                            // |i| >= 2^1024 > DBL_MAX >= |d|

            BigInteger absI = BigInteger.Abs(i);
            int e;
            long m = DecomposeExact(Math.Abs(d), out e);         // |d| = m * 2^e
            int cmp = e >= 0
                ? BigInteger.Compare(absI, (BigInteger)m << e)
                : BigInteger.Compare(absI << (-e), (BigInteger)m);
            return si > 0 ? cmp : -cmp;
        }

        // |value| = m * 2^e, m a 53-bit integer; value finite and non-zero.
        private static long DecomposeExact(double d, out int e)
        {
            long bits = BitConverter.DoubleToInt64Bits(d);
            int exp = (int)((bits >> 52) & 0x7FF);
            long mant = bits & 0xFFFFFFFFFFFFFL;
            if (exp == 0)
            {
                e = -1074;
                return mant;
            }            // subnormal
            e = exp - 1075;
            return mant | (1L << 52);                            // normal: implicit leading 1
        }
    }
}

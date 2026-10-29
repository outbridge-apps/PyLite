using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // Exact rational helpers shared by Fraction and Decimal. Comparisons reduce to BigInteger
    // cross-multiplication (no float cast), so Fraction(1,3) < 0.3334 is exact.
    internal static class Rationals
    {
        // Compare numA/denA vs numB/denB (denA > 0, denB > 0) -> -1/0/1. The cross products may be twice
        // MaxIntBits: long ones run under BigOps.
        public static int Compare(BigInteger numA, BigInteger denA, BigInteger numB, BigInteger denB, EvalContext ctx)
        {
            return BigOps.Multiply(ctx, numA, denB).CompareTo(BigOps.Multiply(ctx, numB, denA));
        }

        // n/d (a Fraction or Decimal) against another number: Ordered with cmp set, Unordered for a NaN
        // float, NotANumber for any other type. An infinity orders past every finite value.
        internal enum Order { NotANumber, Unordered, Ordered }

        internal static Order CompareWith(BigInteger n, BigInteger d, Runtime.Values.ScriptValue other, out int cmp, EvalContext ctx)
        {
            cmp = 0;
            BigInteger on, od = BigInteger.One;
            switch (other)
            {
                case Runtime.Values.FractionValue fr: on = fr.Num; od = fr.Den; break;
                case Runtime.Values.DecimalValue dv: dv.ToRational(out on, out od); break;
                case Runtime.Values.IntValue iv: on = iv.Value; break;
                case Runtime.Values.BoolValue bv: on = bv.Value ? BigInteger.One : BigInteger.Zero; break;
                case Runtime.Values.FloatValue fv:
                    if (double.IsNaN(fv.Value))
                        return Order.Unordered;
                    if (double.IsInfinity(fv.Value))
                    {
                        cmp = fv.Value > 0 ? -1 : 1;
                        return Order.Ordered;
                    }
                    FloatToRational(fv.Value, out on, out od);
                    break;
                default: return Order.NotANumber;
            }
            cmp = Compare(n, d, on, od, ctx);
            return Order.Ordered;
        }

        // The across-kind equality hook of a Fraction or Decimal: an int, bool or float (a NaN or an
        // infinity is never equal); any other kind is not this hook's.
        internal static bool EqualsAcrossKind(Runtime.Values.ScriptValue self, Runtime.Values.ScriptValue other, out bool equal, EvalContext ctx)
        {
            equal = false;
            if (!(other is Runtime.Values.IntValue || other is Runtime.Values.BoolValue || other is Runtime.Values.FloatValue))
                return false;
            BigInteger n, d;
            Runtime.Values.FractionValue fr = self as Runtime.Values.FractionValue;
            if (fr != null)
            {
                n = fr.Num;
                d = fr.Den;
            }
            else
            {
                ((Runtime.Values.DecimalValue)self).ToRational(out n, out d);
            }
            int cmp;
            equal = CompareWith(n, d, other, out cmp, ctx) == Order.Ordered && cmp == 0;
            return true;
        }

        // A finite double -> exact num/den (den > 0). Caller rejects NaN/Inf beforehand.
        public static void FloatToRational(double f, out BigInteger num, out BigInteger den)
        {
            bool neg;
            ulong mant;
            int exp2;
            FloatRepr.Decompose(f, out neg, out mant, out exp2);
            BigInteger m = mant;
            if (exp2 >= 0)
            {
                num = m << exp2;
                den = BigInteger.One;
            }
            else
            {
                num = m;
                den = BigInteger.One << (-exp2);
            }
            if (neg)
                num = -num;
        }

        // Floored division / modulo for BigInteger (Python semantics; b != 0).
        public static BigInteger FloorDiv(BigInteger a, BigInteger b)
        {
            BigInteger q = BigInteger.DivRem(a, b, out BigInteger r);
            if (!r.IsZero && (r.Sign != b.Sign))
                q -= 1;
            return q;
        }
    }
}

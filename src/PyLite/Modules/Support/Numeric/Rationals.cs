using System.Numerics;

namespace Outbridge.PyLite.Modules.Support
{
    // Exact rational helpers shared by Fraction and Decimal. Comparisons reduce to BigInteger
    // cross-multiplication (no float cast), so Fraction(1,3) < 0.3334 is exact.
    internal static class Rationals
    {
        // Compare numA/denA vs numB/denB (denA > 0, denB > 0) -> -1/0/1.
        public static int Compare(BigInteger numA, BigInteger denA, BigInteger numB, BigInteger denB)
        {
            return (numA * denB).CompareTo(numB * denA);
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

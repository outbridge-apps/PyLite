using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // A manual parser of Fraction's _RATIONAL_FORMAT grammar — NO regex. Forms
    // "[+-]int", "[+-]int/int", "[+-][int][.frac][e[+-]exp]", each digit run with PEP 515 underscores (3.11).
    // ASCII digits only, read through IntParser: refused past MaxIntBits by count, the quadratic parse charged.
    internal static class RationalParse
    {
        public static void Parse(EvalContext ctx, string original, out BigInteger num, out BigInteger den)
        {
            var sc = new NumberScan(original);
            int sign = sc.Sign() ? -1 : 1;
            string numDigits = sc.Digits(true);

            if (sc.Accept('/'))
            {
                string denDigits = sc.Digits(true);
                if (denDigits.Length == 0 || numDigits.Length == 0 || !sc.AtEnd)
                    throw Invalid(ctx, original);
                num = sign * IntParser.Parse(ctx, numDigits, 10);
                den = IntParser.Parse(ctx, denDigits, 10);
                return;
            }

            string decimalPart = sc.Accept('.') ? sc.Digits(true) : "";
            int expPart;
            if (sc.Exponent(out expPart, true) == NumberScan.Exp.Malformed)
                throw Invalid(ctx, original);
            if (!sc.AtEnd || (numDigits.Length == 0 && decimalPart.Length == 0))
                throw Invalid(ctx, original);

            string mantStr = numDigits + decimalPart;
            if (mantStr.Length == 0)
                mantStr = "0";
            BigInteger mantissa = IntParser.Parse(ctx, mantStr, 10);
            long scale = (long)expPart - decimalPart.Length;
            if (mantissa.IsZero)
            {
                num = BigInteger.Zero;   // 0e999999999 is 0 without building the power
                den = BigInteger.One;
            }
            else if (scale >= 0)
            {
                num = sign * BigOps.Multiply(ctx, mantissa, BigIntMath.PowTen(ctx, scale, NumericOps.BitLength(mantissa)));
                den = BigInteger.One;
            }
            else
            {
                num = sign * mantissa;
                den = BigIntMath.PowTen(ctx, -scale, 0);
            }
        }

        private static ScriptException Invalid(EvalContext ctx, string s)
        {
            return Raise.ValueError(ctx, "Invalid literal for Fraction: " + ctx.Values.Str(s).Repr(ctx));
        }
    }
}

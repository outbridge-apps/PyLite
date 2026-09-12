using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Modules.Support
{
    // A manual parser of Fraction's _RATIONAL_FORMAT grammar — NO regex. Forms
    // "[+-]int", "[+-]int/int", "[+-][int][.frac][e[+-]exp]". ASCII digits only.
    internal static class RationalParse
    {
        public static void Parse(EvalContext ctx, string original, out BigInteger num, out BigInteger den)
        {
            var sc = new NumberScan(original);
            int sign = sc.Sign() ? -1 : 1;
            string numDigits = sc.Digits();

            if (sc.Accept('/'))
            {
                string denDigits = sc.Digits();
                if (denDigits.Length == 0 || numDigits.Length == 0 || !sc.AtEnd)
                    throw Invalid(ctx, original);
                num = sign * BigInteger.Parse(numDigits);
                den = BigInteger.Parse(denDigits);
                return;
            }

            string decimalPart = sc.Accept('.') ? sc.Digits() : "";
            int expPart;
            if (sc.Exponent(out expPart) == NumberScan.Exp.Malformed)
                throw Invalid(ctx, original);
            if (!sc.AtEnd || (numDigits.Length == 0 && decimalPart.Length == 0))
                throw Invalid(ctx, original);

            string mantStr = numDigits + decimalPart;
            if (mantStr.Length == 0)
                mantStr = "0";
            BigInteger mantissa = BigInteger.Parse(mantStr);
            int scale = expPart - decimalPart.Length;
            if (scale >= 0)
            {
                num = sign * mantissa * BigInteger.Pow(10, scale);
                den = BigInteger.One;
            }
            else
            {
                num = sign * mantissa;
                den = BigInteger.Pow(10, -scale);
            }
        }

        private static ScriptException Invalid(EvalContext ctx, string s)
        {
            return Raise.ValueError(ctx, "Invalid literal for Fraction: " + ctx.Values.Str(s).Repr(ctx));
        }
    }
}

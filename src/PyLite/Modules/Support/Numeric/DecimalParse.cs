using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Modules.Support
{
    // A manual parser of the Python decimal grammar — NO regex, InvariantCulture only.
    // NaN/Inf/garbage/underscore -> decimal.InvalidOperation; too big -> decimal.Overflow. scale>28 rounds
    // half-even to 28 (a documented System.Decimal limit).
    internal static class DecimalParse
    {
        // `exp` is the literal's Python exponent, which the backend's scale cannot express when it is
        // positive: '1E+2' comes back as the value 100 with exp 2, and prints as 1E+2 again.
        public static decimal Parse(EvalContext ctx, string original, out int exp)
        {
            exp = 0;
            var sc = new NumberScan(original);
            if (sc.AtEnd)
                throw Invalid(ctx);
            bool neg = sc.Sign();

            string rest = sc.Text.Substring(sc.Pos).ToLowerInvariant();
            if (rest == "inf" || rest == "infinity" || rest == "nan" || rest == "snan")
                throw Invalid(ctx);

            string intPart = sc.Digits();
            string fracPart = sc.Accept('.') ? sc.Digits() : "";
            if (intPart.Length == 0 && fracPart.Length == 0)
                throw Invalid(ctx);

            int expPart;
            if (sc.Exponent(out expPart) == NumberScan.Exp.Malformed)
                throw Invalid(ctx);
            if (!sc.AtEnd)
                throw Invalid(ctx);   // trailing garbage / underscore / '1.2.3'

            string mantStr = intPart + fracPart;
            BigInteger unscaled = BigInteger.Parse(mantStr);
            int totalExp = expPart - fracPart.Length;

            if (totalExp >= 0)
            {
                exp = totalExp;
                if (unscaled.IsZero)
                    return DecimalBits.Pack(ctx, BigInteger.Zero, 0, neg);   // 0E+2 keeps its exponent
                if (totalExp > 29)   // any nonzero * 10^30 overflows System.Decimal
                    throw Overflow(ctx);
                return DecimalBits.Pack(ctx, unscaled * BigInteger.Pow(10, totalExp), 0, neg);
            }

            int scale = -totalExp;
            exp = totalExp;
            if (unscaled.IsZero)
                return DecimalBits.Pack(ctx, BigInteger.Zero, scale > 28 ? 28 : scale, neg);   // 0E-30 keeps its exponent
            if (scale > 28)
            {
                int drop = scale - 28;
                if (drop > DecimalBits.SignificantDigits(unscaled) + 1)
                    unscaled = BigInteger.Zero;   // rounds away entirely
                else
                    unscaled = DecimalBits.RoundUnscaledToScale(unscaled, scale, 28);
                scale = 28;
                exp = -28;
                if (unscaled.IsZero && mantStr.TrimStart('0').Length > 0)
                    throw DecimalBits.Underflow(ctx);
            }
            return DecimalBits.Pack(ctx, unscaled, scale, neg);
        }

        private static ScriptException Invalid(EvalContext ctx)
        {
            return Raise.Make(ctx, PyExceptionTypes.DecimalInvalidOperation, "[<class 'decimal.ConversionSyntax'>]");
        }

        private static ScriptException Overflow(EvalContext ctx)
        {
            return Raise.Make(ctx, PyExceptionTypes.DecimalOverflow, "[<class 'decimal.Overflow'>]");
        }
    }
}

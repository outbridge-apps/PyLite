using System;
using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Modules.Support
{
    // A manual parser of the Python decimal grammar — NO regex, InvariantCulture only.
    // NaN/Inf/garbage -> decimal.InvalidOperation; too big -> decimal.Overflow; more digits than the backend
    // holds round half-even to the places that fit. Underscores are dropped first, as both of CPython's
    // decimal implementations do (PEP 515 grouping; they do not check where an underscore stands).
    internal static class DecimalParse
    {
        // `exp` is the literal's Python exponent, which the backend's scale cannot express when it is
        // positive: '1E+2' comes back as the value 100 with exp 2, and prints as 1E+2 again.
        public static decimal Parse(EvalContext ctx, string original, out int exp)
        {
            exp = 0;
            var sc = new NumberScan(original.IndexOf('_') < 0 ? original : original.Replace("_", ""));
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

            ctx.Budget.ChargeLinear(original.Length);
            long exp10 = (long)expPart - fracPart.Length;
            string mantStr = Significant(intPart + fracPart, ref exp10);
            BigInteger unscaled = BigInteger.Parse(mantStr);
            int totalExp = (int)Math.Max(int.MinValue + 1, Math.Min(int.MaxValue, exp10));

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
            // Round once, half-even, to the most places (at most 28) whose mantissa fits 96 bits: System.Decimal's
            // rule for a long literal ('31.41592653589793238462643383279' kept 28 places, 30 digits, and
            // overflowed). One rounding from the exact value: rounding an already rounded one could round twice.
            int digits = DecimalBits.SignificantDigits(unscaled);
            int target = Math.Min(28, scale);
            if (digits - (scale - target) > 29)
                target = Math.Max(0, scale - (digits - 29));
            BigInteger fitted = RoundToScale(unscaled, scale, target, digits);
            if (fitted > DecimalBits.MaxMantissa && target > 0)
            {
                target--;   // 29 digits above 7.9e28, or a carry to 30
                fitted = RoundToScale(unscaled, scale, target, digits);
            }
            if (target < scale)
            {
                exp = -target;
                if (fitted.IsZero)
                    throw DecimalBits.Underflow(ctx);
            }
            return DecimalBits.Pack(ctx, fitted, target, neg);   // past 29 integer digits: Overflow
        }

        private static BigInteger RoundToScale(BigInteger m, int scale, int target, int digits)
        {
            if (target >= scale)
                return m;
            int drop = scale - target;
            if (drop > digits + 1)
                return BigInteger.Zero;   // rounds away entirely
            return DecimalBits.RoundUnscaledToScale(m, scale, target);
        }

        // The mantissa without leading zeros and cut to KeptDigits significant digits plus one sticky digit
        // (1 when anything nonzero was cut). System.Decimal holds 29 digits, so a result that does not
        // overflow rounds at least 30 places left of the cut and comes out the same, while a long literal
        // no longer feeds BigInteger.Parse (quadratic, no deadline check).
        private const int KeptDigits = 60;

        private static string Significant(string mant, ref long exp10)
        {
            int lead = 0;
            while (lead < mant.Length - 1 && mant[lead] == '0')
                lead++;
            int sig = mant.Length - lead;
            if (sig <= KeptDigits)
                return lead == 0 ? mant : mant.Substring(lead);
            bool sticky = false;
            for (int i = lead + KeptDigits; i < mant.Length && !sticky; i++)
                sticky = mant[i] != '0';
            exp10 += sig - KeptDigits - 1;
            return mant.Substring(lead, KeptDigits) + (sticky ? "1" : "0");
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

using System.Globalization;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // Fraction.__format__ (Lib/fractions.py, 3.12+): with no type, "n/d" padded; with e/E/f/F/g/G/%, digits
    // rounded half-even from the exact value, never through a float.
    internal sealed partial class FractionValue
    {
        protected internal override ScriptValue FormatSpecCore(string spec, EvalContext ctx)
        {
            if (spec.Length == 0)
                return null;
            FormatSpec f = PyFormat.ParseSpec(ctx, spec);
            bool explicitAlign = f.Align != '\0' && !f.AlignImplied;
            if (f.Type == '\0' && f.Precision < 0 && !f.ZeroPad)
                return ctx.Values.Str(General(ctx, f));
            if ("eEfFgG%".IndexOf(f.Type) < 0 || f.Type == '\0' || (explicitAlign && f.ZeroPad))
                throw Raise.ValueError(ctx, "Invalid format specifier " + ctx.Values.Str(spec).Repr(ctx)
                    + " for object of type 'Fraction'");
            return ctx.Values.Str(FloatStyle(ctx, f));
        }

        private string General(EvalContext ctx, FormatSpec f)
        {
            string body = Grouped(NumericOps.ToDecimalString(BigInteger.Abs(Num), ctx), f.GroupChar);
            if (!Den.IsOne || f.AltForm)
                body += "/" + Grouped(NumericOps.ToDecimalString(Den, ctx), f.GroupChar);
            string sign = Num.Sign < 0 ? "-" : f.Sign == '+' || f.Sign == ' ' ? f.Sign.ToString() : "";
            return Pad(ctx, sign, body, f.Fill, f.Align == '\0' ? '>' : f.Align, f.Width);
        }

        private string FloatStyle(EvalContext ctx, FormatSpec f)
        {
            char type = f.Type;
            int precision = f.Precision < 0 ? 6 : f.Precision;
            ctx.Values.CheckStrLen((long)precision + 64);   // the cap only: the str made at the end charges itself
            bool alt = f.AltForm;
            bool trimZeros = (type == 'g' || type == 'G') && !alt;
            // The '0' flag pads the digits, not the field: fill and alignment stay the defaults.
            char fill = f.ZeroPad ? ' ' : f.Fill;
            char align = f.Align == '\0' || f.AlignImplied ? '>' : f.Align;
            bool zeroPad = f.ZeroPad || (align == '=' && fill == '0');
            bool negative, scientific;
            BigInteger significand;
            int exponent, pointPos;
            if (type == 'f' || type == 'F' || type == '%')
            {
                exponent = type == '%' ? -precision - 2 : -precision;
                significand = RoundToExponent(ctx, Num, Den, exponent, out negative);
                scientific = false;
                pointPos = precision;
            }
            else
            {
                int figures = type == 'g' || type == 'G' ? (precision > 1 ? precision : 1) : precision + 1;
                significand = RoundToFigures(ctx, Num, Den, figures, out negative, out exponent);
                scientific = type == 'e' || type == 'E' || exponent > 0 || exponent + figures <= -4;
                pointPos = scientific ? figures - 1 : -exponent;
            }

            string suffix = "";
            if (type == '%')
            {
                suffix = "%";
            }
            else if (scientific)
            {
                int e = exponent + pointPos;
                suffix = (type == 'E' || type == 'F' || type == 'G' ? "E" : "e") + (e < 0 ? "-" : "+")
                    + System.Math.Abs(e).ToString("00", CultureInfo.InvariantCulture);
            }

            string digits = NumericOps.ToDecimalString(significand, ctx);
            if (digits.Length < pointPos + 1)
                digits = new string('0', pointPos + 1 - digits.Length) + digits;
            string sign = negative ? "-" : f.Sign == '+' || f.Sign == ' ' ? f.Sign.ToString() : "";
            string leading = digits.Substring(0, digits.Length - pointPos);
            string frac = digits.Substring(digits.Length - pointPos);
            if (trimZeros)
                frac = frac.TrimEnd('0');
            string trailing = (frac.Length == 0 && !alt ? "" : ".") + frac + suffix;

            if (zeroPad)
            {
                int minLeading = f.Width - sign.Length - trailing.Length;
                if (f.GroupChar != '\0')
                    minLeading = FloorDiv3x4(minLeading);
                if (leading.Length < minLeading)
                {
                    ctx.Values.CheckStrLen(minLeading);
                    leading = new string('0', minLeading - leading.Length) + leading;
                }
            }
            leading = Grouped(leading, f.GroupChar);
            return Pad(ctx, sign, leading + trailing, fill, align, f.Width);
        }

        // 3 * min_leading // 4 + 1: the zero padding leaves room for the separators added to it.
        private static int FloorDiv3x4(int minLeading)
        {
            int t = 3 * minLeading;
            return (t >= 0 ? t / 4 : -((3 - t) / 4)) + 1;
        }

        private static string Grouped(string digits, char sep)
        {
            return sep == '\0' ? digits : PyFormat.GroupNumber(digits, sep);
        }

        private static string Pad(EvalContext ctx, string sign, string body, char fill, char align, int width)
        {
            int padding = width - sign.Length - body.Length;
            if (padding <= 0)
                return sign + body;
            ctx.Values.CheckStrLen(width);
            switch (align)
            {
                case '<': return sign + body + new string(fill, padding);
                case '^': return new string(fill, padding / 2) + sign + body + new string(fill, padding - padding / 2);
                case '=': return sign + new string(fill, padding) + body;
                default: return new string(fill, padding) + sign + body;
            }
        }

        // 10**k for a precision the caller bounded by MaxStrChars: deadline-checked, as a script's own ** is.
        private static BigInteger TenTo(EvalContext ctx, int k)
        {
            return Modules.Support.BigIntMath.PowTen(ctx, k, 0);
        }

        // n/d to the nearest multiple of 10**exponent, ties to even; the sign is the input's.
        private static BigInteger RoundToExponent(EvalContext ctx, BigInteger n, BigInteger d, int exponent, out bool negative)
        {
            if (exponent >= 0)
                d = BigOps.Multiply(ctx, d, TenTo(ctx, exponent));
            else
                n = BigOps.Multiply(ctx, n, TenTo(ctx, -exponent));
            BigInteger r;
            BigInteger q = NumericOps.FloorDiv(n + (d >> 1), d, out r, ctx);
            if (r.IsZero && d.IsEven)
                q &= -2;
            negative = n.Sign < 0;
            return BigInteger.Abs(q);
        }

        private static BigInteger RoundToFigures(EvalContext ctx, BigInteger n, BigInteger d, int figures,
            out bool negative, out int exponent)
        {
            if (n.IsZero)
            {
                negative = false;
                exponent = 1 - figures;
                return BigInteger.Zero;
            }
            string sn = NumericOps.ToDecimalString(BigInteger.Abs(n), ctx);
            string sd = NumericOps.ToDecimalString(d, ctx);
            int m = sn.Length - sd.Length + (string.CompareOrdinal(sd, sn) <= 0 ? 1 : 0);
            exponent = m - figures;
            BigInteger significand = RoundToExponent(ctx, n, d, exponent, out negative);
            if (NumericOps.ToDecimalString(significand, ctx).Length == figures + 1)
            {
                significand /= 10;
                exponent++;
            }
            return significand;
        }
    }
}

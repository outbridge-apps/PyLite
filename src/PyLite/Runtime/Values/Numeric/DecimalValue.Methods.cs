using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // decimal.Decimal instance methods: quantize / to_integral / normalize / as_tuple /
    // predicates / copy_* / min / max / compare / sqrt / ln / log10 / exp / __format__.
    internal sealed partial class DecimalValue
    {
        // The backend reading: mantissa and scale, for everything that works on the magnitude alone.
        // Anything the exponent is visible in goes through Coeff instead.
        private void Parts(out BigInteger m, out int scale, out bool neg)
        {
            DecimalBits.Unpack(Value, out m, out scale, out neg);
        }

        private decimal OtherDecimal(EvalContext ctx, ScriptValue v)
        {
            decimal d;
            if (TryOperand(ctx, v, out d))
                return d;
            throw Raise.TypeError(ctx, "conversion from " + v.PyTypeName + " to Decimal is not supported");
        }

        // ---- quantize / to_integral ----

        private ScriptValue Quantize(EvalContext ctx, ScriptValue[] args)
        {
            if (args.Length < 1)
                throw Raise.TypeError(ctx, "quantize() missing required argument 'exp'");
            int targetExp = TargetExp(ctx, args[0]);
            string rounding = args.Length >= 2 && args[1].Kind != ValueKind.None
                ? AsRounding(ctx, args[1])
                : ctx.DecimalCtx.Rounding;
            return DoQuantize(ctx, targetExp, DecimalRound.Parse(rounding));
        }

        // round-to-integral-value: the exponent rises to zero but never falls, so an already-integral
        // 1E+2 keeps its exponent (the spec's max(exp, 0)).
        private ScriptValue ToIntegral(EvalContext ctx, ScriptValue[] args)
        {
            string rounding = args.Length >= 1 && args[0].Kind != ValueKind.None
                ? AsRounding(ctx, args[0])
                : ctx.DecimalCtx.Rounding;
            return DoQuantize(ctx, Exp > 0 ? Exp : 0, DecimalRound.Parse(rounding));
        }

        private ScriptValue DoQuantize(EvalContext ctx, int targetExp, RoundingMode mode)
        {
            BigInteger m;
            int exp;
            bool neg;
            Coeff(out m, out exp, out neg);
            if (targetExp == exp)
                return this;
            if (targetExp > 29 || targetExp < -28)
                throw InvalidOp(ctx);
            if (targetExp < exp)
            {
                BigInteger mNew = m * BigInteger.Pow(10, exp - targetExp);
                if (mNew > DecimalBits.MaxMantissa || TooLong(ctx, mNew))
                    throw InvalidOp(ctx);
                return Make(ctx, mNew, targetExp, neg);
            }
            BigInteger divisor = BigInteger.Pow(10, targetExp - exp);
            BigInteger r;
            BigInteger q = BigInteger.DivRem(m, divisor, out r);
            BigInteger qr = DecimalRound.Apply(q, r, divisor, neg, mode);
            if (qr > DecimalBits.MaxMantissa || TooLong(ctx, qr))
                throw InvalidOp(ctx);
            return Make(ctx, qr, targetExp, neg);
        }

        // scaleb(n): the value times 10**n, done on the exponent so no digit is lost.
        private ScriptValue ScaleB(EvalContext ctx, ScriptValue[] a)
        {
            int n = ShiftAmount(ctx, a, "scaleb");
            BigInteger m;
            int exp;
            bool neg;
            Coeff(out m, out exp, out neg);
            long target = (long)exp + n;
            if (target > 60 || target < -60)
                throw target > 0 ? Overflow(ctx) : DecimalBits.Underflow(ctx);
            return Make(ctx, m, (int)target, neg);
        }

        // shift(n): the COEFFICIENT's digits move inside the context precision, the exponent stays put.
        // Digits shifted in are zeros and digits shifted out are dropped, as in the spec.
        private ScriptValue Shift(EvalContext ctx, ScriptValue[] a)
        {
            int n = ShiftAmount(ctx, a, "shift");
            int prec = ctx.DecimalCtx.Prec;
            if (n > prec || n < -prec)
                throw InvalidOp(ctx);
            BigInteger m;
            int exp;
            bool neg;
            Coeff(out m, out exp, out neg);
            if (n >= 0)
                m = m * BigInteger.Pow(10, n) % BigInteger.Pow(10, prec);
            else
                m /= BigInteger.Pow(10, -n);
            return Make(ctx, m, exp, neg);
        }

        private static int ShiftAmount(EvalContext ctx, ScriptValue[] a, string fn)
        {
            if (a.Length < 1 || a[0] == null)
                throw Args.AtLeastError(ctx, fn, 1, 0);
            DecimalValue dv = a[0] as DecimalValue;
            if (dv != null)
            {
                BigInteger m;
                int exp;
                bool neg;
                dv.Coeff(out m, out exp, out neg);
                if (exp != 0)
                    throw InvalidOp(ctx);
                // Clamped like Coerce.ToInt32: the callers' own range checks turn an out-of-range amount
                // into InvalidOperation / Overflow, and the cast never sees a coefficient past an int.
                if (m > int.MaxValue)
                    return neg ? int.MinValue : int.MaxValue;
                return (int)(neg ? -m : m);
            }
            return Coerce.ToInt32(ctx, a[0], fn + "() argument must be an integer");
        }

        // as_integer_ratio(): the exact value as a reduced fraction, which a scaled decimal always is.
        private ScriptValue AsIntegerRatio(EvalContext ctx)
        {
            BigInteger num, den;
            ToRational(out num, out den);
            BigInteger g = BigInteger.GreatestCommonDivisor(BigInteger.Abs(num), den);
            if (g > BigInteger.One)
            {
                num /= g;
                den /= g;
            }
            if (num.IsZero)
                den = BigInteger.One;
            return ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Int(num), ctx.Values.Int(den) });
        }

        // quantize refuses a result the context precision cannot hold, rather than rounding it again.
        private static bool TooLong(EvalContext ctx, BigInteger coeff)
        {
            return DecimalBits.SignificantDigits(coeff) > ctx.DecimalCtx.Prec;
        }

        private int TargetExp(EvalContext ctx, ScriptValue v)
        {
            DecimalValue dv = v as DecimalValue;
            if (dv != null)
                return dv.Exp;
            if (v is IntValue || v is BoolValue)
                return 0;
            throw Raise.TypeError(ctx, "quantize() argument must be a Decimal");
        }

        private static string AsRounding(EvalContext ctx, ScriptValue v)
        {
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, "rounding must be a string");
            switch (s.Value)
            {
                case "ROUND_CEILING": case "ROUND_FLOOR": case "ROUND_UP": case "ROUND_DOWN":
                case "ROUND_HALF_UP": case "ROUND_HALF_DOWN": case "ROUND_HALF_EVEN": case "ROUND_05UP":
                    return s.Value;
                default:
                    throw Raise.TypeError(ctx, "invalid rounding mode");
            }
        }

        // ---- normalize / as_tuple / adjusted ----

        // Trailing zeros come off the coefficient and go onto the exponent, so 100 normalizes to 1E+2
        // and 1.000 to 1; a zero of any exponent normalizes to a plain 0.
        private ScriptValue Normalize(EvalContext ctx)
        {
            BigInteger m;
            int exp;
            bool neg;
            Coeff(out m, out exp, out neg);
            if (m.IsZero)
                return Make(ctx, BigInteger.Zero, 0, neg);
            while (m % 10 == 0)
            {
                m /= 10;
                exp++;
                ctx.Budget.Step();
            }
            return Fix(ctx, m, exp, neg);   // normalize applies the context precision, as in CPython
        }

        private ScriptValue AsTuple(EvalContext ctx)
        {
            BigInteger m;
            int exp;
            bool neg;
            Coeff(out m, out exp, out neg);
            string ds = m.IsZero ? "0" : m.ToString(CultureInfo.InvariantCulture);
            var digits = new ScriptValue[ds.Length];
            for (int i = 0; i < ds.Length; i++)
                digits[i] = ctx.Values.Int(ds[i] - '0');
            ctx.Values.PreCharge(48);
            return new NamedTupleValue(DecimalTupleInfo, new ScriptValue[]
            {
                ctx.Values.Int(neg ? 1 : 0),
                ctx.Values.Tuple(digits),
                ctx.Values.Int(exp),
            });
        }

        // as_tuple() is the DecimalTuple namedtuple (sign, digits, exponent).
        private static readonly string[] DecimalTupleFields = { "sign", "digits", "exponent" };
        private static readonly NamedTupleInfo DecimalTupleInfo = new NamedTupleInfo("DecimalTuple", DecimalTupleFields,
            NamedTupleValue.BuildInstanceType("DecimalTuple", DecimalTupleFields));

        internal int AdjustedInt()
        {
            BigInteger m;
            int exp;
            bool neg;
            Coeff(out m, out exp, out neg);
            int digitCount = m.IsZero ? 1 : DecimalBits.SignificantDigits(m);
            return exp + digitCount - 1;
        }

        // to-engineering-string: the same rendering, except that an exponent shown is a multiple of three
        // and one to three digits stand before the point.
        private string ToEngString()
        {
            BigInteger coeff;
            int exp;
            bool neg;
            Coeff(out coeff, out exp, out neg);
            string digits = coeff.ToString(CultureInfo.InvariantCulture);
            int adjusted = exp + digits.Length - 1;
            if (exp <= 0 && adjusted >= -6)
                return ToDecimalString();
            int e3 = adjusted - ((adjusted % 3) + 3) % 3;
            int lead = adjusted - e3 + 1;
            if (digits.Length < lead)
                digits += new string('0', lead - digits.Length);
            string body = digits.Length > lead
                ? digits.Substring(0, lead) + "." + digits.Substring(lead)
                : digits;
            if (e3 != 0)
                body += "E" + (e3 >= 0 ? "+" : "-") + Math.Abs((long)e3).ToString(CultureInfo.InvariantCulture);
            return neg ? "-" + body : body;
        }

        // ---- predicates / classification ----

        private string NumberClass()
        {
            if (Value == 0m)
                return IsSigned() ? "-Zero" : "+Zero";
            return Value < 0m ? "-Normal" : "+Normal";
        }

        private bool IsSigned()
        {
            BigInteger m;
            int s;
            bool neg;
            Parts(out m, out s, out neg);
            return neg;
        }

        private ScriptValue CopySign(EvalContext ctx, ScriptValue[] args)
        {
            if (args.Length < 1)
                throw Raise.TypeError(ctx, "copy_sign() missing required argument");
            decimal other = OtherDecimal(ctx, args[0]);
            decimal mag = Math.Abs(Value);
            bool otherNeg;
            if (other != 0m)
                otherNeg = other < 0m;
            else
            {
                BigInteger om;
                int os;
                DecimalBits.Unpack(other, out om, out os, out otherNeg);
            }
            return FromExp(ctx, otherNeg ? -mag : mag, Exp);
        }

        private ScriptValue SameQuantum(EvalContext ctx, ScriptValue[] args)
        {
            return ctx.Values.Bool(Exp == TargetExp(ctx, args[0]));
        }

        // ---- min / max / compare ----

        // The winner comes back as it was, exponent included, so max(Decimal('1E+2'), 1) is 1E+2.
        private ScriptValue MinMax(EvalContext ctx, ScriptValue[] args, bool max, bool byMag)
        {
            if (args.Length < 1)
                throw Raise.TypeError(ctx, "min/max() missing required argument");
            decimal other = OtherDecimal(ctx, args[0]);
            decimal a = byMag ? Math.Abs(Value) : Value;
            decimal b = byMag ? Math.Abs(other) : other;
            bool pickOther = max ? b > a : b < a;
            if (!pickOther)
                return this;
            DecimalValue dv = args[0] as DecimalValue;
            return dv != null ? dv : (ScriptValue)FromDecimal(ctx, other);
        }

        private ScriptValue Compare(EvalContext ctx, ScriptValue[] args)
        {
            decimal other = OtherDecimal(ctx, args[0]);
            return FromDecimal(ctx, Value.CompareTo(other));
        }

        // ---- __format__ ----

        protected internal override ScriptValue FormatSpecCore(string spec, EvalContext ctx)
        {
            if (spec.Length == 0)
                return ctx.Values.Str(ToDecimalString());
            FormatSpec f = PyFormat.ParseSpec(ctx, spec);
            char type = f.Type;
            bool percent = type == '%';
            if (type != '\0' && type != 'f' && type != 'F' && !percent)
                throw Raise.ValueError(ctx, "Unknown format code '" + type + "' for object of type 'decimal.Decimal'");

            // No type and no precision: the value's own rendering, padded — so 1E+2 stays 1E+2 under
            // an alignment spec instead of being written out.
            if (type == '\0' && f.Precision < 0)
            {
                string plain = ToDecimalString();
                bool minus = plain.Length > 0 && plain[0] == '-';
                string digitsOnly = minus ? plain.Substring(1) : plain;
                if (f.GroupChar != '\0')
                    digitsOnly = PyFormat.GroupNumber(digitsOnly, f.GroupChar);
                string s0 = minus ? "-" : f.Sign == '+' ? "+" : f.Sign == ' ' ? " " : "";
                return ctx.Values.Str(PyFormat.PadNumeric(ctx, s0, "", digitsOnly, f));
            }

            BigInteger m;
            int scale;
            bool neg;
            Parts(out m, out scale, out neg);
            BigInteger unscaled = m;
            if (percent)
            {
                scale -= 2;   // multiply by 100
                if (scale < 0)
                {
                    unscaled *= BigInteger.Pow(10, -scale);
                    scale = 0;
                }
            }

            int nd = (type == 'f' || type == 'F' || percent)
                ? (f.Precision >= 0 ? f.Precision : 6)
                : (f.Precision >= 0 ? f.Precision : scale);
            if (nd < scale)
            {
                unscaled = DecimalBits.RoundUnscaledToScale(unscaled, scale, nd);
                scale = nd;
            }
            else if (nd > scale)
            {
                unscaled *= BigInteger.Pow(10, nd - scale);
                scale = nd;
            }

            string digits = unscaled.ToString(CultureInfo.InvariantCulture);
            string body;
            if (nd == 0)
                body = digits;
            else if (digits.Length > nd)
                body = digits.Substring(0, digits.Length - nd) + "." + digits.Substring(digits.Length - nd);
            else
                body = "0." + new string('0', nd - digits.Length) + digits;

            if (f.GroupChar != '\0')
                body = PyFormat.GroupNumber(body, f.GroupChar);
            if (percent)
                body += "%";

            string sign = neg ? "-" : f.Sign == '+' ? "+" : f.Sign == ' ' ? " " : "";
            return ctx.Values.Str(PyFormat.PadNumeric(ctx, sign, "", body, f));
        }

        // ---- slots ----

        private static Dictionary<string, SlotDescriptor> BuildSlots()
        {
            var d = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal)
            {
                ["quantize"] = MK("quantize", new[] { "exp", "rounding", "context" }, (self, a, c) => self.Quantize(c, a)),
                ["to_integral"] = MK("to_integral", new[] { "rounding", "context" }, (self, a, c) => self.ToIntegral(c, a)),
                ["to_integral_value"] = MK("to_integral_value", new[] { "rounding", "context" }, (self, a, c) => self.ToIntegral(c, a)),
                ["to_integral_exact"] = MK("to_integral_exact", new[] { "rounding", "context" }, (self, a, c) => self.ToIntegral(c, a)),
                ["normalize"] = M("normalize", (self, a, c) => self.Normalize(c)),
                ["as_tuple"] = M("as_tuple", (self, a, c) => self.AsTuple(c)),
                ["adjusted"] = M("adjusted", (self, a, c) => c.Values.Int(self.AdjustedInt())),
                ["number_class"] = M("number_class", (self, a, c) => c.Values.Str(self.NumberClass())),
                ["is_finite"] = M("is_finite", (self, a, c) => c.Values.Bool(true)),
                ["is_nan"] = M("is_nan", (self, a, c) => c.Values.Bool(false)),
                ["is_qnan"] = M("is_qnan", (self, a, c) => c.Values.Bool(false)),
                ["is_snan"] = M("is_snan", (self, a, c) => c.Values.Bool(false)),
                ["is_infinite"] = M("is_infinite", (self, a, c) => c.Values.Bool(false)),
                ["is_signed"] = M("is_signed", (self, a, c) => c.Values.Bool(self.IsSigned())),
                ["is_zero"] = M("is_zero", (self, a, c) => c.Values.Bool(self.Value == 0m)),
                ["is_normal"] = M("is_normal", (self, a, c) => c.Values.Bool(self.Value != 0m)),
                ["is_subnormal"] = M("is_subnormal", (self, a, c) => c.Values.Bool(false)),
                ["is_canonical"] = M("is_canonical", (self, a, c) => c.Values.Bool(true)),
                ["canonical"] = M("canonical", (self, a, c) => self),
                ["conjugate"] = M("conjugate", (self, a, c) => self),
                ["radix"] = M("radix", (self, a, c) => FromDecimal(c, 10m)),
                ["scaleb"] = MK("scaleb", new[] { "other", "context" }, (self, a, c) => self.ScaleB(c, a)),
                ["shift"] = MK("shift", new[] { "other", "context" }, (self, a, c) => self.Shift(c, a)),
                ["as_integer_ratio"] = M("as_integer_ratio", (self, a, c) => self.AsIntegerRatio(c)),
                ["sqrt"] = MK("sqrt", new[] { "context" }, (self, a, c) => DecimalMath.Sqrt(c, self)),
                ["ln"] = MK("ln", new[] { "context" }, (self, a, c) => DecimalMath.Ln(c, self)),
                ["log10"] = MK("log10", new[] { "context" }, (self, a, c) => DecimalMath.Log10(c, self)),
                ["exp"] = MK("exp", new[] { "context" }, (self, a, c) => DecimalMath.Exp(c, self)),
                ["copy_abs"] = M("copy_abs", (self, a, c) => FromExp(c, Math.Abs(self.Value), self.Exp)),
                ["copy_negate"] = M("copy_negate", (self, a, c) => FromExp(c, -self.Value, self.Exp)),
                ["copy_sign"] = M("copy_sign", (self, a, c) => self.CopySign(c, a)),
                ["same_quantum"] = M("same_quantum", (self, a, c) => self.SameQuantum(c, a)),
                ["min"] = M("min", (self, a, c) => self.MinMax(c, a, false, false)),
                ["max"] = M("max", (self, a, c) => self.MinMax(c, a, true, false)),
                ["min_mag"] = M("min_mag", (self, a, c) => self.MinMax(c, a, false, true)),
                ["max_mag"] = M("max_mag", (self, a, c) => self.MinMax(c, a, true, true)),
                ["compare"] = M("compare", (self, a, c) => self.Compare(c, a)),
                ["compare_signal"] = M("compare_signal", (self, a, c) => self.Compare(c, a)),
                ["logb"] = M("logb", (self, a, c) => FromDecimal(c, self.AdjustedInt())),
                ["to_eng_string"] = M("to_eng_string", (self, a, c) => c.Values.Str(self.ToEngString())),
            };
            return d;
        }

        // Positional implementation that also accepts CPython's keyword spelling (quantize(exp, rounding=...)).
        private static SlotDescriptor MK(string name, string[] names, Func<DecimalValue, ScriptValue[], EvalContext, ScriptValue> f)
        {
            return SlotDescriptor.MakeMethod(name, (self, a, kw, c) => f((DecimalValue)self, StrMethods.WithKw(c, a, kw, name, names), c));
        }

        private static SlotDescriptor M(string name, Func<DecimalValue, ScriptValue[], EvalContext, ScriptValue> f)
        {
            return SlotDescriptor.MakeMethod(name, (self, a, kw, c) => f((DecimalValue)self, a, c));
        }
    }
}

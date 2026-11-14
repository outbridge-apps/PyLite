using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
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
            if (m.IsZero)
                return Make(ctx, BigInteger.Zero, targetExp, neg);   // a zero's exponent may be ±10^9: no power of ten
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
            int prec = f.Precision;
            switch (type)
            {
                case '\0':
                    type = 'G';   // the context's capitals
                    goto case 'g';
                case 'n':
                    type = 'g';   // the C locale: no separators
                    goto case 'g';
                case 'g':
                case 'G':
                    if (prec == 0)
                        prec = 1;
                    break;
                case 'e': case 'E': case 'f': case 'F': case '%':
                    break;
                default:
                    throw Raise.ValueError(ctx, "invalid format string");
            }
            if (prec > 0)
                ctx.Values.CheckStrLen((long)prec + 64);   // the cap only: the str made at the end charges itself

            BigInteger coeff;
            int exp;
            bool neg;
            Coeff(out coeff, out exp, out neg);
            if (type == '%')
                exp += 2;
            // Lib/_pydecimal.py's __format__: round in the context's mode, then place the point. A coefficient
            // padded for a wide precision keeps its zeros as a count, written out as text, never as 10**n.
            RoundingMode mode = ctx.DecimalCtx.Mode;
            int zeros = 0;
            if (prec >= 0)
            {
                if (type == 'e' || type == 'E')
                    RoundToDigits(ref coeff, ref exp, ref zeros, prec + 1, neg, mode);
                else if (type == 'f' || type == 'F' || type == '%')
                    RescaleTo(ref coeff, ref exp, ref zeros, -prec, neg, mode);
                else if (DecimalBits.SignificantDigits(coeff) > prec)
                    RoundToDigits(ref coeff, ref exp, ref zeros, prec, neg, mode);
            }
            bool fixedPoint = type == 'f' || type == 'F' || type == '%';
            if (coeff.IsZero && exp > 0 && fixedPoint)
                exp = 0;

            string digits = coeff.ToString(CultureInfo.InvariantCulture);
            long digitCount = digits.Length + (long)zeros;
            long leftDigits = (long)exp + digitCount;
            long dot;
            if (type == 'e' || type == 'E')
                dot = coeff.IsZero && prec >= 0 ? 1 - prec : 1;
            else if (fixedPoint || (exp <= 0 && leftDigits > -6))
                dot = leftDigits;
            else
                dot = 1;
            long shown = leftDigits - dot;
            string exponent = shown != 0 || type == 'e' || type == 'E'
                ? (type == 'e' || type == 'g' ? "e" : "E") + (shown < 0 ? "-" : "+") + Math.Abs(shown).ToString(CultureInfo.InvariantCulture)
                : "";

            // One buffer of the exact size: a wide result is nearly all zeros, and every copy of them went uncharged.
            long intLength = Math.Max(dot, 1), leadZeros = Math.Max(-dot, 0);
            long fraction = leadZeros + Math.Max(digitCount - Math.Max(dot, 0), 0);
            bool point = fraction > 0 || f.AltForm;
            long size = intLength + (point ? 1 : 0) + fraction + exponent.Length + (type == '%' ? 1 : 0);
            ctx.Values.CheckStrLen(size);
            var text = new char[size];
            int at = PutDigits(text, 0, dot > 0 ? digits : "0", 0, intLength);
            if (point)
                text[at++] = '.';
            at = PutDigits(text, at, "", 0, leadZeros);
            at = PutDigits(text, at, digits, Math.Max(dot, 0), digitCount);
            exponent.CopyTo(0, text, at, exponent.Length);
            if (type == '%')
                text[at + exponent.Length] = '%';

            string sign = neg ? "-" : f.Sign == '+' ? "+" : f.Sign == ' ' ? " " : "";
            string body;
            if (f.GroupChar != '\0')
            {
                string intPart = new string(text, 0, (int)intLength), tail = new string(text, (int)intLength, text.Length - (int)intLength);
                // As _insert_thousands_sep: the '0' flag widens the integer part, separators included.
                if (f.ZeroPad && f.Width > 0)
                {
                    ctx.Values.CheckStrLen(f.Width);
                    int minInt = f.Width - tail.Length - sign.Length, k = intPart.Length;
                    while (k + (k - 1) / 3 < minInt)
                        k++;
                    intPart = new string('0', k - intPart.Length) + intPart;
                }
                body = PyFormat.GroupNumber(intPart, f.GroupChar) + tail;
            }
            else
            {
                body = new string(text);
            }
            return ctx.Values.Str(PyFormat.PadNumeric(ctx, sign, "", body, f));
        }

        // Positions [from, to) of the coefficient's digits, then zeros: what follows the digits is their pad.
        private static int PutDigits(char[] into, int at, string digits, long from, long to)
        {
            for (long i = from; i < to; i++)
                into[at++] = i < digits.Length ? digits[(int)i] : '0';
            return at;
        }

        // _pydecimal's _rescale: the value (coeff followed by `zeros` zeros) at exponent newExp, rounded in the
        // given mode or padded. Rounding first drops pad zeros, which are exact; the backend's coefficient has at
        // most 29 digits, so the divisor stays small.
        private static void RescaleTo(ref BigInteger coeff, ref int exp, ref int zeros, int newExp, bool neg, RoundingMode mode)
        {
            if (coeff.IsZero || newExp <= exp)
            {
                if (!coeff.IsZero)
                    zeros += exp - newExp;
                exp = newExp;
                return;
            }
            int drop = newExp - exp;
            if (drop <= zeros)
            {
                zeros -= drop;
                exp = newExp;
                return;
            }
            exp += zeros;
            drop -= zeros;
            zeros = 0;
            BigInteger divisor = BigInteger.Pow(10, drop), r;
            BigInteger q = BigInteger.DivRem(coeff, divisor, out r);
            coeff = DecimalRound.Apply(q, r, divisor, neg, mode);
            exp = newExp;
        }

        // _pydecimal's _round: to `places` significant digits; a carry that adds a digit (99.97 -> 100.0)
        // is rescaled once more.
        private static void RoundToDigits(ref BigInteger coeff, ref int exp, ref int zeros, int places, bool neg, RoundingMode mode)
        {
            if (coeff.IsZero)
                return;
            int adjusted = exp + DecimalBits.SignificantDigits(coeff) + zeros - 1;
            RescaleTo(ref coeff, ref exp, ref zeros, adjusted + 1 - places, neg, mode);
            int after = exp + DecimalBits.SignificantDigits(coeff) + zeros - 1;
            if (after != adjusted)
                RescaleTo(ref coeff, ref exp, ref zeros, after + 1 - places, neg, mode);
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
                ["copy_sign"] = M1("copy_sign", (self, a, c) => self.CopySign(c, a)),
                ["same_quantum"] = M1("same_quantum", (self, a, c) => self.SameQuantum(c, a)),
                ["min"] = M("min", (self, a, c) => self.MinMax(c, a, false, false)),
                ["max"] = M("max", (self, a, c) => self.MinMax(c, a, true, false)),
                ["min_mag"] = M("min_mag", (self, a, c) => self.MinMax(c, a, false, true)),
                ["max_mag"] = M("max_mag", (self, a, c) => self.MinMax(c, a, true, true)),
                ["compare"] = M1("compare", (self, a, c) => self.Compare(c, a)),
                ["compare_signal"] = M1("compare_signal", (self, a, c) => self.Compare(c, a)),
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

        // A method of one required operand: called bare, it read args[0] past the end (an engine fault).
        private static SlotDescriptor M1(string name, Func<DecimalValue, ScriptValue[], EvalContext, ScriptValue> f)
        {
            return SlotDescriptor.MakeMethod(name, Args.BoundKeywords(name, 1, (self, a, kw, c) =>
            {
                if (a.Length < 1)
                    throw Raise.TypeError(c, name + "() missing required argument other (pos 1)");
                return f((DecimalValue)self, a, c);
            }, "other", "context"));
        }

        // CPython's signatures: no argument, (context=None), or (other, context=None). An unknown keyword or an extra
        // positional was dropped silently (normalize(zzz=1), as_tuple(5)).
        // (A switch, not static tables: the slots may be built from another part's static initializer.)
        private static SlotDescriptor M(string name, Func<DecimalValue, ScriptValue[], EvalContext, ScriptValue> f)
        {
            BuiltinDelegate body = (self, a, kw, c) => f((DecimalValue)self, a, c);
            switch (name)
            {
                case "min":
                case "max":
                case "min_mag":
                case "max_mag":
                    return SlotDescriptor.MakeMethod(name, Args.BoundKeywords(name, 1, body, "other", "context"));
                case "normalize":
                case "number_class":
                case "is_normal":
                case "is_subnormal":
                case "logb":
                case "to_eng_string":
                    return SlotDescriptor.MakeMethod(name, Args.BoundKeywords(name, body, "context"));
                default:
                    return SlotDescriptor.MakeMethod(name, Args.Positional(name, 0, 0, body));
            }
        }
    }
}

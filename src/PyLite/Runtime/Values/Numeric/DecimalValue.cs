using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // decimal.Decimal. Immutable, no NaN/Inf/sNaN. A value is a COEFFICIENT and an
    // EXPONENT, as in the arithmetic spec, but the magnitude is carried by a System.Decimal: `Value` is the
    // number itself and `Exp` is the Python exponent, which the backend's own scale cannot express when it
    // is positive (`1E+2` is 100 with Exp 2). Every operation settles the result's exponent to the spec's
    // "ideal" one, so a trailing zero the arithmetic produced survives into the repr the way CPython's does.
    // Exact cross-type comparison via the equivalent fraction coefficient/10^scale. // truncates and
    // % takes the dividend's sign (real CPython decimal; the doc's floor pins were an error).
    internal sealed partial class DecimalValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("decimal.Decimal", BuildSlots());

        public readonly decimal Value;

        // The Python exponent. Invariant for a nonzero value: Exp >= -scale(Value), and the mantissa is
        // divisible by 10^(Exp + scale(Value)) — which is what makes the coefficient a whole number. A
        // zero is exempt: it carries whatever exponent it was written with (0E-30).
        public readonly int Exp;

        private DecimalValue(decimal v, int exp) { Value = v; Exp = exp; }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        // The natural reading of a backend value: exponent = -scale, which is what every value that never
        // met a positive exponent has.
        internal static DecimalValue FromDecimal(EvalContext ctx, decimal d)
        {
            ctx.Values.PreCharge(48);
            return new DecimalValue(d, -DecimalBits.ScaleOf(d));
        }

        private static DecimalValue FromExp(EvalContext ctx, decimal d, int exp)
        {
            ctx.Values.PreCharge(48);
            return new DecimalValue(d, exp);
        }

        // (coefficient, Python exponent) -> a value carrying that exponent. A positive exponent folds into
        // the mantissa (the backend has no exponent above the point); below -28 the coefficient is clamped
        // to what the backend holds, and a clamp that erases it is the Underflow signal.
        internal static DecimalValue Make(EvalContext ctx, BigInteger coeff, int exp, bool neg)
        {
            ctx.Values.PreCharge(48);
            if (coeff.Sign < 0)
            {
                coeff = -coeff;
                neg = !neg;
            }
            if (exp >= 0)
            {
                if (coeff.IsZero)
                    return new DecimalValue(DecimalBits.Pack(ctx, BigInteger.Zero, 0, neg), exp);
                if (exp > 29)
                    throw Overflow(ctx);
                BigInteger m = coeff * BigInteger.Pow(10, exp);
                if (m > DecimalBits.MaxMantissa)
                    throw Overflow(ctx);
                return new DecimalValue(DecimalBits.Pack(ctx, m, 0, neg), exp);
            }
            int scale = -exp;
            if (scale > 28)
            {
                if (coeff.IsZero)
                    return new DecimalValue(DecimalBits.Pack(ctx, BigInteger.Zero, 28, neg), exp);   // a zero carries any exponent
                int drop = scale - 28;
                if (drop > DecimalBits.SignificantDigits(coeff) + 1)
                    coeff = BigInteger.Zero;
                else
                    coeff = DecimalBits.RoundUnscaledToScale(coeff, scale, 28);
                if (coeff.IsZero)
                    throw DecimalBits.Underflow(ctx);
                scale = 28;
                exp = -28;
            }
            return new DecimalValue(DecimalBits.Pack(ctx, coeff, scale, neg), exp);
        }

        // The coefficient and exponent the spec talks about, recovered from the backend value.
        internal void Coeff(out BigInteger coeff, out int exp, out bool neg)
        {
            int scale;
            DecimalBits.Unpack(Value, out coeff, out scale, out neg);
            exp = Exp;
            int up = Exp + scale;
            if (up > 0 && !coeff.IsZero)
                coeff /= BigInteger.Pow(10, up);
        }

        // The exponent an operation should hand back: the spec's ideal one, reached only as far as the
        // result's own trailing zeros allow — a digit the arithmetic produced is never thrown away.
        private static DecimalValue Settled(EvalContext ctx, decimal v, int ideal)
        {
            return Settled(ctx, v, DecimalBits.ScaleOf(v), ideal);
        }

        // The scale is already in hand on the arithmetic path, where this runs per operation.
        private static DecimalValue Settled(EvalContext ctx, decimal v, int scale, int ideal)
        {
            int natural = -scale;
            if (ideal <= natural)
                return FromExp(ctx, v, natural);
            return FromExp(ctx, v, natural + DecimalBits.TrailingZeros(v, ideal - natural));
        }

        // ---- constructors ----

        internal static ScriptValue Construct(EvalContext ctx, ScriptValue[] args, KwArgs kw)
        {
            ctx.Budget.Step();
            if (args.Length == 0)
                return FromDecimal(ctx, 0m);
            ScriptValue v = args[0];
            DecimalValue dv = v as DecimalValue;
            if (dv != null)
                return dv;
            StrValue sv = v as StrValue;
            if (sv != null)
            {
                int exp;
                decimal parsed = DecimalParse.Parse(ctx, sv.Value, out exp);
                return FromExp(ctx, parsed, exp);
            }
            IntValue iv = v as IntValue;
            if (iv != null)
                return FromDecimal(ctx, IntToDecimal(ctx, iv.Value));
            BoolValue bv = v as BoolValue;
            if (bv != null)
                return FromDecimal(ctx, bv.Value ? 1m : 0m);
            FloatValue fv = v as FloatValue;
            if (fv != null)
                return FromFloat(ctx, fv.Value);
            throw Raise.TypeError(ctx, "conversion from " + v.PyTypeName + " to Decimal is not supported");
        }

        // math.floor/ceil/trunc integrate through the value hooks (Decimal.__floor__ etc.).
        protected internal override ScriptValue FloorCore(EvalContext ctx) { return ctx.Values.Int(new BigInteger(Math.Floor(Value))); }
        protected internal override ScriptValue CeilCore(EvalContext ctx) { return ctx.Values.Int(new BigInteger(Math.Ceiling(Value))); }
        protected internal override ScriptValue TruncCore(EvalContext ctx) { return ctx.Values.Int(new BigInteger(Math.Truncate(Value))); }

        // round(d) -> int, round(d, n) -> Decimal, both through the context rounding mode as in CPython.
        // round(d, n) is a quantize to exponent -n, so it also PADS (round(Decimal('1.5'), 3) -> 1.500) and
        // a negative n lands on a positive exponent (round(Decimal('1234'), -2) -> 1.2E+3).
        protected internal override ScriptValue RoundCore(ScriptValue ndigits, EvalContext ctx)
        {
            RoundingMode mode = ctx.DecimalCtx.Mode;
            if (ndigits == null || ndigits.Kind == ValueKind.None)
                return ctx.Values.Int(new BigInteger(((DecimalValue)DoQuantize(ctx, 0, mode)).Value));
            if (ndigits.Kind != ValueKind.Int && ndigits.Kind != ValueKind.Bool)
                throw Raise.TypeError(ctx, "'" + ndigits.PyTypeName + "' object cannot be interpreted as an integer");
            BigInteger n = NumericOps.AsBigInteger(ndigits);
            if (n > 60 || n < -60)
                throw InvalidOp(ctx);
            return DoQuantize(ctx, -(int)n, mode);
        }

        internal static DecimalValue FromFloat(EvalContext ctx, double f)
        {
            if (double.IsNaN(f))
                throw Raise.Make(ctx, PyExceptionTypes.DecimalInvalidOperation, "cannot convert NaN to Decimal");
            if (double.IsInfinity(f))
                throw Raise.Make(ctx, PyExceptionTypes.DecimalOverflow, "cannot convert Infinity to Decimal");
            if (f == 0.0)
                return FromDecimal(ctx, 0m);
            bool neg;
            ulong mant;
            int exp2;
            FloatRepr.Decompose(f, out neg, out mant, out exp2);
            if (exp2 >= 0)
            {
                BigInteger n = (BigInteger)mant << exp2;
                return FromDecimal(ctx, DecimalBits.Pack(ctx, n, 0, neg));
            }
            int k = -exp2;
            ctx.Values.PreCharge(2L * (k + 40));
            ctx.Budget.Step(1 + k / 8);
            BigInteger d = mant * BigInteger.Pow(5, k);   // f = mant/2^k = mant*5^k / 10^k
            int scale = k;
            // strip trailing zeros so a clean float gets its minimal scale (0.5 -> scale 1, 1.0 -> scale 0).
            while (scale > 0 && d % 10 == 0)
            {
                d /= 10;
                scale--;
            }
            ClampToDecimal(ref d, ref scale);
            if (d.IsZero)
                throw DecimalBits.Underflow(ctx);   // f is not 0 here, so it fell through the floor
            return FromDecimal(ctx, DecimalBits.Pack(ctx, d, scale, neg));
        }

        private static void ClampToDecimal(ref BigInteger m, ref int scale)
        {
            if (scale > 28)
            {
                m = DecimalBits.RoundUnscaledToScale(m, scale, 28);
                scale = 28;
            }
            while (m > DecimalBits.MaxMantissa && scale > 0)
            {
                m = DecimalBits.RoundUnscaledToScale(m, scale, scale - 1);
                scale--;
            }
        }

        internal static decimal IntToDecimal(EvalContext ctx, BigInteger n)
        {
            if (n < DecimalMinBig || n > DecimalMaxBig)
                throw Raise.Make(ctx, PyExceptionTypes.DecimalOverflow, "[<class 'decimal.Overflow'>]");
            return (decimal)n;
        }

        private static readonly BigInteger DecimalMaxBig = new BigInteger(decimal.MaxValue);
        private static readonly BigInteger DecimalMinBig = new BigInteger(decimal.MinValue);

        internal void ToRational(out BigInteger num, out BigInteger den)
        {
            BigInteger m;
            int scale;
            bool neg;
            DecimalBits.Unpack(Value, out m, out scale, out neg);
            num = neg ? -m : m;
            den = BigInteger.Pow(10, scale);
        }

        // ---- arithmetic ----

        // Every arithmetic result is rounded to the context precision with the context mode, as in CPython.
        // The backend computes at its own 28-29 digits first, so a lower prec rounds twice; the two can
        // disagree only when the 28-digit intermediate lands exactly on the prec boundary.
        private static DecimalValue Rounded(EvalContext ctx, decimal v, int ideal)
        {
            int prec = ctx.DecimalCtx.Prec;
            uint lo, mid, hi;
            int scale;
            bool neg;
            DecimalBits.Read(v, out lo, out mid, out hi, out scale, out neg);
            if (DecimalBits.FitsPrec(lo, mid, hi, prec))
                return Settled(ctx, v, scale, ideal);
            int drop = DecimalBits.Digits(lo, mid, hi, prec) - prec;
            RoundingMode mode = ctx.DecimalCtx.Mode;
            // The common shape - a result a digit too long, any mode - stays in 32-bit integer arithmetic.
            // A drop wider than a uint divisor, a carry past 96 bits, or a result wider than prec in its
            // integer part takes the exact BigInteger route below.
            if (scale >= drop && DecimalBits.TryRound(ref lo, ref mid, ref hi, drop, neg, mode))
            {
                int kept = scale - drop;
                if (kept > 0 && !DecimalBits.FitsPrec(lo, mid, hi, prec))   // rounding up grew it: 999 -> 1000
                {
                    DecimalBits.DivRem(ref lo, ref mid, ref hi, 10);         // exact: the mantissa is 10^prec
                    kept--;
                }
                return Settled(ctx, new decimal(unchecked((int)lo), unchecked((int)mid), unchecked((int)hi), neg, (byte)kept), kept, ideal);
            }
            return RoundedSlow(ctx, v, prec, drop, mode, ideal);
        }

        private static DecimalValue RoundedSlow(EvalContext ctx, decimal v, int prec, int drop, RoundingMode mode, int ideal)
        {
            BigInteger m;
            int scale;
            bool neg;
            DecimalBits.Unpack(v, out m, out scale, out neg);
            BigInteger divisor = BigInteger.Pow(10, drop);
            BigInteger r;
            BigInteger q = BigInteger.DivRem(m, divisor, out r);
            q = DecimalRound.Apply(q, r, divisor, neg, mode);
            if (DecimalBits.SignificantDigits(q) > prec)   // rounding up grew the number: 999 -> 1000
            {
                q /= 10;
                drop++;
            }
            // The rounded-away digits become the exponent, which is where CPython puts them too: a result
            // that no longer fits prec below the point reads as 1.2346E+8, not 123460000. Rising toward
            // the ideal exponent past that costs trailing zeros, and never a digit that carries value.
            int exp = drop - scale;
            if (q.IsZero)
                return Make(ctx, q, exp < ideal ? ideal : exp, neg);
            while (exp < ideal && (q % 10).IsZero)
            {
                q /= 10;
                exp++;
            }
            return Make(ctx, q, exp, neg);
        }

        // The backend has no exponent below 1e-28, so a small enough product or quotient collapses to
        // zero. CPython has no floor there, so answering 0 would be the one silently WRONG answer this
        // backend can give: these three raise the signal instead. Addition cannot reach it - every value
        // is a multiple of 1e-28, so a sum is zero only by exact cancellation.
        private static decimal Prod(EvalContext ctx, decimal x, decimal y)
        {
            decimal r = x * y;
            if (DecimalBits.IsZero(r) && x != 0m && y != 0m)
                throw Underflow(ctx);
            return r;
        }

        private static decimal Quot(EvalContext ctx, decimal x, decimal y)
        {
            decimal r = x / y;
            if (DecimalBits.IsZero(r) && x != 0m)
                throw Underflow(ctx);
            return r;
        }

        private static ScriptException Underflow(EvalContext ctx) { return DecimalBits.Underflow(ctx); }

        // // and % are exact in CPython: a result the precision cannot hold is refused, never rounded.
        private static DecimalValue Exact(EvalContext ctx, decimal v, int ideal)
        {
            if (!DecimalBits.FitsPrec(v, ctx.DecimalCtx.Prec))
                throw InvalidOp(ctx);
            return Settled(ctx, v, ideal);
        }

        // The remainder itself is small, but CPython refuses it when the integer QUOTIENT does not fit.
        private static void CheckQuotient(EvalContext ctx, decimal x, decimal y)
        {
            if (!DecimalBits.FitsPrec(decimal.Truncate(x / y), ctx.DecimalCtx.Prec))
                throw InvalidOp(ctx);
        }

        private bool TryOperand(EvalContext ctx, ScriptValue v, out decimal d, out int exp)
        {
            DecimalValue dv = v as DecimalValue;
            if (dv != null)
            {
                d = dv.Value;
                exp = dv.Exp;
                return true;
            }
            exp = 0;
            IntValue iv = v as IntValue;
            if (iv != null)
            {
                d = IntToDecimal(ctx, iv.Value);
                return true;
            }
            BoolValue bv = v as BoolValue;
            if (bv != null)
            {
                d = bv.Value ? 1m : 0m;
                return true;
            }
            d = 0m;
            return false;   // float / Fraction / other -> generic TypeError
        }

        private bool TryOperand(EvalContext ctx, ScriptValue v, out decimal d)
        {
            int exp;
            return TryOperand(ctx, v, out d, out exp);
        }

        protected internal override ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        {
            if (op == PyBinOp.Pow)
                return PowOp(ctx, other, reflected);
            decimal b;
            int be;
            if (!TryOperand(ctx, other, out b, out be))
                return null;
            decimal x = reflected ? b : Value;
            decimal y = reflected ? Value : b;
            int ex = reflected ? be : Exp;
            int ey = reflected ? Exp : be;
            try
            {
                switch (op)
                {
                    case PyBinOp.Add:
                        return Rounded(ctx, x + y, Math.Min(ex, ey));
                    case PyBinOp.Sub:
                        return Rounded(ctx, x - y, Math.Min(ex, ey));
                    case PyBinOp.Mul:
                        return Rounded(ctx, Prod(ctx, x, y), ex + ey);
                    case PyBinOp.TrueDiv:
                        if (y == 0m)
                            throw DivByZero(ctx);
                        return Rounded(ctx, Quot(ctx, x, y), ex - ey);
                    case PyBinOp.FloorDiv:
                        if (y == 0m)
                            throw DivByZero(ctx);
                        return Exact(ctx, decimal.Truncate(x / y), 0);
                    case PyBinOp.Mod:
                        if (y == 0m)
                            throw InvalidOp(ctx);
                        CheckQuotient(ctx, x, y);
                        return Settled(ctx, x % y, Math.Min(ex, ey));
                    default:
                        return null;
                }
            }
            catch (OverflowException)
            {
                throw Overflow(ctx);
            }
        }

        protected internal override ScriptValue DivmodCore(ScriptValue other, bool reflected, EvalContext ctx)
        {
            decimal b;
            int be;
            if (!TryOperand(ctx, other, out b, out be))
                return null;
            decimal x = reflected ? b : Value;
            decimal y = reflected ? Value : b;
            int ex = reflected ? be : Exp;
            int ey = reflected ? Exp : be;
            if (y == 0m)
                throw DivByZero(ctx);
            try
            {
                return ctx.Values.Tuple(new ScriptValue[] { Exact(ctx, decimal.Truncate(x / y), 0), Settled(ctx, x % y, Math.Min(ex, ey)) });
            }
            catch (OverflowException)
            {
                throw Overflow(ctx);
            }
        }

        // x ** y. An integer exponent (int, or a Decimal that is one) is the exact repeated product; any
        // other Decimal exponent goes through exp(y * ln x), which needs a positive base as in CPython.
        private ScriptValue PowOp(EvalContext ctx, ScriptValue other, bool reflected)
        {
            DecimalValue baseV = this;
            ScriptValue expArg = other;
            if (reflected)
            {
                decimal bd;
                if (!TryOperand(ctx, other, out bd))
                    return null;
                baseV = FromDecimal(ctx, bd);
                expArg = this;
            }

            BigInteger n;
            if (!TryIntegerExponent(expArg, out n))
            {
                DecimalValue dy = expArg as DecimalValue;
                if (dy == null)
                    return null;   // Decimal ** float/Fraction -> TypeError
                return DecimalMath.Power(ctx, baseV, dy);
            }

            if (n.Sign == 0)
                return FromDecimal(ctx, 1m);
            BigInteger absN = BigInteger.Abs(n);
            if (absN > 100000)
                throw Overflow(ctx);
            decimal result = 1m;
            try
            {
                for (BigInteger i = 0; i < absN; i++)
                {
                    result *= baseV.Value;
                    ctx.Budget.Step();
                }
                if (n.Sign > 0)
                {
                    if (result == 0m && baseV.Value != 0m)
                        throw Underflow(ctx);
                    long ideal = (long)baseV.Exp * (long)absN;
                    return Rounded(ctx, result, ideal > 60 ? 60 : (ideal < -60 ? -60 : (int)ideal));
                }
                if (result == 0m)
                    throw DivByZero(ctx);
                return Rounded(ctx, Quot(ctx, 1m, result), -60);
            }
            catch (OverflowException)
            {
                throw Overflow(ctx);
            }
        }

        // An int, a bool, or a Decimal whose value is a whole number: CPython's power takes all three
        // down the exact integer path.
        private static bool TryIntegerExponent(ScriptValue v, out BigInteger n)
        {
            IntValue iv = v as IntValue;
            if (iv != null)
            {
                n = iv.Value;
                return true;
            }
            BoolValue bv = v as BoolValue;
            if (bv != null)
            {
                n = bv.Value ? BigInteger.One : BigInteger.Zero;
                return true;
            }
            DecimalValue dv = v as DecimalValue;
            if (dv != null && decimal.Truncate(dv.Value) == dv.Value)
            {
                n = new BigInteger(dv.Value);
                return true;
            }
            n = BigInteger.Zero;
            return false;
        }

        protected internal override ScriptValue UnaryOpCore(PyUnaryOp op, EvalContext ctx)
        {
            switch (op)
            {
                case PyUnaryOp.Neg:
                    return Rounded(ctx, -Value, Exp);
                case PyUnaryOp.Pos:
                    return Rounded(ctx, Value, Exp);   // the CPython idiom for "apply the context to d"
                default:
                    return null;
            }
        }

        protected internal override ScriptValue AbsCore(EvalContext ctx) { return Rounded(ctx, Math.Abs(Value), Exp); }

        private static ScriptException DivByZero(EvalContext ctx) { return Raise.Make(ctx, PyExceptionTypes.DecimalDivisionByZero, "[<class 'decimal.DivisionByZero'>]"); }
        private static ScriptException InvalidOp(EvalContext ctx) { return Raise.Make(ctx, PyExceptionTypes.DecimalInvalidOperation, "[<class 'decimal.InvalidOperation'>]"); }
        private static ScriptException Overflow(EvalContext ctx) { return Raise.Make(ctx, PyExceptionTypes.DecimalOverflow, "[<class 'decimal.Overflow'>]"); }

        // ---- comparisons / hash ----

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            DecimalValue od = other as DecimalValue;
            if (od != null)
                return Value == od.Value;
            FractionValue fr = other as FractionValue;
            if (fr != null)
            {
                BigInteger n, d;
                ToRational(out n, out d);
                return Rationals.Compare(n, d, fr.Num, fr.Den) == 0;
            }
            return false;
        }

        protected internal override bool TryEqualsAcrossKindCore(ScriptValue other, EvalContext ctx, int depth, out bool equal)
        {
            BigInteger n, d;
            IntValue iv = other as IntValue;
            BoolValue bv = other as BoolValue;
            if (iv != null || bv != null)
            {
                ToRational(out n, out d);
                BigInteger o = iv != null ? iv.Value : (bv.Value ? BigInteger.One : BigInteger.Zero);
                equal = Rationals.Compare(n, d, o, BigInteger.One) == 0;
                return true;
            }
            FloatValue fv = other as FloatValue;
            if (fv != null)
            {
                if (double.IsNaN(fv.Value) || double.IsInfinity(fv.Value))
                {
                    equal = false;
                    return true;
                }
                ToRational(out n, out d);
                BigInteger fn, fd;
                Rationals.FloatToRational(fv.Value, out fn, out fd);
                equal = Rationals.Compare(n, d, fn, fd) == 0;
                return true;
            }
            equal = false;
            return false;
        }

        protected internal override bool TryCompareLeafCore(ScriptValue other, EvalContext ctx, int depth, out int cmp)
        {
            DecimalValue od = other as DecimalValue;
            if (od != null)
            {
                cmp = Value.CompareTo(od.Value);
                return true;
            }
            BigInteger n, d;
            FractionValue fr = other as FractionValue;
            if (fr != null)
            {
                ToRational(out n, out d);
                cmp = Rationals.Compare(n, d, fr.Num, fr.Den);
                return true;
            }
            IntValue iv = other as IntValue;
            BoolValue bv = other as BoolValue;
            if (iv != null || bv != null)
            {
                ToRational(out n, out d);
                BigInteger o = iv != null ? iv.Value : (bv.Value ? BigInteger.One : BigInteger.Zero);
                cmp = Rationals.Compare(n, d, o, BigInteger.One);
                return true;
            }
            FloatValue fv = other as FloatValue;
            if (fv != null)
            {
                if (double.IsNaN(fv.Value))
                {
                    cmp = 0;
                    return false;
                }
                if (double.IsInfinity(fv.Value))
                {
                    cmp = fv.Value > 0 ? -1 : 1;
                    return true;
                }
                ToRational(out n, out d);
                BigInteger fn, fd;
                Rationals.FloatToRational(fv.Value, out fn, out fd);
                cmp = Rationals.Compare(n, d, fn, fd);
                return true;
            }
            cmp = 0;
            return false;
        }

        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            BigInteger n, d;
            ToRational(out n, out d);
            return NumericHash.HashRational(n, d);
        }

        protected internal override bool IsTruthyCore(EvalContext ctx) { return Value != 0m; }

        // ---- str / repr ----

        // to-scientific-string of the arithmetic spec: plain digits while the exponent is at or below zero
        // and the value is no smaller than 1e-6, exponential notation otherwise. That is the rule that
        // makes 1E+2 and 1E-7 print as they do while 0.000001 stays written out.
        internal string ToDecimalString()
        {
            BigInteger coeff;
            int exp;
            bool neg;
            Coeff(out coeff, out exp, out neg);
            string digits = coeff.ToString(CultureInfo.InvariantCulture);
            int adjusted = exp + digits.Length - 1;
            string body;
            if (exp <= 0 && adjusted >= -6)
            {
                if (exp == 0)
                    body = digits;
                else if (digits.Length > -exp)
                    body = digits.Substring(0, digits.Length + exp) + "." + digits.Substring(digits.Length + exp);
                else
                    body = "0." + new string('0', -exp - digits.Length) + digits;
            }
            else
            {
                string mant = digits.Length == 1 ? digits : digits.Substring(0, 1) + "." + digits.Substring(1);
                body = mant + "E" + (adjusted >= 0 ? "+" : "-")
                    + Math.Abs((long)adjusted).ToString(CultureInfo.InvariantCulture);
            }
            return neg ? "-" + body : body;
        }

        protected internal override void StrLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append(ToDecimalString()); }
        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append("Decimal('" + ToDecimalString() + "')"); }
    }
}

using System;
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
            if (v.Kind == ValueKind.Tuple || v.Kind == ValueKind.List)
                return FromTuple(ctx, Modules.Support.Iter.ToArray(ctx, v));
            throw Raise.TypeError(ctx, "conversion from " + v.PyTypeName + " to Decimal is not supported");
        }

        // Decimal((sign, digits, exponent)), _decimal's checks and texts; written out and read as a string
        private static ScriptValue FromTuple(EvalContext ctx, ScriptValue[] t)
        {
            if (t.Length != 3)
                throw Raise.ValueError(ctx, "argument must be a sequence of length 3");
            IntValue sign = t[0] as IntValue;
            if (sign == null || !sign.IsSmall || (sign.Small != 0 && sign.Small != 1))
                throw Raise.ValueError(ctx, "sign must be an integer with the value 0 or 1");
            var text = new System.Text.StringBuilder(sign.Small == 1 ? "-" : "");
            StrValue special = t[2] as StrValue;
            if (special != null)
            {
                if (special.Value != "F" && special.Value != "n" && special.Value != "N")
                    throw Raise.ValueError(ctx, "string argument in the third position must be 'F', 'n' or 'N'");
                text.Append(special.Value == "F" ? "Infinity" : special.Value == "n" ? "NaN" : "sNaN");
            }
            else
            {
                if (!(t[2] is IntValue))
                    throw Raise.ValueError(ctx, "exponent must be an integer");
                BigInteger exponent = ((IntValue)t[2]).Value;
                if (exponent < long.MinValue || exponent > long.MaxValue)   // before any text of it: ToString is quadratic
                    throw Raise.Overflow(ctx, "Python int too large to convert to C ssize_t");
                if (t[1].Kind != ValueKind.Tuple && t[1].Kind != ValueKind.List)
                    throw Raise.ValueError(ctx, "coefficient must be a tuple of digits");
                foreach (ScriptValue d in Modules.Support.Iter.ToArray(ctx, t[1]))
                {
                    IntValue digit = d as IntValue;
                    if (digit == null || !digit.IsSmall || digit.Small < 0 || digit.Small > 9)
                        throw Raise.ValueError(ctx, "coefficient must be a tuple of digits");
                    ctx.Values.CheckStrLen(text.Length + 1L);
                    text.Append((char)('0' + digit.Small));
                }
                if (text.Length == 0 || text[text.Length - 1] == '-')
                    text.Append('0');
                text.Append('E').Append(exponent.ToString(CultureInfo.InvariantCulture));
            }
            int exp;
            decimal parsed = DecimalParse.Parse(ctx, text.ToString(), out exp);
            return FromExp(ctx, parsed, exp);
        }

        // math.floor/ceil/trunc integrate through the value hooks (Decimal.__floor__ etc.).
        protected internal override ScriptValue FloorCore(EvalContext ctx) { return ctx.Values.Int(new BigInteger(Math.Floor(Value))); }
        protected internal override ScriptValue CeilCore(EvalContext ctx) { return ctx.Values.Int(new BigInteger(Math.Ceiling(Value))); }
        protected internal override ScriptValue TruncCore(EvalContext ctx) { return ctx.Values.Int(new BigInteger(Math.Truncate(Value))); }

        // round(d) -> int rounds half-even whatever the context says; round(d, n) -> Decimal is a quantize
        // to exponent -n in the context's mode, so it also PADS (round(Decimal('1.5'), 3) -> 1.500) and
        // a negative n lands on a positive exponent (round(Decimal('1234'), -2) -> 1.2E+3).
        protected internal override ScriptValue RoundCore(ScriptValue ndigits, EvalContext ctx)
        {
            RoundingMode mode = ctx.DecimalCtx.Mode;
            if (ndigits == null || ndigits.Kind == ValueKind.None)
                return ctx.Values.Int(new BigInteger(((DecimalValue)DoQuantize(ctx, 0, RoundingMode.HalfEven)).Value));
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

        // An exactly zero sum takes the addends' sign when they agree, else + (- under ROUND_FLOOR), as the
        // decimal spec says; the backend's zero sign follows its internal path (1.30 - 1.3 came out -0.00).
        private static decimal ZeroSign(EvalContext ctx, decimal sum, decimal a, decimal b)
        {
            if (sum != 0m)
                return sum;
            bool na = IsNeg(a), nb = IsNeg(b);
            bool neg = na == nb ? na : ctx.DecimalCtx.Mode == RoundingMode.Floor;
            return neg == IsNeg(sum) ? sum : -sum;
        }

        private static bool IsNeg(decimal d)
        {
            return (decimal.GetBits(d)[3] & int.MinValue) != 0;
        }

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
        private static void CheckQuotient(EvalContext ctx, BigInteger q)
        {
            if (DecimalBits.SignificantDigits(q) > ctx.DecimalCtx.Prec)
                throw InvalidOp(ctx);
        }

        // x / y truncated, and x - q * y. Exact: through decimal's own division the quotient was rounded to 28-29 digits first.
        private static BigInteger DivideExactly(decimal x, decimal y, out BigInteger remainder, out int scale, out bool negX)
        {
            BigInteger mx, my;
            int sx, sy;
            bool negY;
            DecimalBits.Unpack(x, out mx, out sx, out negX);
            DecimalBits.Unpack(y, out my, out sy, out negY);
            scale = Math.Max(sx, sy);
            return BigInteger.DivRem(mx * BigInteger.Pow(10, scale - sx), my * BigInteger.Pow(10, scale - sy), out remainder);
        }

        // 10^0 .. 10^19, the powers a ulong holds.
        private static readonly ulong[] U64Pow10 = BuildU64Pow10();

        private static ulong[] BuildU64Pow10()
        {
            var p = new ulong[20];
            p[0] = 1;
            for (int i = 1; i < p.Length; i++)
                p[i] = p[i - 1] * 10;
            return p;
        }

        // DivideExactly in 64-bit words, for the usual operands whose mantissas, brought to one scale, stay below
        // 2^64: the BigInteger route made // and % 2-3.5 times slower than 1.1.0. The answer is the same.
        private static bool TryDivideSmall(decimal x, decimal y, out ulong q, out ulong r, out int scale, out bool negX)
        {
            uint xl, xm, xh, yl, ym, yh;
            int sx, sy;
            bool negY;
            DecimalBits.Read(x, out xl, out xm, out xh, out sx, out negX);
            DecimalBits.Read(y, out yl, out ym, out yh, out sy, out negY);
            scale = Math.Max(sx, sy);
            q = r = 0;
            if (xh != 0 || yh != 0)
                return false;
            ulong a = ((ulong)xm << 32) | xl;
            ulong b = ((ulong)ym << 32) | yl;
            if (!TryScale(ref a, scale - sx) || !TryScale(ref b, scale - sy) || b == 0)
                return false;
            q = a / b;
            r = a - q * b;
            return true;
        }

        private static bool TryScale(ref ulong v, int k)
        {
            if (k == 0)
                return true;
            if (k >= U64Pow10.Length || v > ulong.MaxValue / U64Pow10[k])
                return false;
            v *= U64Pow10[k];
            return true;
        }

        private static int Digits(ulong v)
        {
            int n = 1;
            while (v >= 10)
            {
                v /= 10;
                n++;
            }
            return n;
        }

        private static decimal SmallDecimal(ulong v, int scale, bool neg)
        {
            return new decimal(unchecked((int)(uint)v), unchecked((int)(uint)(v >> 32)), 0, neg, (byte)scale);
        }

        private static void CheckSmallQuotient(EvalContext ctx, ulong q)
        {
            if (Digits(q) > ctx.DecimalCtx.Prec)
                throw InvalidOp(ctx);
        }

        // FloorQuotient of a ulong quotient: Make(q, 0, neg) without the BigInteger round trip.
        private static DecimalValue SmallQuotient(EvalContext ctx, ulong q, bool neg)
        {
            CheckSmallQuotient(ctx, q);
            ctx.Values.PreCharge(48);
            return new DecimalValue(SmallDecimal(q, 0, neg), 0);
        }

        private static DecimalValue FloorQuotient(EvalContext ctx, decimal x, decimal y, BigInteger q)
        {
            CheckQuotient(ctx, q);
            return Make(ctx, q, 0, SignOf(x) != SignOf(y));
        }

        private static bool SignOf(decimal v)   // a negative zero counts
        {
            uint lo, mid, hi;
            int scale;
            bool neg;
            DecimalBits.Read(v, out lo, out mid, out hi, out scale, out neg);
            return neg;
        }

        private static DecimalValue Remainder(EvalContext ctx, BigInteger r, int scale, bool negX, int ideal)
        {
            return Settled(ctx, DecimalBits.Pack(ctx, r, scale, negX), ideal);
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
                        return Rounded(ctx, ZeroSign(ctx, x + y, x, y), Math.Min(ex, ey));
                    case PyBinOp.Sub:
                        return Rounded(ctx, ZeroSign(ctx, x - y, x, -y), Math.Min(ex, ey));
                    case PyBinOp.Mul:
                        return Rounded(ctx, Prod(ctx, x, y), ex + ey);
                    case PyBinOp.TrueDiv:
                        if (y == 0m)
                            throw x == 0m ? Undefined(ctx) : DivByZero(ctx);
                        return Rounded(ctx, Quot(ctx, x, y), ex - ey);
                    case PyBinOp.FloorDiv:
                        if (y == 0m)
                            throw x == 0m ? Undefined(ctx) : DivByZero(ctx);
                        ulong sq, sr;
                        int ss;
                        bool sn;
                        if (TryDivideSmall(x, y, out sq, out sr, out ss, out sn))
                            return SmallQuotient(ctx, sq, SignOf(x) != SignOf(y));
                        BigInteger fr;
                        int fs;
                        bool fn;
                        return FloorQuotient(ctx, x, y, DivideExactly(x, y, out fr, out fs, out fn));
                    case PyBinOp.Mod:
                        if (y == 0m)
                            throw x == 0m ? Undefined(ctx) : InvalidOp(ctx);
                        ulong tq, tr;
                        int ts;
                        bool tn;
                        if (TryDivideSmall(x, y, out tq, out tr, out ts, out tn))
                        {
                            CheckSmallQuotient(ctx, tq);
                            return Settled(ctx, SmallDecimal(tr, ts, tn), Math.Min(ex, ey));
                        }
                        BigInteger mr;
                        int ms;
                        bool mn;
                        CheckQuotient(ctx, DivideExactly(x, y, out mr, out ms, out mn));
                        return Remainder(ctx, mr, ms, mn, Math.Min(ex, ey));
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
                throw x == 0m ? Undefined(ctx) : Raise.Make(ctx, PyExceptionTypes.DecimalInvalidOperation, "[<class 'decimal.InvalidOperation'>, <class 'decimal.DivisionByZero'>]");
            try
            {
                ulong sq, sr;
                int ss;
                bool sn;
                if (TryDivideSmall(x, y, out sq, out sr, out ss, out sn))
                {
                    DecimalValue fq = SmallQuotient(ctx, sq, SignOf(x) != SignOf(y));
                    return ctx.Values.Tuple(new ScriptValue[] { fq, Settled(ctx, SmallDecimal(sr, ss, sn), Math.Min(ex, ey)) });
                }
                BigInteger r;
                int scale;
                bool negX;
                BigInteger q = DivideExactly(x, y, out r, out scale, out negX);
                return ctx.Values.Tuple(new ScriptValue[] { FloorQuotient(ctx, x, y, q), Remainder(ctx, r, scale, negX, Math.Min(ex, ey)) });
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
            {
                if (baseV.Value == 0m)
                    throw InvalidOp(ctx);   // 0 ** 0 is undefined in the General Decimal Arithmetic
                return FromDecimal(ctx, 1m);
            }
            if (baseV.Value == 0m)
            {
                if (n.Sign < 0)
                    throw DivByZero(ctx);
                return Make(ctx, BigInteger.Zero, 0, SignOf(baseV.Value) && !n.IsEven);
            }
            return PowerByInteger(ctx, baseV, n, expArg);
        }

        // libmpdec's _mpd_qpow_int: left-to-right squaring at prec + the exponent's digits + 2, half-even, then the
        // context's rounding. Repeating the product at the backend's own digits differed from it in the last digit.
        private static ScriptValue PowerByInteger(EvalContext ctx, DecimalValue baseV, BigInteger n, ScriptValue expArg)
        {
            ScriptValue small;
            if (TrySmallPower(ctx, baseV, n, out small))
                return small;
            BigInteger m;
            int e0;
            bool neg0;
            baseV.Coeff(out m, out e0, out neg0);
            bool neg = neg0 && !n.IsEven;
            BigInteger absN = BigInteger.Abs(n);
            int expDigits = DecimalBits.SignificantDigits(absN), expExp = 0;
            DecimalValue de = expArg as DecimalValue;
            if (de != null)
            {
                BigInteger ce;
                bool ne;
                de.Coeff(out ce, out expExp, out ne);
                expDigits = DecimalBits.SignificantDigits(ce);
            }
            if (expDigits > 40)   // no base away from 1 survives this many digits of exponent
            {
                if (m.IsOne && e0 == 0)
                    return Make(ctx, BigInteger.One, 0, neg);
                bool grows = (BigInteger.Log10(m) + e0 > 0) == (n.Sign > 0);
                throw grows ? Overflow(ctx) : Underflow(ctx);
            }
            int work = ctx.DecimalCtx.Prec + expDigits + expExp + 2;
            BigInteger bc = m;
            long be = e0;
            if (n.Sign < 0)
            {
                work++;
                Invert(ref bc, ref be, work);
            }
            BigInteger rc = bc;
            long re = be;
            int top = 0;
            for (BigInteger t = absN; t > BigInteger.One; t >>= 1)
                top++;
            for (int bit = top - 1; bit >= 0; bit--)
            {
                ctx.Budget.Step();
                rc *= rc;
                re += re;
                RoundWork(ref rc, ref re, work);
                if (!((absN >> bit).IsEven))
                {
                    rc *= bc;
                    re += be;
                    RoundWork(ref rc, ref re, work);
                }
                long adjusted = re + DecimalBits.SignificantDigits(rc);
                if (adjusted > 100)
                    throw Overflow(ctx);
                if (adjusted < -100)
                    throw Underflow(ctx);
            }
            return Fix(ctx, rc, (int)re, neg);
        }

        // PowerByInteger when the exact power fits a ulong and the precision, as nearly every one does: no partial
        // power is longer than the whole, so no step of the squaring rounds and the answer is the exact one. The
        // BigInteger route made ** 2-3 times slower than 1.1.0. The steps are the squaring's.
        private static bool TrySmallPower(EvalContext ctx, DecimalValue baseV, BigInteger n, out ScriptValue result)
        {
            result = null;
            if (n.Sign <= 0 || n > 64)
                return false;
            uint lo, mid, hi;
            int scale;
            bool neg;
            DecimalBits.Read(baseV.Value, out lo, out mid, out hi, out scale, out neg);
            int k = (int)n;
            long exp = (long)baseV.Exp * k;
            if (hi != 0 || baseV.Exp != -scale || exp < -28)
                return false;
            ulong b = ((ulong)mid << 32) | lo, r = 1;
            for (int i = 0; i < k; i++)
            {
                if (b != 0 && r > ulong.MaxValue / b)
                    return false;
                r *= b;
            }
            if (Digits(r) > ctx.DecimalCtx.Prec)
                return false;
            for (int t = k; t > 1; t >>= 1)
                ctx.Budget.Step();
            ctx.Values.PreCharge(48);
            result = new DecimalValue(SmallDecimal(r, (int)-exp, neg && (k & 1) == 1), (int)exp);
            return true;
        }

        // 1 / (c * 10^e) as mpd_qdiv gives it: half-even at the working digits; an exact one keeps the ideal exponent.
        private static void Invert(ref BigInteger c, ref long e, int work)
        {
            int shift = work + DecimalBits.SignificantDigits(c) + 2;
            BigInteger r;
            BigInteger q = BigInteger.DivRem(BigInteger.Pow(10, shift), c, out r);
            long ideal = -e;
            long qe = -shift - e;
            if (r.IsZero)
            {
                while (qe < ideal && (q % 10).IsZero)
                {
                    q /= 10;
                    qe++;
                }
            }
            else if ((q % 5).IsZero)
            {
                q += BigInteger.One;   // sticky: never a tie
            }
            RoundWork(ref q, ref qe, work);
            c = q;
            e = qe;
        }

        private static void RoundWork(ref BigInteger c, ref long e, int work)
        {
            int digits = DecimalBits.SignificantDigits(c);
            if (digits <= work)
                return;
            int drop = digits - work;
            BigInteger divisor = BigInteger.Pow(10, drop), r;
            BigInteger q = BigInteger.DivRem(c, divisor, out r);
            q = DecimalRound.Apply(q, r, divisor, false, RoundingMode.HalfEven);
            if (DecimalBits.SignificantDigits(q) > work)
            {
                q /= 10;
                drop++;
            }
            c = q;
            e += drop;
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
                case PyUnaryOp.Neg:   // minus(x) is 0 - x: a zero comes out positive but under ROUND_FLOOR
                    return Rounded(ctx, ZeroSign(ctx, -Value, 0m, -Value), Exp);
                case PyUnaryOp.Pos:   // plus(x) is 0 + x, the CPython idiom for "apply the context to d"
                    return Rounded(ctx, ZeroSign(ctx, Value, 0m, Value), Exp);
                default:
                    return null;
            }
        }

        protected internal override ScriptValue AbsCore(EvalContext ctx) { return Rounded(ctx, Math.Abs(Value), Exp); }

        private static ScriptException DivByZero(EvalContext ctx) { return Raise.Make(ctx, PyExceptionTypes.DecimalDivisionByZero, "[<class 'decimal.DivisionByZero'>]"); }
        private static ScriptException InvalidOp(EvalContext ctx) { return Raise.Make(ctx, PyExceptionTypes.DecimalInvalidOperation, "[<class 'decimal.InvalidOperation'>]"); }
        private static ScriptException Undefined(EvalContext ctx) { return Raise.Make(ctx, PyExceptionTypes.DecimalInvalidOperation, "[<class 'decimal.DivisionUndefined'>]"); }
        private static ScriptException Overflow(EvalContext ctx) { return Raise.Make(ctx, PyExceptionTypes.DecimalOverflow, "[<class 'decimal.Overflow'>]"); }

        // ---- comparisons / hash ----

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            DecimalValue od = other as DecimalValue;
            if (od != null)
                return Value == od.Value;
            int cmp;
            return other is FractionValue && CompareWith(other, out cmp, ctx) == Rationals.Order.Ordered && cmp == 0;
        }

        protected internal override bool TryEqualsAcrossKindCore(ScriptValue other, EvalContext ctx, int depth, out bool equal)
        {
            return Rationals.EqualsAcrossKind(this, other, out equal, ctx);
        }

        protected internal override bool TryCompareLeafCore(ScriptValue other, EvalContext ctx, int depth, out int cmp)
        {
            DecimalValue od = other as DecimalValue;
            if (od != null)
            {
                cmp = Value.CompareTo(od.Value);
                return true;
            }
            return CompareWith(other, out cmp, ctx) == Rationals.Order.Ordered;
        }

        internal Rationals.Order CompareWith(ScriptValue other, out int cmp, EvalContext ctx)
        {
            BigInteger n, d;
            ToRational(out n, out d);
            return Rationals.CompareWith(n, d, other, out cmp, ctx);
        }

        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            BigInteger n, d;
            ToRational(out n, out d);
            ctx.Budget.ChargeLinear((NumericOps.BitLength(n) + NumericOps.BitLength(d)) >> 5);
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

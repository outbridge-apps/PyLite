using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // fractions.Fraction. Immutable, always-reduced rational: sign in Num, Den > 0
    // gcd(|Num|,Den)==1. Exact cross-type comparison vs int/float. (Decimal integration lands with
    // DecimalValue.)
    internal sealed class FractionValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = BuildType();

        public readonly BigInteger Num;
        public readonly BigInteger Den;   // > 0

        private FractionValue(BigInteger num, BigInteger den)
        {
            Num = num;
            Den = den;
        }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        // The single normalization point.
        internal static FractionValue Create(EvalContext ctx, BigInteger num, BigInteger den)
        {
            if (den.IsZero)
                throw Raise.ZeroDivision(ctx, "Fraction(" + num.ToString(CultureInfo.InvariantCulture) + ", 0)");
            if (den.Sign < 0)
            {
                num = -num;
                den = -den;
            }
            BigInteger g = BigInteger.GreatestCommonDivisor(BigInteger.Abs(num), den);
            if (g > BigInteger.One)
            {
                num /= g;
                den /= g;
            }
            int bits = BigIntMath.BitLength(num) + BigIntMath.BitLength(den);
            ctx.Values.PreCharge(bits / 4 + 48);
            ctx.Budget.Step(1 + bits / 1024);
            if (bits > 8192)
                ctx.Budget.CheckDeadlineNow();
            return new FractionValue(num, den);
        }

        internal double AsDouble(EvalContext ctx)
        {
            return ((FloatValue)PyOps.BinaryOp(PyBinOp.TrueDiv, ctx.Values.Int(Num), ctx.Values.Int(Den), ctx)).Value;
        }

        // ---- constructors ----

        internal static FractionValue FromFloat(EvalContext ctx, double f)
        {
            if (double.IsNaN(f))
                throw Raise.ValueError(ctx, "cannot convert float NaN to integer ratio");
            if (double.IsInfinity(f))
                throw Raise.Overflow(ctx, "cannot convert Infinity to integer ratio");
            BigInteger num, den;
            Rationals.FloatToRational(f, out num, out den);
            return Create(ctx, num, den);
        }

        private static readonly ArgSpec CtorSpec = ArgSpec.Create("Fraction")
            .Opt("numerator", ArgSpec.MISSING).Opt("denominator", ArgSpec.MISSING).Seal();

        internal static ScriptValue Construct(EvalContext ctx, ScriptValue[] args, KwArgs kw)
        {
            ctx.Budget.Step();
            var slots = new ScriptValue[CtorSpec.SlotCount];
            ScriptValue[] star;
            CtorSpec.Bind(ctx, args, kw, slots, out star);
            ScriptValue numArg = slots[0];
            ScriptValue denArg = slots[1];

            if (ReferenceEquals(denArg, ArgSpec.MISSING))
            {
                // single-argument form
                if (ReferenceEquals(numArg, ArgSpec.MISSING))
                    return Create(ctx, BigInteger.Zero, BigInteger.One);
                FractionValue fr = numArg as FractionValue;
                if (fr != null)
                    return fr;
                BigInteger n;
                if (TryInt(numArg, out n))
                    return Create(ctx, n, BigInteger.One);
                FloatValue fv = numArg as FloatValue;
                if (fv != null)
                    return FromFloat(ctx, fv.Value);
                StrValue sv = numArg as StrValue;
                if (sv != null)
                {
                    BigInteger num, den;
                    RationalParse.Parse(ctx, sv.Value, out num, out den);
                    return Create(ctx, num, den);
                }
                DecimalValue dv = numArg as DecimalValue;
                if (dv != null)
                {
                    BigInteger num, den;
                    dv.ToRational(out num, out den);
                    return Create(ctx, num, den);
                }
                throw Raise.TypeError(ctx, "argument should be a string or a Rational instance");
            }

            // two-argument form: both must be Rational (int or Fraction)
            BigInteger an, ad, bn, bd;
            if (!TryRational(numArg, out an, out ad, true) || !TryRational(denArg, out bn, out bd, true))
                throw Raise.TypeError(ctx, "both arguments should be Rational instances");
            return Create(ctx, an * bd, ad * bn);
        }

        private static bool TryInt(ScriptValue v, out BigInteger n)
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
            n = BigInteger.Zero;
            return false;
        }

        private static bool TryRational(ScriptValue v, out BigInteger n, out BigInteger d, bool _)
        {
            FractionValue fr = v as FractionValue;
            if (fr != null)
            {
                n = fr.Num;
                d = fr.Den;
                return true;
            }
            IntValue iv = v as IntValue;
            if (iv != null)
            {
                n = iv.Value;
                d = BigInteger.One;
                return true;
            }
            BoolValue bv = v as BoolValue;
            if (bv != null)
            {
                n = bv.Value ? BigInteger.One : BigInteger.Zero;
                d = BigInteger.One;
                return true;
            }
            n = BigInteger.Zero;
            d = BigInteger.One;
            return false;
        }

        // ---- arithmetic ----

        protected internal override ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        {
            if (op == PyBinOp.Pow)
                return PowOp(ctx, other, reflected);

            FloatValue fv = other as FloatValue;
            if (fv != null)
            {
                double d = AsDouble(ctx);
                double a = reflected ? fv.Value : d;
                double b = reflected ? d : fv.Value;
                return PyOps.BinaryOp(op, ctx.Values.Float(a), ctx.Values.Float(b), ctx);
            }

            BigInteger on, od;
            if (!TryRational(other, out on, out od, true))
                return null;   // Decimal / other -> generic TypeError

            BigInteger an, ad, bn, bd;
            if (reflected)
            {
                an = on; ad = od; bn = Num; bd = Den;
            }
            else
            {
                an = Num; ad = Den; bn = on; bd = od;
            }

            switch (op)
            {
                case PyBinOp.Add:
                    return Create(ctx, an * bd + bn * ad, ad * bd);
                case PyBinOp.Sub:
                    return Create(ctx, an * bd - bn * ad, ad * bd);
                case PyBinOp.Mul:
                    return Create(ctx, an * bn, ad * bd);
                case PyBinOp.TrueDiv:
                    if (bn.IsZero)
                        throw Raise.ZeroDivision(ctx, "Fraction(" + (an * bd).ToString(CultureInfo.InvariantCulture) + ", 0)");
                    return Create(ctx, an * bd, ad * bn);
                case PyBinOp.FloorDiv:
                    if (bn.IsZero)
                        throw Raise.ZeroDivision(ctx, "division by zero");
                    return ctx.Values.Int(Rationals.FloorDiv(an * bd, ad * bn));
                case PyBinOp.Mod:
                {
                    if (bn.IsZero)
                        throw Raise.ZeroDivision(ctx, "division by zero");
                    BigInteger q = Rationals.FloorDiv(an * bd, ad * bn);
                    return Create(ctx, an * bd - q * bn * ad, ad * bd);
                }
                default:
                    return null;
            }
        }

        private ScriptValue PowOp(EvalContext ctx, ScriptValue other, bool reflected)
        {
            if (reflected)
            {
                double baseD = ((FloatValue)PyOps.BinaryOp(PyBinOp.Add, other, ctx.Values.Float(0.0), ctx)).Value;
                return ctx.Values.Float(Math.Pow(baseD, AsDouble(ctx)));
            }
            BigInteger n;
            if (TryInt(other, out n))
                return PowInt(ctx, n);
            FractionValue pf = other as FractionValue;
            if (pf != null)
            {
                if (pf.Den == BigInteger.One)
                    return PowInt(ctx, pf.Num);
                return ctx.Values.Float(Math.Pow(AsDouble(ctx), pf.AsDouble(ctx)));
            }
            FloatValue fv = other as FloatValue;
            if (fv != null)
                return ctx.Values.Float(Math.Pow(AsDouble(ctx), fv.Value));
            return null;
        }

        private ScriptValue PowInt(EvalContext ctx, BigInteger n)
        {
            if (n.Sign == 0)
                return Create(ctx, BigInteger.One, BigInteger.One);
            BigInteger absN = BigInteger.Abs(n);
            if (absN > int.MaxValue)
                ctx.Values.PreCharge(long.MaxValue / 4);   // absurd exponent -> abort
            int ni = (int)absN;
            long estBytes = ((long)ni * (BigIntMath.BitLength(Num) + BigIntMath.BitLength(Den))) / 8 + 64;
            ctx.Values.PreCharge(estBytes < 0 ? long.MaxValue / 4 : estBytes);
            BigInteger pn = BigInteger.Pow(Num, ni);
            BigInteger pd = BigInteger.Pow(Den, ni);
            if (n.Sign > 0)
                return Create(ctx, pn, pd);
            if (Num.IsZero)
                throw Raise.ZeroDivision(ctx, "Fraction(" + pd.ToString(CultureInfo.InvariantCulture) + ", 0)");
            return Create(ctx, pd, pn);
        }

        protected internal override ScriptValue UnaryOpCore(PyUnaryOp op, EvalContext ctx)
        {
            switch (op)
            {
                case PyUnaryOp.Neg:
                    return Create(ctx, -Num, Den);
                case PyUnaryOp.Pos:
                    return this;
                default:
                    return null;
            }
        }

        protected internal override ScriptValue AbsCore(EvalContext ctx)
        {
            return Num.Sign < 0 ? Create(ctx, -Num, Den) : this;
        }

        protected internal override ScriptValue DivmodCore(ScriptValue other, bool reflected, EvalContext ctx)
        {
            ScriptValue q = BinaryOpCore(PyBinOp.FloorDiv, other, reflected, ctx);
            if (q == null)
                return null;
            ScriptValue r = BinaryOpCore(PyBinOp.Mod, other, reflected, ctx);
            return ctx.Values.Tuple(new[] { q, r });
        }

        // ---- floor / ceil / trunc / round hooks ----

        protected internal override ScriptValue FloorCore(EvalContext ctx) { return ctx.Values.Int(Rationals.FloorDiv(Num, Den)); }
        protected internal override ScriptValue CeilCore(EvalContext ctx) { return ctx.Values.Int(-Rationals.FloorDiv(-Num, Den)); }
        protected internal override ScriptValue TruncCore(EvalContext ctx) { return ctx.Values.Int(Num / Den); }

        protected internal override ScriptValue RoundCore(ScriptValue ndigits, EvalContext ctx)
        {
            if (ndigits == null || ndigits.Kind == ValueKind.None)
                return ctx.Values.Int(RoundHalfEven(Num, Den));
            BigInteger nd;
            if (!TryInt(ndigits, out nd))
                throw Raise.TypeError(ctx, "'" + ndigits.PyTypeName + "' object cannot be interpreted as an integer");
            int n = nd > 1000000 ? 1000000 : nd < -1000000 ? -1000000 : (int)nd;
            if (n >= 0)
            {
                BigInteger pow = BigInteger.Pow(10, n);
                BigInteger m = RoundHalfEven(Num * pow, Den);
                return Create(ctx, m, pow);
            }
            else
            {
                BigInteger pow = BigInteger.Pow(10, -n);
                BigInteger m = RoundHalfEven(Num, Den * pow);
                return Create(ctx, m * pow, BigInteger.One);
            }
        }

        private static BigInteger RoundHalfEven(BigInteger num, BigInteger den)
        {
            BigInteger q = Rationals.FloorDiv(num, den);
            BigInteger r = num - q * den;   // 0 <= r < den
            BigInteger twice = r * 2;
            if (twice < den)
                return q;
            if (twice > den)
                return q + 1;
            return q.IsEven ? q : q + 1;
        }

        // ---- comparisons / hash ----

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            FractionValue o = other as FractionValue;
            if (o != null)
                return Num == o.Num && Den == o.Den;
            DecimalValue dv = other as DecimalValue;   // both opaque -> reaches here (Fraction == Decimal)
            if (dv != null)
            {
                BigInteger dn, dd;
                dv.ToRational(out dn, out dd);
                return Rationals.Compare(Num, Den, dn, dd) == 0;
            }
            return false;
        }

        protected internal override bool TryEqualsAcrossKindCore(ScriptValue other, EvalContext ctx, int depth, out bool equal)
        {
            BigInteger n;
            if (TryInt(other, out n))
            {
                equal = Rationals.Compare(Num, Den, n, BigInteger.One) == 0;
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
                BigInteger fn, fd;
                Rationals.FloatToRational(fv.Value, out fn, out fd);
                equal = Rationals.Compare(Num, Den, fn, fd) == 0;
                return true;
            }
            equal = false;
            return false;
        }

        protected internal override bool TryCompareLeafCore(ScriptValue other, EvalContext ctx, int depth, out int cmp)
        {
            FractionValue o = other as FractionValue;
            if (o != null)
            {
                cmp = Rationals.Compare(Num, Den, o.Num, o.Den);
                return true;
            }
            DecimalValue dv = other as DecimalValue;
            if (dv != null)
            {
                BigInteger dn, dd;
                dv.ToRational(out dn, out dd);
                cmp = Rationals.Compare(Num, Den, dn, dd);
                return true;
            }
            BigInteger n;
            if (TryInt(other, out n))
            {
                cmp = Rationals.Compare(Num, Den, n, BigInteger.One);
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
                    cmp = fv.Value > 0 ? -1 : 1;   // a finite rational vs ±inf
                    return true;
                }
                BigInteger fn, fd;
                Rationals.FloatToRational(fv.Value, out fn, out fd);
                cmp = Rationals.Compare(Num, Den, fn, fd);
                return true;
            }
            cmp = 0;
            return false;
        }

        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            return NumericHash.HashRational(Num, Den);
        }

        protected internal override bool IsTruthyCore(EvalContext ctx) { return !Num.IsZero; }

        // ---- str / repr ----

        protected internal override void StrLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append(Num.ToString(CultureInfo.InvariantCulture));
            if (Den != BigInteger.One)
            {
                sb.Append("/");
                sb.Append(Den.ToString(CultureInfo.InvariantCulture));
            }
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("Fraction(" + Num.ToString(CultureInfo.InvariantCulture) + ", " + Den.ToString(CultureInfo.InvariantCulture) + ")");
        }

        // ---- limit_denominator (verbatim port) ----

        private ScriptValue LimitDenominator(EvalContext ctx, ScriptValue[] args)
        {
            BigInteger maxDen = 1000000;
            if (args.Length >= 1)
            {
                if (!TryInt(args[0], out maxDen))
                    throw Raise.TypeError(ctx, "'" + args[0].PyTypeName + "' object cannot be interpreted as an integer");
            }
            if (maxDen < 1)
                throw Raise.ValueError(ctx, "max_denominator should be at least 1");
            if (Den <= maxDen)
                return this;

            BigInteger p0 = 0, q0 = 1, p1 = 1, q1 = 0;
            BigInteger n = Num, d = Den;
            int iter = 0;
            while (true)
            {
                BigInteger a = Rationals.FloorDiv(n, d);
                BigInteger q2 = q0 + a * q1;
                if (q2 > maxDen)
                    break;
                BigInteger newP1 = p0 + a * p1;
                p0 = p1;
                q0 = q1;
                p1 = newP1;
                q1 = q2;
                BigInteger newD = n - a * d;
                n = d;
                d = newD;
                ctx.Budget.Step();
                if (++iter % 256 == 0)
                    ctx.Budget.CheckDeadlineNow();
            }
            BigInteger k = Rationals.FloorDiv(maxDen - q0, q1);
            FractionValue bound1 = Create(ctx, p0 + k * p1, q0 + k * q1);
            FractionValue bound2 = Create(ctx, p1, q1);
            // return the bound closer to self
            return CloserBound(ctx, bound1, bound2);
        }

        private ScriptValue CloserBound(EvalContext ctx, FractionValue bound1, FractionValue bound2)
        {
            // |bound2 - self| <= |bound1 - self| ? bound2 : bound1
            BigInteger d2n = BigInteger.Abs(bound2.Num * Den - Num * bound2.Den);
            BigInteger d2d = bound2.Den * Den;
            BigInteger d1n = BigInteger.Abs(bound1.Num * Den - Num * bound1.Den);
            BigInteger d1d = bound1.Den * Den;
            return Rationals.Compare(d2n, d2d, d1n, d1d) <= 0 ? (ScriptValue)bound2 : bound1;
        }

        // ---- slots ----

        private static ScriptTypeInfo BuildType()
        {
            var d = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal)
            {
                ["numerator"] = SlotDescriptor.MakeProperty("numerator", (self, c) => c.Values.Int(((FractionValue)self).Num)),
                ["denominator"] = SlotDescriptor.MakeProperty("denominator", (self, c) => c.Values.Int(((FractionValue)self).Den)),
                ["limit_denominator"] = SlotDescriptor.MakeMethod("limit_denominator", (self, a, kw, c) => ((FractionValue)self).LimitDenominator(c, a)),
                ["conjugate"] = SlotDescriptor.MakeMethod("conjugate", (self, a, kw, c) => self),
            };
            return new ScriptTypeInfo("fractions.Fraction", d);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // math: pow (C99 table), log/log2/log10 (loghelper for ints larger than double), fsum, factorial.
    internal static partial class MathModule
    {
        // ---- pow (port of Modules/mathmodule.c::math_pow) ----

        private static ScriptValue Pow(EvalContext ctx, ScriptValue[] a)
        {
            double fx = AsDouble(ctx, Arg(ctx, a, 0));
            double fy = AsDouble(ctx, Arg(ctx, a, 1));

            if (fy == 0.0)
                return ctx.Values.Float(1.0);        // pow(x, 0) == 1, even nan/inf base
            if (fx == 1.0)
                return ctx.Values.Float(1.0);        // pow(1, y) == 1, even nan/inf exp
            if (double.IsNaN(fx))
                return ctx.Values.Float(fx);
            if (double.IsNaN(fy))
                return ctx.Values.Float(fy);

            if (double.IsInfinity(fx))
            {
                if (fx > 0)
                    return ctx.Values.Float(fy > 0 ? double.PositiveInfinity : 0.0);
                bool oddY = IsOddInteger(fy);
                if (fy > 0)
                    return ctx.Values.Float(oddY ? double.NegativeInfinity : double.PositiveInfinity);
                return ctx.Values.Float(oddY ? -0.0 : 0.0);
            }
            if (double.IsInfinity(fy))
            {
                double absx = Math.Abs(fx);
                if (absx == 1.0)
                    return ctx.Values.Float(1.0);
                if (fy > 0)
                    return ctx.Values.Float(absx > 1.0 ? double.PositiveInfinity : 0.0);
                return ctx.Values.Float(absx > 1.0 ? 0.0 : double.PositiveInfinity);
            }
            if (fx == 0.0)
            {
                if (fy < 0.0)
                    throw Raise.ValueError(ctx, "math domain error");
                return ctx.Values.Float(FloatMath.IsNegZero(fx) && IsOddInteger(fy) ? -0.0 : 0.0);
            }
            if (fx < 0.0 && Math.Floor(fy) != fy)
                throw Raise.ValueError(ctx, "math domain error");

            double r = Math.Pow(fx, fy);
            if (double.IsInfinity(r))
                throw Raise.Overflow(ctx, "math range error");
            if (double.IsNaN(r))
                throw Raise.ValueError(ctx, "math domain error");
            return ctx.Values.Float(r);
        }

        private static bool IsOddInteger(double d)
        {
            return !double.IsNaN(d) && !double.IsInfinity(d) && Math.Floor(d) == d && Math.Floor(d / 2.0) * 2.0 != d;
        }

        // ---- log family ----

        private static ScriptValue Log(EvalContext ctx, ScriptValue[] a)
        {
            double num = LogHelperDouble(ctx, Arg(ctx, a, 0), Math.Log);
            if (a.Length >= 2)
            {
                double den = LogHelperDouble(ctx, a[1], Math.Log);
                if (den == 0.0)
                    throw Raise.ZeroDivision(ctx, "division by zero");
                return ctx.Values.Float(num / den);
            }
            return ctx.Values.Float(num);
        }

        private static ScriptValue LogHelper(EvalContext ctx, ScriptValue arg, Func<double, double> func)
        {
            return ctx.Values.Float(LogHelperDouble(ctx, arg, func));
        }

        // CPython's loghelper: func(float(n)) while n converts, past that func(x) + func(2) * e with
        // n = x * 2**e, x in [0.5, 1) rounded to 53 bits (_PyLong_Frexp). Scaling by 2**(e - 900) instead
        // missed CPython's answer in the last digit for ints of 1,000 bits and more.
        internal static double LogHelperDouble(EvalContext ctx, ScriptValue arg, Func<double, double> func)
        {
            IntValue iv = arg as IntValue;
            BoolValue bv = arg as BoolValue;
            if (iv != null || bv != null)
            {
                BigInteger n = iv != null ? iv.Value : (bv.Value ? BigInteger.One : BigInteger.Zero);
                if (n.Sign <= 0)
                    throw Raise.ValueError(ctx, PositiveInput);
                double d = NearestDouble.FromBigInteger(n);
                if (!double.IsInfinity(d))
                    return func(d);
                long e;
                double m = Frexp(n, out e);
                return func(m) + func(2.0) * e;
            }
            double x = AsDouble(ctx, arg);   // float, or Fraction/Decimal via their __float__
            if (x <= 0.0)
                throw Raise.ValueError(ctx, PositiveInput);
            return func(x);
        }

        private const double InvLn2 = 1.4426950408889634;   // 1/ln(2)
        private const string PositiveInput = "expected a positive input";

        private static ScriptValue Log2(EvalContext ctx, ScriptValue[] a)
        {
            ScriptValue arg = Arg(ctx, a, 0);
            IntValue iv = arg as IntValue;
            BoolValue bv = arg as BoolValue;
            if (iv != null || bv != null)
            {
                BigInteger n = iv != null ? iv.Value : (bv.Value ? BigInteger.One : BigInteger.Zero);
                if (n.Sign <= 0)
                    throw Raise.ValueError(ctx, PositiveInput);
                if (BigIntMath.IsPowerOfTwo(n))
                    return ctx.Values.Float(NumericOps.BitLength(n) - 1);
                return ctx.Values.Float(LogHelperDouble(ctx, arg, M_Log2));
            }
            double fx = AsDouble(ctx, arg);   // a Fraction or Decimal through its __float__
            if (fx <= 0.0)
                throw Raise.ValueError(ctx, PositiveInput);
            double m;
            int e;
            FloatMath.Frexp(fx, out m, out e);
            if (m == 0.5)
                return ctx.Values.Float(e - 1);
            return ctx.Values.Float(Math.Log(m) * InvLn2 + e);
        }

        private static double M_Log2(double d) { return Math.Log(d) * InvLn2; }

        // --- fsum + factorial ----

        private static void AddSummationAndFactorial(EvalContext ctx, Dictionary<string, ScriptValue> m)
        {
            m["fsum"] = Fn("fsum", 1, 1, (c, a) => Fsum(c, a));
            m["factorial"] = Fn("factorial", 1, 1, (c, a) => Factorial(c, a));
        }

        private static ScriptValue Fsum(EvalContext ctx, ScriptValue[] a)
        {
            IScriptIterator it = PyOps.GetIterator(Arg(ctx, a, 0), ctx);
            return ctx.Values.Float(ShewchukSum.Sum(ctx, IterDoubles(ctx, it)));
        }

        private static IEnumerable<double> IterDoubles(EvalContext ctx, IScriptIterator it)
        {
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
                yield return AsDouble(ctx, v);
        }

        // n = x * 2**e with x in [0.5, 1): the top 64 bits with a sticky bit for the rest, rounded once to
        // 53 bits, so x is n's correctly rounded mantissa; a carry to 1.0 becomes 0.5 with e + 1.
        private static double Frexp(BigInteger n, out long e)
        {
            e = NumericOps.BitLength(n);
            int drop = (int)(e - 64);
            BigInteger top = n >> drop;
            if ((top << drop) != n)
                top |= BigInteger.One;
            double x = NearestDouble.FromBigInteger(top) / 18446744073709551616.0;   // / 2**64, exact
            if (x == 1.0)
            {
                x = 0.5;
                e++;
            }
            return x;
        }

        private static ScriptValue Factorial(EvalContext ctx, ScriptValue[] a)
        {
            ScriptValue arg = Arg(ctx, a, 0);
            BigInteger n;
            IntValue iv = arg as IntValue;
            BoolValue bv = arg as BoolValue;
            if (iv != null)
                n = iv.Value;
            else if (bv != null)
                n = bv.Value ? 1 : 0;
            else   // floats too, since 3.10
                throw Raise.TypeError(ctx, "'" + arg.PyTypeName + "' object cannot be interpreted as an integer");

            if (n.Sign < 0)
                throw Raise.ValueError(ctx, "factorial() not defined for negative values");
            if (n < 2)
                return ctx.Values.Int(BigInteger.One);
            // Stirling's n*log2(n/e) bits, a slight under-estimate: past MaxIntBits the result is refused
            // before the multiplications (factorial(10**7) used to run into the deadline). Within the cap
            // n fits a long, since the estimate is at least n from n = 6 on.
            double nb = (double)n;
            double estBits = nb * (Math.Log(nb) * InvLn2 - InvLn2) + 1;
            if (double.IsInfinity(estBits) || estBits > ctx.Limits.MaxIntBits)
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxIntBits", ctx.Limits.MaxIntBits, long.MaxValue);
            ctx.Values.EnsureIntBits((long)estBits);

            long last = (long)n;
            BigInteger result = BigInteger.One;
            for (long i = 2; i <= last; i++)
            {
                result *= i;
                // the multiply is linear in the result so far, about i*log2(i) bits
                ctx.Budget.ChargeLinear((long)(i * Math.Log(i) * InvLn2) >> 6);
                if ((i & 1023) == 0)
                    ctx.Budget.CheckDeadlineNow();
            }
            return ctx.Values.Int(result);
        }
    }
}

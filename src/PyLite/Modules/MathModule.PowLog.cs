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
            double fx = AsDouble(ctx, Arg(ctx, a, 0), "pow");
            double fy = AsDouble(ctx, Arg(ctx, a, 1), "pow");

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
                    throw Raise.ZeroDivision(ctx, "float division by zero");
                return ctx.Values.Float(num / den);
            }
            return ctx.Values.Float(num);
        }

        private static ScriptValue LogHelper(EvalContext ctx, ScriptValue arg, Func<double, double> func)
        {
            return ctx.Values.Float(LogHelperDouble(ctx, arg, func));
        }

        // Port of loghelper: ints larger than double via the bit-length scaling trick.
        private static double LogHelperDouble(EvalContext ctx, ScriptValue arg, Func<double, double> func)
        {
            IntValue iv = arg as IntValue;
            BoolValue bv = arg as BoolValue;
            if (iv != null || bv != null)
            {
                BigInteger n = iv != null ? iv.Value : (bv.Value ? BigInteger.One : BigInteger.Zero);
                if (n.Sign <= 0)
                    throw Raise.ValueError(ctx, "math domain error");
                int e = BigIntMath.BitLength(n) - 1;
                if (e < 1000)
                    return func((double)n);
                int shift = e - 900;
                double m = (double)(n >> shift);
                return func(m) + shift * func(2.0);
            }
            double x = AsDouble(ctx, arg, "log");   // float, or Fraction/Decimal via their __float__
            if (x <= 0.0)
                throw Raise.ValueError(ctx, "math domain error");
            return func(x);
        }

        private const double InvLn2 = 1.4426950408889634;   // 1/ln(2)

        private static ScriptValue Log2(EvalContext ctx, ScriptValue[] a)
        {
            ScriptValue arg = Arg(ctx, a, 0);
            IntValue iv = arg as IntValue;
            BoolValue bv = arg as BoolValue;
            if (iv != null || bv != null)
            {
                BigInteger n = iv != null ? iv.Value : (bv.Value ? BigInteger.One : BigInteger.Zero);
                if (n.Sign <= 0)
                    throw Raise.ValueError(ctx, "math domain error");
                if (BigIntMath.IsPowerOfTwo(n))
                    return ctx.Values.Float(BigIntMath.BitLength(n) - 1);
                return ctx.Values.Float(LogHelperDouble(ctx, arg, M_Log2));
            }
            FloatValue fv = arg as FloatValue;
            if (fv == null)
                throw Raise.TypeError(ctx, "a float is required");
            double fx = fv.Value;
            if (fx <= 0.0)
                throw Raise.ValueError(ctx, "math domain error");
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
            m["fsum"] = Fn("fsum", (c, a) => Fsum(c, a));
            m["factorial"] = Fn("factorial", (c, a) => Factorial(c, a));
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
                yield return AsDouble(ctx, v, "fsum");
        }

        private static ScriptValue Factorial(EvalContext ctx, ScriptValue[] a)
        {
            ScriptValue arg = Arg(ctx, a, 0);
            BigInteger n;
            IntValue iv = arg as IntValue;
            BoolValue bv = arg as BoolValue;
            FloatValue fv = arg as FloatValue;
            if (iv != null)
                n = iv.Value;
            else if (bv != null)
                n = bv.Value ? 1 : 0;
            else if (fv != null)
            {
                double f = fv.Value;
                if (double.IsNaN(f) || double.IsInfinity(f) || Math.Floor(f) != f)
                    throw Raise.ValueError(ctx, "factorial() only accepts integral values");
                n = new BigInteger(f);
            }
            else
                throw Raise.TypeError(ctx, "an integer is required");

            if (n.Sign < 0)
                throw Raise.ValueError(ctx, "factorial() not defined for negative values");
            if (n >= 2)
            {
                double nb = (double)n;
                double estBytes = (nb * (Math.Log(nb) * InvLn2 - InvLn2) + 1) / 8.0;
                // A huge n makes estBytes exceed long range; abort on the memory budget before the cast.
                if (double.IsInfinity(estBytes) || estBytes >= ctx.Limits.MaxAllocBytes)
                    throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxAllocBytes", ctx.Limits.MaxAllocBytes, long.MaxValue);
                ctx.Values.PreCharge((long)estBytes + 64);
            }

            BigInteger result = BigInteger.One;
            for (BigInteger i = 2; i <= n; i++)
            {
                result *= i;
                ctx.Budget.Step();
                if ((i % 1024) == 0)
                    ctx.Budget.CheckDeadlineNow();
            }
            return ctx.Values.Int(result);
        }
    }
}

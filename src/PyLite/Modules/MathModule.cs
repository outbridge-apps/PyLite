using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // The math module. A thin wrapper over System.Math + BigInteger with CPython's
    // errno-as-exception domain/range semantics. Immutable, sharable. Reference: Modules/mathmodule.c.
    internal static partial class MathModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                // 3.4-era core; the 3.5-3.13 members live in AddModern.
                ["pi"] = ctx.Values.Float(Math.PI),
                ["e"] = ctx.Values.Float(Math.E),

                ["ceil"] = Fn("ceil", (c, a) => RoundToInt(c, a, RoundMode.Ceil)),
                ["floor"] = Fn("floor", (c, a) => RoundToInt(c, a, RoundMode.Floor)),
                ["trunc"] = Fn("trunc", (c, a) => RoundToInt(c, a, RoundMode.Trunc)),

                ["fabs"] = Fn("fabs", (c, a) => c.Values.Float(Math.Abs(AsDouble(c, Arg(c, a, 0), "fabs")))),
                ["copysign"] = Fn("copysign", (c, a) => c.Values.Float(FloatMath.CopySign(AsDouble(c, Arg(c, a, 0), "copysign"), AsDouble(c, Arg(c, a, 1), "copysign")))),
                ["fmod"] = Fn("fmod", (c, a) => Fmod(c, a)),
                ["modf"] = Fn("modf", (c, a) => Modf(c, a)),
                ["frexp"] = Fn("frexp", (c, a) => Frexp(c, a)),
                ["ldexp"] = Fn("ldexp", (c, a) => Ldexp(c, a)),
                ["degrees"] = Fn("degrees", (c, a) => c.Values.Float(AsDouble(c, Arg(c, a, 0), "degrees") * (180.0 / Math.PI))),
                ["radians"] = Fn("radians", (c, a) => c.Values.Float(AsDouble(c, Arg(c, a, 0), "radians") * (Math.PI / 180.0))),

                ["exp"] = Wrap("exp", Math.Exp),
                ["expm1"] = Wrap("expm1", FloatMath.Expm1),
                ["sqrt"] = Fn("sqrt", (c, a) => Sqrt(c, a)),
                ["sin"] = Wrap("sin", Math.Sin),
                ["cos"] = Wrap("cos", Math.Cos),
                ["tan"] = Wrap("tan", Math.Tan),
                ["asin"] = Wrap("asin", Math.Asin),
                ["acos"] = Wrap("acos", Math.Acos),
                ["atan"] = Wrap("atan", Math.Atan),
                ["atan2"] = Fn("atan2", (c, a) => c.Values.Float(Math.Atan2(AsDouble(c, Arg(c, a, 0), "atan2"), AsDouble(c, Arg(c, a, 1), "atan2")))),
                ["sinh"] = Wrap("sinh", Math.Sinh),
                ["cosh"] = Wrap("cosh", Math.Cosh),
                ["tanh"] = Wrap("tanh", Math.Tanh),
                ["asinh"] = Wrap("asinh", FloatMath.Asinh),
                ["acosh"] = Wrap("acosh", FloatMath.Acosh),
                ["atanh"] = Wrap("atanh", FloatMath.Atanh),

                ["isfinite"] = Fn("isfinite", (c, a) => Predicate(c, a, PredKind.Finite)),
                ["isinf"] = Fn("isinf", (c, a) => Predicate(c, a, PredKind.Inf)),
                ["isnan"] = Fn("isnan", (c, a) => Predicate(c, a, PredKind.Nan)),

                ["pow"] = Fn("pow", (c, a) => Pow(c, a)),
                ["hypot"] = Fn("hypot", (c, a) => c.Values.Float(FloatMath.Hypot(AsDouble(c, Arg(c, a, 0), "hypot"), AsDouble(c, Arg(c, a, 1), "hypot")))),

                ["log"] = Fn("log", (c, a) => Log(c, a)),
                ["log2"] = Fn("log2", (c, a) => Log2(c, a)),
                ["log10"] = Fn("log10", (c, a) => LogHelper(c, Arg(c, a, 0), Math.Log10)),
                ["log1p"] = Fn("log1p", (c, a) => Log1p(c, a)),
            };
            AddSummationAndFactorial(ctx, m);
            AddModern(ctx, m);
            return ctx.Values.Module("math", m);
        }

        // ---- helpers ----

        private static BuiltinFunctionValue Fn(string name, Func<EvalContext, ScriptValue[], ScriptValue> f)
        {
            return BuiltinFunctionValue.Make(name, (self, args, kw, c) => f(c, args));
        }

        // A single-argument function through the domain/range wrapper.
        // log1p's domain is x > -1; the pole at -1 is a domain error (CPython m_log1p, no overflow path).
        private static ScriptValue Log1p(EvalContext c, ScriptValue[] a)
        {
            double x = AsDouble(c, Arg(c, a, 0), "log1p");
            if (x <= -1.0)
                throw Raise.ValueError(c, "math domain error");
            return c.Values.Float(Math1(c, FloatMath.Log1p(x), x));
        }

        private static BuiltinFunctionValue Wrap(string name, Func<double, double> func)
        {
            return BuiltinFunctionValue.Make(name, (self, args, kw, c) =>
            {
                double x = AsDouble(c, Arg(c, args, 0), name);
                return c.Values.Float(Math1(c, func(x), x));
            });
        }

        private static ScriptValue Arg(EvalContext ctx, ScriptValue[] a, int i)
        {
            if (a.Length <= i)
                throw Raise.TypeError(ctx, "math function missing required argument");
            return a[i];
        }

        // numeric reader; math's non-number text is "a float is required".
        internal static double AsDouble(EvalContext ctx, ScriptValue v, string func)
        {
            BoolValue bv = v as BoolValue;
            if (bv != null)
                return bv.Value ? 1.0 : 0.0;
            IntValue iv = v as IntValue;
            if (iv != null)
                return ToDoubleChecked(ctx, iv.Value);
            FloatValue fv = v as FloatValue;
            if (fv != null)
                return fv.Value;
            FractionValue fr = v as FractionValue;   // __float__ of the other numeric-tower members
            if (fr != null)
                return fr.AsDouble(ctx);
            DecimalValue dv = v as DecimalValue;
            if (dv != null)
                return (double)dv.Value;
            throw Raise.TypeError(ctx, "a float is required");
        }

        internal static double ToDoubleChecked(EvalContext ctx, BigInteger n)
        {
            double d = (double)n;
            if (double.IsInfinity(d))
                throw Raise.Overflow(ctx, "int too large to convert to float");
            return d;
        }

        private static bool IsFinite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }

        // NaN from a non-NaN input -> domain; Inf from a finite input -> range.
        private static double Math1(EvalContext ctx, double r, double x)
        {
            if (double.IsNaN(r) && !double.IsNaN(x))
                throw Raise.ValueError(ctx, "math domain error");
            if (double.IsInfinity(r) && IsFinite(x))
                throw Raise.Overflow(ctx, "math range error");
            return r;
        }

        private enum RoundMode { Ceil, Floor, Trunc }

        private static ScriptValue RoundToInt(EvalContext ctx, ScriptValue[] a, RoundMode mode)
        {
            ScriptValue v = Arg(ctx, a, 0);
            if (v is IntValue)
                return v;
            BoolValue bv = v as BoolValue;
            if (bv != null)
                return ctx.Values.Int(bv.Value ? 1 : 0);
            FloatValue fv = v as FloatValue;
            if (fv == null)
            {
                ScriptValue hook = mode == RoundMode.Ceil ? v.CeilCore(ctx) : mode == RoundMode.Floor ? v.FloorCore(ctx) : v.TruncCore(ctx);
                if (hook != null)
                    return hook;
                throw Raise.TypeError(ctx, "a float is required");
            }
            double x = fv.Value;
            if (double.IsNaN(x))
                throw Raise.ValueError(ctx, "cannot convert float NaN to integer");
            if (double.IsInfinity(x))
                throw Raise.Overflow(ctx, "cannot convert float infinity to integer");
            double r = mode == RoundMode.Ceil ? Math.Ceiling(x) : mode == RoundMode.Floor ? Math.Floor(x) : Math.Truncate(x);
            return ctx.Values.Int(new BigInteger(r));
        }

        private static ScriptValue Fmod(EvalContext ctx, ScriptValue[] a)
        {
            double fx = AsDouble(ctx, Arg(ctx, a, 0), "fmod");
            double fy = AsDouble(ctx, Arg(ctx, a, 1), "fmod");
            if (fy == 0.0)
                throw Raise.ValueError(ctx, "math domain error");
            double r = FloatMath.Fmod(fx, fy);
            if (double.IsNaN(r) && !double.IsNaN(fx) && !double.IsNaN(fy))
                throw Raise.ValueError(ctx, "math domain error");
            return ctx.Values.Float(r);
        }

        private static ScriptValue Modf(EvalContext ctx, ScriptValue[] a)
        {
            double fx = AsDouble(ctx, Arg(ctx, a, 0), "modf");
            double frac, ipart;
            if (double.IsInfinity(fx))
            {
                frac = FloatMath.CopySign(0.0, fx);
                ipart = fx;
            }
            else if (double.IsNaN(fx))
            {
                frac = fx;
                ipart = fx;
            }
            else
            {
                ipart = Math.Truncate(fx);
                frac = fx - ipart;
            }
            return ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Float(frac), ctx.Values.Float(ipart) });
        }

        private static ScriptValue Frexp(EvalContext ctx, ScriptValue[] a)
        {
            double x = AsDouble(ctx, Arg(ctx, a, 0), "frexp");
            double m;
            int e;
            FloatMath.Frexp(x, out m, out e);
            return ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Float(m), ctx.Values.Int(e) });
        }

        private static ScriptValue Ldexp(EvalContext ctx, ScriptValue[] a)
        {
            double fx = AsDouble(ctx, Arg(ctx, a, 0), "ldexp");
            ScriptValue expArg = Arg(ctx, a, 1);
            BigInteger bi;
            IntValue iv = expArg as IntValue;
            BoolValue bv = expArg as BoolValue;
            if (iv != null)
                bi = iv.Value;
            else if (bv != null)
                bi = bv.Value ? 1 : 0;
            else
                throw Raise.TypeError(ctx, "Expected an int as second argument to ldexp.");
            int n = bi > int.MaxValue ? int.MaxValue : bi < int.MinValue ? int.MinValue : (int)bi;
            double r;
            if (FloatMath.LdexpOverflowed(fx, n, out r))
                throw Raise.Overflow(ctx, "math range error");
            return ctx.Values.Float(r);
        }

        private static ScriptValue Sqrt(EvalContext ctx, ScriptValue[] a)
        {
            double fx = AsDouble(ctx, Arg(ctx, a, 0), "sqrt");
            if (fx == 0.0)
                return ctx.Values.Float(fx);   // sqrt(-0.0) = -0.0, no error
            if (fx < 0.0)
                throw Raise.ValueError(ctx, "math domain error");
            return ctx.Values.Float(Math.Sqrt(fx));
        }

        private enum PredKind { Finite, Inf, Nan }

        private static ScriptValue Predicate(EvalContext ctx, ScriptValue[] a, PredKind kind)
        {
            ScriptValue v = Arg(ctx, a, 0);
            if (v is IntValue || v is BoolValue)
                return ctx.Values.Bool(kind == PredKind.Finite);
            FloatValue fv = v as FloatValue;
            if (fv == null)
                throw Raise.TypeError(ctx, "a float is required");
            double x = fv.Value;
            bool r = kind == PredKind.Finite ? IsFinite(x) : kind == PredKind.Inf ? double.IsInfinity(x) : double.IsNaN(x);
            return ctx.Values.Bool(r);
        }
    }
}

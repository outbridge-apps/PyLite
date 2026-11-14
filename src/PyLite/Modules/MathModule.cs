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
    // errno-as-exception domain/range semantics; no state, built per importing run. Reference: Modules/mathmodule.c.
    internal static partial class MathModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                // 3.4-era core; the 3.5-3.13 members live in AddModern.
                ["pi"] = ctx.Values.Float(Math.PI),
                ["e"] = ctx.Values.Float(Math.E),

                ["ceil"] = Fn("ceil", 1, 1, (c, a) => RoundToInt(c, a, RoundMode.Ceil)),
                ["floor"] = Fn("floor", 1, 1, (c, a) => RoundToInt(c, a, RoundMode.Floor)),
                ["trunc"] = Fn("trunc", 1, 1, (c, a) => RoundToInt(c, a, RoundMode.Trunc)),

                ["fabs"] = Fn("fabs", 1, 1, (c, a) => c.Values.Float(Math.Abs(AsDouble(c, Arg(c, a, 0))))),
                ["copysign"] = Fn("copysign", 2, 2, (c, a) => c.Values.Float(FloatMath.CopySign(AsDouble(c, Arg(c, a, 0)), AsDouble(c, Arg(c, a, 1))))),
                ["fmod"] = Fn("fmod", 2, 2, (c, a) => Fmod(c, a)),
                ["modf"] = Fn("modf", 1, 1, (c, a) => Modf(c, a)),
                ["frexp"] = Fn("frexp", 1, 1, (c, a) => Frexp(c, a)),
                ["ldexp"] = Fn("ldexp", 2, 2, (c, a) => Ldexp(c, a)),
                ["degrees"] = Fn("degrees", 1, 1, (c, a) => c.Values.Float(AsDouble(c, Arg(c, a, 0)) * (180.0 / Math.PI))),
                ["radians"] = Fn("radians", 1, 1, (c, a) => c.Values.Float(AsDouble(c, Arg(c, a, 0)) * (Math.PI / 180.0))),

                ["exp"] = Wrap("exp", Math.Exp),
                ["expm1"] = Wrap("expm1", FdLibm.Expm1),
                ["sqrt"] = Fn("sqrt", 1, 1, (c, a) => Sqrt(c, a)),
                ["sin"] = Wrap("sin", FdLibm.Sin, Finite),
                ["cos"] = Wrap("cos", FdLibm.Cos, Finite),
                ["tan"] = Wrap("tan", FdLibm.Tan, Finite),
                ["asin"] = Wrap("asin", Math.Asin, "expected a number in range from -1 up to 1, got "),
                ["acos"] = Wrap("acos", Math.Acos, "expected a number in range from -1 up to 1, got "),
                ["atan"] = Wrap("atan", Math.Atan),
                ["atan2"] = Fn("atan2", 2, 2, (c, a) => c.Values.Float(Math.Atan2(AsDouble(c, Arg(c, a, 0)), AsDouble(c, Arg(c, a, 1))))),
                ["sinh"] = Wrap("sinh", Math.Sinh),
                ["cosh"] = Wrap("cosh", Math.Cosh),
                ["tanh"] = Wrap("tanh", Math.Tanh),
                ["asinh"] = Wrap("asinh", FloatMath.Asinh),
                ["acosh"] = Wrap("acosh", FloatMath.Acosh, "expected argument value not less than 1, got "),
                ["atanh"] = Wrap("atanh", FloatMath.Atanh, "expected a number between -1 and 1, got "),

                ["isfinite"] = Fn("isfinite", 1, 1, (c, a) => Predicate(c, a, PredKind.Finite)),
                ["isinf"] = Fn("isinf", 1, 1, (c, a) => Predicate(c, a, PredKind.Inf)),
                ["isnan"] = Fn("isnan", 1, 1, (c, a) => Predicate(c, a, PredKind.Nan)),

                ["pow"] = Fn("pow", 2, 2, (c, a) => Pow(c, a)),
                ["hypot"] = Fn("hypot", 0, int.MaxValue, (c, a) => c.Values.Float(FloatMath.Hypot(AsDouble(c, Arg(c, a, 0)), AsDouble(c, Arg(c, a, 1))))),

                ["log"] = Fn("log", 1, 2, (c, a) => Log(c, a)),
                ["log2"] = Fn("log2", 1, 1, (c, a) => Log2(c, a)),
                ["log10"] = Fn("log10", 1, 1, (c, a) => LogHelper(c, Arg(c, a, 0), Math.Log10)),
                ["log1p"] = Fn("log1p", 1, 1, (c, a) => Log1p(c, a)),
            };
            AddSummationAndFactorial(ctx, m);
            AddModern(ctx, m);
            AddSpecial(m);
            return ctx.Values.Module("math", m);
        }

        // ---- helpers ----

        // math functions are positional-only C functions: min..max arguments, no keywords (sqrt(4, 5) used to
        // be sqrt(4)).
        private static BuiltinFunctionValue Fn(string name, int min, int max, Func<EvalContext, ScriptValue[], ScriptValue> f)
        {
            return BuiltinFunctionValue.Make(name, (self, args, kw, c) =>
            {
                Args.NoKw(c, kw, name);
                Args.CheckPositional(c, args, name, min, max);
                return f(c, args);
            });
        }

        // A single-argument function through the domain/range wrapper.
        // log1p's domain is x > -1; the pole at -1 is a domain error (CPython m_log1p, no overflow path).
        private static ScriptValue Log1p(EvalContext c, ScriptValue[] a)
        {
            double x = AsDouble(c, Arg(c, a, 0));
            if (x <= -1.0)
                throw Domain(c, "expected argument value > -1, got ", x);
            return c.Values.Float(Math1(c, FloatMath.Log1p(x), x, null));
        }

        // domain: CPython 3.14's text for the function, completed with the argument's repr; null is "math domain error"
        private static BuiltinFunctionValue Wrap(string name, Func<double, double> func, string domain = null)
        {
            return BuiltinFunctionValue.Make(name, (self, args, kw, c) =>
            {
                double x = AsDouble(c, Args.One(c, args, kw, name));
                return c.Values.Float(Math1(c, func(x), x, domain));
            });
        }

        private const string Finite = "expected a finite input, got ";

        internal static ScriptException Domain(EvalContext c, string text, double x)
        {
            return Raise.ValueError(c, text == null ? "math domain error" : text + FloatRepr.Repr(x));
        }

        private static ScriptValue Arg(EvalContext ctx, ScriptValue[] a, int i)
        {
            if (a.Length <= i)
                throw Raise.TypeError(ctx, "math function missing required argument");
            return a[i];
        }

        // numeric reader: a float, or any number with __float__.
        internal static double AsDouble(EvalContext ctx, ScriptValue v)
        {
            BoolValue bv = v as BoolValue;
            if (bv != null)
                return bv.Value ? 1.0 : 0.0;
            IntValue iv = v as IntValue;
            if (iv != null)
                return iv.IsSmall ? iv.Small : NumericOps.ToDoubleChecked(iv.Value, ctx);
            FloatValue fv = v as FloatValue;
            if (fv != null)
                return fv.Value;
            FractionValue fr = v as FractionValue;   // __float__ of the other numeric-tower members
            if (fr != null)
                return fr.AsDouble(ctx);
            DecimalValue dv = v as DecimalValue;
            if (dv != null)
                return NearestDouble.FromDecimal(dv.Value);
            throw Raise.TypeError(ctx, "must be real number, not " + v.PyTypeName);
        }

        private static bool IsFinite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }

        // NaN from a non-NaN input -> domain; Inf from a finite input -> range.
        private static double Math1(EvalContext ctx, double r, double x, string domain)
        {
            if (double.IsNaN(r) && !double.IsNaN(x))
                throw Domain(ctx, domain, x);
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
                throw Raise.TypeError(ctx, mode == RoundMode.Trunc ? "type " + v.PyTypeName + " doesn't define __trunc__ method"
                    : "must be real number, not " + v.PyTypeName);
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
            double fx = AsDouble(ctx, Arg(ctx, a, 0));
            double fy = AsDouble(ctx, Arg(ctx, a, 1));
            if (fy == 0.0)
                throw Raise.ValueError(ctx, "math domain error");
            double r = FloatMath.Fmod(fx, fy);
            if (double.IsNaN(r) && !double.IsNaN(fx) && !double.IsNaN(fy))
                throw Raise.ValueError(ctx, "math domain error");
            return ctx.Values.Float(r);
        }

        private static ScriptValue Modf(EvalContext ctx, ScriptValue[] a)
        {
            double fx = AsDouble(ctx, Arg(ctx, a, 0));
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
            double x = AsDouble(ctx, Arg(ctx, a, 0));
            double m;
            int e;
            FloatMath.Frexp(x, out m, out e);
            return ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Float(m), ctx.Values.Int(e) });
        }

        private static ScriptValue Ldexp(EvalContext ctx, ScriptValue[] a)
        {
            double fx = AsDouble(ctx, Arg(ctx, a, 0));
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
            double fx = AsDouble(ctx, Arg(ctx, a, 0));
            if (fx == 0.0)
                return ctx.Values.Float(fx);   // sqrt(-0.0) = -0.0, no error
            if (fx < 0.0)
                throw Domain(ctx, "expected a nonnegative input, got ", fx);
            return ctx.Values.Float(Math.Sqrt(fx));
        }

        private enum PredKind { Finite, Inf, Nan }

        private static ScriptValue Predicate(EvalContext ctx, ScriptValue[] a, PredKind kind)
        {
            ScriptValue v = Arg(ctx, a, 0);
            FloatValue fv = v as FloatValue;
            double x = fv != null ? fv.Value : AsDouble(ctx, v);   // as CPython: a Fraction converts, an int past double is OverflowError
            bool r = kind == PredKind.Finite ? IsFinite(x) : kind == PredKind.Inf ? double.IsInfinity(x) : double.IsNaN(x);
            return ctx.Values.Bool(r);
        }
    }
}

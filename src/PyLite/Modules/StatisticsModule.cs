using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // The statistics module. Exact summation runs in Fraction (mean([1e16,1,1,-1e16])
    // == 0.5), NOT float. Immutable, sharable. Reference: Lib/statistics.py 3.4 (mode modernized to 3.8+).
    internal static partial class StatisticsModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["mean"] = Fn("mean", (c, a) => Mean(c, Arg(c, a, 0))),
                ["variance"] = KwFn("variance", "xbar", (c, a) => Variance(c, Arg(c, a, 0), Opt(a, 1), true)),
                ["pvariance"] = KwFn("pvariance", "mu", (c, a) => Variance(c, Arg(c, a, 0), Opt(a, 1), false)),
                ["stdev"] = KwFn("stdev", "xbar", (c, a) => Stdev(c, Arg(c, a, 0), Opt(a, 1), true)),
                ["pstdev"] = KwFn("pstdev", "mu", (c, a) => Stdev(c, Arg(c, a, 0), Opt(a, 1), false)),
                ["median"] = Fn("median", (c, a) => Median(c, Arg(c, a, 0))),
                ["median_low"] = Fn("median_low", (c, a) => MedianLowHigh(c, Arg(c, a, 0), false)),
                ["median_high"] = Fn("median_high", (c, a) => MedianLowHigh(c, Arg(c, a, 0), true)),
                ["median_grouped"] = KwFn("median_grouped", "interval", (c, a) => MedianGrouped(c, Arg(c, a, 0), Opt(a, 1))),
                ["mode"] = Fn("mode", (c, a) => Mode(c, Arg(c, a, 0))),
                ["StatisticsError"] = ExcType(ctx),
            };
            AddModern(ctx, m);
            return ctx.Values.Module("statistics", m);
        }

        private static BuiltinFunctionValue Fn(string name, Func<EvalContext, ScriptValue[], ScriptValue> f)
        {
            return BuiltinFunctionValue.Make(name, (self, args, kw, c) => f(c, args));
        }

        // (data, <second>) where the second parameter may also be passed by its keyword name.
        private static BuiltinFunctionValue KwFn(string name, string second, Func<EvalContext, ScriptValue[], ScriptValue> f)
        {
            return BuiltinFunctionValue.Make(name, (self, args, kw, c) => f(c, StrMethods.WithKw(c, args, kw, name, "data", second)));
        }

        private static ScriptValue Opt(ScriptValue[] a, int i)
        {
            return a.Length > i && a[i].Kind != ValueKind.None ? a[i] : null;
        }

        private static ScriptValue Arg(EvalContext ctx, ScriptValue[] a, int i)
        {
            if (a.Length <= i)
                throw Raise.TypeError(ctx, "statistics function missing required argument");
            return a[i];
        }

        private static TypeValue ExcType(EvalContext ctx)
        {
            BuiltinDelegate ctor = (self, args, kw, c) => c.Values.Exception(PyExceptionTypes.StatisticsError, args);
            return ctx.Values.Type("StatisticsError", ctor, null, null, PyExceptionTypes.StatisticsError);
        }

        // ---- exact summation ----

        private struct SumInfo
        {
            public int N;
            public FractionValue Total;
            public bool Dirty;
            public double NonFinite;
            public bool SawFloat;
            public bool SawFraction;
            public bool SawDecimal;
        }

        // CPython's _coerce: int mixes with anything, Fraction mixes with float, and Decimal mixes with
        // neither of them. The refusal is raised where the second type is met, not at the end.
        private static void CheckMix(EvalContext ctx, ref SumInfo info)
        {
            if (!info.SawDecimal || (!info.SawFloat && !info.SawFraction))
                return;
            throw Raise.TypeError(ctx, "don't know how to coerce "
                + (info.SawFloat ? "float" : "Fraction") + " and Decimal");
        }

        private static SumInfo Sum(EvalContext ctx, List<ScriptValue> data)
        {
            var info = new SumInfo { Total = FractionValue.Create(ctx, BigInteger.Zero, BigInteger.One), NonFinite = 0.0 };
            foreach (ScriptValue x in data)
            {
                ctx.Budget.Step();
                FractionValue fr;
                if (ExactRatio(ctx, x, out fr, ref info))
                    info.Total = (FractionValue)PyOps.BinaryOp(PyBinOp.Add, info.Total, fr, ctx);
                info.N++;
            }
            return info;
        }

        // Exact representation of a value as a Fraction; nan/inf -> dirty. Returns false when non-finite.
        private static bool ExactRatio(EvalContext ctx, ScriptValue x, out FractionValue fr, ref SumInfo info)
        {
            fr = null;
            IntValue iv = x as IntValue;
            if (iv != null)
            {
                fr = FractionValue.Create(ctx, iv.Value, BigInteger.One);
                return true;
            }
            BoolValue bv = x as BoolValue;
            if (bv != null)
            {
                fr = FractionValue.Create(ctx, bv.Value ? 1 : 0, BigInteger.One);
                return true;
            }
            FractionValue f = x as FractionValue;
            if (f != null)
            {
                info.SawFraction = true;
                CheckMix(ctx, ref info);
                fr = f;
                return true;
            }
            // A Decimal IS a rational: coefficient over a power of ten, so the exact summation below
            // carries it without losing a digit, and _convert turns the answer back into a Decimal.
            DecimalValue dv = x as DecimalValue;
            if (dv != null)
            {
                info.SawDecimal = true;
                CheckMix(ctx, ref info);
                fr = AsFraction(ctx, dv);
                return true;
            }
            FloatValue fl = x as FloatValue;
            if (fl != null)
            {
                info.SawFloat = true;
                CheckMix(ctx, ref info);
                if (double.IsNaN(fl.Value) || double.IsInfinity(fl.Value))
                {
                    info.Dirty = true;
                    info.NonFinite += fl.Value;
                    return false;
                }
                BigInteger n, d;
                Rationals.FloatToRational(fl.Value, out n, out d);
                fr = FractionValue.Create(ctx, n, d);
                return true;
            }
            throw Raise.TypeError(ctx, "can't convert type '" + x.PyTypeName + "' to numerator/denominator");
        }

        private static FractionValue AsFraction(EvalContext ctx, DecimalValue d)
        {
            BigInteger coeff;
            int exp;
            bool neg;
            d.Coeff(out coeff, out exp, out neg);
            if (neg)
                coeff = -coeff;
            return exp >= 0
                ? FractionValue.Create(ctx, coeff * BigInteger.Pow(10, exp), BigInteger.One)
                : FractionValue.Create(ctx, coeff, BigInteger.Pow(10, -exp));
        }

        private static ScriptValue Dec(EvalContext ctx, BigInteger v)
        {
            return DecimalValue.Make(ctx, BigInteger.Abs(v), 0, v.Sign < 0);
        }

        private static ScriptValue Convert(EvalContext ctx, FractionValue result, SumInfo info)
        {
            // CPython's _convert for a Decimal is T(numerator) / T(denominator), so the division rounds
            // at the context precision exactly as the rest of the module's decimal arithmetic does.
            if (info.SawDecimal)
                return PyOps.BinaryOp(PyBinOp.TrueDiv, Dec(ctx, result.Num), Dec(ctx, result.Den), ctx);
            if (info.SawFloat)
                return ctx.Values.Float(result.AsDouble(ctx));
            if (info.SawFraction)
                return result;
            if (result.Den.IsOne)
                return ctx.Values.Int(result.Num);   // all int: int when exact (CPython _convert), else float
            return ctx.Values.Float(result.AsDouble(ctx));
        }

        // ---- mean / variance / stdev ----

        private static ScriptValue Mean(EvalContext ctx, ScriptValue data)
        {
            List<ScriptValue> lst = Materialize(ctx, data);
            if (lst.Count == 0)
                throw Raise.Make(ctx, PyExceptionTypes.StatisticsError, "mean requires at least one data point");
            SumInfo info = Sum(ctx, lst);
            if (info.Dirty)
                return ctx.Values.Float(info.NonFinite);
            FractionValue result = (FractionValue)PyOps.BinaryOp(PyBinOp.TrueDiv, info.Total, ctx.Values.Int(lst.Count), ctx);
            return Convert(ctx, result, info);
        }

        private static ScriptValue Variance(EvalContext ctx, ScriptValue data, ScriptValue center, bool sample)
        {
            List<ScriptValue> lst = Materialize(ctx, data);
            int n = lst.Count;
            if (sample && n < 2)
                throw Raise.Make(ctx, PyExceptionTypes.StatisticsError, "variance requires at least two data points");
            if (!sample && n < 1)
                throw Raise.Make(ctx, PyExceptionTypes.StatisticsError, "pvariance requires at least one data point");

            SumInfo flags;
            FractionValue ss = SumSquares(ctx, lst, center, out flags);
            if (flags.Dirty)
                return ctx.Values.Float(double.NaN);
            int divisor = sample ? n - 1 : n;
            FractionValue result = (FractionValue)PyOps.BinaryOp(PyBinOp.TrueDiv, ss, ctx.Values.Int(divisor), ctx);
            return Convert(ctx, result, flags);
        }

        private static ScriptValue Stdev(EvalContext ctx, ScriptValue data, ScriptValue center, bool sample)
        {
            ScriptValue v = Variance(ctx, data, center, sample);
            DecimalValue dv = v as DecimalValue;
            if (dv != null)
                return DecimalMath.Sqrt(ctx, dv);   // Decimal data keeps a Decimal answer, as in CPython
            double d = v is FloatValue ? ((FloatValue)v).Value
                : v is IntValue ? (double)((IntValue)v).Value
                : ((FractionValue)v).AsDouble(ctx);
            return ctx.Values.Float(Math.Sqrt(d));
        }

        // sum((x-c)^2) - sum(x-c)^2/n, in exact Fraction (port of _ss).
        private static FractionValue SumSquares(EvalContext ctx, List<ScriptValue> lst, ScriptValue center, out SumInfo flags)
        {
            flags = new SumInfo();
            FractionValue c;
            if (center != null)
            {
                FractionValue cf;
                if (!ExactRatio(ctx, center, out cf, ref flags))
                {
                    flags.Dirty = true;
                    return FractionValue.Create(ctx, BigInteger.Zero, BigInteger.One);
                }
                c = cf;
            }
            else
            {
                SumInfo si = Sum(ctx, lst);
                flags.SawFloat = si.SawFloat;
                flags.SawFraction = si.SawFraction;
                if (si.Dirty)
                {
                    flags.Dirty = true;
                    return FractionValue.Create(ctx, BigInteger.Zero, BigInteger.One);
                }
                c = (FractionValue)PyOps.BinaryOp(PyBinOp.TrueDiv, si.Total, ctx.Values.Int(lst.Count), ctx);
            }

            FractionValue total2 = FractionValue.Create(ctx, BigInteger.Zero, BigInteger.One);
            FractionValue totalErr = FractionValue.Create(ctx, BigInteger.Zero, BigInteger.One);
            foreach (ScriptValue x in lst)
            {
                ctx.Budget.Step();
                FractionValue fx;
                if (!ExactRatio(ctx, x, out fx, ref flags))
                {
                    flags.Dirty = true;
                    return total2;
                }
                FractionValue diff = (FractionValue)PyOps.BinaryOp(PyBinOp.Sub, fx, c, ctx);
                FractionValue sq = (FractionValue)PyOps.BinaryOp(PyBinOp.Mul, diff, diff, ctx);
                total2 = (FractionValue)PyOps.BinaryOp(PyBinOp.Add, total2, sq, ctx);
                totalErr = (FractionValue)PyOps.BinaryOp(PyBinOp.Add, totalErr, diff, ctx);
            }
            FractionValue errSq = (FractionValue)PyOps.BinaryOp(PyBinOp.Mul, totalErr, totalErr, ctx);
            FractionValue correction = (FractionValue)PyOps.BinaryOp(PyBinOp.TrueDiv, errSq, ctx.Values.Int(lst.Count), ctx);
            return (FractionValue)PyOps.BinaryOp(PyBinOp.Sub, total2, correction, ctx);
        }

        // ---- median family ----

        private static ScriptValue Median(EvalContext ctx, ScriptValue data)
        {
            ScriptValue[] arr = SortedArray(ctx, data);
            if (arr.Length == 0)
                throw Raise.Make(ctx, PyExceptionTypes.StatisticsError, "no median for empty data");
            int n = arr.Length;
            if ((n & 1) == 1)
                return arr[n / 2];
            int i = n / 2;
            ScriptValue s = PyOps.BinaryOp(PyBinOp.Add, arr[i - 1], arr[i], ctx);
            return PyOps.BinaryOp(PyBinOp.TrueDiv, s, ctx.Values.Int(2), ctx);
        }

        private static ScriptValue MedianLowHigh(EvalContext ctx, ScriptValue data, bool high)
        {
            ScriptValue[] arr = SortedArray(ctx, data);
            if (arr.Length == 0)
                throw Raise.Make(ctx, PyExceptionTypes.StatisticsError, "no median for empty data");
            int n = arr.Length;
            if ((n & 1) == 1)
                return arr[n / 2];
            return high ? arr[n / 2] : arr[n / 2 - 1];
        }

        private static ScriptValue MedianGrouped(EvalContext ctx, ScriptValue data, ScriptValue intervalArg)
        {
            ScriptValue[] arr = SortedArray(ctx, data);
            int n = arr.Length;
            if (n == 0)
                throw Raise.Make(ctx, PyExceptionTypes.StatisticsError, "no median for empty data");
            if (n == 1)
                return ctx.Values.Float(NumToDouble(ctx, arr[0]));
            double interval = intervalArg == null ? 1.0 : NumToDouble(ctx, intervalArg);
            ScriptValue x = arr[n / 2];
            int l1 = 0;
            while (l1 < n && !PyOps.Equals(arr[l1], x, ctx, 0))
                l1++;
            int l2 = l1;
            while (l2 + 1 < n && PyOps.Equals(arr[l2 + 1], x, ctx, 0))
                l2++;
            double cf = l1;
            double f = l2 - l1 + 1;
            double xd = NumToDouble(ctx, x);
            double result = (xd - interval / 2.0) + interval * (n / 2.0 - cf) / f;
            return ctx.Values.Float(result);
        }

        private static double NumToDouble(EvalContext ctx, ScriptValue v)
        {
            IntValue iv = v as IntValue;
            if (iv != null)
                return (double)iv.Value;
            BoolValue bv = v as BoolValue;
            if (bv != null)
                return bv.Value ? 1.0 : 0.0;
            FloatValue fl = v as FloatValue;
            if (fl != null)
                return fl.Value;
            FractionValue fr = v as FractionValue;
            if (fr != null)
                return fr.AsDouble(ctx);
            throw Raise.TypeError(ctx, "can't convert type '" + v.PyTypeName + "' to a number");
        }

        // ---- mode ----

        private static ScriptValue Mode(EvalContext ctx, ScriptValue data)
        {
            DictValue counts = ctx.Values.Dict(8);
            IScriptIterator it = PyOps.GetIterator(data, ctx);
            ScriptValue x;
            while (it.MoveNext(ctx, out x))
            {
                ctx.Budget.Step();
                ScriptValue cur;
                BigInteger c = counts.TryGet(x, ctx, out cur) ? ((IntValue)cur).Value + 1 : BigInteger.One;
                counts.SetItem(x, ctx.Values.Int(c), ctx);   // hashing x -> TypeError if unhashable
            }
            if (counts.Count == 0)
                throw Raise.Make(ctx, PyExceptionTypes.StatisticsError, "no mode for empty data");

            BigInteger maxCount = BigInteger.MinusOne;
            IScriptIterator keys = PyOps.GetIterator(counts, ctx);
            ScriptValue k;
            while (keys.MoveNext(ctx, out k))
            {
                ScriptValue cur;
                counts.TryGet(k, ctx, out cur);
                if (((IntValue)cur).Value > maxCount)
                    maxCount = ((IntValue)cur).Value;
            }
            IScriptIterator keys2 = PyOps.GetIterator(counts, ctx);
            while (keys2.MoveNext(ctx, out k))
            {
                ScriptValue cur;
                counts.TryGet(k, ctx, out cur);
                if (((IntValue)cur).Value == maxCount)
                    return k;
            }
            return ctx.Values.None;   // unreachable
        }

        // ---- materialization / sort ----

        private static List<ScriptValue> Materialize(EvalContext ctx, ScriptValue data)
        {
            var lst = new List<ScriptValue>();
            IScriptIterator it = PyOps.GetIterator(data, ctx);
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
            {
                ctx.Budget.Step();
                lst.Add(v);
            }
            return lst;
        }

        private static ScriptValue[] SortedArray(EvalContext ctx, ScriptValue data)
        {
            List<ScriptValue> lst = Materialize(ctx, data);
            ScriptValue[] arr = lst.ToArray();
            StableSort.Sort(ctx, arr, null);
            return arr;
        }
    }
}

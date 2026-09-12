using System;
using System.Collections.Generic;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // statistics members from CPython 3.6-3.11: fmean, geometric_mean, harmonic_mean
    // multimode, quantiles, correlation, covariance, linear_regression. Float paths sum via ShewchukSum
    // (exact partials) mirroring CPython's fsum usage.
    internal static partial class StatisticsModule
    {
        private static void AddModern(EvalContext ctx, Dictionary<string, ScriptValue> m)
        {
            m["fmean"] = BuiltinFunctionValue.Make("fmean", FMean);
            m["geometric_mean"] = Fn("geometric_mean", GeometricMean);
            m["harmonic_mean"] = BuiltinFunctionValue.Make("harmonic_mean", HarmonicMean);
            m["multimode"] = Fn("multimode", MultiMode);
            m["quantiles"] = BuiltinFunctionValue.Make("quantiles", Quantiles);
            m["correlation"] = BuiltinFunctionValue.Make("correlation", Correlation);
            m["covariance"] = Fn("covariance", Covariance);
            m["linear_regression"] = BuiltinFunctionValue.Make("linear_regression", LinearRegression);
        }

        private static ScriptException StatsError(EvalContext ctx, string msg)
        {
            return Raise.Make(ctx, PyExceptionTypes.StatisticsError, msg);
        }

        private static double ToDouble(EvalContext ctx, ScriptValue v)
        {
            if (v.Kind == ValueKind.Float)
                return ((FloatValue)v).Value;
            if (v.Kind == ValueKind.Int || v.Kind == ValueKind.Bool)
                return (double)Outbridge.PyLite.Runtime.Values.NumericOps.AsBigInteger(v);
            FractionValue fr = v as FractionValue;
            if (fr != null)
                return fr.AsDouble(ctx);
            DecimalValue dv = v as DecimalValue;
            if (dv != null)
                return (double)dv.Value;   // fmean and friends answer in float, as they do in CPython
            throw Raise.TypeError(ctx, "can't convert type '" + v.PyTypeName + "' to float");
        }

        private static double[] Doubles(EvalContext ctx, ScriptValue data)
        {
            List<ScriptValue> lst = Materialize(ctx, data);
            var d = new double[lst.Count];
            for (int i = 0; i < lst.Count; i++)
                d[i] = ToDouble(ctx, lst[i]);
            return d;
        }

        // ---- fmean (3.8; weights= 3.11) ----
        private static ScriptValue FMean(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, a, "fmean", 1, 2);
            double[] xs = Doubles(ctx, a[0]);
            ScriptValue wArg = a.Length == 2 ? a[1] : null;
            ScriptValue wkw;
            if (kw.TryGet("weights", out wkw))
                wArg = wkw;
            if (wArg == null || wArg.Kind == ValueKind.None)
            {
                if (xs.Length == 0)
                    throw StatsError(ctx, "fmean requires at least one data point");
                return ctx.Values.Float(ShewchukSum.Sum(ctx, xs) / xs.Length);
            }
            double[] ws = Doubles(ctx, wArg);
            if (ws.Length != xs.Length)
                throw StatsError(ctx, "data and weights must be the same length");
            var products = new double[xs.Length];
            for (int i = 0; i < xs.Length; i++)
                products[i] = xs[i] * ws[i];
            double den = ShewchukSum.Sum(ctx, ws);
            if (den == 0.0)
                throw StatsError(ctx, "sum of weights must be non-zero");
            return ctx.Values.Float(ShewchukSum.Sum(ctx, products) / den);
        }

        // ---- geometric_mean (3.8) ----
        private static ScriptValue GeometricMean(EvalContext ctx, ScriptValue[] a)
        {
            double[] xs = Doubles(ctx, Arg(ctx, a, 0));
            if (xs.Length == 0)
                throw StatsError(ctx, "geometric mean requires a non-empty dataset containing positive numbers");
            var logs = new double[xs.Length];
            for (int i = 0; i < xs.Length; i++)
            {
                if (xs[i] <= 0.0)
                    throw StatsError(ctx, "geometric mean requires a non-empty dataset containing positive numbers");
                logs[i] = Math.Log(xs[i]);
            }
            return ctx.Values.Float(Math.Exp(ShewchukSum.Sum(ctx, logs) / xs.Length));
        }

        // ---- harmonic_mean (3.6; weights= 3.10) ----
        private static ScriptValue HarmonicMean(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, a, "harmonic_mean", 1, 2);
            double[] xs = Doubles(ctx, a[0]);
            if (xs.Length == 0)
                throw StatsError(ctx, "harmonic_mean requires at least one data point");
            ScriptValue wArg = a.Length == 2 ? a[1] : null;
            ScriptValue wkw;
            if (kw.TryGet("weights", out wkw))
                wArg = wkw;
            // Exact-Fraction reciprocals (CPython computes via _sum, so harmonic_mean([40,60]) == 48.0 exactly).
            List<ScriptValue> data = Materialize(ctx, a[0]);
            List<ScriptValue> weights = null;
            if (wArg != null && wArg.Kind != ValueKind.None)
            {
                weights = Materialize(ctx, wArg);
                if (weights.Count != data.Count)
                    throw StatsError(ctx, "Number of weights does not match data size");
            }
            var ignore = new SumInfo();
            FractionValue total = FractionValue.Create(ctx, System.Numerics.BigInteger.Zero, System.Numerics.BigInteger.One);
            FractionValue wTotal = FractionValue.Create(ctx, System.Numerics.BigInteger.Zero, System.Numerics.BigInteger.One);
            for (int i = 0; i < data.Count; i++)
            {
                ctx.Budget.Step();
                double x = ToDouble(ctx, data[i]);
                double w = weights != null ? ToDouble(ctx, weights[i]) : 1.0;
                if (x < 0.0 || w < 0.0)
                    throw StatsError(ctx, "harmonic mean does not support negative values");
                if (x == 0.0)
                    return ctx.Values.Float(0.0);
                FractionValue fx, fw;
                if (!ExactRatio(ctx, data[i], out fx, ref ignore))
                    return ctx.Values.Float(double.NaN);   // nan/inf input
                if (weights != null)
                {
                    if (!ExactRatio(ctx, weights[i], out fw, ref ignore))
                        return ctx.Values.Float(double.NaN);
                }
                else
                    fw = FractionValue.Create(ctx, System.Numerics.BigInteger.One, System.Numerics.BigInteger.One);
                FractionValue recip = (FractionValue)PyOps.BinaryOp(PyBinOp.TrueDiv, fw, fx, ctx);
                total = (FractionValue)PyOps.BinaryOp(PyBinOp.Add, total, recip, ctx);
                wTotal = (FractionValue)PyOps.BinaryOp(PyBinOp.Add, wTotal, fw, ctx);
            }
            FractionValue mean = (FractionValue)PyOps.BinaryOp(PyBinOp.TrueDiv, wTotal, total, ctx);
            return ctx.Values.Float(mean.AsDouble(ctx));
        }

        // ---- multimode (3.8): all most-frequent values, first-encountered order; empty -> [] ----
        private static ScriptValue MultiMode(EvalContext ctx, ScriptValue[] a)
        {
            List<ScriptValue> lst = Materialize(ctx, Arg(ctx, a, 0));
            DictValue counts = ctx.Values.Dict(lst.Count);
            var one = ctx.Values.Int(1);
            foreach (ScriptValue x in lst)
            {
                ctx.Budget.Step();
                ScriptValue cur;
                counts.SetItem(x, counts.TryGet(x, ctx, out cur)
                    ? PyOps.BinaryOp(PyBinOp.Add, cur, one, ctx) : one, ctx);
            }
            System.Numerics.BigInteger best = 0;
            var t = counts.Table;
            for (int p = 0; p < t.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, v;
                if (t.TryGetEntryAt(p, out h, out k, out v) && ((IntValue)v).Value > best)
                    best = ((IntValue)v).Value;
            }
            ListValue result = ctx.Values.List(4);
            for (int p = 0; p < t.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, v;
                if (t.TryGetEntryAt(p, out h, out k, out v) && ((IntValue)v).Value == best)
                    result.Add(k, ctx);
            }
            return result;
        }

        // ---- quantiles (3.8): exclusive/inclusive cut points via the engine operators ----
        private static ScriptValue Quantiles(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, a, "quantiles", 1);
            int n = 4;
            ScriptValue v;
            if (kw.TryGet("n", out v))
                n = Coerce.ToInt32(ctx, v, "'" + v.PyTypeName + "' object cannot be interpreted as an integer");
            string method = "exclusive";
            if (kw.TryGet("method", out v) && v.Kind == ValueKind.Str)
                method = ((StrValue)v).Value;
            if (n < 1)
                throw StatsError(ctx, "n must be at least 1");
            ScriptValue[] data = SortedArray(ctx, a[0]);
            int ld = data.Length;
            if (ld < 2)
                throw StatsError(ctx, "must have at least two data points");
            ListValue result = ctx.Values.List(n - 1);
            IntValue nVal = ctx.Values.Int(n);
            if (method == "inclusive")
            {
                int mm = ld - 1;
                for (int i = 1; i < n; i++)
                {
                    ctx.Budget.Step();
                    int j = i * mm / n, delta = i * mm - j * n;
                    result.Add(Interpolate(ctx, data[j], data[j + 1], n - delta, delta, nVal), ctx);
                }
                return result;
            }
            if (method != "exclusive")
                throw Raise.ValueError(ctx, "Unknown method: '" + method + "'");
            int m = ld + 1;
            for (int i = 1; i < n; i++)
            {
                ctx.Budget.Step();
                int j = i * m / n;
                j = j < 1 ? 1 : (j > ld - 1 ? ld - 1 : j);
                int delta = i * m - j * n;
                result.Add(Interpolate(ctx, data[j - 1], data[j], n - delta, delta, nVal), ctx);
            }
            return result;
        }

        // (lo*(n-delta) + hi*delta) / n
        private static ScriptValue Interpolate(EvalContext ctx, ScriptValue lo, ScriptValue hi, int wLo, int wHi, IntValue n)
        {
            ScriptValue s = PyOps.BinaryOp(PyBinOp.Add,
                PyOps.BinaryOp(PyBinOp.Mul, lo, ctx.Values.Int(wLo), ctx),
                PyOps.BinaryOp(PyBinOp.Mul, hi, ctx.Values.Int(wHi), ctx), ctx);
            return PyOps.BinaryOp(PyBinOp.TrueDiv, s, n, ctx);
        }

        // ---- correlation / covariance / linear_regression (3.10) ----

        private static void CenteredPair(EvalContext ctx, ScriptValue[] a, string fn,
            out double[] cx, out double[] cy)
        {
            double[] xs = Doubles(ctx, a[0]);
            double[] ys = Doubles(ctx, a[1]);
            if (xs.Length != ys.Length)
                throw StatsError(ctx, fn + " requires that both inputs have same number of data points");
            if (xs.Length < 2)
                throw StatsError(ctx, fn + " requires at least two data points");
            double xbar = ShewchukSum.Sum(ctx, xs) / xs.Length;
            double ybar = ShewchukSum.Sum(ctx, ys) / ys.Length;
            cx = new double[xs.Length];
            cy = new double[ys.Length];
            for (int i = 0; i < xs.Length; i++)
            {
                cx[i] = xs[i] - xbar;
                cy[i] = ys[i] - ybar;
            }
        }

        private static double SumProducts(EvalContext ctx, double[] p, double[] q)
        {
            var prod = new double[p.Length];
            for (int i = 0; i < p.Length; i++)
                prod[i] = p[i] * q[i];
            return ShewchukSum.Sum(ctx, prod);
        }

        private static ScriptValue Correlation(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, a, "correlation", 2);
            ScriptValue mv;
            if (kw.TryGet("method", out mv) && mv.Kind == ValueKind.Str && ((StrValue)mv).Value != "linear")
                throw Raise.ValueError(ctx, "Unknown method: '" + ((StrValue)mv).Value + "'");   // ranked -> v3
            double[] cx, cy;
            CenteredPair(ctx, a, "correlation", out cx, out cy);
            double sxy = SumProducts(ctx, cx, cy);
            double sxx = SumProducts(ctx, cx, cx);
            double syy = SumProducts(ctx, cy, cy);
            if (sxx == 0.0 || syy == 0.0)
                throw StatsError(ctx, "at least one of the inputs is constant");
            return ctx.Values.Float(sxy / Math.Sqrt(sxx * syy));
        }

        private static ScriptValue Covariance(EvalContext ctx, ScriptValue[] a)
        {
            Args.Exactly(ctx, a, "covariance", 2);
            double[] cx, cy;
            CenteredPair(ctx, a, "covariance", out cx, out cy);
            return ctx.Values.Float(SumProducts(ctx, cx, cy) / (cx.Length - 1));
        }

        private static ScriptValue LinearRegression(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, a, "linear_regression", 2);
            bool proportional = false;
            ScriptValue pv;
            if (kw.TryGet("proportional", out pv))
                proportional = pv.IsTruthy(ctx);
            double slope, intercept;
            if (proportional)
            {
                double[] xs = Doubles(ctx, a[0]);
                double[] ys = Doubles(ctx, a[1]);
                if (xs.Length != ys.Length)
                    throw StatsError(ctx, "linear regression requires that both inputs have same number of data points");
                if (xs.Length < 2)
                    throw StatsError(ctx, "linear regression requires at least two data points");
                double sxy = SumProducts(ctx, xs, ys);
                double sxx = SumProducts(ctx, xs, xs);
                if (sxx == 0.0)
                    throw StatsError(ctx, "x is constant");
                slope = sxy / sxx;
                intercept = 0.0;
            }
            else
            {
                double[] cx, cy;
                CenteredPair(ctx, new[] { a[0], a[1] }, "linear regression", out cx, out cy);
                double sxx = SumProducts(ctx, cx, cx);
                if (sxx == 0.0)
                    throw StatsError(ctx, "x is constant");
                slope = SumProducts(ctx, cx, cy) / sxx;
                double xbar = ShewchukSum.Sum(ctx, Doubles(ctx, a[0])) / cx.Length;
                double ybar = ShewchukSum.Sum(ctx, Doubles(ctx, a[1])) / cy.Length;
                intercept = ybar - slope * xbar;
            }
            // LinearRegression(slope=..., intercept=...) namedtuple (CPython parity).
            ScriptTypeInfo ti = NamedTupleValue.BuildInstanceType("LinearRegression", LinRegFields);
            var info = new NamedTupleInfo("LinearRegression", LinRegFields, ti);
            return new NamedTupleValue(info, new ScriptValue[] { ctx.Values.Float(slope), ctx.Values.Float(intercept) });
        }

        private static readonly string[] LinRegFields = { "slope", "intercept" };
    }
}

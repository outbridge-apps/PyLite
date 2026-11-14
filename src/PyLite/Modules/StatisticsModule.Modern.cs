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
            m["fmean"] = BuiltinFunctionValue.Make("fmean", Args.LeadKeywords("fmean", FMean, 1, "data", "weights"));
            m["geometric_mean"] = Fn("geometric_mean", GeometricMean, "data");
            m["harmonic_mean"] = BuiltinFunctionValue.Make("harmonic_mean", Args.LeadKeywords("harmonic_mean", HarmonicMean, 1, "data", "weights"));
            m["multimode"] = Fn("multimode", MultiMode, "data");
            m["quantiles"] = BuiltinFunctionValue.Make("quantiles", Quantiles);
            m["correlation"] = BuiltinFunctionValue.Make("correlation", Args.KnownKeywords("correlation", 2, Correlation, "x", "y", "method"));
            m["covariance"] = Fn("covariance", Covariance, "x", "y");
            m["LinearRegression"] = LinearRegressionType(ctx);
            m["linear_regression"] = BuiltinFunctionValue.Make("linear_regression", Args.KnownKeywords("linear_regression", 2, LinearRegression, "x", "y", "proportional"));
        }

        private static ScriptException StatsError(EvalContext ctx, string msg)
        {
            return Raise.Make(ctx, PyExceptionTypes.StatisticsError, msg);
        }

        // fmean and friends answer in float, as they do in CPython
        private static double ToDouble(EvalContext ctx, ScriptValue v)
        {
            double d;
            if (Coerce.TryToReal(ctx, v, true, out d))
                return d;
            throw Raise.TypeError(ctx, "can't convert type '" + v.PyTypeName + "' to float");
        }

        private static double[] Doubles(EvalContext ctx, ScriptValue data)
        {
            List<ScriptValue> lst = Materialize(ctx, data);
            ctx.Values.PreCharge(8L * lst.Count);
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
        // CPython 3.14: fsum(map(log, positives)) and exp of the mean. Each item is logged as math.log does, so
        // a 400-digit int has a log (it was inf); a zero makes the answer 0.0, a negative is an error.
        private static ScriptValue GeometricMean(EvalContext ctx, ScriptValue[] a)
        {
            List<ScriptValue> items = Materialize(ctx, Arg(ctx, a, 0));
            ctx.Values.PreCharge(8L * items.Count);
            var logs = new double[items.Count];   // the reading charged a step per item
            int k = 0;
            bool foundZero = false;
            foreach (ScriptValue x in items)
            {
                FloatValue fv = x as FloatValue;
                int sign = fv != null ? (fv.Value > 0.0 || double.IsNaN(fv.Value) ? 1 : fv.Value == 0.0 ? 0 : -1) : SignOf(ctx, x);
                if (sign > 0)
                    logs[k++] = fv != null ? Math.Log(fv.Value) : MathModule.LogHelperDouble(ctx, x, Ln);
                else if (sign == 0)
                    foundZero = true;
                else
                    throw Raise.Make(ctx, PyExceptionTypes.StatisticsError, new[] { (ScriptValue)ctx.Values.Str("No negative inputs allowed"), x });
            }
            if (items.Count == 0)
                throw StatsError(ctx, "Must have a non-empty dataset");
            if (k < logs.Length)
                Array.Resize(ref logs, k);
            double total = ShewchukSum.Sum(ctx, logs);
            if (double.IsNaN(total))
                return ctx.Values.Float(NumericOps.PyNaN);
            if (foundZero)
                return ctx.Values.Float(double.IsPositiveInfinity(total) ? NumericOps.PyNaN : 0.0);
            double r = Math.Exp(total / items.Count);
            if (double.IsInfinity(r) && !double.IsInfinity(total))
                throw Raise.Overflow(ctx, "math range error");
            return ctx.Values.Float(r);
        }

        // A value's sign for the negative and zero tests, exact for an int: a 400-digit int has no float, and
        // CPython never converts it here (harmonic_mean([10**400, 1]) is 2.0).
        private static double SignProbe(EvalContext ctx, ScriptValue v)
        {
            IntValue iv = v as IntValue;
            return iv != null && !iv.IsSmall ? iv.Value.Sign : ToDouble(ctx, v);
        }

        private static readonly Func<double, double> Ln = Math.Log;

        // geometric_mean's `x > 0.0 or math.isnan(x)` (1: logged), `x == 0.0` (0), else -1 for a non-float: an
        // int directly, anything else through the script's own comparisons.
        private static int SignOf(EvalContext ctx, ScriptValue x)
        {
            ctx.Budget.Step();   // the slow path may call the script's comparisons
            IntValue iv = x as IntValue;
            if (iv != null)
                return iv.IsSmall ? Math.Sign(iv.Small) : iv.Value.Sign;
            FloatValue zero = ctx.Values.Float(0.0);
            if (PyOps.RichCompare(PyCmpOp.Gt, x, zero, ctx).IsTruthy(ctx) || double.IsNaN(MathModule.AsDouble(ctx, x)))
                return 1;
            return PyOps.RichCompare(PyCmpOp.Eq, x, zero, ctx).IsTruthy(ctx) ? 0 : -1;
        }

        // ---- harmonic_mean (3.6; weights= 3.10) ----
        private static ScriptValue HarmonicMean(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, a, "harmonic_mean", 1, 2);
            ScriptValue wArg = a.Length == 2 ? a[1] : null;
            ScriptValue wkw;
            if (kw.TryGet("weights", out wkw))
                wArg = wkw;
            // Exact-Fraction reciprocals (CPython computes via _sum, so harmonic_mean([40,60]) == 48.0 exactly).
            // The data is read once: an iterator has nothing left for a second pass.
            List<ScriptValue> data = Materialize(ctx, a[0]);
            if (data.Count == 0)
                throw StatsError(ctx, "harmonic_mean requires at least one data point");
            List<ScriptValue> weights = null;
            if (wArg != null && wArg.Kind != ValueKind.None)
            {
                weights = Materialize(ctx, wArg);
                if (weights.Count != data.Count)
                    throw StatsError(ctx, "Number of weights does not match data size");
            }
            if (data.Count == 1 && weights == null)
            {
                if (SignProbe(ctx, data[0]) < 0.0)
                    throw StatsError(ctx, "harmonic mean does not support negative values");
                return data[0];   // CPython returns the one value as it is
            }
            var ignore = new SumInfo();
            FractionValue one = FractionValue.Create(ctx, System.Numerics.BigInteger.One, System.Numerics.BigInteger.One);
            FractionValue total = FractionValue.Create(ctx, System.Numerics.BigInteger.Zero, System.Numerics.BigInteger.One);
            FractionValue wTotal = total;
            for (int i = 0; i < data.Count; i++)
            {
                ctx.Budget.Step();
                double x = SignProbe(ctx, data[i]);
                double w = weights != null ? SignProbe(ctx, weights[i]) : 1.0;
                if (x < 0.0 || w < 0.0)
                    throw StatsError(ctx, "harmonic mean does not support negative values");
                FractionValue fx, fw = one;
                if (weights != null && !ExactRatio(ctx, weights[i], out fw, ref ignore))
                    return ctx.Values.Float(NumericOps.PyNaN);
                wTotal = (FractionValue)PyOps.BinaryOp(PyBinOp.Add, wTotal, fw, ctx);
                if (w == 0.0 || double.IsPositiveInfinity(x))
                    continue;                      // `w / x if w else 0`; w / inf is 0.0
                if (x == 0.0)
                    return ctx.Values.Int(0);      // CPython answers its ZeroDivisionError with 0
                if (!ExactRatio(ctx, data[i], out fx, ref ignore))
                    return ctx.Values.Float(NumericOps.PyNaN);   // a NaN
                FractionValue recip = (FractionValue)PyOps.BinaryOp(PyBinOp.TrueDiv, fw, fx, ctx);
                total = (FractionValue)PyOps.BinaryOp(PyBinOp.Add, total, recip, ctx);
            }
            if (total.Num.IsZero)
                throw StatsError(ctx, "Weighted sum must be positive");
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
            for (int p = t.WalkStart(ctx); p < t.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, v;
                if (t.TryGetEntryAt(p, out h, out k, out v) && ((IntValue)v).Value > best)
                    best = ((IntValue)v).Value;
            }
            ListValue result = ctx.Values.List(4);
            for (int p = t.WalkStart(ctx); p < t.EntriesUsed; p++)
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
            var r = new KwReader(ctx, kw, "quantiles");   // n and method are keyword-only; anything else is refused
            ScriptValue v = r.GetOrNull("n");
            int n = v == null ? 4 : Coerce.ToInt32(ctx, v, "'" + v.PyTypeName + "' object cannot be interpreted as an integer");
            v = r.GetOrNull("method");
            string method = v != null && v.Kind == ValueKind.Str ? ((StrValue)v).Value : "exclusive";
            r.RejectInvalid();
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
                    long t = (long)i * mm;   // i * mm overflowed int past 2**31 (quantiles of 100001 points, n=30000)
                    int j = (int)(t / n);
                    long delta = t - (long)j * n;
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
                long t = (long)i * m;
                int j = (int)(t / n);
                j = j < 1 ? 1 : (j > ld - 1 ? ld - 1 : j);
                long delta = t - (long)j * n;
                result.Add(Interpolate(ctx, data[j - 1], data[j], n - delta, delta, nVal), ctx);
            }
            return result;
        }

        // (lo*(n-delta) + hi*delta) / n
        private static ScriptValue Interpolate(EvalContext ctx, ScriptValue lo, ScriptValue hi, long wLo, long wHi, IntValue n)
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
            double xbar, ybar;
            CenteredPair(ctx, a, fn, out cx, out cy, out xbar, out ybar);
        }

        // Each input is read once (an iterator has no second pass); the means come back for the caller.
        private static void CenteredPair(EvalContext ctx, ScriptValue[] a, string fn,
            out double[] cx, out double[] cy, out double xbar, out double ybar)
        {
            double[] xs = Doubles(ctx, a[0]);
            double[] ys = Doubles(ctx, a[1]);
            if (xs.Length != ys.Length)
                throw StatsError(ctx, fn + " requires that both inputs have same number of data points");
            if (xs.Length < 2)
                throw StatsError(ctx, fn + " requires at least two data points");
            xbar = ShewchukSum.Sum(ctx, xs) / xs.Length;
            ybar = ShewchukSum.Sum(ctx, ys) / ys.Length;
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

        // _rank(data, start=(n - 1) / -2): ties take the mean of their places. Centering first kept the order and the ties.
        private static double[] CenteredRanks(EvalContext ctx, double[] xs)
        {
            ctx.Budget.ChargeLinear(xs.Length);
            ctx.Values.PreCharge(12L * xs.Length);
            int n = xs.Length;
            var order = new int[n];
            for (int i = 0; i < n; i++)
                order[i] = i;
            Array.Sort((double[])xs.Clone(), order);
            var ranks = new double[n];
            double start = (n - 1) / -2.0;
            for (int i = 0; i < n;)
            {
                int j = i;
                while (j + 1 < n && xs[order[j + 1]] == xs[order[i]])
                    j++;
                double rank = start + (i + j) / 2.0;
                for (int k = i; k <= j; k++)
                    ranks[order[k]] = rank;
                i = j + 1;
            }
            return ranks;
        }

        private static ScriptValue Correlation(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, a, "correlation", 2);
            ScriptValue mv;
            bool ranked = false;
            if (kw.TryGet("method", out mv))
            {
                string method = mv.Kind == ValueKind.Str ? ((StrValue)mv).Value : null;
                ranked = method == "ranked";
                if (!ranked && method != "linear")
                    throw Raise.ValueError(ctx, "Unknown method: " + mv.Repr(ctx));
            }
            double[] cx, cy;
            CenteredPair(ctx, a, "correlation", out cx, out cy);
            if (ranked)
            {
                cx = CenteredRanks(ctx, cx);
                cy = CenteredRanks(ctx, cy);
            }

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
                double xbar, ybar;
                CenteredPair(ctx, a, "linear regression", out cx, out cy, out xbar, out ybar);
                double sxx = SumProducts(ctx, cx, cx);
                if (sxx == 0.0)
                    throw StatsError(ctx, "x is constant");
                slope = SumProducts(ctx, cx, cy) / sxx;
                intercept = ybar - slope * xbar;
            }
            // LinearRegression(slope=..., intercept=...) namedtuple (CPython parity).
            return new NamedTupleValue(LinRegInfo, new ScriptValue[] { ctx.Values.Float(slope), ctx.Values.Float(intercept) });
        }

        private static readonly string[] LinRegFields = { "slope", "intercept" };
        private static readonly NamedTupleInfo LinRegInfo = new NamedTupleInfo("LinearRegression", LinRegFields,
            NamedTupleValue.BuildInstanceType("statistics.LinearRegression", LinRegFields));

        private static TypeValue LinearRegressionType(EvalContext ctx)
        {
            var statics = new Dictionary<string, ScriptValue>(StringComparer.Ordinal) { ["_fields"] = NamedTupleValue.FieldsTuple(LinRegInfo, ctx) };
            return ctx.Values.EngineType(LinRegInfo.InstanceType.Name, (s, a, k, c) => NamedTupleValue.Construct(LinRegInfo, a, k, c), LinRegInfo.InstanceType, statics);
        }
    }
}

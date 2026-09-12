using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // math members added in CPython 3.5-3.13 (modernization): tau/inf/nan, isclose
    // gcd/lcm, isqrt, prod, comb/perm, dist + n-ary hypot, remainder, nextafter/ulp, cbrt/exp2, sumprod, fma.
    internal static partial class MathModule
    {
        private static void AddModern(EvalContext ctx, Dictionary<string, ScriptValue> m)
        {
            m["tau"] = ctx.Values.Float(2.0 * Math.PI);
            m["inf"] = ctx.Values.Float(double.PositiveInfinity);
            m["nan"] = ctx.Values.Float(double.NaN);

            m["isclose"] = BuiltinFunctionValue.Make("isclose", IsClose);
            m["gcd"] = Fn("gcd", Gcd);
            m["lcm"] = Fn("lcm", Lcm);
            m["isqrt"] = Fn("isqrt", Isqrt);
            m["prod"] = BuiltinFunctionValue.Make("prod", Prod);
            m["comb"] = Fn("comb", Comb);
            m["perm"] = Fn("perm", Perm);
            m["dist"] = Fn("dist", Dist);
            m["remainder"] = Fn("remainder", Remainder);
            m["nextafter"] = BuiltinFunctionValue.Make("nextafter", NextAfter);
            m["ulp"] = Fn("ulp", (c, a) => c.Values.Float(Ulp(AsDouble(c, Arg2(c, a, 0, "ulp"), "ulp"))));
            m["cbrt"] = Fn("cbrt", (c, a) => c.Values.Float(Cbrt(AsDouble(c, Arg2(c, a, 0, "cbrt"), "cbrt"))));
            m["exp2"] = Wrap("exp2", x => Math.Pow(2.0, x));
            m["sumprod"] = Fn("sumprod", SumProd);
            m["fma"] = Fn("fma", Fma);
            // n-ary hypot (3.8) replaces the 2-arg entry from the base table.
            m["hypot"] = Fn("hypot", HypotN);
        }

        private static ScriptValue Arg2(EvalContext ctx, ScriptValue[] a, int i, string name)
        {
            if (a.Length <= i)
                throw Raise.TypeError(ctx, name + "() missing required argument");
            return a[i];
        }

        private static BigInteger AsInt(EvalContext ctx, ScriptValue v, string func)
        {
            if (v.Kind == ValueKind.Int || v.Kind == ValueKind.Bool)
                return NumericOps.AsBigInteger(v);
            throw Raise.TypeError(ctx, "'" + v.PyTypeName + "' object cannot be interpreted as an integer");
        }

        // ---- isclose (PEP 485, 3.5) ----
        private static ScriptValue IsClose(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            if (a.Length != 2)
                throw Raise.TypeError(ctx, "isclose expected 2 arguments, got " + a.Length);
            double x = AsDouble(ctx, a[0], "isclose");
            double y = AsDouble(ctx, a[1], "isclose");
            double relTol = 1e-09, absTol = 0.0;
            ScriptValue v;
            if (kw.TryGet("rel_tol", out v))
                relTol = AsDouble(ctx, v, "isclose");
            if (kw.TryGet("abs_tol", out v))
                absTol = AsDouble(ctx, v, "isclose");
            if (relTol < 0.0 || absTol < 0.0)
                throw Raise.ValueError(ctx, "tolerances must be non-negative");
            if (x == y)
                return ctx.Values.Bool(true);   // catches equal infinities too
            if (double.IsInfinity(x) || double.IsInfinity(y))
                return ctx.Values.Bool(false);
            double diff = Math.Abs(x - y);   // NaN operands make diff NaN -> all comparisons false
            bool close = diff <= Math.Abs(relTol * y) || diff <= Math.Abs(relTol * x) || diff <= absTol;
            return ctx.Values.Bool(close);
        }

        // ---- gcd/lcm (3.5/3.9, n-ary) ----
        private static ScriptValue Gcd(EvalContext ctx, ScriptValue[] a)
        {
            BigInteger g = BigInteger.Zero;
            for (int i = 0; i < a.Length; i++)
            {
                ctx.Budget.Step();
                g = BigInteger.GreatestCommonDivisor(g, AsInt(ctx, a[i], "gcd"));
            }
            return ctx.Values.Int(g);
        }

        private static ScriptValue Lcm(EvalContext ctx, ScriptValue[] a)
        {
            BigInteger l = BigInteger.One;
            for (int i = 0; i < a.Length; i++)
            {
                ctx.Budget.Step();
                BigInteger n = BigInteger.Abs(AsInt(ctx, a[i], "lcm"));
                if (n.IsZero)
                    return ctx.Values.Int(0);
                BigInteger g = BigInteger.GreatestCommonDivisor(l, n);
                ctx.Values.EnsureIntBits(NumericOps.BitLength(l) + NumericOps.BitLength(n));
                l = l / g * n;
            }
            return ctx.Values.Int(l);
        }

        // ---- isqrt (3.8): Newton on BigInteger ----
        private static ScriptValue Isqrt(EvalContext ctx, ScriptValue[] a)
        {
            BigInteger n = AsInt(ctx, Arg2(ctx, a, 0, "isqrt"), "isqrt");
            if (n.Sign < 0)
                throw Raise.ValueError(ctx, "isqrt() argument must be nonnegative");
            if (n.IsZero)
                return ctx.Values.Int(0);
            long bits = NumericOps.BitLength(n);
            BigInteger x = BigInteger.One << (int)((bits + 1) / 2);   // initial guess >= sqrt(n)
            while (true)
            {
                ctx.Budget.Step();
                BigInteger y = (x + n / x) >> 1;
                if (y >= x)
                    return ctx.Values.Int(x);
                x = y;
            }
        }

        // ---- prod (3.8) ----
        private static ScriptValue Prod(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            if (a.Length != 1)
                throw Raise.TypeError(ctx, "prod expected 1 argument, got " + a.Length);
            ScriptValue acc;
            if (!kw.TryGet("start", out acc))
                acc = ctx.Values.Int(1);
            IScriptIterator it = PyOps.GetIterator(a[0], ctx);
            ScriptValue x;
            while (it.MoveNext(ctx, out x))
                acc = PyOps.BinaryOp(PyBinOp.Mul, acc, x, ctx);
            return acc;
        }

        // ---- comb/perm (3.8): exact multiplicative loops ----
        private static ScriptValue Comb(EvalContext ctx, ScriptValue[] a)
        {
            if (a.Length != 2)
                throw Raise.TypeError(ctx, "comb expected 2 arguments, got " + a.Length);
            BigInteger n = AsInt(ctx, a[0], "comb"), k = AsInt(ctx, a[1], "comb");
            if (n.Sign < 0)
                throw Raise.ValueError(ctx, "n must be a non-negative integer");
            if (k.Sign < 0)
                throw Raise.ValueError(ctx, "k must be a non-negative integer");
            if (k > n)
                return ctx.Values.Int(0);
            if (n - k < k)
                k = n - k;
            BigInteger result = BigInteger.One;
            for (BigInteger i = 1; i <= k; i++)
            {
                ctx.Budget.Step();
                ctx.Values.EnsureIntBits(NumericOps.BitLength(result) + NumericOps.BitLength(n));
                result = result * (n - k + i) / i;   // exact at every step
            }
            return ctx.Values.Int(result);
        }

        private static ScriptValue Perm(EvalContext ctx, ScriptValue[] a)
        {
            if (a.Length < 1 || a.Length > 2)
                throw Raise.TypeError(ctx, "perm expected 1 or 2 arguments, got " + a.Length);
            BigInteger n = AsInt(ctx, a[0], "perm");
            if (n.Sign < 0)
                throw Raise.ValueError(ctx, "n must be a non-negative integer");
            BigInteger k = n;
            if (a.Length == 2 && a[1].Kind != ValueKind.None)
            {
                k = AsInt(ctx, a[1], "perm");
                if (k.Sign < 0)
                    throw Raise.ValueError(ctx, "k must be a non-negative integer");
            }
            if (k > n)
                return ctx.Values.Int(0);
            BigInteger result = BigInteger.One;
            for (BigInteger i = n - k + 1; i <= n; i++)
            {
                ctx.Budget.Step();
                ctx.Values.EnsureIntBits(NumericOps.BitLength(result) + NumericOps.BitLength(n));
                result *= i;
            }
            return ctx.Values.Int(result);
        }

        // ---- hypot(*coords) (3.8) and dist(p, q) (3.8): scaled to avoid spurious overflow ----
        private static ScriptValue HypotN(EvalContext ctx, ScriptValue[] a)
        {
            var coords = new double[a.Length];
            for (int i = 0; i < a.Length; i++)
                coords[i] = AsDouble(ctx, a[i], "hypot");
            return ctx.Values.Float(ScaledNorm(coords));
        }

        private static ScriptValue Dist(EvalContext ctx, ScriptValue[] a)
        {
            if (a.Length != 2)
                throw Raise.TypeError(ctx, "dist expected 2 arguments, got " + a.Length);
            double[] p = ReadPoint(ctx, a[0]);
            double[] q = ReadPoint(ctx, a[1]);
            if (p.Length != q.Length)
                throw Raise.ValueError(ctx, "both points must have the same number of dimensions");
            var diff = new double[p.Length];
            for (int i = 0; i < p.Length; i++)
                diff[i] = p[i] - q[i];
            return ctx.Values.Float(ScaledNorm(diff));
        }

        private static double[] ReadPoint(EvalContext ctx, ScriptValue v)
        {
            IScriptIterator it = PyOps.GetIterator(v, ctx);
            var list = new List<double>();
            ScriptValue item;
            while (it.MoveNext(ctx, out item))
                list.Add(AsDouble(ctx, item, "dist"));
            return list.ToArray();
        }

        private static double ScaledNorm(double[] xs)
        {
            double max = 0.0;
            bool anyNan = false;
            for (int i = 0; i < xs.Length; i++)
            {
                double ax = Math.Abs(xs[i]);
                if (double.IsInfinity(ax))
                    return double.PositiveInfinity;   // inf wins even over NaN (C99 hypot)
                if (double.IsNaN(ax))
                    anyNan = true;
                else if (ax > max)
                    max = ax;
            }
            if (anyNan)
                return double.NaN;
            if (max == 0.0)
                return 0.0;
            double sum = 0.0;
            for (int i = 0; i < xs.Length; i++)
            {
                double r = xs[i] / max;
                sum += r * r;
            }
            return max * Math.Sqrt(sum);
        }

        // ---- remainder (3.7): IEEE 754 remainder with CPython domain errors ----
        private static ScriptValue Remainder(EvalContext ctx, ScriptValue[] a)
        {
            if (a.Length != 2)
                throw Raise.TypeError(ctx, "remainder expected 2 arguments, got " + a.Length);
            double x = AsDouble(ctx, a[0], "remainder");
            double y = AsDouble(ctx, a[1], "remainder");
            if (double.IsNaN(x) || double.IsNaN(y))
                return ctx.Values.Float(double.NaN);
            if (double.IsInfinity(x) || y == 0.0)
                throw Raise.ValueError(ctx, "math domain error");
            return ctx.Values.Float(Math.IEEERemainder(x, y));
        }

        // ---- nextafter/ulp (3.9; steps= 3.12) via the ordered-bits mapping ----

        private static long OrderedBits(double d)
        {
            long bits = BitConverter.DoubleToInt64Bits(d);
            return bits >= 0 ? bits : long.MinValue - bits;
        }

        private static double FromOrderedBits(long ob)
        {
            return BitConverter.Int64BitsToDouble(ob >= 0 ? ob : long.MinValue - ob);
        }

        private static ScriptValue NextAfter(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            if (a.Length != 2)
                throw Raise.TypeError(ctx, "nextafter expected 2 arguments, got " + a.Length);
            double x = AsDouble(ctx, a[0], "nextafter");
            double y = AsDouble(ctx, a[1], "nextafter");
            BigInteger steps = BigInteger.One;
            ScriptValue sv;
            if (kw.TryGet("steps", out sv) && sv.Kind != ValueKind.None)
            {
                steps = AsInt(ctx, sv, "nextafter");
                if (steps.Sign < 0)
                    throw Raise.ValueError(ctx, "steps must be a non-negative integer");
            }
            if (double.IsNaN(x))
                return ctx.Values.Float(x);
            if (double.IsNaN(y))
                return ctx.Values.Float(y);
            if (x == y || steps.IsZero)
                return ctx.Values.Float(steps.IsZero ? x : y);
            long ox = OrderedBits(x), oy = OrderedBits(y);
            BigInteger span = BigInteger.Abs((BigInteger)oy - ox);
            BigInteger move = steps < span ? steps : span;
            long target = ox < oy ? ox + (long)move : ox - (long)move;
            return ctx.Values.Float(FromOrderedBits(target));
        }

        private static double Ulp(double x)
        {
            if (double.IsNaN(x))
                return x;
            x = Math.Abs(x);
            if (double.IsInfinity(x))
                return x;
            if (x == 0.0)
                return double.Epsilon;   // smallest subnormal
            if (x == double.MaxValue)
                return x - FromOrderedBits(OrderedBits(x) - 1);
            return FromOrderedBits(OrderedBits(x) + 1) - x;
        }

        // ---- cbrt (3.11): exp/log seed + two Newton refinements (net472 has no Math.Cbrt) ----
        private static double Cbrt(double x)
        {
            if (x == 0.0 || double.IsNaN(x) || double.IsInfinity(x))
                return x;
            double ax = Math.Abs(x);
            double y = Math.Exp(Math.Log(ax) / 3.0);
            y = y - (y * y * y - ax) / (3.0 * y * y);
            y = y - (y * y * y - ax) / (3.0 * y * y);
            return x < 0 ? -y : y;
        }

        // ---- sumprod (3.12): pairwise multiply-add through the engine operators ----
        private static ScriptValue SumProd(EvalContext ctx, ScriptValue[] a)
        {
            if (a.Length != 2)
                throw Raise.TypeError(ctx, "sumprod expected 2 arguments, got " + a.Length);
            IScriptIterator ip = PyOps.GetIterator(a[0], ctx);
            IScriptIterator iq = PyOps.GetIterator(a[1], ctx);
            ScriptValue total = ctx.Values.Int(0);
            while (true)
            {
                ScriptValue p, q;
                bool hp = ip.MoveNext(ctx, out p);
                bool hq = iq.MoveNext(ctx, out q);
                if (hp != hq)
                    throw Raise.ValueError(ctx, "Inputs are not the same length");
                if (!hp)
                    return total;
                total = PyOps.BinaryOp(PyBinOp.Add, total, PyOps.BinaryOp(PyBinOp.Mul, p, q, ctx), ctx);
            }
        }

        // ---- fma (3.13): software double-double (Dekker two-product + two-sum) ----
        private static ScriptValue Fma(EvalContext ctx, ScriptValue[] a)
        {
            if (a.Length != 3)
                throw Raise.TypeError(ctx, "fma expected 3 arguments, got " + a.Length);
            double x = AsDouble(ctx, a[0], "fma");
            double y = AsDouble(ctx, a[1], "fma");
            double z = AsDouble(ctx, a[2], "fma");
            bool invalidProd = (x == 0.0 && double.IsInfinity(y)) || (double.IsInfinity(x) && y == 0.0);
            if (invalidProd)
            {
                if (double.IsNaN(z))
                    return ctx.Values.Float(double.NaN);   // CPython special case
                throw Raise.ValueError(ctx, "invalid operation in fma");
            }
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z)
                || double.IsInfinity(x) || double.IsInfinity(y) || double.IsInfinity(z))
            {
                return ctx.Values.Float(x * y + z);   // IEEE special-value arithmetic
            }
            double r = FmaFinite(x, y, z, 0);
            if (double.IsInfinity(r))
                throw Raise.Overflow(ctx, "math range error");
            return ctx.Values.Float(r);
        }

        private static double FmaFinite(double x, double y, double z, int depth)
        {
            double p = x * y;
            if (double.IsInfinity(p) && depth == 0)
                return 2.0 * FmaFinite(x * 0.5, y, z * 0.5, 1);   // rescale: true fma may still be finite
            if (double.IsInfinity(p))
                return p;
            // Dekker split is exact only away from the overflow edge; fall back unfused there.
            if (Math.Abs(x) >= 9.9e307 || Math.Abs(y) >= 9.9e307 || Math.Abs(p) >= 9.9e307)
                return p + z;
            const double SPLIT = 134217729.0;   // 2^27 + 1
            double tx = SPLIT * x, hx = tx - (tx - x), lx = x - hx;
            double ty = SPLIT * y, hy = ty - (ty - y), ly = y - hy;
            double err = ((hx * hy - p) + hx * ly + lx * hy) + lx * ly;   // x*y == p + err exactly
            double s = p + z;
            double bb = s - p;
            double errSum = (p - (s - bb)) + (z - bb);   // two-sum error
            return s + (errSum + err);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
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
            m["nan"] = ctx.Values.Float(NumericOps.PyNaN);

            m["isclose"] = BuiltinFunctionValue.Make("isclose", Args.LeadKeywords("isclose", IsClose, 2, "a", "b", "rel_tol", "abs_tol"));
            m["gcd"] = Fn("gcd", 0, int.MaxValue, Gcd);
            m["lcm"] = Fn("lcm", 0, int.MaxValue, Lcm);
            m["isqrt"] = Fn("isqrt", 1, 1, Isqrt);
            m["prod"] = BuiltinFunctionValue.Make("prod", Args.KnownKeywords("prod", Prod, "start"));
            m["comb"] = Fn("comb", 2, 2, Comb);
            m["perm"] = Fn("perm", 1, 2, Perm);
            m["dist"] = Fn("dist", 2, 2, Dist);
            m["remainder"] = Fn("remainder", 2, 2, Remainder);
            m["nextafter"] = BuiltinFunctionValue.Make("nextafter", Args.KnownKeywords("nextafter", NextAfter, "steps"));
            m["ulp"] = Fn("ulp", 1, 1, (c, a) => c.Values.Float(Ulp(AsDouble(c, Arg2(c, a, 0, "ulp")))));
            m["cbrt"] = Fn("cbrt", 1, 1, (c, a) => c.Values.Float(Cbrt(AsDouble(c, Arg2(c, a, 0, "cbrt")))));
            m["exp2"] = Wrap("exp2", x => Math.Pow(2.0, x));
            m["sumprod"] = Fn("sumprod", 2, 2, SumProd);
            m["fma"] = Fn("fma", 3, 3, Fma);
            // n-ary hypot (3.8) replaces the 2-arg entry from the base table.
            m["hypot"] = Fn("hypot", 0, int.MaxValue, HypotN);
        }

        private static ScriptValue Arg2(EvalContext ctx, ScriptValue[] a, int i, string name)
        {
            if (a.Length <= i)
                throw Raise.TypeError(ctx, name + "() missing required argument");
            return a[i];
        }

        // ---- isclose (PEP 485, 3.5) ----
        private static ScriptValue IsClose(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Unpack(ctx, a, "isclose", 2, 2);
            double x = AsDouble(ctx, a[0]);
            double y = AsDouble(ctx, a[1]);
            double relTol = 1e-09, absTol = 0.0;
            ScriptValue v;
            if (kw.TryGet("rel_tol", out v))
                relTol = AsDouble(ctx, v);
            if (kw.TryGet("abs_tol", out v))
                absTol = AsDouble(ctx, v);
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
                g = BigOps.Gcd(ctx, g, Coerce.ToIndex(ctx, a[i], "gcd"));
            }
            return ctx.Values.Int(g);
        }

        private static ScriptValue Lcm(EvalContext ctx, ScriptValue[] a)
        {
            BigInteger l = BigInteger.One;
            for (int i = 0; i < a.Length; i++)
            {
                ctx.Budget.Step();
                BigInteger n = BigInteger.Abs(Coerce.ToIndex(ctx, a[i], "lcm"));
                if (n.IsZero)
                    l = BigInteger.Zero;
                if (l.IsZero)
                    continue;   // the answer is 0; the arguments after it are still checked
                BigInteger g = BigOps.Gcd(ctx, l, n);
                long bitsL = NumericOps.BitLength(l), bitsN = NumericOps.BitLength(n);
                ctx.Values.EnsureIntBits(bitsL + bitsN);
                // l * n / g, the division on the shorter one: a running lcm of small numbers divides the small one
                l = bitsN <= bitsL ? BigOps.Multiply(ctx, l, BigOps.Divide(ctx, n, g)) : BigOps.Multiply(ctx, BigOps.Divide(ctx, l, g), n);
            }
            return ctx.Values.Int(l);
        }

        // ---- isqrt (3.8): Newton on BigInteger ----
        private static ScriptValue Isqrt(EvalContext ctx, ScriptValue[] a)
        {
            BigInteger n = Coerce.ToIndex(ctx, Arg2(ctx, a, 0, "isqrt"), "isqrt");
            if (n.Sign < 0)
                throw Raise.ValueError(ctx, "isqrt() argument must be nonnegative");
            return ctx.Values.Int(IntegerSqrt(ctx, n));
        }

        internal static BigInteger IntegerSqrt(EvalContext ctx, BigInteger n)
        {
            if (n.IsZero)
                return n;
            // CPython's mathmodule.c algorithm: root doubles its correct bits per pass, so the divisions grow
            // geometrically and the whole costs about one full-size division (Newton from 2^(bits/2) did
            // ~log2(bits) of them, with no deadline check between).
            int c = (int)((NumericOps.BitLength(n) - 1) / 2);
            BigInteger root = BigInteger.One;
            int d = 0;
            for (int s = NumericOps.BitLengthLong(c) - 1; s >= 0; s--)
            {
                ctx.Budget.Step();
                ctx.Budget.CheckDeadlineNow();
                int e = d;
                d = c >> s;
                root = (root << (d - e - 1)) + BigOps.Divide(ctx, n >> (2 * c - e - d + 1), root);
            }
            return BigOps.Multiply(ctx, root, root) > n ? root - 1 : root;
        }

        // ---- prod (3.8) ----
        private static ScriptValue Prod(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Unpack(ctx, a, "prod", 1, 1);
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
            BigInteger n = Coerce.ToIndex(ctx, a[0], "comb"), k = Coerce.ToIndex(ctx, a[1], "comb");
            if (n.Sign < 0)
                throw Raise.ValueError(ctx, "n must be a non-negative integer");
            if (k.Sign < 0)
                throw Raise.ValueError(ctx, "k must be a non-negative integer");
            if (k > n)
                return ctx.Values.Int(0);
            if (n - k < k)
                k = n - k;
            BigInteger result = BigInteger.One;
            long nb = NumericOps.BitLength(n);   // bounds every factor's bits
            for (BigInteger i = 1; i <= k; i++)
            {
                ctx.Budget.Step();
                long rb = NumericOps.BitLength(result);
                ctx.Values.EnsureIntBits(rb + nb);
                result = BigOps.Multiply(ctx, result, n - k + i, rb, nb) / i;   // exact at every step
            }
            return ctx.Values.Int(result);
        }

        private static ScriptValue Perm(EvalContext ctx, ScriptValue[] a)
        {
            if (a.Length < 1 || a.Length > 2)
                throw Raise.TypeError(ctx, "perm expected 1 or 2 arguments, got " + a.Length);
            BigInteger n = Coerce.ToIndex(ctx, a[0], "perm");
            if (n.Sign < 0)
                throw Raise.ValueError(ctx, "n must be a non-negative integer");
            BigInteger k = n;
            if (a.Length == 2 && a[1].Kind != ValueKind.None)
            {
                k = Coerce.ToIndex(ctx, a[1], "perm");
                if (k.Sign < 0)
                    throw Raise.ValueError(ctx, "k must be a non-negative integer");
            }
            if (k > n)
                return ctx.Values.Int(0);
            BigInteger result = BigInteger.One;
            long nb = NumericOps.BitLength(n);   // bounds every factor's bits
            for (BigInteger i = n - k + 1; i <= n; i++)
            {
                ctx.Budget.Step();
                long rb = NumericOps.BitLength(result);
                ctx.Values.EnsureIntBits(rb + nb);
                result = BigOps.Multiply(ctx, result, i, rb, nb);
            }
            return ctx.Values.Int(result);
        }

        // ---- hypot(*coords) (3.8) and dist(p, q) (3.8): scaled to avoid spurious overflow ----
        private static ScriptValue HypotN(EvalContext ctx, ScriptValue[] a)
        {
            var coords = new double[a.Length];
            for (int i = 0; i < a.Length; i++)
                coords[i] = AsDouble(ctx, a[i]);
            return ctx.Values.Float(ScaledNorm(coords));
        }

        private static ScriptValue Dist(EvalContext ctx, ScriptValue[] a)
        {
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
            ScriptValue[] items = Iter.ToArray(ctx, v);   // charged and capped: an endless iterator ends on a limit
            var xs = new double[items.Length];
            for (int i = 0; i < xs.Length; i++)
                xs[i] = AsDouble(ctx, items[i]);
            return xs;
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
                return NumericOps.PyNaN;
            if (max == 0.0 || xs.Length <= 1)
                return max;
            return VectorNorm(xs, max);
        }

        // CPython's vector_norm: lossless scaling and squaring, then one differential correction of the root.
        private static double VectorNorm(double[] xs, double max)
        {
            double mantissa;
            int exponent;
            FloatMath.Frexp(max, out mantissa, out exponent);
            if (exponent < -1023)   // ldexp(1.0, -exponent) would overflow: subnormals to normals first
            {
                const double DblMin = 2.2250738585072014e-308;
                var scaled = new double[xs.Length];
                for (int i = 0; i < xs.Length; i++)
                    scaled[i] = xs[i] / DblMin;
                return DblMin * VectorNorm(scaled, max / DblMin);
            }
            double scale = exponent <= 1022 ? BitConverter.Int64BitsToDouble((long)(1023 - exponent) << 52)
                : 2.2250738585072014e-308 / (1L << (exponent - 1022));   // 2**-1023, 2**-1024: subnormal and exact
            double csum = 1.0, frac1 = 0.0, frac2 = 0.0, hi, lo, h, x;
            for (int i = 0; i < xs.Length; i++)
            {
                x = xs[i] * scale;
                DoubleLength.Mul(x, x, out hi, out lo);
                h = csum + hi;
                frac2 += hi - (h - csum);
                csum = h;
                frac1 += lo;
            }
            h = Math.Sqrt(csum - 1.0 + (frac1 + frac2));
            DoubleLength.Mul(-h, h, out hi, out lo);
            x = csum + hi;
            frac2 += hi - (x - csum);
            csum = x;
            frac1 += lo;
            x = csum - 1.0 + (frac1 + frac2);
            h += x / (2.0 * h);
            return h / scale;
        }

        // ---- remainder (3.7): IEEE 754 remainder with CPython domain errors ----
        private static ScriptValue Remainder(EvalContext ctx, ScriptValue[] a)
        {
            double x = AsDouble(ctx, a[0]);
            double y = AsDouble(ctx, a[1]);
            if (double.IsNaN(x) || double.IsNaN(y))
                return ctx.Values.Float(double.IsNaN(x) ? x : y);   // m_remainder returns the NaN it was given
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
            Args.Unpack(ctx, a, "nextafter", 2, 2);
            double x = AsDouble(ctx, a[0]);
            double y = AsDouble(ctx, a[1]);
            BigInteger steps = BigInteger.One;
            ScriptValue sv;
            if (kw.TryGet("steps", out sv) && sv.Kind != ValueKind.None)
            {
                steps = Coerce.ToIndex(ctx, sv, "nextafter");
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

        // ---- sumprod (3.12): CPython's phases: machine-word ints, then floats in triple length, then the operators ----
        private static ScriptValue SumProd(EvalContext ctx, ScriptValue[] a)
        {
            IScriptIterator ip = PyOps.GetIterator(a[0], ctx);
            IScriptIterator iq = PyOps.GetIterator(a[1], ctx);
            ScriptValue total = ctx.Values.Int(0);
            bool intPath = true, fltPath = true, intUsed = false, fltUsed = false;
            long intTotal = 0;
            double hi = 0.0, lo = 0.0, tiny = 0.0;
            while (true)
            {
                ctx.Budget.Step();
                ScriptValue p, q;
                bool hp = ip.MoveNext(ctx, out p);
                bool hq = iq.MoveNext(ctx, out q);
                if (hp != hq)
                    throw Raise.ValueError(ctx, "Inputs are not the same length");
                bool finished = !hp;
                if (intPath)
                {
                    long next;
                    if (!finished && TryAddProduct(intTotal, p, q, out next))
                    {
                        intTotal = next;
                        intUsed = true;
                        continue;
                    }
                    intPath = false;
                    if (intUsed)
                        total = PyOps.BinaryOp(PyBinOp.Add, total, ctx.Values.Int(intTotal), ctx);
                }
                if (fltPath)
                {
                    double x, y;
                    if (!finished && TryFloatPair(p, q, ctx, out x, out y))
                    {
                        double nh, nl, nt;
                        DoubleLength.TripleFma(x, y, hi, lo, tiny, out nh, out nl, out nt);
                        if (!double.IsNaN(nh) && !double.IsInfinity(nh))
                        {
                            hi = nh;
                            lo = nl;
                            tiny = nt;
                            fltUsed = true;
                            continue;
                        }
                    }
                    fltPath = false;
                    if (fltUsed)
                        total = PyOps.BinaryOp(PyBinOp.Add, total, ctx.Values.Float(DoubleLength.TripleToDouble(hi, lo, tiny)), ctx);
                }
                if (finished)
                    return total;
                total = PyOps.BinaryOp(PyBinOp.Add, total, PyOps.BinaryOp(PyBinOp.Mul, p, q, ctx), ctx);
            }
        }

        private static bool TryAddProduct(long total, ScriptValue p, ScriptValue q, out long result)
        {
            result = 0;
            IntValue ip = p as IntValue, iq = q as IntValue;
            if (ip == null || iq == null || !ip.IsSmall || !iq.IsSmall)
                return false;
            long a = ip.Small, b = iq.Small;
            long product = unchecked(a * b);
            if (a != 0 && (a == -1 ? b == long.MinValue : product / a != b))
                return false;
            result = unchecked(total + product);
            return ((total ^ result) & (product ^ result)) >= 0;
        }

        // A float with a float, an int or a bool: what arises from price times quantity, or a vector of bools.
        private static bool TryFloatPair(ScriptValue p, ScriptValue q, EvalContext ctx, out double x, out double y)
        {
            x = y = 0.0;
            FloatValue fp = p as FloatValue, fq = q as FloatValue;
            if (fp != null && fq != null)
            {
                x = fp.Value;
                y = fq.Value;
                return true;
            }
            if (fp != null)
            {
                x = fp.Value;
                return TryIntAsDouble(q, ctx, out y);
            }
            if (fq != null)
            {
                y = fq.Value;
                return TryIntAsDouble(p, ctx, out x);
            }
            return false;
        }

        private static readonly BigInteger FloatableBelow = BigInteger.One << 1023;

        private static bool TryIntAsDouble(ScriptValue v, EvalContext ctx, out double d)
        {
            d = 0.0;
            BoolValue bv = v as BoolValue;
            if (bv != null)
            {
                d = bv.Value ? 1.0 : 0.0;
                return true;
            }
            IntValue iv = v as IntValue;
            if (iv == null)
                return false;
            if (iv.IsSmall)
            {
                d = iv.Small;
                return true;
            }
            if (BigInteger.Abs(iv.Value) >= FloatableBelow)
                return false;
            d = NumericOps.ToDoubleChecked(iv.Value, ctx);
            return true;
        }

        // ---- fma (3.13): software double-double (Dekker two-product + two-sum) ----
        private static ScriptValue Fma(EvalContext ctx, ScriptValue[] a)
        {
            double x = AsDouble(ctx, a[0]);
            double y = AsDouble(ctx, a[1]);
            double z = AsDouble(ctx, a[2]);
            bool invalidProd = (x == 0.0 && double.IsInfinity(y)) || (double.IsInfinity(x) && y == 0.0);
            if (invalidProd)
            {
                if (double.IsNaN(z))
                    return ctx.Values.Float(NumericOps.PyNaN);   // CPython special case
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

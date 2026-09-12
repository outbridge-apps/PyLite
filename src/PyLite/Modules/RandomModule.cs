using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // The random module. Wraps System.Random (2026-07-08 decision — MT19937 dropped)
    // the public API + argument validation + per-seed determinism are preserved. Per-run (reads ctx.Random).
    internal static class RandomModule
    {
        private const double TwoPi = 2.0 * Math.PI;
        private static readonly double NvMagic = 4.0 * Math.Exp(-0.5) / Math.Sqrt(2.0);
        private static readonly double Log4 = Math.Log(4.0);
        private static readonly double SgMagic = 1.0 + Math.Log(4.5);

        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["seed"] = Fn("seed", Seed),
                ["random"] = Fn("random", (c, a) => c.Values.Float(Rand(c).NextDouble())),
                ["getrandbits"] = Fn("getrandbits", (c, a) => c.Values.Int(GetRandBits(c, a))),
                ["randrange"] = Fn("randrange", (c, a) => c.Values.Int(RandRange(c, a))),
                ["randint"] = Fn("randint", RandInt),
                ["choice"] = Fn("choice", Choice),
                ["shuffle"] = Fn("shuffle", Shuffle),
                ["sample"] = BuiltinFunctionValue.Make("sample", (self, a, kw, c) => Sample(c, a, kw)),
                ["uniform"] = Fn("uniform", (c, a) => c.Values.Float(D(c, a, 0) + (D(c, a, 1) - D(c, a, 0)) * Rand(c).NextDouble())),
                ["triangular"] = Fn("triangular", Triangular),
                ["expovariate"] = Fn("expovariate", Expovariate),
                ["normalvariate"] = Fn("normalvariate", NormalVariate),
                ["gauss"] = Fn("gauss", Gauss),
                ["gammavariate"] = Fn("gammavariate", (c, a) => c.Values.Float(GammaVariate(c, D(c, a, 0), D(c, a, 1)))),
                ["betavariate"] = Fn("betavariate", BetaVariate),
                // 3.6/3.9 members
                ["choices"] = BuiltinFunctionValue.Make("choices", Choices),
                ["randbytes"] = Fn("randbytes", RandBytes),
            };
            return ctx.Values.Module("random", m);
        }

        private static BuiltinFunctionValue Fn(string name, Func<EvalContext, ScriptValue[], ScriptValue> f)
        {
            return BuiltinFunctionValue.Make(name, (self, args, kw, c) => f(c, args));
        }

        private static RandomState Rand(EvalContext ctx)
        {
            if (ctx.Random == null)
                ctx.Random = RandomState.CreateEntropy();
            return ctx.Random;
        }

        // ---- seed ----

        private static ScriptValue Seed(EvalContext ctx, ScriptValue[] args)
        {
            ctx.Budget.Step();
            RandomState st = Rand(ctx);
            st.GaussNext = null;   // seed() also discards the cached second gauss() variate (CPython)
            if (args.Length == 0 || args[0].Kind == ValueKind.None)
            {
                st.ReseedEntropy();
                return ctx.Values.None;
            }
            ScriptValue a = args[0];
            BigInteger n;
            IntValue iv = a as IntValue;
            BoolValue bv = a as BoolValue;
            StrValue sv = a as StrValue;
            if (iv != null)
                n = BigInteger.Abs(iv.Value);
            else if (bv != null)
                n = bv.Value ? BigInteger.One : BigInteger.Zero;
            else if (sv != null)
                n = StrSeed(sv.Value);
            else
                n = BigInteger.Abs(PyOps.Hash(a, ctx, 0));
            st.ReseedInt(DeriveSeed(n));
            return ctx.Values.None;
        }

        private static BigInteger StrSeed(string s)
        {
            byte[] utf = Encoding.UTF8.GetBytes(s);
            byte[] sha;
            using (SHA512 h = SHA512.Create())
                sha = h.ComputeHash(utf);
            var concat = new byte[utf.Length + sha.Length];
            Array.Copy(utf, concat, utf.Length);
            Array.Copy(sha, 0, concat, utf.Length, sha.Length);
            BigInteger n = BigInteger.Zero;
            for (int i = 0; i < concat.Length; i++)
                n = n * 256 + concat[i];
            return n;
        }

        private static int DeriveSeed(BigInteger n)
        {
            uint acc = 0;
            while (n > 0)
            {
                acc ^= (uint)(n & 0xFFFFFFFF);
                n >>= 32;
            }
            return unchecked((int)acc);
        }

        // ---- getrandbits / _randbelow ----

        private static BigInteger GetRandBits(EvalContext ctx, ScriptValue[] args)
        {
            if (args.Length < 1 || !(args[0] is IntValue || args[0] is BoolValue))
                throw Raise.TypeError(ctx, "getrandbits() takes exactly one int argument");
            BigInteger k = args[0] is BoolValue ? (((BoolValue)args[0]).Value ? 1 : 0) : ((IntValue)args[0]).Value;
            if (k < 0)
                throw Raise.ValueError(ctx, "number of bits must be non-negative");
            if (k.IsZero)
                return BigInteger.Zero;   // 3.9+: getrandbits(0) == 0
            // A k-bit result must respect the int-bits budget; guard BEFORE any (long)/(int) cast so a
            // huge k aborts on the budget instead of overflowing the cast into an engine fault.
            if (k > ctx.Limits.MaxIntBits)
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxIntBits", ctx.Limits.MaxIntBits, ctx.Limits.MaxIntBits + 1L);
            ctx.Values.PreCharge((long)(k / 8) + 64);
            ctx.Budget.Step(1 + (int)(k > 1024 ? 1024 : k) / 64);
            ctx.Budget.CheckDeadlineNow();
            int words = (int)((k + 31) / 32);
            BigInteger result = BigInteger.Zero;
            int rem = (int)(k % 32);
            for (int i = 0; i < words; i++)
            {
                uint r = Rand(ctx).NextUint32();
                if (i == words - 1 && rem != 0)
                    r >>= 32 - rem;
                result |= (BigInteger)r << (32 * i);
                ctx.Budget.Step();
            }
            return result;
        }

        private static BigInteger RandBelow(EvalContext ctx, BigInteger n)
        {
            if (n <= 0)
                return BigInteger.Zero;
            int k = BigIntMath.BitLength(n);
            BigInteger r = GetRandBits(ctx, new ScriptValue[] { ctx.Values.Int(k) });
            int guard = 0;
            while (r >= n)
            {
                r = GetRandBits(ctx, new ScriptValue[] { ctx.Values.Int(k) });
                ctx.Budget.Step();
                if (++guard > 100000)
                    return r % n;   // pathological safety net (never hit with correct k)
            }
            return r;
        }

        // ---- randrange / randint / choice ----

        private static BigInteger RandRange(EvalContext ctx, ScriptValue[] args)
        {
            if (args.Length < 1)
                throw Raise.TypeError(ctx, "randrange() missing required argument");
            BigInteger istart = AsRangeInt(ctx, args[0], 1);
            if (args.Length < 2 || args[1].Kind == ValueKind.None)
            {
                if (istart > 0)
                    return RandBelow(ctx, istart);
                throw Raise.ValueError(ctx, "empty range for randrange()");
            }
            BigInteger istop = AsRangeInt(ctx, args[1], 2);
            BigInteger width = istop - istart;
            BigInteger istep = args.Length >= 3 ? AsRangeInt(ctx, args[2], 3) : BigInteger.One;
            if (istep == 1)
            {
                if (width > 0)
                    return istart + RandBelow(ctx, width);
                throw Raise.ValueError(ctx, "empty range for randrange()");
            }
            if (istep == 0)
                throw Raise.ValueError(ctx, "zero step for randrange()");
            BigInteger nn = istep > 0
                ? Rationals.FloorDiv(width + istep - 1, istep)
                : Rationals.FloorDiv(width + istep + 1, istep);
            if (nn <= 0)
                throw Raise.ValueError(ctx, "empty range for randrange()");
            return istart + istep * RandBelow(ctx, nn);
        }

        private static ScriptValue RandInt(EvalContext ctx, ScriptValue[] args)
        {
            BigInteger a = AsRangeInt(ctx, args[0], 1);
            BigInteger b = AsRangeInt(ctx, args[1], 2);
            return ctx.Values.Int(RandRange(ctx, new ScriptValue[] { ctx.Values.Int(a), ctx.Values.Int(b + 1) }));
        }

        private static BigInteger AsRangeInt(EvalContext ctx, ScriptValue v, int argNo)
        {
            IntValue iv = v as IntValue;
            if (iv != null)
                return iv.Value;
            BoolValue bv = v as BoolValue;
            if (bv != null)
                return bv.Value ? 1 : 0;
            throw Raise.TypeError(ctx, "'" + v.PyTypeName + "' object cannot be interpreted as an integer");
        }

        private static ScriptValue Choice(EvalContext ctx, ScriptValue[] args)
        {
            ScriptValue seq = args[0];
            long n = PyOps.Length(seq, ctx);
            if (n == 0)
                throw Raise.IndexError(ctx, "Cannot choose from an empty sequence");
            BigInteger i = RandBelow(ctx, n);
            return IndexInto(ctx, seq, i);
        }

        // ---- shuffle / sample ----

        private static ScriptValue Shuffle(EvalContext ctx, ScriptValue[] args)
        {
            ListValue lst = args[0] as ListValue;
            if (lst == null)
                throw Raise.TypeError(ctx, "'" + args[0].PyTypeName + "' object does not support item assignment");
            ScriptValue randomCb = args.Length >= 2 && args[1].Kind != ValueKind.None ? args[1] : null;
            long n = PyOps.Length(lst, ctx);
            for (long i = n - 1; i >= 1; i--)
            {
                BigInteger j;
                if (randomCb == null)
                    j = RandBelow(ctx, i + 1);
                else
                {
                    double u = CallFloat(ctx, randomCb);
                    j = new BigInteger(u * (i + 1));
                }
                ScriptValue vi = lst.GetItem(ctx.Values.Int(i), ctx);
                ScriptValue vj = lst.GetItem(ctx.Values.Int(j), ctx);
                lst.SetItem(ctx.Values.Int(i), vj, ctx);
                lst.SetItem(ctx.Values.Int(j), vi, ctx);
                ctx.Budget.Step();
            }
            return ctx.Values.None;
        }

        // sample(population, k, *, counts=None): sets are refused (3.11+); counts expands the population.
        private static ScriptValue Sample(EvalContext ctx, ScriptValue[] args, KwArgs kw)
        {
            args = Outbridge.PyLite.Runtime.Values.StrMethods.WithKw(ctx, args, kw, "sample", "population", "k", "counts");
            if (args.Length < 1)
                throw Raise.TypeError(ctx, "sample() missing required argument 'population'");
            ScriptValue population = args[0];
            if (population.Kind == ValueKind.Set || population.Kind == ValueKind.FrozenSet || population.Kind == ValueKind.Dict)
                throw Raise.TypeError(ctx, "Population must be a sequence.  For dicts or sets, use sorted(d).");
            if (args.Length < 2 || !(args[1] is IntValue || args[1] is BoolValue))
                throw Raise.TypeError(ctx, "sample() k must be an int");
            BigInteger k = args[1] is BoolValue ? (((BoolValue)args[1]).Value ? 1 : 0) : ((IntValue)args[1]).Value;
            if (args.Length >= 3 && args[2].Kind != ValueKind.None)
            {
                ListValue expanded = ctx.Values.List(8);
                IScriptIterator pit = PyOps.GetIterator(population, ctx);
                IScriptIterator cit = PyOps.GetIterator(args[2], ctx);
                ScriptValue item, cnt;
                while (pit.MoveNext(ctx, out item))
                {
                    if (!cit.MoveNext(ctx, out cnt))
                        throw Raise.ValueError(ctx, "The number of counts does not match the population");
                    if (!(cnt is IntValue || cnt is BoolValue))
                        throw Raise.TypeError(ctx, "Counts must be integers");
                    BigInteger times = NumericOps.AsBigInteger(cnt);
                    if (times < 0)
                        throw Raise.ValueError(ctx, "Counts must be non-negative");
                    for (BigInteger t = 0; t < times; t++)
                        expanded.Add(item, ctx);
                }
                if (cit.MoveNext(ctx, out cnt))
                    throw Raise.ValueError(ctx, "The number of counts does not match the population");
                population = expanded;
            }
            long n = PyOps.Length(population, ctx);
            if (k < 0 || k > n)
                throw Raise.ValueError(ctx, "Sample larger than population");

            int kk = (int)k;
            var result = ctx.Values.List(kk);
            BigInteger setsize = 21;
            if (kk > 5)
            {
                BigInteger p = 1;
                while (p < 3 * (BigInteger)kk)
                    p *= 4;
                setsize += p;
            }

            if (n <= setsize)
            {
                var pool = new List<ScriptValue>();
                IScriptIterator it = PyOps.GetIterator(population, ctx);
                ScriptValue v;
                while (it.MoveNext(ctx, out v))
                    pool.Add(v);
                for (int i = 0; i < kk; i++)
                {
                    BigInteger j = RandBelow(ctx, n - i);
                    result.Add(pool[(int)j], ctx);
                    pool[(int)j] = pool[(int)(n - i - 1)];
                    ctx.Budget.Step();
                }
            }
            else
            {
                var selected = new HashSet<BigInteger>();
                for (int i = 0; i < kk; i++)
                {
                    BigInteger j = RandBelow(ctx, n);
                    while (selected.Contains(j))
                    {
                        j = RandBelow(ctx, n);
                        ctx.Budget.Step();
                    }
                    selected.Add(j);
                    result.Add(IndexInto(ctx, population, j), ctx);
                }
            }
            return result;
        }

        // ---- continuous distributions ----

        private static ScriptValue Triangular(EvalContext ctx, ScriptValue[] args)
        {
            double low = args.Length >= 1 ? D(ctx, args, 0) : 0.0;
            double high = args.Length >= 2 ? D(ctx, args, 1) : 1.0;
            bool hasMode = args.Length >= 3 && args[2].Kind != ValueKind.None;
            double u = Rand(ctx).NextDouble();
            double c;
            if (high == low)
                return ctx.Values.Float(low);
            c = hasMode ? (D(ctx, args, 2) - low) / (high - low) : 0.5;
            if (u > c)
            {
                u = 1.0 - u;
                c = 1.0 - c;
                double t = low;
                low = high;
                high = t;
            }
            return ctx.Values.Float(low + (high - low) * Math.Sqrt(u * c));
        }

        private static ScriptValue Expovariate(EvalContext ctx, ScriptValue[] args)
        {
            double lambd = D(ctx, args, 0);
            if (lambd == 0.0)
                throw Raise.ZeroDivision(ctx, "float division by zero");
            return ctx.Values.Float(-Math.Log(1.0 - Rand(ctx).NextDouble()) / lambd);
        }

        private static ScriptValue NormalVariate(EvalContext ctx, ScriptValue[] args)
        {
            double mu = args.Length >= 1 ? D(ctx, args, 0) : 0.0;     // 3.11+: mu=0.0, sigma=1.0 defaults
            double sigma = args.Length >= 2 ? D(ctx, args, 1) : 1.0;
            RandomState st = Rand(ctx);
            double z;
            while (true)
            {
                double u1 = st.NextDouble();
                double u2 = 1.0 - st.NextDouble();
                z = NvMagic * (u1 - 0.5) / u2;
                double zz = z * z / 4.0;
                if (zz <= -Math.Log(u2))
                    break;
                ctx.Budget.Step();
            }
            return ctx.Values.Float(mu + z * sigma);
        }

        private static ScriptValue Gauss(EvalContext ctx, ScriptValue[] args)
        {
            double mu = args.Length >= 1 ? D(ctx, args, 0) : 0.0;     // 3.11+: mu=0.0, sigma=1.0 defaults
            double sigma = args.Length >= 2 ? D(ctx, args, 1) : 1.0;
            RandomState st = Rand(ctx);
            double? cached = st.GaussNext;
            st.GaussNext = null;
            double z;
            if (cached.HasValue)
                z = cached.Value;
            else
            {
                double x2pi = st.NextDouble() * TwoPi;
                double g2rad = Math.Sqrt(-2.0 * Math.Log(1.0 - st.NextDouble()));
                z = Math.Cos(x2pi) * g2rad;
                st.GaussNext = Math.Sin(x2pi) * g2rad;
            }
            return ctx.Values.Float(mu + z * sigma);
        }

        private static double GammaVariate(EvalContext ctx, double alpha, double beta)
        {
            if (alpha <= 0.0 || beta <= 0.0)
                throw Raise.ValueError(ctx, "gammavariate: alpha and beta must be > 0.0");
            RandomState st = Rand(ctx);
            if (alpha > 1.0)
            {
                double ainv = Math.Sqrt(2.0 * alpha - 1.0);
                double bbb = alpha - Log4;
                double ccc = alpha + ainv;
                while (true)
                {
                    double u1 = st.NextDouble();
                    if (!(1e-7 < u1 && u1 < 0.9999999))
                    {
                        ctx.Budget.Step();
                        continue;
                    }
                    double u2 = 1.0 - st.NextDouble();
                    double v = Math.Log(u1 / (1.0 - u1)) / ainv;
                    double x = alpha * Math.Exp(v);
                    double z = u1 * u1 * u2;
                    double r = bbb + ccc * v - x;
                    if (r + SgMagic - 4.5 * z >= 0.0 || r >= Math.Log(z))
                        return x * beta;
                    ctx.Budget.Step();
                }
            }
            if (alpha == 1.0)
            {
                double u = st.NextDouble();
                while (u <= 1e-7)
                {
                    u = st.NextDouble();
                    ctx.Budget.Step();
                }
                return -Math.Log(u) * beta;
            }
            while (true)
            {
                double u = st.NextDouble();
                double b = (Math.E + alpha) / Math.E;
                double p = b * u;
                double x;
                if (p <= 1.0)
                    x = Math.Pow(p, 1.0 / alpha);
                else
                    x = -Math.Log((b - p) / alpha);
                double u1 = st.NextDouble();
                if (p > 1.0)
                {
                    if (u1 <= Math.Pow(x, alpha - 1.0))
                        return x * beta;
                }
                else if (u1 <= Math.Exp(-x))
                {
                    return x * beta;
                }
                ctx.Budget.Step();
            }
        }

        private static ScriptValue BetaVariate(EvalContext ctx, ScriptValue[] args)
        {
            double alpha = D(ctx, args, 0);
            double beta = D(ctx, args, 1);
            double y = GammaVariate(ctx, alpha, 1.0);
            if (y == 0.0)
                return ctx.Values.Float(0.0);
            return ctx.Values.Float(y / (y + GammaVariate(ctx, beta, 1.0)));
        }

        // ---- helpers ----

        private static double D(EvalContext ctx, ScriptValue[] args, int i)
        {
            if (args.Length <= i)
                throw Raise.TypeError(ctx, "missing required argument");
            return MathModule.AsDouble(ctx, args[i], "random");
        }

        private static double CallFloat(EvalContext ctx, ScriptValue callable)
        {
            ScriptValue r = ctx.CallHook(callable, Array.Empty<ScriptValue>(), KwArgs.Empty);
            FloatValue fv = r as FloatValue;
            if (fv != null)
                return fv.Value;
            IntValue iv = r as IntValue;
            if (iv != null)
                return (double)iv.Value;
            throw Raise.TypeError(ctx, "random() callback must return a float");
        }

        private static ScriptValue IndexInto(EvalContext ctx, ScriptValue seq, BigInteger i)
        {
            ScriptValue idx = ctx.Values.Int(i);
            ListValue lv = seq as ListValue;
            if (lv != null)
                return lv.GetItem(idx, ctx);
            TupleValue tv = seq as TupleValue;
            if (tv != null)
                return tv.GetItem(idx, ctx);
            StrValue sv = seq as StrValue;
            if (sv != null)
                return sv.GetItem(idx, ctx);
            RangeValue rv = seq as RangeValue;
            if (rv != null)
                return rv.GetItem(idx, ctx);
            BytesValue bytes = seq as BytesValue;
            if (bytes != null)
                return bytes.GetItem(idx, ctx);
            ScriptValue viaHook = seq.GetItemCore(idx, ctx);
            if (viaHook != null)
                return viaHook;
            throw Raise.TypeError(ctx, "'" + seq.PyTypeName + "' object is not subscriptable");
        }

        // --- choices (3.6) / randbytes (3.9) ----

        // choices(population, weights=None, *, cum_weights=None, k=1). The unweighted path uses
        // floor(random()*n) (NOT _randbelow) — documented CPython behavior.
        private static ScriptValue Choices(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, args, "choices", 1, 2);
            ScriptValue[] population = Materialize(ctx, args[0]);
            ScriptValue weightsArg = args.Length == 2 ? args[1] : null;
            ScriptValue v;
            if (kw.TryGet("weights", out v))
                weightsArg = v;
            ScriptValue cumArg = null;
            if (kw.TryGet("cum_weights", out v))
                cumArg = v;
            int k = 1;
            if (kw.TryGet("k", out v))
            {
                System.Numerics.BigInteger kb = AsIndexInt(ctx, v, "k");
                if (kb > int.MaxValue)   // (int)10**100 would overflow; a k this large blows the step budget anyway
                    throw Raise.Make(ctx, PyExceptionTypes.OverflowError, "k too large to convert to C ssize_t");
                k = (int)kb;
            }

            bool hasWeights = weightsArg != null && weightsArg.Kind != ValueKind.None;
            bool hasCum = cumArg != null && cumArg.Kind != ValueKind.None;
            if (hasWeights && hasCum)
                throw Raise.TypeError(ctx, "Cannot specify both weights and cumulative weights");

            int n = population.Length;
            RandomState rng = Rand(ctx);
            // Cap the initial capacity: a large k grows the list per element (each Add charges), so it aborts
            // on the step/memory budget instead of pre-allocating a k-sized backing array up front.
            ListValue result = ctx.Values.List(Math.Min(k < 0 ? 0 : k, 1024));

            if (!hasWeights && !hasCum)
            {
                for (int i = 0; i < k; i++)
                {
                    ctx.Budget.Step();
                    int idx = (int)Math.Floor(rng.NextDouble() * n);
                    if (idx >= n)
                        throw Raise.IndexError(ctx, "list index out of range");   // n == 0
                    result.Add(population[idx], ctx);
                }
                return result;
            }

            double[] cum = hasCum ? ReadWeights(ctx, cumArg) : Accumulate(ReadWeights(ctx, weightsArg));
            if (cum.Length != n)
                throw Raise.ValueError(ctx, "The number of weights does not match the population");
            double total = cum.Length > 0 ? cum[cum.Length - 1] : 0.0;
            if (double.IsNaN(total) || double.IsInfinity(total))
                throw Raise.ValueError(ctx, "Total of weights must be finite");
            if (total <= 0.0)
                throw Raise.ValueError(ctx, "Total of weights must be greater than zero");
            for (int i = 0; i < k; i++)
            {
                ctx.Budget.Step();
                int idx = BisectRight(cum, rng.NextDouble() * total, n - 1);
                result.Add(population[idx], ctx);
            }
            return result;
        }

        private static ScriptValue[] Materialize(EvalContext ctx, ScriptValue seq)
        {
            IScriptIterator it = PyOps.GetIterator(seq, ctx);
            var list = new List<ScriptValue>();
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
            {
                ctx.Values.PreCharge(16);
                list.Add(v);
            }
            return list.ToArray();
        }

        private static double[] ReadWeights(EvalContext ctx, ScriptValue seq)
        {
            IScriptIterator it = PyOps.GetIterator(seq, ctx);
            var list = new List<double>();
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
                list.Add(D2(ctx, v));
            return list.ToArray();
        }

        private static double D2(EvalContext ctx, ScriptValue v)
        {
            if (v.Kind == ValueKind.Float)
                return ((FloatValue)v).Value;
            if (v.Kind == ValueKind.Int || v.Kind == ValueKind.Bool)
                return (double)NumericOps.AsBigInteger(v);
            throw Raise.TypeError(ctx, "weights must be numbers, not " + v.PyTypeName);
        }

        private static double[] Accumulate(double[] w)
        {
            var cum = new double[w.Length];
            double s = 0.0;
            for (int i = 0; i < w.Length; i++)
            {
                s += w[i];
                cum[i] = s;
            }
            return cum;
        }

        // rightmost insertion point clamped to hi (CPython bisect.bisect with hi = n-1).
        private static int BisectRight(double[] cum, double x, int hi)
        {
            int lo = 0;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (x < cum[mid])
                    hi = mid;
                else
                    lo = mid + 1;
            }
            return lo;
        }

        private static BigInteger AsIndexInt(EvalContext ctx, ScriptValue v, string name)
        {
            if (v.Kind == ValueKind.Int || v.Kind == ValueKind.Bool)
                return NumericOps.AsBigInteger(v);
            throw Raise.TypeError(ctx, "'" + v.PyTypeName + "' object cannot be interpreted as an integer");
        }

        private static ScriptValue RandBytes(EvalContext ctx, ScriptValue[] a)
        {
            Args.Exactly(ctx, a, "randbytes", 1);
            BigInteger n = AsIndexInt(ctx, a[0], "randbytes");
            if (n.Sign < 0)
                throw Raise.ValueError(ctx, "number of bytes must be non-negative");
            int count = Coerce.ToSsize(ctx, n);
            ctx.Values.PreCharge(24 + (long)count);
            var buf = new byte[count];
            Rand(ctx).NextBytes(buf);
            return ctx.Values.Bytes(buf);
        }
    }
}

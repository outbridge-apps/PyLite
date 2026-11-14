using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // The random module over System.Random rather than MT19937 (the streams are not CPython's, see deviations.md):
    // the public API, argument validation and per-seed determinism are preserved. Per-run (reads ctx.Random).
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
                ["seed"] = Fn("seed", 0, 2, Seed, new[] { "a", "version" }, new object[] { NoneDefault, 2 }),
                ["random"] = Fn("random", 0, 0, (c, a) => c.Values.Float(Rand(c).NextDouble())),
                ["getrandbits"] = Fn("getrandbits", 1, 1, (c, a) => c.Values.Int(GetRandBits(c, a))),
                ["randrange"] = Fn("randrange", 1, 3, (c, a) => c.Values.Int(RandRange(c, a)), new[] { "start", "stop", "step" }, new object[] { null, NoneDefault, 1 }),
                ["randint"] = Fn("randint", 2, 2, RandInt, new[] { "a", "b" }, new object[2]),
                ["choice"] = Fn("choice", 1, 1, Choice, new[] { "seq" }, new object[1]),
                ["shuffle"] = BuiltinFunctionValue.Make("shuffle", (self, a, kw, c) => Shuffle(c, a, kw)),
                ["sample"] = BuiltinFunctionValue.Make("sample", (self, a, kw, c) => Sample(c, a, kw)),
                ["uniform"] = Fn("uniform", 2, 2, (c, a) => c.Values.Float(D(c, a, 0) + (D(c, a, 1) - D(c, a, 0)) * Rand(c).NextDouble()), new[] { "a", "b" }, new object[2]),
                ["triangular"] = Fn("triangular", 0, 3, Triangular, new[] { "low", "high", "mode" }, new object[] { 0.0, 1.0, NoneDefault }),
                ["expovariate"] = Fn("expovariate", 0, 1, Expovariate, new[] { "lambd" }, new object[] { 1.0 }),
                ["normalvariate"] = Fn("normalvariate", 0, 2, NormalVariate, new[] { "mu", "sigma" }, new object[] { 0.0, 1.0 }),
                ["gauss"] = Fn("gauss", 0, 2, Gauss, new[] { "mu", "sigma" }, new object[] { 0.0, 1.0 }),
                ["gammavariate"] = Fn("gammavariate", 2, 2, (c, a) => c.Values.Float(GammaVariate(c, D(c, a, 0), D(c, a, 1))), new[] { "alpha", "beta" }, new object[2]),
                ["betavariate"] = Fn("betavariate", 2, 2, BetaVariate, new[] { "alpha", "beta" }, new object[2]),
                // 3.6/3.9 members
                ["choices"] = BuiltinFunctionValue.Make("choices", Args.LeadKeywords("choices", Choices, 1, "population", "weights", "cum_weights", "k")),
                ["randbytes"] = Fn("randbytes", 1, 1, RandBytes, new[] { "n" }, new object[1]),
            };
            return ctx.Values.Module("random", m);
        }

        private static readonly object NoneDefault = new object();

        // With names the function takes its parameters by keyword too; a keyword was dropped, silently: gauss(mu=100)
        // drew from the standard normal. A default is a number, NoneDefault, or null for a required parameter.
        private static BuiltinFunctionValue Fn(string name, int min, int max, Func<EvalContext, ScriptValue[], ScriptValue> f,
            string[] names = null, object[] defaults = null)
        {
            return BuiltinFunctionValue.Make(name, (self, args, kw, c) =>
            {
                if (kw.Count != 0)
                {
                    if (names == null)
                        throw Raise.TypeError(c, "Random." + name + "() takes no keyword arguments");
                    args = BindKw(c, args, kw, name, names, defaults);
                }
                Args.Between(c, args, name, min, max);
                return f(c, args);
            });
        }

        private static RandomState Rand(EvalContext ctx)
        {
            if (ctx.Random == null)
                ctx.Random = RandomState.CreateEntropy();
            return ctx.Random;
        }

        // ---- seed ----

        private static ScriptValue[] BindKw(EvalContext c, ScriptValue[] args, KwArgs kw, string name, string[] names, object[] defaults)
        {
            var slots = new ScriptValue[names.Length];
            if (args.Length > names.Length)
                throw Args.AtMostError(c, name, names.Length, args.Length);
            Array.Copy(args, slots, args.Length);
            for (int j = 0; j < kw.Count; j++)
            {
                int idx = Array.IndexOf(names, kw.NameAt(j));
                if (idx < 0)
                    throw KwReader.UnexpectedError(c, "Random." + name, kw.NameAt(j));
                if (slots[idx] != null)
                    throw Raise.TypeError(c, "Random." + name + "() got multiple values for argument '" + kw.NameAt(j) + "'");
                slots[idx] = kw.ValueAt(j);
            }
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                    continue;
                object d = defaults[i];
                if (d == null)
                    throw Raise.TypeError(c, "Random." + name + "() missing 1 required positional argument: '" + names[i] + "'");
                slots[i] = ReferenceEquals(d, NoneDefault) ? c.Values.None : d is double ? c.Values.Float((double)d) : (ScriptValue)c.Values.Int((int)d);
            }
            return slots;
        }

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
            {
                ctx.Budget.ChargeLinear(sv.Value.Length);
                n = StrSeed(BytesCodec.Encode(ctx, sv.Value, "utf-8"));   // strict: a lone surrogate is CPython's UnicodeEncodeError
            }
            else
                n = BigInteger.Abs(PyOps.Hash(a, ctx, 0));
            st.ReseedInt(DeriveSeed(n));
            return ctx.Values.None;
        }

        private static BigInteger StrSeed(byte[] utf)
        {
            byte[] sha;
            using (SHA512 h = SHA512.Create())
                sha = h.ComputeHash(utf);
            var concat = new byte[utf.Length + sha.Length];
            Array.Copy(utf, concat, utf.Length);
            Array.Copy(sha, 0, concat, utf.Length, sha.Length);
            // int.from_bytes(concat, 'big') as little-endian bytes plus a zero sign byte: one pass, where
            // n * 256 + byte per byte was quadratic in the string
            var le = new byte[concat.Length + 1];
            for (int i = 0; i < concat.Length; i++)
                le[i] = concat[concat.Length - 1 - i];
            return new BigInteger(le);
        }

        // The 32-bit words of n >= 0 XOR-folded, read off its little-endian bytes (shifting n down a word at
        // a time was quadratic in its length).
        private static int DeriveSeed(BigInteger n)
        {
            byte[] b = n.ToByteArray();
            uint acc = 0;
            for (int i = 0; i < b.Length; i++)
                acc ^= (uint)b[i] << (8 * (i & 3));
            return unchecked((int)acc);
        }

        // ---- getrandbits / _randbelow ----

        private static BigInteger GetRandBits(EvalContext ctx, ScriptValue[] args)
        {
            if (args.Length < 1 || !(args[0] is IntValue || args[0] is BoolValue))
                throw Raise.TypeError(ctx, "getrandbits() takes exactly one int argument");
            return RandBits(ctx, args[0] is BoolValue ? (((BoolValue)args[0]).Value ? 1 : 0) : ((IntValue)args[0]).Value);
        }

        // The typed core the module's own draws call directly: no argument array, no boxed k.
        private static BigInteger RandBits(EvalContext ctx, BigInteger k)
        {
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
            int rem = (int)(k % 32);
            RandomState st = Rand(ctx);
            if (words <= 2)
            {
                ulong acc = 0;
                for (int i = 0; i < words; i++)
                {
                    uint r = st.NextUint32();
                    if (i == words - 1 && rem != 0)
                        r >>= 32 - rem;
                    acc |= (ulong)r << (32 * i);
                    ctx.Budget.Step();
                }
                return new BigInteger(acc);
            }
            // The words little-endian with a zero sign byte, in one pass: OR-ing in shifted words copied the
            // growing result every time.
            var bytes = new byte[4 * words + 1];
            for (int i = 0; i < words; i++)
            {
                uint r = st.NextUint32();
                if (i == words - 1 && rem != 0)
                    r >>= 32 - rem;
                bytes[4 * i] = (byte)r;
                bytes[4 * i + 1] = (byte)(r >> 8);
                bytes[4 * i + 2] = (byte)(r >> 16);
                bytes[4 * i + 3] = (byte)(r >> 24);
                ctx.Budget.Step();
            }
            return new BigInteger(bytes);
        }

        private static BigInteger RandBelow(EvalContext ctx, BigInteger n)
        {
            if (n <= 0)
                return BigInteger.Zero;
            int k = (int)NumericOps.BitLength(n);
            BigInteger r = RandBits(ctx, k);
            int guard = 0;
            while (r >= n)
            {
                r = RandBits(ctx, k);
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
            BigInteger istart = Coerce.ToIndex(ctx, args[0], "randrange");
            if (args.Length < 2 || args[1].Kind == ValueKind.None)
            {
                if (istart > 0)
                    return RandBelow(ctx, istart);
                throw Raise.ValueError(ctx, "empty range for randrange()");
            }
            BigInteger istop = Coerce.ToIndex(ctx, args[1], "randrange");
            BigInteger istep = args.Length >= 3 ? Coerce.ToIndex(ctx, args[2], "randrange") : BigInteger.One;
            return RandRange(ctx, istart, istop, istep);
        }

        private static BigInteger RandRange(EvalContext ctx, BigInteger istart, BigInteger istop, BigInteger istep)
        {
            BigInteger width = istop - istart;
            if (istep == 1)
            {
                if (width > 0)
                    return istart + RandBelow(ctx, width);
                throw Raise.ValueError(ctx, "empty range for randrange()");
            }
            if (istep == 0)
                throw Raise.ValueError(ctx, "zero step for randrange()");
            BigInteger nn = istep > 0
                ? BigOps.FloorDiv(ctx, width + istep - 1, istep)
                : BigOps.FloorDiv(ctx, width + istep + 1, istep);
            if (nn <= 0)
                throw Raise.ValueError(ctx, "empty range for randrange()");
            return istart + BigOps.Multiply(ctx, istep, RandBelow(ctx, nn));
        }

        private static ScriptValue RandInt(EvalContext ctx, ScriptValue[] args)
        {
            BigInteger a = Coerce.ToIndex(ctx, args[0], "randrange");
            BigInteger b = Coerce.ToIndex(ctx, args[1], "randrange");
            return ctx.Values.Int(RandRange(ctx, a, b + 1, BigInteger.One));
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

        // shuffle(x): the random= callback went in 3.11.
        private static ScriptValue Shuffle(EvalContext ctx, ScriptValue[] args, KwArgs kw)
        {
            if (args.Length > 1)
                throw Raise.TypeError(ctx, "Random.shuffle() takes 2 positional arguments but " + (args.Length + 1) + " were given");
            args = StrMethods.WithKw(ctx, args, kw, "shuffle", "x");
            if (args.Length != 1)
                throw Raise.TypeError(ctx, "Random.shuffle() missing 1 required positional argument: 'x'");
            ListValue lst = args[0] as ListValue;
            if (lst == null)
                throw Raise.TypeError(ctx, "'" + args[0].PyTypeName + "' object does not support item assignment");
            // In place: four ints per element were charged as kept, and a list of 1M ended at MaxAllocBytes.
            var items = lst.Items;
            for (int i = items.Count - 1; i >= 1; i--)
            {
                int j = (int)RandBelow(ctx, i + 1);
                ScriptValue vi = items[i];
                items[i] = items[j];
                items[j] = vi;
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
                ScriptValue[] pool = Iter.ToArray(ctx, population);   // charged and capped
                if (pool.Length != n)   // a __len__ that disagrees with the iteration: indexes below would leave the pool
                    throw Raise.TypeError(ctx, "Population must be a sequence.  For dicts or sets, use sorted(d).");
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
            double lambd = args.Length >= 1 ? D(ctx, args, 0) : 1.0;
            if (lambd == 0.0)
                throw Raise.ZeroDivision(ctx, "division by zero");
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
            return MathModule.AsDouble(ctx, args[i]);
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
                System.Numerics.BigInteger kb = Coerce.ToIndex(ctx, v, "k");
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
            return Iter.ToArray(ctx, seq);
        }

        private static double[] ReadWeights(EvalContext ctx, ScriptValue seq)
        {
            ScriptValue[] items = Iter.ToArray(ctx, seq);   // charged and capped: weights=itertools.repeat(1.0) ends on a limit
            var ws = new double[items.Length];
            for (int i = 0; i < ws.Length; i++)
                ws[i] = D2(ctx, items[i]);
            return ws;
        }

        private static double D2(EvalContext ctx, ScriptValue v)
        {
            if (v.Kind == ValueKind.Float)
                return ((FloatValue)v).Value;
            if (v.Kind == ValueKind.Int || v.Kind == ValueKind.Bool)
                return NumericOps.ToDoubleChecked(NumericOps.AsBigInteger(v), ctx);   // CPython's total + 0.0: OverflowError
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

        private static ScriptValue RandBytes(EvalContext ctx, ScriptValue[] a)
        {
            Args.Exactly(ctx, a, "randbytes", 1);
            BigInteger n = Coerce.ToIndex(ctx, a[0], "randbytes");
            if (n.Sign < 0)
                throw Raise.ValueError(ctx, "number of bytes must be non-negative");
            int count = Coerce.ToSsize(ctx, n);
            ctx.Values.CheckStrLen(count);   // the cap first: 250M bytes were drawn before Bytes refused them
            ctx.Values.PreCharge(24 + (long)count);
            var buf = new byte[count];
            RandomState r = Rand(ctx);
            // Drawn a block at a time with a charge each: one native call was seconds on one step. The blocks
            // draw the same byte stream as one call (the last one is exactly what is left).
            var block = new byte[Math.Min(count, 1 << 16)];
            for (int at = 0; at < count; at += block.Length)
            {
                if (count - at < block.Length)
                    block = new byte[count - at];
                ctx.Budget.ChargeLinear(block.Length);
                r.NextBytes(block);
                Buffer.BlockCopy(block, 0, buf, at, block.Length);
            }
            return ctx.Values.Bytes(buf);
        }
    }
}

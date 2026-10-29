using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // The 1.1.0 sort (natural merge sort + typed keys) against the 1.0.0 one (OracleSort): the same order
    // on random inputs of every shape, plus run shapes, comparison counts and the budget.
    [Trait("Category", "Compatibility")]
    public class SortTests
    {
        // A record class whose __lt__ compares an int field: the generic path, calling script code.
        private const string Prelude =
            "class R:\n    def __init__(self, v):\n        self.v = v\n    def __lt__(self, o):\n        return self.v < o.v\n"
            + "def make(v):\n    return R(v)\n";

        // One context serves thousands of sorts: give it room, so a loaded box cannot turn the test into a
        // deadline or budget abort.
        private static EvResult NewRun()
        {
            return Ev.Run(Prelude, ResourceLimits.Default.With(deadlineMs: 300_000, maxSteps: 2_000_000_000, maxAllocBytes: 1_073_741_824));
        }

        private static ScriptValue RandomValue(EvalContext ctx, Random rnd, int shape, ScriptValue makeRecord)
        {
            switch (shape)
            {
                case 0:
                    return ctx.Values.Int(rnd.Next(-50, 50));
                case 1:
                    return ctx.Values.Int(new BigInteger(long.MaxValue) * rnd.Next(-3, 4) + rnd.Next(-5, 5));
                case 2:
                {
                    double[] specials = { -0.0, 0.0, double.PositiveInfinity, double.NegativeInfinity, 1e308, -1e-308 };
                    return ctx.Values.Float(rnd.Next(4) == 0 ? specials[rnd.Next(specials.Length)] : Math.Round(rnd.NextDouble() * 20 - 10, 1));
                }
                case 3:
                {
                    string[] parts = { "a", "ab", "abc", "\U0001F600", "\uFFFF", "é", "", "Z", "\uD7FF" };
                    return ctx.Values.Str(parts[rnd.Next(parts.Length)] + parts[rnd.Next(parts.Length)]);
                }
                case 4:
                    return rnd.Next(2) == 0 ? (ScriptValue)ctx.Values.Int(rnd.Next(-5, 5)) : ctx.Values.Float(rnd.Next(-10, 10) / 2.0);
                case 5:
                    return ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Int(rnd.Next(3)), ctx.Values.Str(rnd.Next(3).ToString()) });
                default:
                    return ctx.CallHook1(makeRecord, ctx.Values.Int(rnd.Next(-20, 20)));
            }
        }

        private static void SortLikeCallers(Action<EvalContext, ScriptValue[], ScriptValue[]> sort, EvalContext ctx,
            ScriptValue[] values, ScriptValue[] keys, bool reverse)
        {
            if (reverse)
            {
                Array.Reverse(values);
                if (keys != null)
                    Array.Reverse(keys);
            }
            sort(ctx, values, keys);
            if (reverse)
                Array.Reverse(values);
        }

        [Fact]
        public void SameOrderAsTheOracleOnRandomInputs()
        {
            EvResult run = NewRun();
            EvalContext ctx = run.Ctx;
            ScriptValue make = run.Get("make");
            var rnd = new Random(20261028);
            int[] lengths = { 0, 1, 2, 3, 31, 32, 33, 63, 64, 65, 100, 127, 128, 129, 500, 1000, 5000 };
            for (int trial = 0; trial < 2000; trial++)
            {
                int n = trial < lengths.Length * 6 ? lengths[trial % lengths.Length] : rnd.Next(0, trial % 50 == 0 ? 5000 : 300);
                int shape = rnd.Next(7);
                var values = new ScriptValue[n];
                for (int i = 0; i < n; i++)
                    values[i] = RandomValue(ctx, rnd, shape, make);
                if (rnd.Next(3) == 0 && n > 10 && shape != 6)
                {
                    var prefix = new ScriptValue[n / 2];   // a long presorted run at the front
                    Array.Copy(values, prefix, prefix.Length);
                    if (Capture(() => OracleSort.Sort(ctx, prefix, null)) == null)
                        Array.Copy(prefix, values, prefix.Length);
                }
                ScriptValue[] keys = null;
                if (rnd.Next(2) == 0)
                {
                    keys = new ScriptValue[n];
                    int keyShape = rnd.Next(7);
                    for (int i = 0; i < n; i++)
                        keys[i] = RandomValue(ctx, rnd, keyShape, make);
                }
                bool reverse = rnd.Next(2) == 0;

                ScriptValue[] expected = (ScriptValue[])values.Clone(), actual = (ScriptValue[])values.Clone();
                ScriptValue[] expectedKeys = keys == null ? null : (ScriptValue[])keys.Clone();
                ScriptValue[] actualKeys = keys == null ? null : (ScriptValue[])keys.Clone();
                string oracleError = Capture(() => SortLikeCallers(OracleSort.Sort, ctx, expected, expectedKeys, reverse));
                string newError = Capture(() => SortLikeCallers(StableSort.Sort, ctx, actual, actualKeys, reverse));
                Assert.True(oracleError == newError, "trial " + trial + ": " + oracleError + " vs " + newError);
                if (oracleError != null)
                {
                    AssertPermutation(values, actual);
                    continue;
                }
                for (int i = 0; i < n; i++)
                    Assert.True(ReferenceEquals(expected[i], actual[i]), "trial " + trial + " (n=" + n + ", shape " + shape + "): differs at " + i);
            }
        }

        // Only the exception TYPE must match: the two algorithms compare different pairs first, so the
        // operand order inside a TypeError's text may differ.
        private static string Capture(Action a)
        {
            try
            {
                a();
                return null;
            }
            catch (ScriptException e)
            {
                return e.Value.ExcType.Name;
            }
        }

        private static void AssertPermutation(ScriptValue[] before, ScriptValue[] after)
        {
            Assert.Equal(before.Length, after.Length);
            var seen = new HashSet<ScriptValue>(new RefEq());
            foreach (ScriptValue v in after)
                seen.Add(v);
            foreach (ScriptValue v in before)
                Assert.Contains(v, seen);
        }

        private sealed class RefEq : IEqualityComparer<ScriptValue>
        {
            public bool Equals(ScriptValue x, ScriptValue y) { return ReferenceEquals(x, y); }
            public int GetHashCode(ScriptValue v) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(v); }
        }

        [Fact]
        public void NaNKeysLeaveAPermutation()
        {
            EvResult run = NewRun();
            EvalContext ctx = run.Ctx;
            var rnd = new Random(7);
            var values = new ScriptValue[1000];
            for (int i = 0; i < values.Length; i++)
                values[i] = ctx.Values.Float(rnd.Next(10) == 0 ? double.NaN : rnd.NextDouble());
            var copy = (ScriptValue[])values.Clone();
            StableSort.Sort(ctx, copy, null);
            AssertPermutation(values, copy);
        }

        private static string Shape(string name, int n)
        {
            switch (name)
            {
                case "presorted": return "list(range(" + n + "))";
                case "reversed": return "list(range(" + n + ", 0, -1))";
                case "equal": return "[7] * " + n;
                case "organ": return "list(range(" + n / 2 + ")) + list(range(" + n / 2 + ", 0, -1))";
                case "sawtooth": return "[i % 37 for i in range(" + n + ")]";
                default: return "[(i * 7919) % " + n + " for i in range(" + n + ")]";
            }
        }

        [Theory]
        [InlineData("presorted")]
        [InlineData("reversed")]
        [InlineData("equal")]
        [InlineData("organ")]
        [InlineData("sawtooth")]
        [InlineData("scattered")]
        public void RunShapesSortStablyOnEveryPath(string shape)
        {
            string data = Shape(shape, 3000);
            string src = "import random\nrandom.seed(3)\ndata = " + data + "\nexpected = sorted(data)\n"
                + "pairs = [(v, i) for i, v in enumerate(data)]\n"
                + "byKey = sorted(pairs, key=lambda p: p[0])\n"
                + "stable = byKey == sorted(pairs)\n"
                + "floats = sorted(float(v) for v in data) == [float(v) for v in expected]\n"
                + "strs = sorted('%06d' % v for v in data) == ['%06d' % v for v in expected]\n"
                + "swapped = list(data)\nfor i in range(0, len(swapped) - 1, 100):\n    swapped[i], swapped[i + 1] = swapped[i + 1], swapped[i]\n"
                + "nearly = sorted(swapped) == expected\n"
                + "rev = sorted(pairs, key=lambda p: p[0], reverse=True) == sorted(pairs, key=lambda p: -p[0])\n"
                + "r = (stable, floats, strs, nearly, rev, all(expected[i] <= expected[i + 1] for i in range(len(expected) - 1)))\n";
            Assert.Equal("(True, True, True, True, True, True)", Ev.Run(src).Repr("r"));
        }

        [Theory]
        [InlineData("list(range(1000))", 999)]
        [InlineData("list(range(1000, 0, -1))", 999)]
        public void PresortedAndReversedTakeNMinusOneComparisons(string data, int expected)
        {
            string src = "count = [0]\nclass C:\n    def __init__(self, v):\n        self.v = v\n    def __lt__(self, o):\n"
                + "        count[0] += 1\n        return self.v < o.v\nitems = [C(v) for v in " + data + "]\nitems.sort()\nr = count[0]\n";
            Assert.Equal(expected.ToString(), Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void AComparisonThatNeverEndsStopsOnTheStepLimit()
        {
            string src = "class C:\n    def __lt__(self, o):\n        while True:\n            pass\nsorted([C(), C()])\n";
            var lim = ResourceLimits.Default.With(maxSteps: 1_000_000);
            EngineAbort a = Assert.Throws<EngineAbort>(() => Ev.Run(src, lim));
            Assert.Equal(EngineAbortKind.Steps, a.Kind);
        }

        [Fact]
        public void SortedOverACheapLongIteratorStopsOnTheCollectionCap()
        {
            var lim = ResourceLimits.Default.With(maxCollectionItems: 100_000);
            EngineAbort a = Assert.Throws<EngineAbort>(() => Ev.Run("import itertools\nsorted(itertools.repeat(0, 10_000_000))\n", lim));
            Assert.Equal(EngineAbortKind.Memory, a.Kind);
        }

        [Fact]
        public void ALargeSortStopsOnMemoryNotOnAHostException()
        {
            var lim = ResourceLimits.Default.With(maxAllocBytes: 48 * 1024 * 1024);
            EngineAbort a = Assert.Throws<EngineAbort>(() => Ev.Run("x = [0] * 2_000_000\nfor j in range(3):\n    y = sorted(x)\n", lim));
            Assert.Equal(EngineAbortKind.Memory, a.Kind);
        }

        [Fact]
        public void TypedSortsChargeTheirSteps()
        {
            var lim = ResourceLimits.Default.With(maxSteps: 3_000_000);
            EngineAbort a = Assert.Throws<EngineAbort>(() => Ev.Run("x = list(range(200_000))\nfor j in range(10):\n    y = sorted(x)\n", lim));
            Assert.Equal(EngineAbortKind.Steps, a.Kind);
        }

        [Fact]
        public void ARaisingComparisonLeavesTheListWhole()
        {
            string src = "import random\nrandom.seed(5)\nclass C:\n    def __init__(self, v):\n        self.v = v\n    def __lt__(self, o):\n"
                + "        if random.random() < 0.01:\n            raise RuntimeError('cmp')\n        return self.v < o.v\n"
                + "items = [C(v) for v in range(500)]\nrandom.shuffle(items)\nbefore = sorted(c.v for c in items)\nraised = 0\n"
                + "for k in range(20):\n    try:\n        items.sort()\n    except RuntimeError:\n        raised += 1\n"
                + "r = (raised > 0, sorted(c.v for c in items) == before, len(items))\n";
            Assert.Equal("(True, True, 500)", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void SortedAndListSortAgree()
        {
            string src = "import random\nrandom.seed(9)\ndata = [(random.randint(0, 20), i) for i in range(2000)]\n"
                + "a = sorted(data, key=lambda p: p[0], reverse=True)\nb = list(data)\nb.sort(key=lambda p: p[0], reverse=True)\nr = a == b\n";
            Assert.Equal("True", Ev.Run(src).Repr("r"));
        }
    }
}

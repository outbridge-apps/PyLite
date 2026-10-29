using Outbridge.PyLite.Runtime.Errors;
using Xunit;

namespace Outbridge.PyLite.Tests.Evaluator
{
    // T2.3 oracle: the same comprehension evaluated through the superinstruction-fused range drain
    // and through a manual for-loop (pure tree-walk) must agree via repr (type-exact — a fused int
    // where the tree-walk makes a bool/big-int would show). Ranges include negatives (floor-div and
    // mod signs), and the overflow rows force the per-element fallback mid-drain.
    [Trait("Category", "Compatibility")]
    public class FusedCompTests
    {
        private static void AssertEqualsLoop(string expr, string range, string cond = null)
        {
            string filter = cond != null ? " if " + cond : "";
            string loopFilter = cond != null ? "    if not (" + cond + "):\n        continue\n" : "";
            var res = Ev.Run(
                "a = [" + expr + " for x in " + range + filter + "]\n"
                + "b = []\n"
                + "for x in " + range + ":\n"
                + loopFilter
                + "    b.append(" + expr + ")\n"
                + "r = (repr(a) == repr(b), len(a) > 0)\n");
            Assert.Equal("(True, True)", res.Repr("r"));
        }

        [Theory]
        [InlineData("x")]
        [InlineData("x * 2 + 1")]
        [InlineData("x * x - 3 * x + 7")]
        [InlineData("x % 5")]
        [InlineData("x % -5")]
        [InlineData("x // 3")]
        [InlineData("x // -3")]
        [InlineData("-x")]
        [InlineData("~x")]
        [InlineData("+x")]
        [InlineData("x ** 2")]
        [InlineData("x ** 3")]
        [InlineData("x << 3")]
        [InlineData("x >> 2")]
        [InlineData("x & 7")]
        [InlineData("x | 9")]
        [InlineData("x ^ 5")]
        [InlineData("x if x > 0 else -x")]
        [InlineData("(x + 3) * (x - 2) % 11")]
        public void FusedBodyMatchesTreeWalk(string expr)
        {
            AssertEqualsLoop(expr, "range(-60, 60)");
        }

        [Theory]
        [InlineData("x * 2", "x % 2 == 0")]
        [InlineData("x", "x % 3 == 0 and x > 10")]
        [InlineData("x + 1", "not (x % 7)")]
        [InlineData("x", "0 <= x < 40")]
        [InlineData("x", "x % 5")]                       // int truth
        [InlineData("x", "x > 5 or x < -50")]
        [InlineData("x if x % 2 else -x", "x % 3 != 2")]
        public void FusedCondMatchesTreeWalk(string expr, string cond)
        {
            AssertEqualsLoop(expr, "range(-60, 60)", cond);
        }

        [Fact]
        public void NegativeStepRangeMatches()
        {
            AssertEqualsLoop("x * 3 - 1", "range(50, -50, -3)", "x % 2 == 0");
        }

        // Per-element fallback: overflow inside the fused chain re-runs THAT element through the
        // exact path (big-int promotion) while its neighbors stay fused — results identical.
        [Theory]
        [InlineData("(x + 4611686018427387900) * 4")]     // every element: the mul overflows
        [InlineData("x * 10 ** 18")]                      // mixed: |x| >= 10 falls back, the rest fuse
        [InlineData("-(x + (-9223372036854775748))")]     // x == -60 hits exactly -long.MinValue (USub guard)
        public void OverflowFallsBackPerElement(string expr)
        {
            AssertEqualsLoop(expr, "range(-60, 60)");
        }

        [Fact]
        public void CondOverflowFallsBackPerElement()
        {
            AssertEqualsLoop("x", "range(-60, 60)", "x * 10 ** 18 < 6 * 10 ** 18");
        }

        // The operator edges TryLongBinOp refuses at runtime: negative/zero exponents (floats / the
        // exact path), 0/±1 bases, shift counts at and past the guards, and the MinValue division.
        [Theory]
        [InlineData("x ** -1", "range(1, 6)")]                              // int ** -int => float
        [InlineData("x ** 0", "range(-5, 5)")]
        [InlineData("0 ** x", "range(3)")]
        [InlineData("(0 - 1) ** x", "range(6)")]                            // -1 base: alternating, exact path
        [InlineData("x << 62", "range(-2, 3)")]                             // 1<<62 fits, 2<<62 promotes
        [InlineData("x >> 100", "range(-5, 5)")]                            // fully-shifted-out sign, fused
        [InlineData("x << x", "range(6)")]
        [InlineData("(x + (-9223372036854775748)) // -1", "range(-60, -50)")]   // MinValue // -1 at x=-60
        [InlineData("(x + (-9223372036854775748)) % -1", "range(-60, -50)")]
        public void OperatorEdgesMatchTreeWalk(string expr, string range)
        {
            AssertEqualsLoop(expr, range);
        }

        // The overflow test of the long multiply (P1, no thrown OverflowException) against BigInteger.
        [Fact]
        public void LongMultiplyOverflowIsExact()
        {
            long[] edges = { 0, 1, -1, 2, -2, 3, int.MaxValue, int.MinValue, (long)int.MaxValue + 1, (long)int.MinValue - 1,
                3037000499, 3037000500, -3037000499, -3037000500, long.MaxValue, long.MinValue, long.MaxValue / 2,
                long.MinValue / 2, 4294967296, -4294967296, 1L << 62, -(1L << 62) };
            foreach (long a in edges)
            {
                foreach (long b in edges)
                {
                    System.Numerics.BigInteger exact = (System.Numerics.BigInteger)a * b;
                    bool fits = exact >= long.MinValue && exact <= long.MaxValue;
                    long r;
                    Assert.Equal(fits, Outbridge.PyLite.Runtime.Values.NumericOps.TryMulLong(a, b, out r));
                    if (fits)
                        Assert.Equal((long)exact, r);
                }
            }
        }

        // Range bounds at the drain's own Hi limit (long.MaxValue/4): every x*x overflows — all
        // elements take the per-element big-int fallback inside the fused drain.
        [Fact]
        public void BigRangeBoundsFallBack()
        {
            AssertEqualsLoop("x * x", "range(2305843009213693947, 2305843009213693951)");
        }

        [Fact]
        public void EmptyResultsStayEmpty()
        {
            var res = Ev.Run(
                "r1 = [x * 2 for x in range(0)]\n"
                + "r2 = [x for x in range(10) if x > 99]\n"
                + "r = (r1, r2)\n");
            Assert.Equal("([], [])", res.Repr("r"));
        }

        // Constant conditions fuse as int truth: `if 5` keeps everything, `if 0` keeps nothing.
        [Fact]
        public void ConstantConditionsFuse()
        {
            var res = Ev.Run(
                "r1 = [x for x in range(5) if 5]\n"
                + "r2 = [x for x in range(5) if 0]\n"
                + "r = (r1, r2)\n");
            Assert.Equal("([0, 1, 2, 3, 4], [])", res.Repr("r"));
        }

        // A deep flat chain fuses into a linked delegate chain that recurses per level at compile
        // and invoke time without a stack probe — bounded by MaxAstDepth, like the compiled path.
        [Fact]
        public void DeepFusedChainRuns()
        {
            var sb = new System.Text.StringBuilder("r = [x");
            for (int i = 0; i < 400; i++)
                sb.Append(" + x");
            sb.Append(" for x in range(3)]\nn = r[1]\n");
            Assert.Equal("401", Ev.Run(sb.ToString()).Repr("n"));
        }

        // Edge cases the fused chain refuses at runtime must raise the SAME typed errors via the
        // general re-run (zero divisor, negative shift count).
        [Theory]
        [InlineData("r = [x // (x - 5) for x in range(10)]", "ZeroDivisionError")]
        [InlineData("r = [x % (x - 3) for x in range(10)]", "ZeroDivisionError")]
        [InlineData("r = [x << (x - 3) for x in range(10)]", "ValueError")]
        public void FusedEdgeErrorsStayTyped(string src, string excType)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(src));
            Assert.Equal(excType, ex.Value.ExcType.Name);
        }

        [Fact]
        public void FusedSetAndDictMatchLoop()
        {
            var res = Ev.Run(
                "s1 = {x % 7 for x in range(-30, 60)}\n"
                + "s2 = set()\n"
                + "for x in range(-30, 60):\n"
                + "    s2.add(x % 7)\n"
                + "d1 = {x % 5: x * 3 for x in range(40)}\n"
                + "d2 = {}\n"
                + "for x in range(40):\n"
                + "    d2[x % 5] = x * 3\n"
                + "r = (repr(sorted(s1)) == repr(sorted(s2)), repr(d1) == repr(d2), len(s1), len(d1))\n");
            Assert.Equal("(True, True, 7, 5)", res.Repr("r"));
        }

        // The fused drain skips the loop-variable slot write — that must stay unobservable: the
        // comp scope is private (Py3), so an outer x is untouched.
        [Fact]
        public void LoopVariableStaysComprehensionPrivate()
        {
            var res = Ev.Run("x = 99\nr1 = [x * 2 for x in range(5)]\nr = (x, r1)\n");
            Assert.Equal("(99, [0, 2, 4, 6, 8])", res.Repr("r"));
        }

        // MaxIntBits < 128 keeps fusion off entirely (IntOpSmall's own threshold), so exotic configs
        // still reach the big path's estimate-based aborts — and stay correct.
        [Fact]
        public void ExoticMaxIntBitsConfigTakesTheExactPath()
        {
            var res = Ev.Run("r = [x * 2 + 1 for x in range(20) if x % 2 == 0]\n",
                Outbridge.PyLite.Hosting.ResourceLimits.Default.With(maxIntBits: 100));
            Assert.Equal("[1, 5, 9, 13, 17, 21, 25, 29, 33, 37]", res.Repr("r"));
        }

        // Fusion skips intermediate-value charges but never the drain's per-element Step — steps and
        // the wall-clock deadline still bound a fused bomb.
        [Fact]
        public void FusedCompStillBudgetBounded()
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(
                "r = [x * x for x in range(10**9) if x % 2 == 0]",
                Outbridge.PyLite.Hosting.ResourceLimits.Default.With(maxSteps: 100_000)));
            Assert.Equal(EngineAbortKind.Steps, ex.Kind);
        }

        [Fact]
        public void FusedCompRespectsWallClockDeadline()
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(
                "r = [x * 3 + 1 for x in range(10**9)]",
                Outbridge.PyLite.Hosting.ResourceLimits.Default.With(deadlineMs: 200)));
            Assert.Equal(EngineAbortKind.Deadline, ex.Kind);
        }

        // The fused cache lives on the shared AST (benign race): the SAME CompiledScript must run
        // correctly on repeated executions (second run reuses the cached delegates).
        [Fact]
        public void FusedDelegatesSurviveRerunOfTheSameCompiledScript()
        {
            var engine = new Outbridge.PyLite.Hosting.ScriptEngine(new Outbridge.PyLite.Hosting.EngineOptions());
            var compiled = engine.Compile(
                "r = [x * x + 1 for x in range(100) if x % 3 != 0]\nresult = repr((len(r), r[0], r[-1]))\n", "t23");
            var req = new Outbridge.PyLite.Hosting.RunRequest { ScalarOutputNames = new[] { "result" } };
            string first = null;
            for (int i = 0; i < 5; i++)
            {
                var run = engine.Run(compiled, req);
                Assert.True(run.Succeeded, run.Succeeded ? null : run.Error.Message);
                if (first == null)
                    first = run.ScalarsOut["result"];
                else
                    Assert.Equal(first, run.ScalarsOut["result"]);
            }
            Assert.Equal("(66, 2, 9605)", first);
        }
    }

    // T2.3 over the ITERABLE drain: machine-int items unbox into the fused body per element; every
    // other item type (bool included — identity must keep its type) takes the general path for that
    // element only, so mixed sources interleave both engines inside one comprehension.
    [Trait("Category", "Compatibility")]
    public class FusedIterableCompTests
    {
        private static void AssertEqualsLoop(string expr, string source, string cond = null)
        {
            string filter = cond != null ? " if " + cond : "";
            string loopFilter = cond != null ? "    if not (" + cond + "):\n        continue\n" : "";
            var res = Ev.Run(
                "src = " + source + "\n"
                + "a = [" + expr + " for x in src" + filter + "]\n"
                + "b = []\n"
                + "for x in src:\n"
                + loopFilter
                + "    b.append(" + expr + ")\n"
                + "r = (repr(a) == repr(b), len(a) > 0)\n");
            Assert.Equal("(True, True)", res.Repr("r"));
        }

        [Theory]
        [InlineData("x")]
        [InlineData("x * 2 + 1")]
        [InlineData("x * x - 3 * x + 7")]
        [InlineData("x % 5")]
        [InlineData("x // -3")]
        [InlineData("-x")]
        [InlineData("x ** 2")]
        [InlineData("x & 7")]
        [InlineData("x if x > 0 else -x")]
        public void FusedBodyOverListMatchesTreeWalk(string expr)
        {
            AssertEqualsLoop(expr, "list(range(-60, 60))");
        }

        [Theory]
        [InlineData("x * 2", "x % 2 == 0")]
        [InlineData("x", "0 <= x < 40")]
        [InlineData("x + 1", "not (x % 7)")]
        [InlineData("x", "x % 5")]
        public void FusedCondOverListMatchesTreeWalk(string expr, string cond)
        {
            AssertEqualsLoop(expr, "list(range(-60, 60))", cond);
        }

        // The per-item switch: ints fuse, str repeats, float multiplies, bool computes as 0/1, a
        // big int promotes — all inside ONE comprehension, each through its correct engine.
        [Fact]
        public void MixedTypeSourceFallsBackPerItem()
        {
            AssertEqualsLoop("x * 2", "[1, 2, 'ab', 3.5, True, 10 ** 30, 4]");
        }

        // Identity must preserve TYPES: bools stay bools (excluded from unboxing), strings/floats
        // pass through untouched.
        [Fact]
        public void IdentityPreservesItemTypes()
        {
            var res = Ev.Run("r = [x for x in [True, False, 1, 'a', 2.5]]\n");
            Assert.Equal("[True, False, 1, 'a', 2.5]", res.Repr("r"));
        }

        // Items near long.MaxValue are small (long-backed) — the fused add overflows and falls back
        // for those elements only.
        [Fact]
        public void OverflowItemFallsBack()
        {
            AssertEqualsLoop("x + 100", "[9223372036854775800, 1, -9223372036854775800]");
        }

        [Theory]
        [InlineData("r = [x // (x - 5) for x in [1, 5]]", "ZeroDivisionError")]
        [InlineData("r = [x << (0 - x) for x in [0, 1]]", "ValueError")]
        public void FusedIterableEdgeErrorsStayTyped(string src, string excType)
        {
            var ex = Assert.Throws<Outbridge.PyLite.Runtime.Errors.ScriptException>(() => Ev.Run(src));
            Assert.Equal(excType, ex.Value.ExcType.Name);
        }

        // Non-list sources through the same drain: tuple, str (never unboxes), dict (iterates int
        // keys), and a lazy map iterator feeding int items.
        [Fact]
        public void OtherSourcesMatchLoop()
        {
            AssertEqualsLoop("x * 3 - 1", "(4, -7, 12, 0)");
            AssertEqualsLoop("x + x", "'abc'");
            AssertEqualsLoop("x * 2", "{1: 'a', 2: 'b', 3: 'c'}");
            AssertEqualsLoop("x * 3", "list(map(lambda v: v + 1, range(10)))");
        }

        [Fact]
        public void SetAndDictCompOverListMatchLoop()
        {
            var res = Ev.Run(
                "src = list(range(-30, 60))\n"
                + "s1 = {x % 7 for x in src}\n"
                + "s2 = set()\n"
                + "for x in src:\n"
                + "    s2.add(x % 7)\n"
                + "d1 = {x % 5: x * 3 for x in src}\n"
                + "d2 = {}\n"
                + "for x in src:\n"
                + "    d2[x % 5] = x * 3\n"
                + "r = (repr(sorted(s1)) == repr(sorted(s2)), repr(d1) == repr(d2))\n");
            Assert.Equal("(True, True)", res.Repr("r"));
        }

        // A fused condition filtering for a NON-fusable element (a call) — the halves mix.
        [Fact]
        public void FusedCondWithGeneralElement()
        {
            AssertEqualsLoop("str(x)", "list(range(-20, 20))", "x % 2 == 0");
        }

        // Budget parity through the fused iterable drain: the per-element Step still bounds it.
        [Fact]
        public void FusedIterableCompRespectsDeadline()
        {
            var ex = Assert.Throws<Outbridge.PyLite.Runtime.Errors.EngineAbort>(() => Ev.Run(
                "L = list(range(50000))\nwhile True:\n    r = [x * x for x in L]\n",
                Outbridge.PyLite.Hosting.ResourceLimits.Default.With(deadlineMs: 200)));
            Assert.Equal(Outbridge.PyLite.Runtime.Errors.EngineAbortKind.Deadline, ex.Kind);
        }

        [Fact]
        public void FusedIterableDelegatesSurviveRerun()
        {
            var engine = new Outbridge.PyLite.Hosting.ScriptEngine(new Outbridge.PyLite.Hosting.EngineOptions());
            var compiled = engine.Compile(
                "L = list(range(80))\nr = [x * 2 + 1 for x in L if x % 3 != 0]\n"
                + "result = repr((len(r), r[0], r[-1]))\n", "t23i");
            var req = new Outbridge.PyLite.Hosting.RunRequest { ScalarOutputNames = new[] { "result" } };
            string first = null;
            for (int i = 0; i < 5; i++)
            {
                var run = engine.Run(compiled, req);
                Assert.True(run.Succeeded, run.Succeeded ? null : run.Error.Message);
                if (first == null)
                    first = run.ScalarsOut["result"];
                else
                    Assert.Equal(first, run.ScalarsOut["result"]);
            }
            Assert.Equal("(53, 3, 159)", first);
        }
    }

    // T2.3c over the ODOMETER (genexp / multi-clause): the fused leaf reads the expression's sole
    // loop variable from its slot (the clause walk already wrote it). Expressions over TWO locals
    // never fuse — the compiled path owns them; conditions may reference an OUTER clause's variable.
    [Trait("Category", "Compatibility")]
    public class FusedOdometerCompTests
    {
        private static void AssertGenexpEqualsLoop(string expr, string source, string cond = null)
        {
            string filter = cond != null ? " if " + cond : "";
            string loopFilter = cond != null ? "    if not (" + cond + "):\n        continue\n" : "";
            var res = Ev.Run(
                "src = " + source + "\n"
                + "a = list(" + expr + " for x in src" + filter + ")\n"
                + "b = []\n"
                + "for x in src:\n"
                + loopFilter
                + "    b.append(" + expr + ")\n"
                + "r = (repr(a) == repr(b), len(a) > 0)\n");
            Assert.Equal("(True, True)", res.Repr("r"));
        }

        [Theory]
        [InlineData("x * 2 + 1", null)]
        [InlineData("x * x - 3 * x", null)]
        [InlineData("x % 5", "x % 3 == 0")]
        [InlineData("x if x > 0 else -x", "x % 5")]
        public void FusedGenexpMatchesTreeWalk(string expr, string cond)
        {
            AssertGenexpEqualsLoop(expr, "range(-60, 60)", cond);
        }

        [Fact]
        public void FusedGenexpSumAndPartialConsumption()
        {
            var res = Ev.Run(
                "s = sum(x * 2 + 1 for x in range(-60, 60))\n"
                + "g = (x * 3 for x in range(10))\n"
                + "r = (s, next(g), next(g))\n");
            Assert.Equal("(0, 0, 3)", res.Repr("r"));
        }

        // Single-variable element fuses against the INNER clause's slot; a two-variable element
        // must NOT fuse and stays correct on the compiled path.
        [Fact]
        public void MultiClauseSingleVarElementMatchesNestedLoop()
        {
            var res = Ev.Run(
                "a = [y * 2 + 1 for x in range(6) for y in range(x)]\n"
                + "b = []\n"
                + "for x in range(6):\n"
                + "    for y in range(x):\n"
                + "        b.append(y * 2 + 1)\n"
                + "r = (repr(a) == repr(b), len(a))\n");
            Assert.Equal("(True, 15)", res.Repr("r"));
        }

        [Fact]
        public void MultiClauseTwoVarElementStaysCorrect()
        {
            var res = Ev.Run(
                "a = [x * 100 + y for x in range(5) for y in range(4)]\n"
                + "b = []\n"
                + "for x in range(5):\n"
                + "    for y in range(4):\n"
                + "        b.append(x * 100 + y)\n"
                + "r = (repr(a) == repr(b), a[7], a[-1])\n");
            Assert.Equal("(True, 103, 403)", res.Repr("r"));
        }

        // A condition on clause i may reference clause j<=i's variable: both conds here fuse
        // against DIFFERENT slots (outer x, inner y), cached per clause.
        [Fact]
        public void MultiClauseOuterVarCondsMatchNestedLoop()
        {
            var res = Ev.Run(
                "a = [y for x in range(8) if x % 2 == 0 for y in range(x) if y % 3 != 0]\n"
                + "b = []\n"
                + "for x in range(8):\n"
                + "    if x % 2 == 0:\n"
                + "        for y in range(x):\n"
                + "            if y % 3 != 0:\n"
                + "                b.append(y)\n"
                + "r = (repr(a) == repr(b), len(a) > 0)\n");
            Assert.Equal("(True, True)", res.Repr("r"));
        }

        // Multi-clause dict: two-var key stays compiled, single-var value fuses — mixed engines
        // inside one element.
        [Fact]
        public void MultiClauseDictAndSetMatchLoop()
        {
            var res = Ev.Run(
                "d1 = {x * 10 + y: y * 2 for x in range(4) for y in range(3)}\n"
                + "d2 = {}\n"
                + "for x in range(4):\n"
                + "    for y in range(3):\n"
                + "        d2[x * 10 + y] = y * 2\n"
                + "s1 = {y % 5 for x in range(4) for y in range(x * 3)}\n"
                + "s2 = set()\n"
                + "for x in range(4):\n"
                + "    for y in range(x * 3):\n"
                + "        s2.add(y % 5)\n"
                + "r = (repr(d1) == repr(d2), repr(sorted(s1)) == repr(sorted(s2)))\n");
            Assert.Equal("(True, True)", res.Repr("r"));
        }

        // A condition referencing a LATER clause's still-unbound variable: the fused read sees an
        // unbound slot, falls back, and the general path raises the canonical error — the same
        // exception type a non-fusable (float) twin of the shape raises.
        [Fact]
        public void UnboundLocalFallsBackToCanonicalError()
        {
            var fused = Assert.Throws<Outbridge.PyLite.Runtime.Errors.ScriptException>(() => Ev.Run(
                "r = [x for x in range(3) if y > 0 for y in range(3)]\n"));
            var plain = Assert.Throws<Outbridge.PyLite.Runtime.Errors.ScriptException>(() => Ev.Run(
                "r = [x for x in range(3) if y > 0.0 for y in range(3)]\n"));
            Assert.Equal(plain.Value.ExcType.Name, fused.Value.ExcType.Name);
        }

        // Mixed item types through the genexp leaf: ints fuse off the slot, bool/str/float/big fall
        // back per element; identity preserves every type.
        [Fact]
        public void GenexpMixedItemsAndIdentity()
        {
            AssertGenexpEqualsLoop("x * 2", "[1, True, 'ab', 3.5, 10 ** 30, 4]");
            var res = Ev.Run("r = list(x for x in [True, 2.5, 'a', 7])\n");
            Assert.Equal("[True, 2.5, 'a', 7]", res.Repr("r"));
        }

        [Fact]
        public void GenexpOverflowFallsBackPerElement()
        {
            AssertGenexpEqualsLoop("x * 10 ** 18", "range(-60, 60)");
        }

        [Fact]
        public void GenexpFusedEdgeErrorsStayTyped()
        {
            var ex = Assert.Throws<Outbridge.PyLite.Runtime.Errors.ScriptException>(() => Ev.Run(
                "r = list(x // (x - 2) for x in range(5))\n"));
            Assert.Equal("ZeroDivisionError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void FusedGenexpRespectsDeadline()
        {
            var ex = Assert.Throws<Outbridge.PyLite.Runtime.Errors.EngineAbort>(() => Ev.Run(
                "while True:\n    s = sum(x * x for x in range(200000))\n",
                Outbridge.PyLite.Hosting.ResourceLimits.Default.With(deadlineMs: 200)));
            Assert.Equal(Outbridge.PyLite.Runtime.Errors.EngineAbortKind.Deadline, ex.Kind);
        }

        [Fact]
        public void FusedMultiClauseRespectsSteps()
        {
            var ex = Assert.Throws<Outbridge.PyLite.Runtime.Errors.EngineAbort>(() => Ev.Run(
                "r = [y for x in range(10**6) for y in range(10**6)]",
                Outbridge.PyLite.Hosting.ResourceLimits.Default.With(maxSteps: 100_000)));
            Assert.Equal(Outbridge.PyLite.Runtime.Errors.EngineAbortKind.Steps, ex.Kind);
        }

        [Fact]
        public void FusedOdometerDelegatesSurviveRerun()
        {
            var engine = new Outbridge.PyLite.Hosting.ScriptEngine(new Outbridge.PyLite.Hosting.EngineOptions());
            var compiled = engine.Compile(
                "r = [y * 2 for x in range(6) if x % 2 == 0 for y in range(x)]\n"
                + "s = sum(v * 3 for v in range(50) if v % 2 == 0)\n"
                + "result = repr((len(r), r[-1], s))\n", "t23c");
            var req = new Outbridge.PyLite.Hosting.RunRequest { ScalarOutputNames = new[] { "result" } };
            string first = null;
            for (int i = 0; i < 5; i++)
            {
                var run = engine.Run(compiled, req);
                Assert.True(run.Succeeded, run.Succeeded ? null : run.Error.Message);
                if (first == null)
                    first = run.ScalarsOut["result"];
                else
                    Assert.Equal(first, run.ScalarsOut["result"]);
            }
            Assert.Equal("(6, 6, 1800)", first);
        }
    }
}

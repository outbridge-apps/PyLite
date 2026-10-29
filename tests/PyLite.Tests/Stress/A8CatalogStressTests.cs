using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Outbridge.PyLite.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace Outbridge.PyLite.Tests.Stress
{
    // T-TST-03..07: the executable A8 adversarial catalog (impl/12 R-6 normative table). Each vector is a
    // named test driven through the PUBLIC ScriptEngine, with an external Stopwatch echelon deliberately
    // wider than the engine deadline (so we catch the ENGINE budget, not the watchdog). The invariants:
    // (1) hostile scripts end in a LEGAL outcome (BudgetExceeded/SyntaxError/HostError), never a crash;
    // (2) size constructors abort BEFORE allocation; (3) EngineAbort is uncatchable; (4) process stays alive.
    [Collection("TimeSensitive")]   // asserts wall-clock bounds; must not race the rest of the suite
    [Trait("Category", "ResourceLimits")]
    [Trait("Category", "MachineSensitive")]
    public class A8CatalogStressTests
    {
        private readonly ITestOutputHelper _out;
        public A8CatalogStressTests(ITestOutputHelper output) { _out = output; }

        private static ScriptEngine Engine(ResourceLimits lim = null)
        {
            return new ScriptEngine(new EngineOptions
            {
                DefaultLimits = lim ?? ResourceLimits.Default.With(deadlineMs: 3000),
                Clock = new SystemClock(TimeSpan.Zero, "UTC"),
                SleepPolicy = new NoOpSleepPolicy(),
            });
        }

        // Run within an external time cap; asserts the engine returned (did not hang) and the outcome kind.
        // The cap is a HANG DETECTOR only — the guarantee under test is r.Error.Kind == BudgetExceeded,
        // produced by the engine's own 3 s deadline. So it may be generous: 10x the engine deadline still
        // catches a real hang, and no longer fires when the host is busy.
        private RunResult RunGuarded(string source, ResourceLimits lim = null, int hardCapMs = 30000)
        {
            var engine = Engine(lim);
            RunResult result = null;
            var t = Task.Run(() => result = engine.Run(source, new RunRequest()));
            Assert.True(t.Wait(hardCapMs), "engine hung past the external cap (" + hardCapMs + "ms)");
            Assert.NotNull(result);
            return result;
        }

        private void AssertBudget(string source, ResourceLimits lim = null)
        {
            RunResult r = RunGuarded(source, lim);
            _out.WriteLine(r.Error != null ? r.Error.Kind + ": " + r.Error.Message : "SUCCEEDED");
            Assert.False(r.Succeeded, "expected a budget failure but the script succeeded");
            Assert.Equal(ScriptErrorKind.BudgetExceeded, r.Error.Kind);
        }

        // ---- #1 mult-stack-depth ----
        [Fact]
        public void A8_01_RecursionDepth()
        {
            AssertBudget("def f(n):\n    return f(n + 1)\nf(0)\n");
        }

        // ---- #2 parser-SOE ----
        [Fact]
        public void A8_02_ParserDeepNesting()
        {
            string src = new string('(', 200000) + "1" + new string(')', 200000) + "\n";
            RunResult r = RunGuarded(src, hardCapMs: 16000);
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.SyntaxError, r.Error.Kind);
        }

        // ---- #3 data-recursion: deep nested list == / repr ----
        [Fact]
        public void A8_03_DeepDataStructure()
        {
            AssertBudget("x = []\nfor i in range(2000000):\n    x = [x]\nrepr(x)\n");
        }

        // ---- #4 probe-mid-frame ----
        [Fact]
        public void A8_04_RecursionLargeFrame()
        {
            AssertBudget("def f(n):\n    a = [n] * 100\n    return f(n + 1) + a[0]\nf(0)\n");
        }

        // ---- #5 reentrancy-budget: re.sub callback recurses ----
        [Fact]
        public void A8_05_ReentrantCallback()
        {
            AssertBudget(
                "import re\n" +
                "def f(m):\n" +
                "    return re.sub(r'x', f, 'x')\n" +
                "re.sub(r'x', f, 'x')\n");
        }

        // ---- #6 dag-repr-bomb ----
        [Fact]
        public void A8_06_DagReprBomb()
        {
            AssertBudget("x = [0]\nfor i in range(60):\n    x = [x, x]\nrepr(x)\n");
        }

        // ---- #7 builtin-buffers ----
        [Fact]
        public void A8_07_StringGrowth()
        {
            AssertBudget("s = 'x'\nfor i in range(10000000):\n    s = s + s\n",
                ResourceLimits.Default.With(deadlineMs: 3000, maxAllocBytes: 64_000_000));
        }

        // ---- #9 bigint-quadratic: abort BEFORE allocation ----
        [Fact]
        public void A8_09_BigIntBeforeAllocation()
        {
            long before = GC.GetTotalMemory(true);
            RunResult r = RunGuarded("x = 1 << (10 ** 8)\n");
            long after = GC.GetTotalMemory(false);
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.BudgetExceeded, r.Error.Kind);
            Assert.True(after - before < 32_000_000, "a huge int was allocated before the cap fired: +" + (after - before));
        }

        // ---- #10 size-ctor: abort BEFORE allocation ----
        [Fact]
        public void A8_10_SizeCtorBeforeAllocation()
        {
            long before = GC.GetTotalMemory(true);
            RunResult r = RunGuarded("x = 'a'.rjust(10 ** 8)\n");
            long after = GC.GetTotalMemory(false);
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.BudgetExceeded, r.Error.Kind);
            Assert.True(after - before < 32_000_000, "a huge string was allocated before the cap fired");
        }

        // ---- #13 swallow-abort: except cannot catch EngineAbort ----
        [Fact]
        public void A8_13_SwallowAbortImpossible()
        {
            AssertBudget("while True:\n    try:\n        pass\n    except:\n        pass\n",
                ResourceLimits.Default.With(maxSteps: 500_000, deadlineMs: 3000));
        }

        // ---- #14 finally-bomb ----
        [Fact]
        public void A8_14_FinallyUnderTerminating()
        {
            AssertBudget("while True:\n    try:\n        pass\n    finally:\n        x = 1\n",
                ResourceLimits.Default.With(maxSteps: 500_000, deadlineMs: 3000));
        }

        // ---- #16 hash-flooding ----
        [Fact]
        public void A8_16_HashFlooding()
        {
            AssertBudget("d = {}\nfor i in range(100000000):\n    d[i] = i\n",
                ResourceLimits.Default.With(deadlineMs: 3000));
        }

        // ---- #17 numeric-key-eq: SUCCESS, dict collapses to len 1 ----
        [Fact]
        public void A8_17_NumericKeyCollapse()
        {
            var engine = Engine();
            RunResult r = engine.Run("d = {True: 'a', 1: 'b', 1.0: 'c'}\nn = len(d)\n",
                new RunRequest { ScalarOutputNames = new[] { "n" } });
            Assert.True(r.Succeeded);
            Assert.Equal("1", r.ScalarsOut["n"]);
        }

        // ---- #18 itertools-buffers over an infinite source ----
        [Fact]
        public void A8_18_ItertoolsInfinite()
        {
            AssertBudget(
                "import itertools\n" +
                "for x in itertools.count():\n" +
                "    y = x * x\n",
                ResourceLimits.Default.With(maxSteps: 2_000_000, deadlineMs: 3000));
        }

        // ---- #19 redos-count ----
        [Fact]
        public void A8_19_ReDoS()
        {
            AssertBudget(
                "import re\n" +
                "re.match(r'(a+)+$', 'a' * 40 + 'b')\n");
        }

        // ---- #23 marshal-out-bomb ----
        [Fact]
        public void A8_23_HugeExceptionMessage()
        {
            RunResult r = RunGuarded("raise ValueError('x' * (10 ** 8))\n");
            Assert.False(r.Succeeded);
            // Either a runtime ValueError (message truncated for the host) or a memory abort — both legal.
            Assert.True(r.Error.Kind == ScriptErrorKind.RuntimeError || r.Error.Kind == ScriptErrorKind.BudgetExceeded);
        }

        // ---- #24 exc-chain-abuse ----
        [Fact]
        public void A8_24_ExceptionChainAbuse()
        {
            AssertBudget(
                "while True:\n" +
                "    try:\n" +
                "        raise ValueError('x')\n" +
                "    except ValueError:\n" +
                "        pass\n",
                ResourceLimits.Default.With(maxSteps: 1_000_000, deadlineMs: 3000));
        }

        // ---- PLAN §9.3 explicit: while True infinite loop -> Deadline ----
        [Fact]
        public void Plan93_InfiniteLoopDeadline()
        {
            var sw = Stopwatch.StartNew();
            RunResult r = RunGuarded("while True:\n    pass\n",
                ResourceLimits.Default.With(deadlineMs: 1000));
            sw.Stop();
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.BudgetExceeded, r.Error.Kind);
            Assert.True(sw.ElapsedMilliseconds < 5000, "deadline overrun: " + sw.ElapsedMilliseconds + "ms");
        }

        // ---- PLAN §9.3 explicit: 10 ** 10 ** 6 aborts before allocation ----
        [Fact]
        public void Plan93_TowerIntBeforeAllocation()
        {
            RunResult r = RunGuarded("x = 10 ** 10 ** 6\n");
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.BudgetExceeded, r.Error.Kind);
        }

        // ---- PLAN §9.3 explicit: mutual recursion ----
        [Fact]
        public void Plan93_MutualRecursion()
        {
            AssertBudget("def a(n):\n    return b(n + 1)\ndef b(n):\n    return a(n + 1)\na(0)\n");
        }
    }
}

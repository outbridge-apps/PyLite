using Outbridge.PyLite.Runtime.Errors;
using Xunit;

namespace Outbridge.PyLite.Tests.Evaluator
{
    // The frame FREELIST (RunFastBind): recursion hands each depth its own pooled frame, sibling
    // subtrees reuse unwound ones, and every reused frame behaves exactly like a fresh one —
    // isolated locals, wiped state, canonical unbound-local errors.
    [Trait("Category", "Compatibility")]
    public class FrameFreelistTests
    {
        // Each recursion depth gets an independent frame: the outer x must survive the nested call
        // that reused/acquired other frames.
        [Fact]
        public void RecursiveLocalsAreIsolatedPerDepth()
        {
            var r = Ev.Run(
                "def g(n):\n"
                + "    x = n\n"
                + "    if n > 0:\n"
                + "        y = g(n - 1)\n"
                + "    else:\n"
                + "        y = 0\n"
                + "    return x + y\n"
                + "r = g(10)\n");
            Assert.Equal("55", r.Repr("r"));
        }

        // A frame returned to the pool is wiped: a later call that leaves the local unassigned must
        // raise UnboundLocalError, not see the previous call's value.
        [Fact]
        public void StaleLocalsAreWipedAcrossPooledCalls()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(
                "def h(flag):\n"
                + "    if flag:\n"
                + "        v = 1\n"
                + "    return v\n"
                + "a = h(True)\n"
                + "b = h(False)\n"));
            Assert.Equal("UnboundLocalError", ex.Value.ExcType.Name);
        }

        // Depth beyond the pool cap (16): deeper levels allocate, the unwind refills the pool —
        // values stay exact through both regimes.
        [Fact]
        public void DeepRecursionBeyondPoolCapStaysCorrect()
        {
            var r = Ev.Run(
                "def s(n):\n"
                + "    if n == 0:\n"
                + "        return 0\n"
                + "    return n + s(n - 1)\n"
                + "r = s(200)\n",
                Outbridge.PyLite.Hosting.ResourceLimits.Default.With(maxCallDepth: 400));
            Assert.Equal("20100", r.Repr("r"));
        }

        // Tree recursion (the fib shape): sibling subtrees at the same depth reuse each other's
        // unwound frames — the hot path of the freelist.
        [Fact]
        public void TreeRecursionSiblingReuseIsCorrect()
        {
            var r = Ev.Run(
                "def fib(n):\n"
                + "    if n < 2:\n"
                + "        return n\n"
                + "    return fib(n - 1) + fib(n - 2)\n"
                + "r = (fib(15), fib(10))\n");
            Assert.Equal("(610, 55)", r.Repr("r"));
        }

        [Fact]
        public void MutualRecursionStaysCorrect()
        {
            var r = Ev.Run(
                "def even(n):\n"
                + "    if n == 0:\n"
                + "        return True\n"
                + "    return odd(n - 1)\n"
                + "def odd(n):\n"
                + "    if n == 0:\n"
                + "        return False\n"
                + "    return even(n - 1)\n"
                + "r = (even(100), odd(101))\n");
            Assert.Equal("(True, True)", r.Repr("r"));
        }

        // An exceptional unwind still releases frames through the finally; the same function keeps
        // computing correctly afterwards.
        [Fact]
        public void RaiseUnwindReleasesFramesCleanly()
        {
            var r = Ev.Run(
                "def f(n):\n"
                + "    if n == 0:\n"
                + "        raise ValueError('deep')\n"
                + "    return f(n - 1)\n"
                + "try:\n"
                + "    f(30)\n"
                + "except ValueError:\n"
                + "    pass\n"
                + "def fib(n):\n"
                + "    if n < 2:\n"
                + "        return n\n"
                + "    return fib(n - 1) + fib(n - 2)\n"
                + "r = fib(12)\n");
            Assert.Equal("144", r.Repr("r"));
        }

        // Functions with cell vars (closures) never pool — factories keep independent cells.
        [Fact]
        public void ClosureFactoriesKeepIndependentCells()
        {
            var r = Ev.Run(
                "def mk(n):\n"
                + "    def get():\n"
                + "        return n\n"
                + "    return get\n"
                + "g1 = mk(1)\n"
                + "g2 = mk(2)\n"
                + "r = (g1(), g2(), g1())\n");
            Assert.Equal("(1, 2, 1)", r.Repr("r"));
        }

        // Recursion is still bounded by MaxCallDepth (the freelist changes frames, not the guard).
        [Fact]
        public void RecursionStillBoundedByCallDepth()
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(
                "def f(n):\n    return f(n + 1)\nf(0)\n"));
            Assert.Equal(EngineAbortKind.Recursion, ex.Kind);
        }
    }

    // T2.4b: lambda bodies ride the closure-compiled expression form (cache on the shared
    // FunctionInfo). Oracle: a lambda and a def with the SAME body expression must agree exactly —
    // the def body's return goes through the statement tree-walk.
    [Trait("Category", "Compatibility")]
    public class CompiledLambdaBodyTests
    {
        private static void AssertLambdaEqualsDef(string body)
        {
            var res = Ev.Run(
                "f1 = lambda x: " + body + "\n"
                + "def f2(x):\n"
                + "    return " + body + "\n"
                + "a = [f1(i) for i in range(-30, 30)]\n"
                + "b = [f2(i) for i in range(-30, 30)]\n"
                + "r = repr(a) == repr(b)\n");
            Assert.Equal("True", res.Repr("r"));
        }

        [Theory]
        [InlineData("x * 2 + 1")]
        [InlineData("x % 2 == 0")]
        [InlineData("x * x - 3 * x + 7")]
        [InlineData("x if x > 0 else -x")]
        [InlineData("0 <= x < 20")]
        [InlineData("not x")]
        [InlineData("x > 0 and x % 3 == 1")]
        [InlineData("(x + 10 ** 30) - 10 ** 30")]   // big-int fallback inside the compiled body
        public void CompiledLambdaMatchesDef(string body)
        {
            AssertLambdaEqualsDef(body);
        }

        // Non-whitelisted bodies (calls, displays, starred params) fall back to the tree walk.
        [Fact]
        public void NonWhitelistedBodiesFallBack()
        {
            var r = Ev.Run(
                "f1 = lambda x: len(str(x))\n"
                + "f2 = lambda x: [x, x]\n"
                + "f3 = lambda *a: len(a)\n"
                + "r = (f1(1234), f2(7), f3(1, 2, 3))\n");
            Assert.Equal("(4, [7, 7], 3)", r.Repr("r"));
        }

        // Names beyond the parameter resolve through the same EvalName the tree walk uses:
        // module globals and enclosing-scope free variables.
        [Fact]
        public void GlobalAndFreeNamesInCompiledBody()
        {
            var r = Ev.Run(
                "k = 7\n"
                + "fg = lambda x: x + k\n"
                + "def mk(m):\n"
                + "    return lambda x: x + m\n"
                + "ff = mk(100)\n"
                + "k = 8\n"                        // the global read is live, not captured
                + "r = (fg(10), ff(10))\n");
            Assert.Equal("(18, 110)", r.Repr("r"));
        }

        [Fact]
        public void DefaultsAndTwoParams()
        {
            var r = Ev.Run(
                "f = lambda x, y=5: x + y\n"
                + "r = (f(1), f(1, 2))\n");
            Assert.Equal("(6, 3)", r.Repr("r"));
        }

        // Errors inside a compiled lambda body stay typed and still name <lambda> in the traceback.
        [Fact]
        public void ErrorsStayTypedWithLambdaFrame()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(
                "f = lambda x: x // (x - 3)\n"
                + "r = [f(i) for i in range(10)]\n"));
            Assert.Equal("ZeroDivisionError", ex.Value.ExcType.Name);
        }

        // Integration: the exact filter/map/sorted-key shapes the perf rows measure.
        [Fact]
        public void FilterMapSortKeyIntegration()
        {
            var r = Ev.Run(
                "lst = list(range(10))\n"
                + "a = list(filter(lambda x: x % 2 == 0, lst))\n"
                + "b = list(map(lambda x: x * 3, lst))\n"
                + "c = sorted(lst, key=lambda x: -x)\n"
                + "r = (a, b[:4], c[:4])\n");
            Assert.Equal("([0, 2, 4, 6, 8], [0, 3, 6, 9], [9, 8, 7, 6])", r.Repr("r"));
        }

        // The compiled cache lives on the shared FunctionInfo: the SAME CompiledScript reruns
        // correctly (later runs reuse the cached delegate with fresh globals).
        [Fact]
        public void CompiledLambdaSurvivesRerunOfTheSameCompiledScript()
        {
            var engine = new Outbridge.PyLite.Hosting.ScriptEngine(new Outbridge.PyLite.Hosting.EngineOptions());
            var compiled = engine.Compile(
                "r = sorted(range(20), key=lambda x: (x * 7) % 5)\n"
                + "result = repr((r[0], r[-1], len(r)))\n", "t24");
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
            Assert.Equal("(0, 17, 20)", first);
        }
    }
}

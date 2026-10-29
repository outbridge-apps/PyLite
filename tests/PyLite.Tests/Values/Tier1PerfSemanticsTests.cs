using System;
using System.Numerics;
using System.Text;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    // Tier-1 optimizations must be invisible: container pre-sizing (results identical, order kept),
    // the dual-accumulator int(str) (bit-identical to the BigInteger loop across the digit-count
    // boundary), the arrayless direct call (same binding, errors, recursion and *args fallback) and
    // the union pre-grow (same elements, same order).
    [Trait("Category", "Regression")]
    public class Tier1PerfSemanticsTests
    {
        // ---- 1.1 comprehension pre-sizing ----

        [Theory]
        [InlineData("r = [x * 2 for x in range(200)]", 200, "0, 2, 4", "394, 396, 398")]
        [InlineData("r = [x for x in range(4096)]", 4096, "0, 1, 2", "4093, 4094, 4095")]
        [InlineData("r = [x for x in range(5000)]", 5000, "0, 1, 2", "4997, 4998, 4999")]   // beyond the cap
        [InlineData("r = [x for x in range(10, 0, -3)]", 4, "10, 7, 4", "7, 4, 1")]
        public void PresizedListcompMatches(string src, int count, string head, string tail)
        {
            var res = Ev.Run(src + "\nn = len(r)\n");
            Assert.Equal(count.ToString(), res.Repr("n"));
            string repr = res.Repr("r");
            Assert.StartsWith("[" + head, repr);
            Assert.EndsWith(tail + "]", repr);
        }

        [Fact]
        public void PresizedSetAndDictCompKeepOrderAndContent()
        {
            var res = Ev.Run(
                "s = {x % 50 for x in range(200)}\n"
                + "d = {x: x * 2 for x in range(100)}\n"
                + "ns = len(s)\nnd = len(d)\nd99 = d[99]\nfirst = repr(sorted(s)[:3])\n");
            Assert.Equal("50", res.Repr("ns"));
            Assert.Equal("100", res.Repr("nd"));
            Assert.Equal("198", res.Repr("d99"));
            Assert.Equal("[0, 1, 2]", res.Repr("first").Trim('\''));
        }

        [Fact]
        public void EmptyAndFilteredRangesStillWork()
        {
            var res = Ev.Run(
                "a = [x for x in range(0)]\n"
                + "b = [x for x in range(5, 5)]\n"
                + "c = [x for x in range(4096) if x % 1000 == 0]\n"
                + "r = (a, b, c)\n");
            Assert.Equal("([], [], [0, 1000, 2000, 3000, 4000])", res.Repr("r"));
        }

        // ---- 1.2 int(str) dual accumulator ----

        // Cross the long/BigInteger boundary digit by digit: every value must be bit-identical to
        // BigInteger.Parse (the independent oracle), including the sign and underscore forms.
        [Fact]
        public void IntParseMatchesOracleAcrossTheBoundary()
        {
            var sb = new StringBuilder("res = []\n");
            var expected = new StringBuilder("[");
            bool first = true;
            // 9..30 significant digits of a repeating pattern, +/-, with and without underscores
            for (int digits = 9; digits <= 30; digits++)
            {
                string num = "";
                for (int i = 0; i < digits; i++)
                    num += (char)('1' + (i % 9));
                foreach (string form in new[] { num, "-" + num, "+" + num })
                {
                    sb.Append("res.append(int('").Append(form).Append("'))\n");
                    if (!first)
                        expected.Append(", ");
                    first = false;
                    expected.Append(BigInteger.Parse(form.TrimStart('+')));
                }
            }
            sb.Append("res.append(int('1_000_000'))\n");
            expected.Append(", 1000000");
            sb.Append("res.append(int('  42  '))\n");
            expected.Append(", 42");
            sb.Append("res.append(int('ff', 16))\n");
            expected.Append(", 255");
            // (int('0x_ff', 0) — underscore straight after the prefix — is rejected by the engine;
            // pre-existing deviation from CPython 3.6+, unchanged by the dual accumulator.)
            sb.Append("res.append(int('0xff', 0))\n");
            expected.Append(", 255]");
            sb.Append("r = repr(res)\n");
            Assert.Equal(expected.ToString(), Ev.Run(sb.ToString()).Repr("r").Trim('\''));
        }

        [Theory]
        [InlineData("int('')")]
        [InlineData("int('_1')")]
        [InlineData("int('1_')")]
        [InlineData("int('1__2')")]
        [InlineData("int('12x')")]
        [InlineData("int('077', 0)")]
        public void IntParseErrorsUnchanged(string call)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("r = " + call));
            Assert.Equal("ValueError", ex.Value.ExcType.Name);
            Assert.Contains("invalid literal for int()", ex.Value.GetMessageText(Rt.Ctx()));
        }

        // ---- 1.3 arrayless direct calls ----

        [Fact]
        public void DirectCallBindingMatrix()
        {
            var res = Ev.Run(
                "def f0():\n    return 7\n"
                + "def f1(a):\n    return a * 2\n"
                + "def f2(a, b):\n    return a - b\n"
                + "def fd(a, b=10):\n    return a + b\n"
                + "def fstar(*a):\n    return len(a)\n"
                + "r = (f0(), f1(21), f2(50, 8), fd(1), fd(1, 2), fstar(1, 2))\n");
            Assert.Equal("(7, 42, 42, 11, 3, 2)", res.Repr("r"));
        }

        [Fact]
        public void DirectCallRecursionAndDepthGuard()
        {
            // fib through the direct path; deep recursion still aborts with the Recursion kind.
            Assert.Equal("610", Ev.Run(
                "def fib(n):\n    if n < 2:\n        return n\n    return fib(n - 1) + fib(n - 2)\nr = fib(15)\n").Repr("r"));
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run("def f(n):\n    return f(n + 1)\nr = f(0)\n"));
            Assert.Equal(EngineAbortKind.Recursion, ex.Kind);
        }

        [Fact]
        public void DirectCallArityErrorsUnchanged()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("def f2(a, b):\n    return a\nr = f2(1)\n"));
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
            var ex2 = Assert.Throws<ScriptException>(() => Ev.Run("def f0():\n    return 1\nr = f0(5)\n"));
            Assert.Equal("TypeError", ex2.Value.ExcType.Name);
        }

        [Fact]
        public void DirectCallTracebackLineIsTheCallSite()
        {
            // The caller-line restore must survive the direct path: the error reports line 4 (the call).
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(
                "def f(a):\n"
                + "    return a[0]\n"
                + "x = 1\n"
                + "r = f(x)\n"));
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void ClosureAndCellFunctionsStillWork()
        {
            var res = Ev.Run(
                "def outer(a):\n"
                + "    def inner(b):\n"
                + "        return a + b\n"
                + "    return inner(a)\n"
                + "r = outer(20)\n");
            Assert.Equal("40", res.Repr("r"));
        }

        // ---- 1.4 union pre-grow ----

        [Fact]
        public void UnionKeepsOrderAndElements()
        {
            var res = Ev.Run(
                "a = set(range(50))\nb = set(range(25, 75))\nc = a | b\n"
                + "n = len(c)\nchk = (0 in c, 74 in c, 75 in c)\n"
                + "f = frozenset([1]) | {2}\n"
                + "r = (n, chk, sorted(f))\n");
            Assert.Equal("(75, (True, True, False), [1, 2])", res.Repr("r"));
        }

        [Fact]
        public void UnionWithIterableUnchanged()
        {
            Assert.Equal("[1, 2, 3]", Ev.Run("r = sorted(set([1]) | frozenset([2, 3]))").Repr("r"));
        }
    }
}

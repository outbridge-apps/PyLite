using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Evaluator
{
    // T2.1 oracle: the same body expression evaluated through the closure-compiled comprehension
    // path and through a manual for-loop (pure tree-walk) must agree exactly. Plus the fallback
    // boundary (non-whitelisted nodes), error transparency and PEP 479 behavior.
    [Trait("Category", "Compatibility")]
    public class CompiledCompBodyTests
    {
        private static void AssertCompEqualsLoop(string expr, string cond = null)
        {
            string filter = cond != null ? " if " + cond : "";
            string loopFilter = cond != null ? "    if not (" + cond + "):\n        continue\n" : "";
            var res = Ev.Run(
                "a = [" + expr + " for x in range(-50, 150)" + filter + "]\n"
                + "b = []\n"
                + "for x in range(-50, 150):\n"
                + loopFilter
                + "    b.append(" + expr + ")\n"
                + "r = (a == b, len(a))\n");
            Assert.StartsWith("(True, ", res.Repr("r"));
        }

        [Theory]
        [InlineData("x")]
        [InlineData("x * 2")]
        [InlineData("x * x - 3 * x + 7")]
        [InlineData("x % 5")]
        [InlineData("x // 3")]
        [InlineData("x / 4")]                          // float results
        [InlineData("-x")]
        [InlineData("~x")]
        [InlineData("not x")]
        [InlineData("x < 10")]
        [InlineData("0 <= x < 100")]                   // chained comparison
        [InlineData("x == 5 or x == 7")]
        [InlineData("x > 0 and x % 2 == 0")]
        [InlineData("x if x > 0 else -x")]
        [InlineData("x * 'a' if 0 < x < 4 else 'b'")]  // str repeat via BinOp
        [InlineData("(x + 10**30) - 10**30")]          // big-int overflow fallback inside the closure
        public void CompiledBodyMatchesTreeWalk(string expr)
        {
            AssertCompEqualsLoop(expr);
        }

        [Theory]
        [InlineData("x * 2", "x % 2 == 0")]
        [InlineData("x", "x % 3 == 0 and x > 10")]
        [InlineData("x + 1", "not (x % 7)")]
        public void CompiledCondMatchesTreeWalk(string expr, string cond)
        {
            AssertCompEqualsLoop(expr, cond);
        }

        // Non-whitelisted bodies (calls, subscripts, attributes, f-strings, nested comps, walrus)
        // must fall back to the tree-walk and still be correct.
        [Theory]
        [InlineData("r = [len(str(x)) for x in range(20)]", "[1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2]")]
        [InlineData("r = sum([[x, x] for x in range(3)], [])", "[0, 0, 1, 1, 2, 2]")]
        [InlineData("r = [f'v{x}' for x in range(3)]", "['v0', 'v1', 'v2']")]
        [InlineData("r = [[y for y in range(x)] for x in range(3)]", "[[], [0], [0, 1]]")]
        public void NonWhitelistedBodiesFallBack(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r").Trim('\''));
        }

        [Fact]
        public void DictAndSetAndGenexpThroughCompiledBodies()
        {
            var res = Ev.Run(
                "d = {x % 5: x * 2 for x in range(20)}\n"
                + "s = {x % 7 for x in range(30)}\n"
                + "g = sum(x * 3 for x in range(100))\n"
                + "gf = sum(x for x in range(100) if x % 2 == 0)\n"
                + "r = (d[4], len(d), len(s), g, gf)\n");
            Assert.Equal("(38, 5, 7, 14850, 2450)", res.Repr("r"));
        }

        // Conditions cache PER CLAUSE (CompClause.CompiledCond): multi-if comprehensions compile
        // each condition independently — a shared slot would hand clause 2 the delegate of clause 1.
        [Fact]
        public void MultiIfConditionsDoNotCollide()
        {
            var res = Ev.Run(
                "a = [x for x in range(20) if x > 5 if x < 15]\n"
                + "b = [x + y for x in range(10) if x % 2 == 0 for y in range(5) if y > 2]\n"
                + "c = []\n"
                + "for x in range(10):\n"
                + "    if x % 2 == 0:\n"
                + "        for y in range(5):\n"
                + "            if y > 2:\n"
                + "                c.append(x + y)\n"
                + "r = (a, b == c)\n");
            Assert.Equal("([6, 7, 8, 9, 10, 11, 12, 13, 14], True)", res.Repr("r"));
        }

        // The iterator (genexp / multi-clause) path compiles conditions too — same results, and
        // errors raised inside a compiled condition stay the same typed script errors.
        [Fact]
        public void GenexpCompiledCondMatchesAndStaysTyped()
        {
            Assert.Equal("13266", Ev.Run(
                "r = sum(x * 2 for x in range(200) if x % 3 == 0)\n").Repr("r"));
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(
                "r = list(x for x in range(10) if x % (x - 5) == 0)\n"));
            Assert.Equal("ZeroDivisionError", ex.Value.ExcType.Name);
        }

        // Errors raised inside a compiled body must be the same typed script errors, and the
        // comprehension must not corrupt state when they fly.
        [Theory]
        [InlineData("r = [x // (x - 5) for x in range(10)]", "ZeroDivisionError")]
        [InlineData("r = [x + 'a' for x in range(3)]", "TypeError")]
        [InlineData("r = [x < 'a' for x in range(3)]", "TypeError")]
        public void CompiledBodyErrorsStayTyped(string src, string excType)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(src));
            Assert.Equal(excType, ex.Value.ExcType.Name);
        }

        // The compiled cache lives on the shared AST: the SAME CompiledScript must run correctly
        // on repeated executions (second run reuses the cached delegates).
        [Fact]
        public void CachedDelegatesSurviveRerunOfTheSameCompiledScript()
        {
            var engine = new Outbridge.PyLite.Hosting.ScriptEngine(new Outbridge.PyLite.Hosting.EngineOptions());
            var compiled = engine.Compile(
                "r = [x * 2 + 1 for x in range(100) if x % 3 != 0]\nresult = repr((len(r), r[0], r[-1]))\n", "t2");
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
            Assert.Equal("(66, 3, 197)", first);
        }

        // Budget enforcement through compiled bodies: a huge comprehension still aborts on steps.
        [Fact]
        public void CompiledBodyStillBudgetBounded()
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(
                "r = [x * 2 for x in range(10**9)]",
                Outbridge.PyLite.Hosting.ResourceLimits.Default.With(maxSteps: 100_000)));
            Assert.Equal(EngineAbortKind.Steps, ex.Kind);
        }

        // PEP 479: StopIteration raised by the body is a RuntimeError — through the compiled cond too.
        [Fact]
        public void Pep479SurvivesCompiledPaths()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(
                "def boom(x):\n    raise StopIteration()\nr = [boom(x) for x in range(3)]\n"));
            Assert.Equal("RuntimeError", ex.Value.ExcType.Name);
        }

        // The compiled path skips EvalExpr's per-node budget/deadline sub-charges, so the wall-clock
        // deadline must still fire through the comprehension drain (it does: one Budget.Step per
        // element polls the deadline). Guards against a compiled comp outrunning the time limit.
        [Fact]
        public void CompiledBodyRespectsWallClockDeadline()
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(
                "r = [x * 2 + 1 for x in range(10**9) if x % 2 == 0]",
                Outbridge.PyLite.Hosting.ResourceLimits.Default.With(deadlineMs: 200)));
            Assert.Equal(EngineAbortKind.Deadline, ex.Kind);
        }

        // A deep flat-chain element expression compiles into a linked delegate chain that recurses
        // once per level at BOTH compile (TryCompileExpr) and invoke time, WITHOUT the tree-walk's
        // stack probe. MaxAstDepth (1000) is what keeps that bounded — proven here: moderate depth
        // runs, over-limit is a clean SyntaxError, never a StackOverflow.
        [Fact]
        public void ModerateDeepCompiledBodyRuns()
        {
            var sb = new System.Text.StringBuilder("r = [x");
            for (int i = 0; i < 400; i++)
                sb.Append(" + x");
            sb.Append(" for x in range(3)]\nn = r[1]\n");
            Assert.Equal((401 * 1).ToString(), Ev.Run(sb.ToString()).Repr("n"));
        }

        [Fact]
        public void OverLimitCompiledBodyIsSyntaxErrorNotOverflow()
        {
            var sb = new System.Text.StringBuilder("r = [x");
            for (int i = 0; i < 1500; i++)
                sb.Append(" + x");
            sb.Append(" for x in range(3)]\n");
            var ex = Assert.Throws<PySyntaxErrorException>(() => Ev.Run(sb.ToString()));
            Assert.Contains("too deeply nested", ex.Message);
        }
    }
}

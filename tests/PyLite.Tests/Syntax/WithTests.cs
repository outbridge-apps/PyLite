using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    // `with`, lowered by the parser into try/except/finally: what CPython's tests cannot say — no manager
    // can swallow a resource limit, the hidden names stay hidden, the nesting limits hold.
    [Trait("Category", "Compatibility")]
    public class WithTests
    {
        private const string Swallow =
            "class Swallow:\n    def __enter__(self):\n        return self\n    def __exit__(self, t, v, tb):\n        print('exit', t)\n        return True\n";

        private static RunResult RunOnEngine(string src, ResourceLimits limits = null, EngineOptions options = null)
        {
            var engine = new ScriptEngine(options ?? new EngineOptions { DefaultLimits = limits ?? ResourceLimits.Default });
            return engine.Run(src, new RunRequest { Limits = limits });
        }

        [Theory]
        [InlineData("while True:\n        pass", "MaxSteps")]
        [InlineData("x = []\n    while True:\n        x.append('y' * 1000)", "MaxAllocBytes")]
        public void ASwallowingExitCannotSwallowAResourceLimit(string body, string limit)
        {
            var lim = ResourceLimits.Default.With(maxSteps: 2_000_000, maxAllocBytes: 16 * 1024 * 1024);
            RunResult r = RunOnEngine(Swallow + "with Swallow():\n    " + body + "\nprint('after')\n", lim);
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.BudgetExceeded, r.Error.Kind);
            Assert.Equal(limit, r.Error.LimitName);
            Assert.DoesNotContain("after", r.PrintOutput ?? "");
            Assert.DoesNotContain("exit", r.PrintOutput ?? "");   // the manager is not called on the way out
        }

        [Fact]
        public void TheDeadlineEndsTheRunInsideAWith()
        {
            var lim = ResourceLimits.Default.With(deadlineMs: 300, maxSteps: 2_000_000_000);
            RunResult r = RunOnEngine(Swallow + "with Swallow():\n    while True:\n        pass\n", lim);
            Assert.Equal(ScriptErrorKind.BudgetExceeded, r.Error.Kind);
            Assert.Equal("DeadlineMs", r.Error.LimitName);
        }

        [Theory]
        [InlineData("def f():\n    with M():\n        return 1\nf()")]
        [InlineData("for i in range(1):\n    with M():\n        break")]
        [InlineData("for i in range(1):\n    with M():\n        continue")]
        [InlineData("with M():\n    pass")]
        [InlineData("try:\n    with M():\n        raise KeyError(1)\nexcept KeyError:\n    pass")]
        public void ExitRunsExactlyOnce(string body)
        {
            string src = "calls = []\nclass M:\n    def __enter__(self):\n        return self\n    def __exit__(self, *a):\n        calls.append(a[0])\n"
                + body + "\nr = len(calls)\n";
            Assert.Equal("1", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void AnExceptionInEnterDoesNotCallExit()
        {
            string src = "calls = []\nclass M:\n    def __enter__(self):\n        raise ValueError('e')\n    def __exit__(self, *a):\n        calls.append(1)\n"
                + "try:\n    with M():\n        calls.append(2)\nexcept ValueError:\n    pass\nr = calls\n";
            Assert.Equal("[]", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void AnExceptionInExitReplacesTheBodysAndChainsIt()
        {
            string src = "class M:\n    def __enter__(self):\n        return self\n    def __exit__(self, t, v, tb):\n        raise KeyError('from exit')\n"
                + "try:\n    with M():\n        raise ValueError('from body')\nexcept Exception as e:\n    r = (type(e).__name__, type(e.__context__).__name__, e.__context__.args)\n";
            Assert.Equal("('KeyError', 'ValueError', ('from body',))", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void ExitReceivesTheClassTheExceptionAndNone()
        {
            string src = "class E(LookupError):\n    pass\nclass M:\n    def __enter__(self):\n        return self\n    def __exit__(self, t, v, tb):\n"
                + "        global r\n        r = (t is E, issubclass(t, LookupError), isinstance(v, E), tb is None)\n        return True\n"
                + "with M():\n    raise E('x')\n";
            Assert.Equal("(True, True, True, True)", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void TypeOfAnExceptionIsItsClass()
        {
            string src = "class E(KeyError):\n    pass\ntry:\n    raise ValueError(1)\nexcept Exception as e:\n    a = (type(e) is ValueError, issubclass(type(e), Exception))\n"
                + "try:\n    raise E(1)\nexcept Exception as e:\n    b = (type(e) is E, issubclass(type(e), LookupError), type(e).__name__)\nr = (a, b)\n";
            Assert.Equal("((True, True), (True, True, 'E'))", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void AnErrorInTheBodyReportsItsOwnLine()
        {
            RunResult r = RunOnEngine("class M:\n    def __enter__(self):\n        return self\n    def __exit__(self, *a):\n        return False\nwith M():\n    x = 1\n    y = 1 // 0\n");
            Assert.Equal("ZeroDivisionError", r.Error.PythonType);
            Assert.Equal(8, r.Error.Line);
        }

        [Fact]
        public void HiddenNamesStayOutOfAPythonModulesAttributes()
        {
            var options = new EngineOptions
            {
                DefaultLimits = ResourceLimits.Default,
                PythonModules = new Dictionary<string, string>
                {
                    ["helpers"] = "import io\nwith io.StringIO('x') as f:\n    first = f.read()\n",
                },
            };
            var engine = new ScriptEngine(options);
            RunResult r2 = engine.Run("import helpers\nnames = ','.join(sorted(n for n in dir(helpers) if not n.startswith('__')))\nhidden = hasattr(helpers, '$with1_mgr')\n",
                new RunRequest { ScalarOutputNames = new[] { "names", "hidden" } });
            Assert.True(r2.Succeeded, r2.Error?.Message);
            Assert.Equal("f,first,io", r2.ScalarsOut["names"]);
            Assert.Equal("False", r2.ScalarsOut["hidden"]);
        }

        [Fact]
        public void AHostCannotAskForAHiddenName()
        {
            var engine = new ScriptEngine(new EngineOptions { DefaultLimits = ResourceLimits.Default });
            RunResult r = engine.Run("import io\nwith io.StringIO() as f:\n    pass\n", new RunRequest { ScalarOutputNames = new[] { "$with1_ok" } });
            Assert.False(r.Succeeded);
        }

        [Fact]
        public void TheHelpersSurviveAHostTableWithoutDefaults()
        {
            HostFunctionTable t = HostFunctionTable.CreateDefault();
            t.Remove("print");
            var engine = new ScriptEngine(new EngineOptions { DefaultLimits = ResourceLimits.Default });
            RunResult r = engine.Run("import io\nwith io.BytesIO(b'ab') as f:\n    v = f.read()\n",
                new RunRequest { Builtins = t, ScalarOutputNames = new[] { "v" } });
            Assert.True(r.Succeeded, r.Error?.Message);
            Assert.Equal("b'ab'", r.ScalarsOut["v"]);
        }

        private static string Nested(int depth)
        {
            var sb = new StringBuilder("class M:\n    def __enter__(self):\n        return 1\n    def __exit__(self, *a):\n        return False\nn = 0\n");
            for (int i = 0; i < depth; i++)
                sb.Append(new string(' ', i)).Append("with M() as v").Append(i).Append(":\n");
            sb.Append(new string(' ', depth)).Append("n = ").Append(depth).Append('\n');
            return sb.ToString();
        }

        [Fact]
        public void NestingUpToTheStatementLimitRuns()
        {
            Assert.Equal("63", Ev.Run(Nested(63)).Repr("n"));
        }

        [Fact]
        public void NestingPastTheStatementLimitIsASyntaxError()
        {
            RunResult r = RunOnEngine(Nested(64));
            Assert.Equal(ScriptErrorKind.SyntaxError, r.Error.Kind);
            Assert.Contains("too deeply nested", r.Error.Message);
        }

        [Fact]
        public void ItemsCountTowardsTheStatementLimit()
        {
            var items = new List<string>();
            for (int i = 0; i < 70; i++)
                items.Add("M()");
            RunResult r = RunOnEngine("class M:\n    def __enter__(self):\n        return 1\n    def __exit__(self, *a):\n        return False\nwith "
                + string.Join(", ", items) + ":\n    pass\n");
            Assert.Equal(ScriptErrorKind.SyntaxError, r.Error.Kind);
            Assert.Contains("too deeply nested", r.Error.Message);
            items.RemoveRange(0, 10);
            RunResult ok = RunOnEngine("class M:\n    def __enter__(self):\n        return 1\n    def __exit__(self, *a):\n        return False\nwith "
                + string.Join(", ", items) + ":\n    pass\n");
            Assert.True(ok.Succeeded, ok.Error?.Message);
        }

        [Fact]
        public void RecursionThroughAWithStopsAtTheCallDepth()
        {
            RunResult r = RunOnEngine("import io\ndef f(n):\n    with io.StringIO() as s:\n        return f(n + 1)\nf(0)\n");
            Assert.False(r.Succeeded);
            Assert.NotEqual(ScriptErrorKind.EngineFault, r.Error.Kind);
        }

        [Fact]
        public void OpenIsStillRefusedByName()
        {
            RunResult r = RunOnEngine("with open('x') as f:\n    pass\n");
            Assert.Equal("NameError", r.Error.PythonType);
        }
    }
}

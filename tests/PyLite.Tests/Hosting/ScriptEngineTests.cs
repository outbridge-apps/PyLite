using System;
using System.Collections.Generic;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // PKG-07 core (Compile/cache + synchronous Run pipeline) + RFC-001 §3 host prelude modules.
    [Trait("Category", "Sandbox")]
    public class ScriptEngineTests
    {
        private static ScriptEngine NewEngine()
        {
            return new ScriptEngine(new EngineOptions());
        }

        private static string Out(RunResult r, string name)
        {
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.ToString());
            return r.ScalarsOut[name];
        }

        // ---- core: scalar output ----

        [Fact]
        public void SimpleScalarOutput()
        {
            RunResult r = NewEngine().Run("x = 1 + 2\n",
                new RunRequest { ScalarOutputNames = new[] { "x" } });
            Assert.Equal("3", Out(r, "x"));
        }

        [Fact]
        public void MultipleOutputsAndContainerStr()
        {
            RunResult r = NewEngine().Run("a = 5\nb = [1, 2, 3]\n",
                new RunRequest { ScalarOutputNames = new[] { "a", "b" } });
            Assert.Equal("5", Out(r, "a"));
            Assert.Equal("[1, 2, 3]", r.ScalarsOut["b"]);   // str(list) == repr(list), R-4
        }

        [Fact]
        public void UnassignedDeclaredOutputIsAbsent()
        {
            RunResult r = NewEngine().Run("x = 1\n",
                new RunRequest { ScalarOutputNames = new[] { "x", "missing" } });
            Assert.True(r.Succeeded);
            Assert.True(r.ScalarsOut.ContainsKey("x"));
            Assert.False(r.ScalarsOut.ContainsKey("missing"));
        }

        [Fact]
        public void NonSerializableOutputIsHostError()
        {
            RunResult r = NewEngine().Run("def f():\n    return 1\n",
                new RunRequest { ScalarOutputNames = new[] { "f" } });
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Contains("not serializable", r.Error.Message);
        }

        // ---- core: scalar input coercion (R-5) ----

        [Theory]
        [InlineData("42", "r = n + 1\n", "43")]         // int
        [InlineData("007", "r = n + 1\n", "8")]         // leading zeros -> int
        [InlineData("-7", "r = n + 1\n", "-6")]         // signed int
        [InlineData("3.5", "r = n + 0.5\n", "4.0")]     // float
        [InlineData("true", "r = n\n", "True")]         // bool (any case)
        [InlineData("FALSE", "r = n\n", "False")]       // bool
        [InlineData("hello", "r = n + '!'\n", "hello!")]// str
        [InlineData("", "r = n + 'x'\n", "x")]          // empty -> str
        [InlineData("inf", "r = n + '?'\n", "inf?")]    // inf stays str
        public void ScalarInputCoercion(string raw, string body, string expected)
        {
            RunResult r = NewEngine().Run(body, new RunRequest
            {
                ScalarsIn = new Dictionary<string, string> { ["n"] = raw },
                ScalarOutputNames = new[] { "r" },
            });
            Assert.Equal(expected, Out(r, "r"));
        }

        [Fact]
        public void NullScalarInputIsNone()
        {
            RunResult r = NewEngine().Run("r = n is None\n", new RunRequest
            {
                ScalarsIn = new Dictionary<string, string> { ["n"] = null },
                ScalarOutputNames = new[] { "r" },
            });
            Assert.Equal("True", Out(r, "r"));
        }

        // ---- core: print buffer ----

        [Fact]
        public void PrintBuffered()
        {
            RunResult r = NewEngine().Run("print('hi')\nprint('there')\n", new RunRequest());
            Assert.True(r.Succeeded);
            Assert.Equal("hi\nthere\n", r.PrintOutput);
        }

        // ---- core: errors ----

        [Fact]
        public void SyntaxErrorFromRunDoesNotThrow()
        {
            RunResult r = NewEngine().Run("x = = 5\n", new RunRequest());
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.SyntaxError, r.Error.Kind);
        }

        [Fact]
        public void CompileThrowsSyntaxException()
        {
            var ex = Assert.Throws<PyLiteSyntaxException>(() => NewEngine().Compile("x = = 5\n", "t"));
            Assert.Equal(ScriptErrorKind.SyntaxError, ex.Error.Kind);
        }

        [Fact]
        public void TryCompileReturnsFalseOnSyntaxError()
        {
            var e = NewEngine();
            Outbridge.PyLite.Syntax.CompiledScript cs;
            ScriptError err;
            Assert.False(e.TryCompile("x = = 5\n", "t", out cs, out err));
            Assert.Null(cs);
            Assert.Equal(ScriptErrorKind.SyntaxError, err.Kind);
        }

        [Fact]
        public void TryCompileReturnsTrueOnValidSource()
        {
            var e = NewEngine();
            Outbridge.PyLite.Syntax.CompiledScript cs;
            ScriptError err;
            Assert.True(e.TryCompile("a = 1\n", "t", out cs, out err));
            Assert.NotNull(cs);
            Assert.Null(err);
        }

        [Fact]
        public void GetOrCompileHitsCache()
        {
            var e = NewEngine();
            Assert.Same(e.GetOrCompile("a = 1\n"), e.GetOrCompile("a = 1\n"));
        }

        [Fact]
        public void RuntimeErrorMapping()
        {
            RunResult r = NewEngine().Run("raise ValueError('boom')\n", new RunRequest());
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.RuntimeError, r.Error.Kind);
            Assert.Equal("ValueError", r.Error.PythonType);
            Assert.Contains("boom", r.Error.Message);
        }

        [Fact]
        public void BudgetExceededOnRunawayLoop()
        {
            RunResult r = NewEngine().Run("while True:\n    pass\n",
                new RunRequest { Limits = ResourceLimits.Default.With(maxSteps: 50_000) });
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.BudgetExceeded, r.Error.Kind);
            Assert.Equal("MaxSteps", r.Error.LimitName);
        }

        // ---- core: compile cache (R-14) ----

        [Fact]
        public void CompileCacheReturnsSameInstance()
        {
            ScriptEngine e = NewEngine();
            var c1 = e.Compile("a = 1\n", "t");
            var c2 = e.Compile("a = 1\n", "t");
            Assert.Same(c1, c2);
        }

        [Fact]
        public void CacheDisabledRecompiles()
        {
            var e = new ScriptEngine(new EngineOptions { CompiledScriptCacheEntries = 0 });
            var c1 = e.Compile("a = 1\n", "t");
            var c2 = e.Compile("a = 1\n", "t");
            Assert.NotSame(c1, c2);
        }

        // ---- core: built-in module registry ----

        [Fact]
        public void BuiltinModuleImportWorks()
        {
            RunResult r = NewEngine().Run(
                "import functools\nx = functools.reduce(lambda a, b: a + b, [1, 2, 3, 4], 0)\n",
                new RunRequest { ScalarOutputNames = new[] { "x" } });
            Assert.Equal("10", Out(r, "x"));
        }

        [Fact]
        public void StatsAreReported()
        {
            RunResult r = NewEngine().Run("import functools\nx = 1\n",
                new RunRequest { ScalarOutputNames = new[] { "x" } });
            Assert.True(r.Succeeded);
            Assert.True(r.Stats.Steps > 0);
            Assert.Equal(1, r.Stats.ModulesImported);
        }

        // RegexOps/RegexTime were documented but never filled before the single Snapshot (D19).
        [Fact]
        public void RegexStatsAreReported()
        {
            RunResult r = NewEngine().Run("import re\nx = len(re.findall('a', 'aaa')) + bool(re.match('b', 'b'))\n",
                new RunRequest { ScalarOutputNames = new[] { "x" } });
            Assert.Equal("4", Out(r, "x"));
            Assert.True(r.Stats.RegexOps >= 2);
            Assert.True(r.Stats.Elapsed > TimeSpan.Zero);
        }

        // ---- RFC-001 §3: host prelude modules ----

        private static EngineOptions WithPython(string name, string source, params string[] prelude)
        {
            return new EngineOptions
            {
                PythonModules = new Dictionary<string, string> { [name] = source },
                PreludeModules = prelude.Length > 0 ? prelude : null,
            };
        }

        // A prelude function calling back into the main script runs it on the main script's evaluator
        // (D20: the hooks were bound to whichever evaluator ran first, the prelude's).
        [Fact]
        public void PreludeCallbacksRunTheMainScript()
        {
            var e = new ScriptEngine(WithPython("lib",
                "def apply(f, x):\n    return f(x)\ndef keyed(xs, k):\n    return sorted(xs, key=k)\n", "lib"));
            RunResult r = e.Run("K = 3\ndef g(v):\n    return v * K\na = apply(g, 2)\nb = keyed([3, 1, 2], lambda v: -g(v))\n",
                new RunRequest { ScalarOutputNames = new[] { "a", "b" } });
            Assert.Equal("6", Out(r, "a"));
            Assert.Equal("[3, 2, 1]", r.ScalarsOut["b"]);
        }

        [Fact]
        public void OneEvaluatorPerProgramPerRun()
        {
            var r = Outbridge.PyLite.Tests.Evaluator.Ev.Run(
                "from string import Template\nr = sorted(['b', 'a'], key=lambda s: Template('$x').substitute(x=s))\n");
            Assert.Equal("['a', 'b']", r.Repr("r"));
            Assert.Equal(2, r.Ctx.Evaluators.Count);   // the script and string's Python source
            foreach (var kv in r.Ctx.Evaluators)
                Assert.Same(kv.Value, Outbridge.PyLite.Runtime.Evaluator.Evaluator.For(kv.Key, r.Ctx));
        }

        [Fact]
        public void PreludeNamesAreVisibleWithoutImport()
        {
            var e = new ScriptEngine(WithPython("lib",
                "def greet(name):\n    return 'hi ' + name\nVERSION = 7\n", "lib"));
            RunResult r = e.Run("a = greet('bob')\nb = VERSION\n",
                new RunRequest { ScalarOutputNames = new[] { "a", "b" } });
            Assert.Equal("hi bob", Out(r, "a"));
            Assert.Equal("7", r.ScalarsOut["b"]);
        }

        [Fact]
        public void PreludePrivateNamesAreHidden()
        {
            var e = new ScriptEngine(WithPython("lib", "PUB = 5\n_secret = 9\n", "lib"));
            Assert.Equal("5", Out(e.Run("a = PUB\n",
                new RunRequest { ScalarOutputNames = new[] { "a" } }), "a"));

            RunResult r = e.Run("a = _secret\n", new RunRequest { ScalarOutputNames = new[] { "a" } });
            Assert.False(r.Succeeded);
            Assert.Equal("NameError", r.Error.PythonType);
        }

        [Fact]
        public void ScriptShadowsPreludeName()
        {
            var e = new ScriptEngine(WithPython("lib", "X = 1\n", "lib"));
            RunResult r = e.Run("X = 99\nout = X\n",
                new RunRequest { ScalarOutputNames = new[] { "out" } });
            Assert.Equal("99", Out(r, "out"));
        }

        [Fact]
        public void PythonModuleImportExposesAllNames()
        {
            // Not a prelude: reached via `import`. Attribute access sees private names too (only `import *`
            // hides them).
            var e = new ScriptEngine(WithPython("helpers", "def foo():\n    return 42\n_v = 'p'\n"));
            RunResult r = e.Run("import helpers\na = helpers.foo()\nb = helpers._v\n",
                new RunRequest { ScalarOutputNames = new[] { "a", "b" } });
            Assert.Equal("42", Out(r, "a"));
            Assert.Equal("p", r.ScalarsOut["b"]);
        }

        [Fact]
        public void PreludeRecordClassIsCanonicalVocabulary()
        {
            var e = new ScriptEngine(WithPython("shapes",
                "class Point:\n" +
                "    def __init__(self, x, y):\n" +
                "        self.x = x\n" +
                "        self.y = y\n" +
                "    def mag2(self):\n" +
                "        return self.x * self.x + self.y * self.y\n", "shapes"));
            RunResult r = e.Run("p = Point(3, 4)\na = p.mag2()\nb = repr(p)\n",
                new RunRequest { ScalarOutputNames = new[] { "a", "b" } });
            Assert.Equal("25", Out(r, "a"));
            Assert.Equal("Point(x=3, y=4)", r.ScalarsOut["b"]);
        }

        [Fact]
        public void PerRunModuleStateIsIsolated()
        {
            var e = new ScriptEngine(WithPython("lib",
                "state = []\ndef push():\n    state.append(1)\n    return len(state)\n", "lib"));
            var req = new RunRequest { ScalarOutputNames = new[] { "out" } };
            RunResult r1 = e.Run("push()\nout = str(push())\n", req);
            RunResult r2 = e.Run("push()\nout = str(push())\n", req);
            Assert.Equal("2", Out(r1, "out"));
            Assert.Equal("2", Out(r2, "out"));   // run 2 gets a fresh module instance (PLAN §6)
        }

        [Fact]
        public void SyntaxErrorInPythonModuleFailsAtBuild()
        {
            var ex = Assert.Throws<ArgumentException>(() => new ScriptEngine(new EngineOptions
            {
                PythonModules = new Dictionary<string, string> { ["bad"] = "def \n" },
            }));
            Assert.Contains("bad", ex.Message);
        }

        [Fact]
        public void UnknownPreludeNameFailsAtBuild()
        {
            Assert.Throws<ArgumentException>(() => new ScriptEngine(new EngineOptions
            {
                PreludeModules = new[] { "nope" },
            }));
        }

        // ---- host C# module (IModuleProvider) as a prelude ----

        private sealed class ConstProvider : IModuleProvider
        {
            public string Name { get { return "consts"; } }

            public ModuleValue CreateInstance(EvalContext ctx)
            {
                var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
                m["ANSWER"] = ctx.Values.Int(42);
                return ctx.Values.Module("consts", m);
            }
        }

        [Fact]
        public void NativeProviderAsPrelude()
        {
            var e = new ScriptEngine(new EngineOptions
            {
                ExtraModules = new IModuleProvider[] { new ConstProvider() },
                PreludeModules = new[] { "consts" },
            });
            RunResult r = e.Run("a = ANSWER\n", new RunRequest { ScalarOutputNames = new[] { "a" } });
            Assert.Equal("42", Out(r, "a"));
        }

        // The server ceiling obeys the engine hard caps also when a request sets no limits of its own.
        [Fact]
        public void CeilingAboveHardCapIsClamped()
        {
            var e = new ScriptEngine(new EngineOptions { DefaultLimits = ResourceLimits.Default.With(maxCallDepth: 100_000) });
            RunResult r = e.Run("def f(n):\n    return f(n + 1)\nf(0)\n", null);
            Assert.Equal("MaxCallDepth", r.Error.LimitName);
            Assert.Equal(512, r.Stats.PeakCallDepth);
        }

        [Fact]
        public void SleepIsCountedInStats()
        {
            RunResult r = NewEngine().Run("import time\ntime.sleep(0.03)\ntime.sleep(0.02)\n", null);
            Assert.True(r.Succeeded);
            Assert.Equal(50L, r.Stats.SleepMs);
        }
    }
}

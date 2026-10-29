using System;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // PKG-07 T-HST-10: the sanctioned host-function way to raise a script-catchable error.
    [Trait("Category", "Sandbox")]
    public class ScriptExceptionsTests
    {
        [Fact]
        public void PyErrorBuildsScriptException()
        {
            var se = Assert.IsType<ScriptException>(ScriptExceptions.PyError("ValueError", "x"));
            Assert.Equal("ValueError", se.Value.ExcType.Name);
        }

        [Fact]
        public void TypeErrorShortcut()
        {
            var se = (ScriptException)ScriptExceptions.TypeError("t");
            Assert.Equal("TypeError", se.Value.ExcType.Name);
        }

        [Fact]
        public void KeyErrorMessageIsRepr()
        {
            var se = (ScriptException)ScriptExceptions.KeyError("k");
            Assert.Equal("'k'", se.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]
        public void ForgedBudgetTypeIsRejected()
        {
            Assert.Throws<PyLiteEngineException>(() => ScriptExceptions.PyError("EngineAbort", "x"));
        }

        [Fact]
        public void UnknownTypeIsRejected()
        {
            Assert.Throws<PyLiteEngineException>(() => ScriptExceptions.PyError("NoSuchError", "x"));
        }

        [Fact]
        public void NullTypeThrows()
        {
            Assert.Throws<ArgumentNullException>(() => ScriptExceptions.PyError(null, "x"));
        }

        // ---- through a run: a host builtin raising a script-catchable error ----

        private static HostFunctionTable WithBoom()
        {
            var t = HostFunctionTable.CreateDefault();
            t.Set("boom", (self, args, kw, c) => throw ScriptExceptions.ValueError("host says no"));
            return t;
        }

        [Fact]
        public void ScriptCatchesHostRaisedError()
        {
            var e = new ScriptEngine(new EngineOptions());
            RunResult r = e.Run("caught = 0\ntry:\n    boom()\nexcept ValueError:\n    caught = 1\n",
                new RunRequest { Builtins = WithBoom(), ScalarOutputNames = new[] { "caught" } });
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.ToString());
            Assert.Equal("1", r.ScalarsOut["caught"]);
        }

        [Fact]
        public void UncaughtHostErrorIsRuntimeErrorNotHostError()
        {
            var e = new ScriptEngine(new EngineOptions());
            RunResult r = e.Run("boom()\n", new RunRequest { Builtins = WithBoom() });
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.RuntimeError, r.Error.Kind);   // NOT HostError
            Assert.Equal("ValueError", r.Error.PythonType);
            Assert.Contains("host says no", r.Error.Message);
        }

        // R-10: an ARBITRARY C# exception from a host override becomes HostError (fenced by HostCallGuard),
        // not EngineFault. (ScriptExceptions.* is the escape hatch for a script-catchable error — above.)
        [Fact]
        public void ArbitraryHostExceptionIsHostError()
        {
            var t = HostFunctionTable.CreateDefault();
            t.Set("crash", (self, args, kw, c) => throw new InvalidOperationException("kaboom"));
            var e = new ScriptEngine(new EngineOptions());
            RunResult r = e.Run("crash()\n", new RunRequest { Builtins = t });
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Contains("host function 'crash' failed", r.Error.Message);
            Assert.Contains("kaboom", r.Error.Message);
        }
    }
}

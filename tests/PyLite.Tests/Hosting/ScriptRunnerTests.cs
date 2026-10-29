using System;
using System.Collections.Generic;
using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // PKG-07 T-HST-09: the flat host facade (ScriptRunner). Uses a fresh engine per runner via the internal ctor so tests are
    // independent of the process-wide shared engine.
    [Trait("Category", "Sandbox")]
    public class ScriptRunnerTests
    {
        private static ScriptRunner NewRunner()
        {
            // internal ctor: same assembly (InternalsVisibleTo test project) — a dedicated engine per test.
            return new ScriptRunner(new ScriptEngine(new EngineOptions()));
        }

        [Fact]
        public void HappyPath()
        {
            var r = NewRunner();
            r.SetScalar("a", "41");
            r.DeclareScalarOutput("b");
            Assert.Equal(0, r.Run("b = a + 1\n"));
            Assert.Equal("42", r.GetScalar("b"));
        }

        [Fact]
        public void SyntaxErrorCode1()
        {
            var r = NewRunner();
            Assert.Equal(1, r.Run("x =\n"));
            Assert.Equal("SyntaxError", r.GetErrorType());
            Assert.True(r.GetErrorLine() > 0);
        }

        [Fact]
        public void RuntimeErrorCode2()
        {
            var r = NewRunner();
            Assert.Equal(2, r.Run("raise KeyError('k')\n"));
            Assert.Equal("KeyError", r.GetErrorType());
            Assert.Contains("k", r.GetErrorMessage());
        }

        [Fact]
        public void DeadlineBudgetCode3()
        {
            var r = NewRunner();
            r.SetLimitTimeoutMs(200);
            Assert.Equal(3, r.Run("while True:\n    pass\n"));
            Assert.Equal("DeadlineMs", r.GetErrorLimit());
        }

        [Fact]
        public void NonSerializableOutputCode4()
        {
            var r = NewRunner();
            r.DeclareScalarOutput("f");
            Assert.Equal(4, r.Run("f = lambda: 1\n"));
            Assert.Equal("HostError", r.GetErrorType());
        }

        [Fact]
        public void GetScalarBeforeRunIsNull()
        {
            Assert.Null(NewRunner().GetScalar("x"));
        }

        [Fact]
        public void PrintCallbackReceivesLines()
        {
            var got = new List<string>();
            var r = NewRunner();
            r.SetPrintCallback(got.Add);
            Assert.Equal(0, r.Run("print('hi')\n"));
            Assert.Equal(new[] { "hi" }, got);
            Assert.Null(r.GetPrintOutput());   // callback mode
        }

        [Fact]
        public void PrintBufferedWhenNoCallback()
        {
            var r = NewRunner();
            Assert.Equal(0, r.Run("print('hi')\n"));
            Assert.Equal("hi\n", r.GetPrintOutput());
        }

        [Fact]
        public void ConfigSurvivesAcrossRunsResultDoesNot()
        {
            var r = NewRunner();
            r.SetScalar("a", "10");
            r.DeclareScalarOutput("b");
            Assert.Equal(0, r.Run("b = a + 1\n"));
            Assert.Equal("11", r.GetScalar("b"));
            // second source, same config
            Assert.Equal(0, r.Run("b = a * 2\n"));
            Assert.Equal("20", r.GetScalar("b"));
        }

        [Fact]
        public void ResetClearsInputAndResult()
        {
            var r = NewRunner();
            r.SetScalar("a", "1");
            r.DeclareScalarOutput("b");
            r.Run("b = a\n");
            r.Reset();
            Assert.Null(r.GetScalar("b"));
            // after reset, 'a' is gone -> NameError
            Assert.Equal(2, r.Run("b = a\n"));
        }

        [Fact]
        public void InvalidLimitThrowsImmediately()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => NewRunner().SetLimitTimeoutMs(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => NewRunner().SetLimitMemoryBytes(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => NewRunner().SetLimitRecursion(0));
        }

        [Fact]
        public void MemoryLimitBudgetCode3()
        {
            var r = NewRunner();
            r.SetLimitMemoryBytes(4096);
            Assert.Equal(3, r.Run("x = [0] * 1000000\n"));
            Assert.Equal(ScriptErrorKind.BudgetExceeded, (ScriptErrorKind)r.GetErrorCode());
        }

        [Fact]
        public void StatsAfterSuccess()
        {
            var r = NewRunner();
            r.DeclareScalarOutput("x");
            Assert.Equal(0, r.Run("x = sum(range(100))\n"));
            Assert.True(r.GetStepCount() > 0);
            Assert.True(r.GetElapsedMs() >= 0);
        }

        [Fact]
        public void RecursionLimitDownwardOnly()
        {
            var r = NewRunner();
            r.SetLimitRecursion(10);
            // deep recursion trips the lowered call-depth budget
            Assert.Equal(3, r.Run("def f(n):\n    return f(n + 1)\nf(0)\n"));
            Assert.Equal("MaxCallDepth", r.GetErrorLimit());
        }
    }
}

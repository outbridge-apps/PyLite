using System.Text;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // What stands between a run and the process: the free memory check before the thread, the probe in the clause walk.
    // Thread.Start failing is checked by the burst stand under a job cap: the suite has no way to make it fail here.
    [Trait("Category", "Sandbox")]
    public class FreeMemoryTests
    {
        [Fact]
        public void BelowWhatARunMayNeedItIsRefusedAndNothingOfItRuns()
        {
            var engine = new ScriptEngine(new EngineOptions { FreeCommitReserveBytes = 1L << 62 });
            RunResult r = engine.Run("print('ran')", new RunRequest());
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Equal("FreeMemory", r.Error.LimitName);
            Assert.Equal("", r.PrintOutput);
        }

        // Round 4 review: a compile thread is a 32 MB stack too, and passes the same check (it started unchecked).
        [Fact]
        public void BelowWhatACompileMayNeedItIsRefused()
        {
            var engine = new ScriptEngine(new EngineOptions { FreeCommitReserveBytes = 1L << 62 });
            CompiledScript compiled;
            ScriptError error;
            Assert.False(engine.TryCompile("x = 1\n", "s", out compiled, out error));
            Assert.Equal(ScriptErrorKind.HostError, error.Kind);
            Assert.Equal("FreeMemory", error.LimitName);
            Assert.True(new ScriptEngine(new EngineOptions()).TryCompile("x = 1\n", "s", out compiled, out error));
        }

        [Fact]
        public void WithMemoryToSpareTheRunStarts()
        {
            RunResult r = new ScriptEngine(new EngineOptions()).Run("print('ran')", new RunRequest());
            Assert.Null(r.Error);
            Assert.Equal("ran\n", r.PrintOutput);
        }

        [Fact]
        public void TheReserveIsTwoHundredMegabytes()
        {
            Assert.Equal(200L << 20, new EngineOptions().FreeCommitReserveBytes);
        }

        [Fact]
        public void TheCheckReadsWhatWindowsReports()
        {
            Assert.False(FreeCommit.IsBelow(1));
            Assert.True(FreeCommit.IsBelow(long.MaxValue));
        }

        // Past what the source limit lets a script write: without the probe this kills the test process.
        [Fact]
        public void ClausesPastTheStackEndTheRunNotTheProcess()
        {
            var source = new StringBuilder("r = [0 for x in range(2)");
            for (int i = 0; i < 500000; i++)
            {
                source.Append(" if 1");
            }
            source.Append("]\n");
            var limits = new SyntaxLimits { MaxSourceChars = source.Length + 1, MaxTokens = 2000000 };
            CompiledScript compiled = Compiler.Compile(source.ToString(), "<clauses>", limits);
            // Unwinding this deep took 39 s on a busy box, past the point where a run counts as left behind.
            var options = new EngineOptions { DefaultLimits = ResourceLimits.Default.With(deadlineMs: 300000) };
            RunResult r = new ScriptEngine(options).Run(compiled, new RunRequest());
            Assert.Equal("CSharpStack", r.Error.LimitName);
            Assert.Equal(ScriptErrorKind.BudgetExceeded, r.Error.Kind);
        }
    }
}

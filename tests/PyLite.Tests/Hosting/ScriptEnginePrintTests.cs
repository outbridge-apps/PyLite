using System;
using System.Collections.Generic;
using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // PKG-07 phase 2: print sink routing via OutputSink (R-9) — line-boundary flushing, buffered vs callback,
    // callback-failure mapping, and print preservation on error.
    [Trait("Category", "Sandbox")]
    public class ScriptEnginePrintTests
    {
        private static ScriptEngine NewEngine()
        {
            return new ScriptEngine(new EngineOptions());
        }

        [Fact]
        public void PrintSinkReceivesLinesWithoutNewline()
        {
            var lines = new List<string>();
            RunResult r = NewEngine().Run("print('a')\nprint('b')\n",
                new RunRequest { PrintSink = lines.Add });
            Assert.True(r.Succeeded);
            Assert.Equal(new[] { "a", "b" }, lines);
            Assert.Null(r.PrintOutput);   // callback mode: the host already got the lines
        }

        [Fact]
        public void PrintSinkAssemblesPartialLinesAcrossCalls()
        {
            var lines = new List<string>();
            RunResult r = NewEngine().Run("print('x', end='')\nprint('y')\n",
                new RunRequest { PrintSink = lines.Add });
            Assert.True(r.Succeeded);
            Assert.Equal(new[] { "xy" }, lines);
        }

        [Fact]
        public void PrintSinkFlushesTrailingPartialLine()
        {
            var lines = new List<string>();
            RunResult r = NewEngine().Run("print('tail', end='')\n",
                new RunRequest { PrintSink = lines.Add });
            Assert.True(r.Succeeded);
            Assert.Equal(new[] { "tail" }, lines);   // FlushFinal emits the newline-less remainder
        }

        [Fact]
        public void ThrowingPrintSinkIsHostError()
        {
            RunResult r = NewEngine().Run("print('boom')\n", new RunRequest
            {
                PrintSink = s => throw new InvalidOperationException("nope"),
            });
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Contains("host print callback failed", r.Error.Message);
            Assert.Contains("nope", r.Error.Message);
        }

        [Fact]
        public void BufferedPrintPreservedOnRuntimeError()
        {
            RunResult r = NewEngine().Run("print('before')\nraise ValueError('x')\n", new RunRequest());
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.RuntimeError, r.Error.Kind);
            Assert.Equal("before\n", r.PrintOutput);   // R-16: print output preserved on error
        }

        [Fact]
        public void PrintSinkReceivesLinesEmittedBeforeAnError()
        {
            var lines = new List<string>();
            RunResult r = NewEngine().Run("print('line1')\nraise ValueError('x')\n",
                new RunRequest { PrintSink = lines.Add });
            Assert.False(r.Succeeded);
            Assert.Equal(new[] { "line1" }, lines);   // the complete line was already flushed to the sink
        }
    }
}

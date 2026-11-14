using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // Review 6, stage 3: the caller's stack (e1-01, e1-02) and the run thread's wait (p1-01).
    [Trait("Category", "Sandbox")]
    public class Review6HostTests
    {
        // On .NET Framework the parser's probe refuses once half of a thread's reserve is used, so a host that deep got
        // SyntaxError for print(1). A cache miss now compiles off the caller's thread.
        [Fact]
        public void AHostPastHalfItsStackStillCompiles()
        {
            var engine = new ScriptEngine(new EngineOptions());
            ScriptError tryError = null;
            RunResult run = null;
            string compiled = null;
            Exception fault = null;
            var thread = new Thread(() => AtTheProbeLine(() =>
            {
                try
                {
                    CompiledScript c;
                    engine.TryCompile("print(1)\n", "<script>", out c, out tryError);
                    compiled = engine.Compile("y = (((1)))\n").ScriptName;
                    run = engine.Run("x = 2\n", new RunRequest { ScalarOutputNames = new[] { "x" } });
                }
                catch (Exception ex)
                {
                    fault = ex;
                }
            }), 512 * 1024);
            thread.Start();
            thread.Join();
            Assert.Null(fault);
            Assert.Null(tryError);
            Assert.Equal("<script>", compiled);
            Assert.Equal("2", run.ScalarsOut["x"]);
        }

        [Fact]
        public void ASyntaxErrorFromTryCompileIsTheSameAtAnyDepth()
        {
            var engine = new ScriptEngine(new EngineOptions { CompiledScriptCacheEntries = 0 });
            CompiledScript c;
            ScriptError shallow;
            engine.TryCompile("x = (\n", "<script>", out c, out shallow);
            ScriptError deep = null;
            var thread = new Thread(() => AtTheProbeLine(() => engine.TryCompile("x = (\n", "<script>", out c, out deep)), 512 * 1024);
            thread.Start();
            thread.Join();
            Assert.Equal(ScriptErrorKind.SyntaxError, deep.Kind);
            Assert.Equal(shallow.Message, deep.Message);
            Assert.Equal(shallow.Line, deep.Line);
        }

        // Recurses until the stack probe refuses (half of the reserve used), then calls work at that depth.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int AtTheProbeLine(Action work)
        {
            try
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();
            }
            catch (InsufficientExecutionStackException)
            {
                work();
                return 0;
            }
            return AtTheProbeLine(work) + 1;   // not a tail call: each level keeps its frame
        }

        // e1-06: Wait(TimeSpan) truncated to whole milliseconds, so the last fraction of each sleep spun a step a turn
        // (200 sleeps of 0.5 ms cost 1-1.6M steps, a different count each run). One step per quantum now.
        [Fact]
        public void ShortSleepsCostTheSameFewSteps()
        {
            var engine = new ScriptEngine(new EngineOptions());
            const string src = "import time\nfor _ in range(200):\n    time.sleep(0.0005)\n";
            RunResult a = engine.Run(src, new RunRequest { Limits = TestEngines.Unhurried });
            RunResult b = engine.Run(src, new RunRequest { Limits = TestEngines.Unhurried });
            Assert.True(a.Succeeded && b.Succeeded);
            Assert.Equal(a.Stats.Steps, b.Stats.Steps);
            Assert.True(a.Stats.Steps < 2000, a.Stats.Steps.ToString());
        }

        // e1-07: memory the process could not get is a shortage the host retries, not an engine fault; the exception still
        // reaches the EngineFault event, where an engine allocation the budget missed would show.
        [Fact]
        public void OutOfMemoryIsAShortageNotAnEngineFault()
        {
            ScriptError e = ScriptErrorBuilder.Build(new OutOfMemoryException(), null);
            Assert.Equal(ScriptErrorKind.HostError, e.Kind);
            Assert.Equal("FreeMemory", e.LimitName);
            var thread = new RunnerThread(() => { throw new OutOfMemoryException(); }, () => null);
            thread.Start();
            Assert.True(thread.WaitCompleted(10000));
            Assert.Equal(ScriptErrorKind.HostError, thread.TakeResult().Error.Kind);
            Assert.IsType<OutOfMemoryException>(thread.EngineFault);
        }

        // A method or property a host builds with SlotDescriptor is host code: fenced like a host function, and a hang in
        // it is a leak on time. The engine's own descriptors are not.
        [Fact]
        public void AHostBuiltMethodIsFencedAsHostCode()
        {
            Assert.True(SlotDescriptor.MakeMethod("m", (s, a, k, c) => s).IsHostProvided);
            Assert.False(Rt.Ctx().Values.Dict(1).TypeInfo.Slots["get"].IsHostProvided);

            var table = HostFunctionTable.CreateDefault();
            table.Set("make", (s, a, kw, c) => c.Values.BoundMethod(c.Values.Int(1),
                SlotDescriptor.MakeMethod("boom", (self, args, k, cx) => { throw new InvalidOperationException("no"); })));
            RunResult r = new ScriptEngine(new EngineOptions()).Run("make()()\n", new RunRequest { Builtins = table });
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Contains("'boom'", r.Error.Message);
        }

        // A ready thread waits about one time slice per runnable thread on its core: the window grows with the runs per
        // core, and stays 5 s under ordinary load.
        [Theory]
        [InlineData(0, 8, 5000)]
        [InlineData(80, 8, 5000)]
        [InlineData(180, 8, 11500)]
        [InlineData(180, 1, 90000)]
        [InlineData(100000, 1, 120000)]
        public void TheStarvationWindowGrowsWithTheRunsPerCore(int live, int cores, int expected)
        {
            Assert.Equal(expected, ScriptEngine.StarvationWindowMs(live, cores));
        }
    }

    // p1-03: the free-memory check reads a figure that does not yet show stacks admitted and not started, so callers
    // arriving together all passed it. Alone in its pass: other tests' runs move both the figure and the counter.
    [Collection("HeapSensitive")]
    [Trait("Category", "Sandbox")]
    public class AdmittedStacksTests
    {
        [Fact]
        public void StacksAdmittedTogetherAreCountedTogether()
        {
            const long stack = 32L << 20;
            long free = FreeCommit.AvailableBytes();
            long maxAlloc = ResourceLimits.Default.MaxAllocBytes;
            long reserve = free - maxAlloc - stack * 5 / 2;   // room for about two run stacks
            if (free < 0 || reserve <= 0)
                return;   // Windows did not answer, or the box is already short
            var engine = new ScriptEngine(new EngineOptions { FreeCommitReserveBytes = reserve });
            const int callers = 24;
            var gate = new Barrier(callers);
            RunResult[] results = Enumerable.Range(0, callers).Select(_ => Task.Factory.StartNew(() =>
            {
                gate.SignalAndWait();
                return engine.Run("import time\ntime.sleep(1)\n", new RunRequest());
            }, TaskCreationOptions.LongRunning)).ToArray().Select(t => t.Result).ToArray();
            int admitted = results.Count(r => r.Error == null || r.Error.LimitName != "FreeMemory");
            Assert.True(admitted <= 6, "admitted " + admitted + " of " + callers);
            Assert.Equal(0, ScriptEngine.UnstartedStackBytes);
        }

        // Round 3 review: a run that compiles holds its syntax tree outside MaxAllocBytes, so the check counts it; a
        // compiled (or cached) script compiles nothing. Checked on the figure: the free memory itself moves too much
        // under a parallel suite (the GC keeps the heap a compile grew) for a run to show a 64 MB margin.
        [Fact]
        public void TheFreeMemoryCheckCountsTheCompileOfAMiss()
        {
            CompiledScript compiled = new ScriptEngine(new EngineOptions()).Compile("x = 1\n");
            Assert.Equal(0, ScriptEngine.CompileBytes(compiled, null));
            Assert.Equal(6 * 128, ScriptEngine.CompileBytes(null, "x = 1\n"));
            Assert.Equal(524288L * 128, ScriptEngine.CompileBytes(null, new string('x', 2000000)));   // the lexer refuses it at once
        }
    }
}

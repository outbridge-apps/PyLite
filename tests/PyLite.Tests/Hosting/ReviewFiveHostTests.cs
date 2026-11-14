using System;
using System.Threading;
using System.Threading.Tasks;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // Review 5: what the host sees of a starved run, of an abort it reset itself, and of the breaker's refusal.
    [Trait("Category", "Sandbox")]
    public class ReviewFiveHostTests
    {
        private static ResourceLimits Short()
        {
            return ResourceLimits.Default.With(deadlineMs: 50);
        }

        // No clock yet: the runner has not got the CPU to start the run, and past deadline+grace+slack it is not hung.
        [Fact]
        public void ARunWhoseClockHasNotStartedIsNotLeaked()
        {
            var thread = new RunnerThread(() =>
            {
                Thread.Sleep(6000);
                return RunResult.Failure(new ScriptError(ScriptErrorKind.HostError, null, "late", 0, 0, null, null), null, new RunStatistics());
            }, () => null);
            thread.Start();
            Assert.True(ScriptEngine.WaitForRun(thread, new EvalContext[1], Short()));
        }

        // A host call that does not return: the same time to a leak as before the wait knew about starvation.
        [Fact]
        public void AHungHostCallIsStillLeakedOnTime()
        {
            var release = new ManualResetEventSlim(false);
            var table = HostFunctionTable.CreateDefault();
            var entered = new ManualResetEventSlim(false);
            table.Set("block", (s, a, kw, c) =>
            {
                entered.Set();
                release.Wait();
                return c.Values.None;
            });
            var engine = new ScriptEngine(new EngineOptions());
            var started = System.Diagnostics.Stopwatch.StartNew();
            RunResult r = engine.Run("block()\n", new RunRequest { Builtins = table, Limits = ResourceLimits.Default.With(deadlineMs: 100) });
            long ms = started.ElapsedMilliseconds;
            release.Set();
            Assert.True(entered.IsSet);
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Contains("leaked", r.Error.Message);
            Assert.InRange(ms, 4000, 20000);   // the low bound is the point; a loaded box took 13 s
        }

        private static RunResult RunHost(Action host)
        {
            var table = HostFunctionTable.CreateDefault();
            table.Set("host", (s, a, kw, c) =>
            {
                host();
                return c.Values.None;
            });
            return new ScriptEngine(new EngineOptions()).Run("host()\n", new RunRequest { Builtins = table });
        }

        [Fact]
        public void AnAbortTheHostResetItselfIsAHostError()
        {
            RunResult r = RunHost(() =>
            {
                try
                {
                    Thread.CurrentThread.Abort();
                }
                catch (ThreadAbortException)
                {
                    Thread.ResetAbort();
                    throw;
                }
            });
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
        }

        [Fact]
        public void AnAbortFromAnotherThreadsTaskIsAHostError()
        {
            RunResult r = RunHost(() => Task.Run(() =>
            {
                try
                {
                    Thread.CurrentThread.Abort();
                }
                catch (ThreadAbortException)
                {
                    Thread.ResetAbort();
                    throw;
                }
            }).GetAwaiter().GetResult());
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
        }

        [Fact]
        public void TheBreakersRefusalKeepsItsName()
        {
            ScriptError e = LeakBreaker.Refusal();
            Assert.Equal(ScriptErrorKind.HostError, e.Kind);
            Assert.Equal("CircuitBreaker", e.LimitName);
        }

        [Fact]
        public void AFrozenClockRefusesALocalInstant()
        {
            Assert.Throws<ArgumentException>(() => new FrozenClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local), TimeSpan.Zero, null));
            Assert.Throws<ArgumentException>(() => new FrozenClock(new DateTime(2026, 1, 1), TimeSpan.Zero, null));
        }
    }
}

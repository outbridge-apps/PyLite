using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // The engine does not limit how many runs execute at once: Run is synchronous, so the host's own threads bound
    // it. The only refusal left is the leaked-thread breaker.
    [Trait("Category", "Sandbox")]
    public class UnlimitedRunsTests
    {
        private sealed class Blocker
        {
            public readonly ManualResetEventSlim Release = new ManualResetEventSlim(false);
            public readonly HostFunctionTable Table = HostFunctionTable.CreateDefault();
            public int Entered;

            public Blocker()
            {
                Table.Set("block", (s, a, kw, c) =>
                {
                    Interlocked.Increment(ref Entered);
                    Release.Wait();
                    return c.Values.None;
                });
            }
        }

        // 64 callers run side by side: more than the 32 the hard cap once allowed, none refused, none waiting.
        [Fact]
        public void EveryCallerRunsAtOnce()
        {
            var b = new Blocker();
            // The run's own deadline is not the point here, and 3 s can pass before a loaded box reaches block().
            var engine = new ScriptEngine(new EngineOptions { DefaultLimits = ResourceLimits.Default.With(deadlineMs: 60_000) });
            var results = new ConcurrentBag<RunResult>();
            var threads = Enumerable.Range(0, 64).Select(_ => new Thread(() =>
                results.Add(engine.Run("block()\n", new RunRequest { Builtins = b.Table }))) { IsBackground = true }).ToList();
            threads.ForEach(t => t.Start());
            var sw = Stopwatch.StartNew();
            while (Volatile.Read(ref b.Entered) < 64 && sw.ElapsedMilliseconds < 15_000)
                Thread.Sleep(5);
            int entered = Volatile.Read(ref b.Entered);
            b.Release.Set();
            threads.ForEach(t => Assert.True(t.Join(30_000)));
            Assert.Equal(64, entered);
            Assert.Equal(64, results.Count(r => r.Succeeded));
        }

#pragma warning disable 618
        // Still compile in hosts of earlier versions, and change nothing.
        [Fact]
        public void TheObsoleteOptionsAreIgnored()
        {
            var b = new Blocker();
            var engine = new ScriptEngine(new EngineOptions { MaxConcurrentRuns = 1, GlobalMemoryCeilingBytes = 1000 });
            var first = new Thread(() => engine.Run("block()\n", new RunRequest { Builtins = b.Table })) { IsBackground = true };
            first.Start();
            var sw = Stopwatch.StartNew();
            while (Volatile.Read(ref b.Entered) < 1 && sw.ElapsedMilliseconds < 15_000)
                Thread.Sleep(5);
            RunResult second = engine.Run("x = 1\n", new RunRequest());
            b.Release.Set();
            Assert.True(first.Join(30_000));
            Assert.True(second.Succeeded, second.Error == null ? "" : second.Error.Message);
        }
#pragma warning restore 618

        // A host function may start a run: it runs like any other, with nothing to wait for.
        [Fact]
        public void ARunFromInsideARunJustRuns()
        {
            ScriptEngine engine = new ScriptEngine(new EngineOptions());
            HostFunctionTable t = HostFunctionTable.CreateDefault();
            t.Set("inner", (s, a, kw, c) =>
            {
                RunResult r = engine.Run("y = 1\n", null);
                return c.Values.Str(r.Succeeded ? "ok" : r.Error.Message);
            });
            RunResult outer = engine.Run("r = inner()\n", new RunRequest { Builtins = t, ScalarOutputNames = new[] { "r" } });
            Assert.Equal("ok", outer.ScalarsOut["r"]);
        }

        // A token cancelled before the run: nothing of it runs (the first statement used to, on an idle engine).
        [Fact]
        public void ACancelledTokenRunsNothing()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                string printed = null;
                RunResult r = new ScriptEngine(new EngineOptions()).Run("print('ran')\n",
                    new RunRequest { CancellationToken = cts.Token, PrintSink = line => printed = line });
                Assert.Equal(ScriptErrorKind.BudgetExceeded, r.Error.Kind);
                Assert.Equal("CancellationToken", r.Error.LimitName);
                Assert.Null(printed);
            }
        }

        // One slot used to mean a hung host call held every later run in the queue until it returned, forever if it
        // never did (review 4, H2). Now they run beside it, and the breaker alone refuses, at its threshold.
        [Fact]
        public void AHungHostCallHoldsUpNobody()
        {
            var b = new Blocker();
            var engine = new ScriptEngine(new EngineOptions
            {
                CircuitBreakerLeakedThreads = 2,
                DefaultLimits = ResourceLimits.Default.With(deadlineMs: 200),
            });
            RunResult hung = null;
            var t = new Thread(() => hung = engine.Run("block()\n", new RunRequest { Builtins = b.Table })) { IsBackground = true };
            t.Start();
            var sw = Stopwatch.StartNew();
            while (Volatile.Read(ref b.Entered) < 1 && sw.ElapsedMilliseconds < 15_000)
                Thread.Sleep(5);
            try
            {
                sw.Restart();
                RunResult next = engine.Run("x = 1\n", new RunRequest());
                Assert.True(next.Succeeded, next.Error == null ? "" : next.Error.Message);
                Assert.True(sw.ElapsedMilliseconds < 5_000, "it waited " + sw.ElapsedMilliseconds + " ms");
            }
            finally
            {
                b.Release.Set();
            }
            Assert.True(t.Join(30_000));
            Assert.NotNull(hung);
        }
    }
}

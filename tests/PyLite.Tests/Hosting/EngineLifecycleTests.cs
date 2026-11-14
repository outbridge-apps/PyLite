using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // What a host sees across runs, through the public Run only: the engine is whole after any failed run,
    // a cancelled run leaves nothing running,
    // and leaked runner threads open the breaker, and close it when they return.
    [Trait("Category", "Sandbox")]
    public class EngineLifecycleTests
    {
        private static RunResult Ok(ScriptEngine engine)
        {
            return engine.Run("total = sum(range(10))\n", new RunRequest { ScalarOutputNames = new[] { "total" } });
        }

        [Theory]
        [InlineData("raise ValueError('bad')\n", "RuntimeError")]
        [InlineData("def f(:\n", "SyntaxError")]
        [InlineData("while True:\n    pass\n", "BudgetExceeded")]
        [InlineData("def f(n):\n    return f(n + 1)\nf(0)\n", "BudgetExceeded")]
        [InlineData("x = 'a' * (10 ** 9)\n", "BudgetExceeded")]
        [InlineData("fail()\n", "HostError")]
        public void TheEngineIsWholeAfterAFailedRun(string src, string kind)
        {
            var table = HostFunctionTable.CreateDefault();
            table.Set("fail", (self, args, kw, c) => { throw new InvalidOperationException("host side"); });
            var engine = new ScriptEngine(new EngineOptions
            {
                DefaultLimits = ResourceLimits.Default.With(maxSteps: 1_000_000),
            });
            for (int i = 0; i < 3; i++)
            {
                RunResult bad = engine.Run(src, new RunRequest { Builtins = table });
                Assert.Equal(kind, bad.Error.Kind.ToString());
                RunResult good = Ok(engine);
                Assert.True(good.Succeeded, "run after " + kind + ": " + (good.Error == null ? "" : good.Error.Message));
                Assert.Equal("45", good.ScalarsOut["total"]);
            }
        }

        // Cancellation ends the run, its runner thread exits, and no script code runs after Run returns.
        [Theory]
        [InlineData("while True:\n    tick()\n")]
        [InlineData("x = 3 ** 400000\nwhile True:\n    tick()\n    y = x * x\n")]   // cancelled inside a BigOps multiply
        public void ACancelledRunLeavesNothingRunning(string src)
        {
            int ticks = 0;
            Thread runner = null;
            var table = HostFunctionTable.CreateDefault();
            table.Set("tick", (self, args, kw, c) =>
            {
                runner = Thread.CurrentThread;
                Interlocked.Increment(ref ticks);
                return c.Values.None;
            });
            var engine = new ScriptEngine(new EngineOptions
            {
                DefaultLimits = ResourceLimits.Default.With(deadlineMs: 60_000, maxIntBits: 4_000_000),
            });
            using (var cts = new CancellationTokenSource())
            {
                var watcher = new Thread(() =>
                {
                    var sw = Stopwatch.StartNew();
                    while (Volatile.Read(ref ticks) < 1 && sw.ElapsedMilliseconds < 30_000)
                        Thread.Sleep(5);
                    cts.Cancel();
                }) { IsBackground = true };
                watcher.Start();
                RunResult r = engine.Run(src, new RunRequest { Builtins = table, CancellationToken = cts.Token });
                Assert.Equal(ScriptErrorKind.BudgetExceeded, r.Error.Kind);
                Assert.Equal("CancellationToken", r.Error.LimitName);
            }
            Assert.NotNull(runner);
            Assert.True(runner.Join(5000), "the runner thread is still alive");
            int after = Volatile.Read(ref ticks);
            Thread.Sleep(300);
            Assert.Equal(after, Volatile.Read(ref ticks));
            Assert.True(Ok(engine).Succeeded);
        }

        // A host function that blocks past deadline + grace + 5 s leaks its runner thread: Run returns a
        // HostError, the engine raises ThreadLeaked, and at the breaker's threshold every
        // run is refused. When the host function returns, the thread ends and the engine serves runs again.
        [Fact]
        public void LeakedThreadsStopTheEngineUntilTheyReturn()
        {
            var release = new ManualResetEventSlim(false);
            var events = new ConcurrentQueue<EngineEventKind>();
            var table = HostFunctionTable.CreateDefault();
            table.Set("block", (self, args, kw, c) =>
            {
                release.Wait();   // deaf to the run's deadline, as a stuck host call is
                return c.Values.None;
            });
            var engine = new ScriptEngine(new EngineOptions
            {
                CircuitBreakerLeakedThreads = 2,
                DefaultLimits = ResourceLimits.Default.With(deadlineMs: 200),
                EventSink = e => events.Enqueue(e.Kind),
            });
            var leaked = new ConcurrentBag<RunResult>();
            var threads = Enumerable.Range(0, 2).Select(_ => new Thread(() =>
                leaked.Add(engine.Run("block()\nx = 1\n", new RunRequest { Builtins = table }))) { IsBackground = true }).ToList();
            try
            {
                threads.ForEach(t => t.Start());
                threads.ForEach(t => Assert.True(t.Join(60_000)));
                Assert.Equal(2, leaked.Count(r => r.Error != null && r.Error.Kind == ScriptErrorKind.HostError));
                Assert.Equal(2, events.Count(k => k == EngineEventKind.ThreadLeaked));
                Assert.Contains(EngineEventKind.CircuitOpened, events);

                RunResult refused = Ok(engine);
                Assert.Equal(ScriptErrorKind.HostError, refused.Error.Kind);
                Assert.Equal("engine unavailable: leaked thread limit reached", refused.Error.Message);
                Assert.Equal(2, refused.Stats.LeakedThreadsAtStart);   // documented, and always 0 before (Q04)
            }
            finally
            {
                release.Set();
            }
            var sw = Stopwatch.StartNew();
            RunResult again;
            do
            {
                Thread.Sleep(20);
                again = Ok(engine);
            }
            while (!again.Succeeded && sw.ElapsedMilliseconds < 30_000);
            Assert.True(again.Succeeded, again.Error == null ? "" : again.Error.Message);
            Assert.Contains(EngineEventKind.CircuitClosed, events);
        }
    }
}

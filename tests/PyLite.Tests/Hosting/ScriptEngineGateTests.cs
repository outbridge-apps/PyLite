using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // PKG-07 T-HST-08: run orchestration — the RunGate (concurrency + memory ceiling), name validation (R-7),
    // and the EventSink. Each test builds its own engine so slots/ceilings are isolated.
    [Trait("Category", "Sandbox")]
    public class ScriptEngineGateTests
    {
        [Fact]
        public void SaturationRefusesExtraRun()
        {
            var release = new ManualResetEventSlim(false);
            var entered = new CountdownEvent(2);
            var table = HostFunctionTable.CreateDefault();
            table.Set("block", (self, args, kw, c) =>
            {
                entered.Signal();
                release.Wait();
                return c.Values.None;
            });

            var engine = new ScriptEngine(new EngineOptions { MaxConcurrentRuns = 2 });
            // Dedicated threads (not Task.Run): under a parallel full-suite run the thread pool can be
            // starved for seconds, so pool-queued starters miss a short entered.Wait window (flaky).
            RunResult r1 = null, r2 = null;
            var th1 = new Thread(() => r1 = engine.Run("block()\n", new RunRequest { Builtins = table })) { IsBackground = true };
            var th2 = new Thread(() => r2 = engine.Run("block()\n", new RunRequest { Builtins = table })) { IsBackground = true };
            th1.Start();
            th2.Start();
            try
            {
                Assert.True(entered.Wait(30000));   // both blocking runs now hold slots inside block()

                RunResult refused = engine.Run("x = 1\n", new RunRequest());
                Assert.False(refused.Succeeded);
                Assert.Equal(ScriptErrorKind.BudgetExceeded, refused.Error.Kind);
                Assert.Equal("MaxConcurrentRuns", refused.Error.LimitName);
            }
            finally
            {
                release.Set();
                th1.Join(10000);
                th2.Join(10000);
            }
            Assert.True(r1 != null && r1.Succeeded);
            Assert.True(r2 != null && r2.Succeeded);
        }

        [Fact]
        public void GlobalMemoryCeilingRefusesRun()
        {
            // The per-run MaxAllocBytes (256 MB default) alone exceeds this ceiling, so even one run is refused.
            var engine = new ScriptEngine(new EngineOptions { GlobalMemoryCeilingBytes = 1000 });
            RunResult r = engine.Run("x = 1\n", new RunRequest());
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.BudgetExceeded, r.Error.Kind);
            Assert.Equal("GlobalMemoryCeiling", r.Error.LimitName);
        }

        [Fact]
        public void InvalidInputNameIsHostError()
        {
            RunResult r = new ScriptEngine(new EngineOptions()).Run("x = 1\n",
                new RunRequest { ScalarsIn = new Dictionary<string, string> { ["bad name"] = "1" } });
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Contains("invalid variable name", r.Error.Message);
        }

        [Fact]
        public void KeywordInputNameIsHostError()
        {
            RunResult r = new ScriptEngine(new EngineOptions()).Run("x = 1\n",
                new RunRequest { ScalarsIn = new Dictionary<string, string> { ["class"] = "1" } });
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Contains("invalid variable name", r.Error.Message);   // R-7: a reserved word is rejected
        }

        [Fact]
        public void ScalarJsonNameCollisionIsHostError()
        {
            RunResult r = new ScriptEngine(new EngineOptions()).Run("x = 1\n", new RunRequest
            {
                ScalarsIn = new Dictionary<string, string> { ["v"] = "1" },
                JsonIn = new Dictionary<string, string> { ["v"] = "2" },
            });
            Assert.False(r.Succeeded);
            Assert.Equal(ScriptErrorKind.HostError, r.Error.Kind);
            Assert.Contains("both ScalarsIn and JsonIn", r.Error.Message);
        }

        [Fact]
        public void EventSinkReceivesRunCompleted()
        {
            var events = new List<EngineEvent>();
            var engine = new ScriptEngine(new EngineOptions
            {
                EventSink = ev => { lock (events) { events.Add(ev); } },
            });
            engine.Run("x = 1\n", new RunRequest());
            lock (events)
            {
                Assert.Contains(events, ev => ev.Kind == EngineEventKind.RunCompleted && ev.Stats != null);
            }
        }

        [Fact]
        public void ManySequentialRunsSucceedOnDedicatedThreads()
        {
            var engine = new ScriptEngine(new EngineOptions());
            for (int i = 0; i < 20; i++)
            {
                RunResult r = engine.Run("x = sum(range(50))\n",
                    new RunRequest { ScalarOutputNames = new[] { "x" } });
                Assert.True(r.Succeeded);
                Assert.Equal("1225", r.ScalarsOut["x"]);
            }
        }
    }
}

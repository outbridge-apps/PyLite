using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // PKG-07 T-HST-08: run orchestration: name validation (R-7), the EventSink, runs side by side.
    [Trait("Category", "Sandbox")]
    public class ScriptEngineGateTests
    {

        // A Thread.Abort of the host thread (an ASP.NET request timeout) anywhere in Run leaves the engine whole:
        // four more runs still go side by side.
        [Fact]
        public void HostThreadAbortsLeaveTheEngineWhole()
        {
            var engine = new ScriptEngine(new EngineOptions());
            engine.Run("x = 1\n", new RunRequest());   // the engine's statics are built before any abort lands
            var rnd = new System.Random(7);
            for (int i = 0; i < 600; i++)   // with 200 a reverted fix still passed one time in three; 600 leave one in 27
            {
                var t = new Thread(() =>
                {
                    try
                    {
                        while (true)
                            engine.Run("x = 1\n", new RunRequest());
                    }
                    catch (ThreadAbortException)
                    {
                        Thread.ResetAbort();
                    }
                }) { IsBackground = true };
                // no captured context: an abort in the CLR's AsyncLocal culture callback fail-fasts the host
                using (ExecutionContext.SuppressFlow())
                    t.Start();
                Thread.Sleep(rnd.Next(0, 3));
                t.Abort();
                Assert.True(t.Join(30000));
            }

            var release = new ManualResetEventSlim(false);
            var entered = new CountdownEvent(4);
            var table = HostFunctionTable.CreateDefault();
            table.Set("block", (self, args, kw, c) =>
            {
                entered.Signal();
                release.Wait();
                return c.Values.None;
            });
            var results = new RunResult[4];
            var threads = new Thread[4];
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                threads[i] = new Thread(() => results[k] = engine.Run("block()\n", new RunRequest { Builtins = table })) { IsBackground = true };
                threads[i].Start();
            }
            bool allIn = entered.Wait(30000);
            release.Set();
            foreach (Thread t in threads)
                t.Join(30000);
            Assert.True(allIn, "an aborted host thread cost a later run");
            foreach (RunResult r in results)
                Assert.True(r != null && r.Succeeded);
        }

        // An unseeded random (and uuid4, which drew from it) was seeded from Environment.TickCount: runs started
        // within one 15.6 ms tick printed the same numbers (149 of 160 parallel runs matched another). random()
        // alone: uuid4 now draws from the OS, so with it in the text the test held even with the seeding reverted.
        [Theory]
        [InlineData("import random\nr = str(random.random())\n")]
        [InlineData("import uuid\nr = str(uuid.uuid4())\n")]
        public void ParallelRunsDrawDifferentRandomNumbers(string src)
        {
            var engine = new ScriptEngine(new EngineOptions());
            engine.Run(src, new RunRequest());
            var outs = new string[16];
            var start = new Barrier(16);
            var threads = new Thread[16];
            for (int i = 0; i < 16; i++)
            {
                int k = i;
                threads[i] = new Thread(() =>
                {
                    start.SignalAndWait();
                    RunResult r = engine.Run(src, new RunRequest { ScalarOutputNames = new[] { "r" } });
                    outs[k] = r.Succeeded ? r.ScalarsOut["r"] : "failed " + k + ": " + r.Error.Message;
                }) { IsBackground = true };
                threads[i].Start();
            }
            foreach (Thread t in threads)
                t.Join(60000);
            Assert.Equal(16, new HashSet<string>(outs).Count);
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

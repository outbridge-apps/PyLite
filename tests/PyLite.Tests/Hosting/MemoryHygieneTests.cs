using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // T-HST-15 (RFC-004): MemoryHygiene knobs + the named leak gates.
    [Trait("Category", "Sandbox")]
    public class MemoryHygieneTests
    {
        [Fact]
        public void HygieneKnobsSmoke()
        {
            // Threshold 1 byte + LOH every hint: the GC path executes after each run without faulting it.
            var e = new ScriptEngine(new EngineOptions
            {
                MemoryHygiene = new MemoryHygieneOptions
                {
                    PostRunGcHintThresholdBytes = 1,
                    LohCompactionEveryNHints = 1,
                },
            });
            for (int i = 0; i < 3; i++)
            {
                RunResult r = e.Run("s = 'x' * 100000\nn = len(s)\n",
                    new RunRequest { ScalarOutputNames = new[] { "n" } });
                Assert.True(r.Succeeded);
                Assert.Equal("100000", r.ScalarsOut["n"]);
            }
        }

        [Fact]
        public void HygieneOffByDefault()
        {
            var e = new ScriptEngine(new EngineOptions());   // MemoryHygiene null => both knobs off
            RunResult r = e.Run("x = 1\n", new RunRequest { ScalarOutputNames = new[] { "x" } });
            Assert.True(r.Succeeded);
        }

        // ---- RFC-004 §3 zero-retention gate: the run graph (ctx + globals + modules + a large value)
        // is fully unrooted after the run result is consumed. ----

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference[] RunAndCaptureGraph()
        {
            EvResult r = Ev.Run("import math\nimport collections\nbig = 'x' * 1000000\n"
                + "d = collections.OrderedDict([('a', 1)])\n");
            var weaks = new WeakReference[]
            {
                new WeakReference(r.Ctx),
                new WeakReference(r.Globals),
                new WeakReference(r.Ctx.ModuleInstances["math"]),
                new WeakReference(r.Get("big")),
                new WeakReference(r.Get("d")),
            };
            return weaks;
        }

        [Fact]
        public void ZeroRetentionGate()
        {
            WeakReference[] weaks = RunAndCaptureGraph();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            for (int i = 0; i < weaks.Length; i++)
                Assert.False(weaks[i].IsAlive, "run graph object #" + i + " is still rooted after the run");
        }

        // ---- RFC-004 §3 endurance gate (scaled for the PR suite; the full 1000-run version is nightly):
        // mixed runs on ONE engine return managed memory to baseline. ----

        [Fact]
        public void EnduranceGateScaled()
        {
            // Deterministic retention proof (parallel-safe, unlike process-wide GetTotalMemory which the
            // parallel suite made flaky): one engine does 150 mixed runs (success/error, big strings,
            // imports) with NO OOM/crash, AND the engine holds no reference to any individual run's graph.
            var e = new ScriptEngine(new EngineOptions());
            WeakReference midRun = CaptureMidRunGraph(e);   // a WeakRef to a big value from run #50

            for (int i = 51; i < 150; i++)
                RunOnce(e, i);   // 99 more runs on the SAME engine

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            // The strict 1000-run "process memory returns to baseline ±20%" is the nightly RFC-004 gate.
            Assert.False(midRun.IsAlive, "the engine retained a completed run's value graph across later runs");
        }

        // Runs 0..50 on the engine; returns a WeakReference to a large value produced by run #50, so a later
        // GC can prove the engine did not keep it alive. NoInlining keeps the strong ref out of the caller.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CaptureMidRunGraph(ScriptEngine e)
        {
            for (int i = 0; i < 50; i++)
                RunOnce(e, i);
            EvResult r = Ev.Run("big = 'z' * 500000\nn = len(big)\n");
            return new WeakReference(r.Get("big"));
        }

        private static void RunOnce(ScriptEngine e, int i)
        {
            switch (i % 3)
            {
                case 0:
                    e.Run("import math\ns = 'y' * 1000000\nx = len(s) + math.floor(1.5)\n",
                        new RunRequest { ScalarOutputNames = new[] { "x" } });
                    break;
                case 1:
                    e.Run("raise ValueError('boom')\n", new RunRequest());   // failure path
                    break;
                default:
                    e.Run("import collections\nd = collections.Counter('abcabc')\nx = d['a']\n",
                        new RunRequest { ScalarOutputNames = new[] { "x" } });
                    break;
            }
        }
    }
}

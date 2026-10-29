using System;
using System.Diagnostics;
using System.Linq;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Syntax;

namespace Outbridge.PyLite.PerfBench
{
    internal static partial class Program
    {
        // ---------- runners ----------

        private static BenchResult MeasurePyLite(Bench b)
        {
            var engine = new ScriptEngine(new EngineOptions { DefaultLimits = ResourceLimits.Batch });
            CompiledScript compiled = engine.Compile(b.Source, b.Name);   // compile once; runs measure execution
            var request = new RunRequest { ScalarOutputNames = new[] { "result" } };
            return Measure("pylite:" + b.Name, () =>
            {
                RunResult r = engine.Run(compiled, request);
                if (!r.Succeeded)
                    throw new InvalidOperationException("PyLite bench failed: " + b.Name + ": " + r.Error.Message);
            });
        }

        private static BenchResult MeasureIron(Bench b)
        {
            Microsoft.Scripting.Hosting.ScriptEngine engine = IronPython.Hosting.Python.CreateEngine();
            engine.SetSearchPaths(new[] { IronStdLib() });
            Microsoft.Scripting.Hosting.CompiledCode compiled =
                engine.CreateScriptSourceFromString(b.Source).Compile();
            return Measure("iron:" + b.Name, () =>
            {
                Microsoft.Scripting.Hosting.ScriptScope scope = engine.CreateScope();   // fresh globals per run (parity with PyLite)
                compiled.Execute(scope);
                object result;
                if (!scope.TryGetVariable("result", out result))
                    throw new InvalidOperationException("Iron bench produced no result: " + b.Name);
            });
        }

        private const string TinyScript = "x = sum(range(100))\nresult = x\n";

        private static void RunTiny()
        {
            var engine = new ScriptEngine(new EngineOptions());
            RunResult r = engine.Run(TinyScript, new RunRequest { ScalarOutputNames = new[] { "result" } });
            if (!r.Succeeded)
                throw new InvalidOperationException("cold run failed");
        }

        private static void RunTinyIron()
        {
            Microsoft.Scripting.Hosting.ScriptEngine engine = IronPython.Hosting.Python.CreateEngine();
            Microsoft.Scripting.Hosting.ScriptScope scope = engine.CreateScope();
            engine.Execute(TinyScript, scope);
        }

        // The genuine first run in a fresh process: one sample, JIT and static initialization included.
        // A single sample cannot be a gate input (its spread is larger than the value), so it is
        // reported for information only and engine_spinup carries the gate.
        private static Row BenchColdStart()
        {
            Console.Write("cold_start ".PadRight(28, '.'));
            BenchResult py = Measure("pylite:cold", RunTiny, warmup: 0, measured: 1);
            Console.Write(" pylite " + Fmt(py.MedianMs) + " ");
            BenchResult iron = Measure("iron:cold", RunTinyIron, warmup: 0, measured: 1);
            Console.WriteLine("iron " + Fmt(iron.MedianMs));
            return new Row { Name = "cold_start", Kind = "cold", Gated = false, Py = py, Iron = iron };
        }

        // Steady-state cost of standing up an engine and running a tiny script, measured over enough
        // samples to be a stable gate input. IronPython keeps few samples: one spin-up costs ~200 ms
        // there and its column is informational anyway.
        private static Row BenchEngineSpinup()
        {
            Console.Write("engine_spinup ".PadRight(28, '.'));
            BenchResult py = Measure("pylite:spinup", RunTiny, warmup: 20, measured: 51);
            Console.Write(" pylite " + Fmt(py.MedianMs) + " ");
            BenchResult iron = Measure("iron:spinup", RunTinyIron, warmup: 1, measured: 5);
            Console.WriteLine("iron " + Fmt(iron.MedianMs));
            return new Row { Name = "engine_spinup", Kind = "spinup", Gated = true, Py = py, Iron = iron };
        }

        // ---------- the stopwatch runner (R-15) ----------

        private sealed class BenchResult
        {
            public string Name;
            public double MedianMs, P95Ms, MinMs, MaxMs;
            public int Iterations;
            public double[] SamplesMs;        // in run order
            public long AllocBytesPerRun;     // the whole AppDomain, runner threads included
            public int Gen0, Gen2;            // collections during the measured runs
        }

        private static BenchResult Measure(string name, Action action, int? warmup = null, int? measured = null)
        {
            int w = warmup ?? _warmup, m = measured ?? _measured;
            for (int i = 0; i < w; i++)
                action();
            GC.Collect(2, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
            var times = new double[m];
            var sw = new Stopwatch();
            long alloc0 = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
            int gen0 = GC.CollectionCount(0), gen2 = GC.CollectionCount(2);
            for (int i = 0; i < m; i++)
            {
                sw.Restart();
                action();
                sw.Stop();
                times[i] = sw.Elapsed.TotalMilliseconds;
            }
            long alloc = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize - alloc0;
            var sorted = (double[])times.Clone();
            Array.Sort(sorted);
            return new BenchResult
            {
                Name = name,
                MinMs = sorted[0],
                MedianMs = sorted[m / 2],
                MaxMs = sorted[m - 1],
                P95Ms = sorted[Math.Min(m - 1, (int)Math.Ceiling(m * 0.95) - 1)],
                Iterations = m,
                SamplesMs = times,
                AllocBytesPerRun = alloc / m,
                Gen0 = GC.CollectionCount(0) - gen0,
                Gen2 = GC.CollectionCount(2) - gen2,
            };
        }
    }
}

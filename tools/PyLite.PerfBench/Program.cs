using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Outbridge.PyLite.PerfBench
{
    // T-TST-18: comparative perf benches PyLite vs IronPython 3.4.2 (impl/12 R-15). Stopwatch runner:
    // warmup runs (JIT/caches, not counted), forced GC, measured runs -> median/p95/min. The multiple vs
    // IronPython is informational; the regression gate is vs OUR OWN saved baseline (x1.5).
    //
    // The gate only compares runs of the SAME scale: loop sizes are divided by `scale`, so a quick 1/5
    // run against a full-scale baseline has 5x of slack and can never fire. The baseline records the
    // scale it was taken at, a mismatch skips the gate, and a skipped gate fails a --full (CI) run.
    //
    // Parts: Corpus (the benches), Runners (stopwatch runner), Matrix (--matrix/--focus), LoadGuard,
    // Baseline (report + baseline gate).
    internal static partial class Program
    {
        private const double GateFactor = 1.5;                   // regression = slower than baseline x1.5
        private static int _warmup = 2;
        private static int _measured = 7;

        private static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            bool focus = Array.IndexOf(args, "--focus") >= 0;
            if (focus || Array.IndexOf(args, "--matrix") >= 0)
            {
                // --only <cat> limits the battery to one category and prints to console only (the
                // .md report is written exclusively by a full run, so partial runs cannot clobber it).
                int onlyIdx = Array.IndexOf(args, "--only");
                string only = onlyIdx >= 0 && onlyIdx + 1 < args.Length ? args[onlyIdx + 1] : null;
                // --no-iron skips the IronPython arm: the PyLite numbers alone, which is what an A/B
                // between two builds compares. Console only, never the report.
                bool noIron = Array.IndexOf(args, "--no-iron") >= 0;
                int ceIdx = Array.IndexOf(args, "--control-every");
                if (ceIdx >= 0 && ceIdx + 1 < args.Length)
                    ControlEveryRows = Math.Max(1, int.Parse(args[ceIdx + 1], CultureInfo.InvariantCulture));
                // --focus runs the cases behind the performance plan instead of the matrix; console only.
                return focus
                    ? RunMatrix(FocusBenches.All(), only, noIron, null)
                    : RunMatrix(MatrixBenches.All(), only, noIron, "PERF-MATRIX.md");
            }
            bool full = Array.IndexOf(args, "--full") >= 0;      // spec-scale loops (weekly CI)
            bool saveBaseline = Array.IndexOf(args, "--save-baseline") >= 0;
            if (full)
            {
                _warmup = 3;
                _measured = 15;
            }
            int scale = full ? 1 : 5;                            // default: spec sizes ÷ 5 for a quick local run

            Console.WriteLine("PyLite vs IronPython 3.4.2 — scale 1/" + scale + ", warmup " + _warmup + ", measured " + _measured);
            var results = new List<Row>();

            // ---- startup: the process-first run (JIT included) and the steady-state engine spin-up ----
            results.Add(BenchColdStart());
            results.Add(BenchEngineSpinup());

            foreach (Bench b in Benches(scale))
            {
                Console.Write((b.Name + " ").PadRight(28, '.'));
                BenchResult py = MeasurePyLite(b);
                Console.Write(" pylite " + Fmt(py.MedianMs) + " ");
                BenchResult iron = MeasureIron(b);
                Console.WriteLine("iron " + Fmt(iron.MedianMs));
                results.Add(new Row { Name = b.Name, Kind = b.Kind, Gated = true, Py = py, Iron = iron });
            }

            string report = BuildReport(results, scale);
            string outPath = Path.Combine(RepoRoot(), "docs", "PERF-REPORT.md");
            File.WriteAllText(outPath, report, new UTF8Encoding(false));
            Console.WriteLine("\nreport -> " + outPath);
            if (saveBaseline)
            {
                SaveBaseline(results, scale);
                return 0;
            }
            return CheckBaseline(results, scale, full) ? 0 : 1;
        }

        private static string IronStdLib()
        {
            string next = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(next))
                return next;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".nuget", "packages", "ironpython.stdlib", "3.4.2", "content", "lib");
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PLAN.md")))
                dir = dir.Parent;
            return dir != null ? dir.FullName : Environment.CurrentDirectory;
        }

        private static string Fmt(double ms)
        {
            return ms.ToString(ms >= 100 ? "0" : "0.0", CultureInfo.InvariantCulture);
        }
    }
}

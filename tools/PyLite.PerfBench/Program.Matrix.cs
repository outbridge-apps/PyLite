using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Syntax;

namespace Outbridge.PyLite.PerfBench
{
    internal static partial class Program
    {
        // ---------- the function-level matrix (--matrix) and the focused cases (--focus) ----------
        //
        // Every row is measured per run, but one SAMPLE is K back-to-back runs, K calibrated per engine
        // so a sample lasts about SampleTargetMs: a 2 ms row measured as single runs is mostly timer and
        // scheduler noise on a shared box. The whole PyLite arm runs before the whole IronPython arm so
        // the two heaps never interleave, and every sample starts from a collected heap.

        private const double SampleTargetMs = 150;
        private const int MaxRunsPerSample = 1000;
        private const int MatrixWarmup = 2;
        private const int MatrixMeasured = 7;

        private sealed class MatrixRow
        {
            public string Cat;
            public string Name;
            public string Source;
            public int PyRuns;        // runs per sample, PyLite arm
            public int IronRuns;      // runs per sample, IronPython arm
            public BenchResult Py;    // per-run ms; null => PyLite failed to run the source
            public BenchResult Iron;  // per-run ms; null => IronPython failed or its arm was skipped
            public string PyError;
            public string IronError;
            public bool PyDisturbed;   // measured next to a control spike: left out of every verdict
            public bool IronDisturbed;
            public double PyOthers;    // share of the machine other processes took while this row was measured
            public double IronOthers;
            public double Multiple { get { return Py != null && Iron != null && Iron.MedianMs > 0 ? Py.MedianMs / Iron.MedianMs : double.NaN; } }

            // The two engines' min..max ranges overlap: this box cannot tell them apart.
            public bool WithinNoise { get { return Py != null && Iron != null && Py.MinMs <= Iron.MaxMs && Iron.MinMs <= Py.MaxMs; } }
        }

        private static int RunMatrix(IEnumerable<MatrixBenches.MBench> benches, string only, bool noIron, string reportFile)
        {
            var rows = new List<MatrixRow>();
            foreach (MatrixBenches.MBench b in benches)
            {
                if (only == null || Array.IndexOf(only.Split(','), b.Cat) >= 0)   // --only a,b,c
                    rows.Add(new MatrixRow { Cat = b.Cat, Name = b.Name, Source = b.Source });
            }
            Console.WriteLine("PyLite vs IronPython 3.4.2 — " + rows.Count + " rows; warmup " + MatrixWarmup + ", measured "
                + MatrixMeasured + " samples of ~" + SampleTargetMs + " ms each (median, min–max per run); control every "
                + ControlEveryRows + " rows");

            Console.WriteLine("\n-- PyLite arm --");
            for (int i = 0; i < rows.Count; i++)
            {
                if (i % ControlEveryRows == 0)
                    SampleControl(false, i);
                MeasurePyLiteRow(rows[i]);
            }
            SampleControl(false, rows.Count);
            if (!noIron)
            {
                Console.WriteLine("\n-- IronPython arm --");
                for (int i = 0; i < rows.Count; i++)
                {
                    if (i % ControlEveryRows == 0)
                        SampleControl(true, i);
                    MeasureIronRow(rows[i]);
                }
                SampleControl(true, rows.Count);
            }
            LoadVerdict load = JudgeLoad(rows, noIron);

            Console.WriteLine("\n-- per run, ms --");
            foreach (MatrixRow r in rows)
            {
                // tab-separated so two runs (an A/B) can be compared mechanically
                Console.WriteLine("ROW\t" + r.Cat + "/" + r.Name
                    + "\t" + (r.Py != null ? FmtFine(r.Py.MedianMs) + "\t" + FmtFine(r.Py.MinMs) + "\t" + FmtFine(r.Py.MaxMs) : "FAIL\t\t")
                    + "\t" + (r.Iron != null ? FmtFine(r.Iron.MedianMs) + "\t" + FmtFine(r.Iron.MinMs) + "\t" + FmtFine(r.Iron.MaxMs) : "-\t\t")
                    + "\t" + Verdict(r));
            }

            Console.WriteLine("\nCONTROL\tarm-bias\t" + load.ArmBias.ToString("0.00", CultureInfo.InvariantCulture)
                + "\tdisturbed\t" + load.Disturbed + "/" + rows.Count + "\t" + (load.Changed ? "LOAD CHANGED" : "stable"));
            if (load.Changed)
                Console.WriteLine(load.Why + " — the arms are not comparable; rerun when the box is quieter");
            if (reportFile == null || only != null || noIron)
            {
                Console.WriteLine("\nreport not written (" + (reportFile == null ? "--focus" : only != null ? "--only " + only : "--no-iron") + ")");
                return load.Changed ? 2 : 0;
            }
            if (load.Changed)
            {
                Console.WriteLine("report not written: it would replace an honest report with a dishonest one");
                return 2;
            }
            string outPath = Path.Combine(RepoRoot(), "docs", reportFile);
            File.WriteAllText(outPath, BuildMatrixReport(rows, load), new UTF8Encoding(false));
            Console.WriteLine("\nreport -> " + outPath);
            return 0;
        }

        private static void MeasurePyLiteRow(MatrixRow row)
        {
            Console.Write((row.Cat + "/" + row.Name + " ").PadRight(36, '.'));
            try
            {
                var engine = new ScriptEngine(new EngineOptions { DefaultLimits = ResourceLimits.Batch });
                CompiledScript compiled = engine.Compile(row.Source, row.Name);
                var request = new RunRequest { ScalarOutputNames = new[] { "result" } };
                Action run = () =>
                {
                    RunResult r = engine.Run(compiled, request);
                    if (!r.Succeeded)
                        throw new InvalidOperationException(r.Error.Message);
                };
                OthersCpuPercent();
                row.PyRuns = RunsPerSample(run);
                row.Py = MeasureSamples(run, row.PyRuns);
                row.PyOthers = OthersCpuPercent();
                Console.WriteLine(" x" + row.PyRuns.ToString(CultureInfo.InvariantCulture).PadRight(5) + FmtRange(row.Py)
                    + "  others " + row.PyOthers.ToString("0", CultureInfo.InvariantCulture) + "%");
            }
            catch (Exception ex)
            {
                row.PyError = Shorten(ex.Message);
                Console.WriteLine(" FAIL " + row.PyError);
            }
        }

        private static void MeasureIronRow(MatrixRow row)
        {
            Console.Write((row.Cat + "/" + row.Name + " ").PadRight(36, '.'));
            try
            {
                Microsoft.Scripting.Hosting.ScriptEngine iron = IronPython.Hosting.Python.CreateEngine();
                iron.SetSearchPaths(new[] { IronStdLib() });
                Microsoft.Scripting.Hosting.CompiledCode compiled = iron.CreateScriptSourceFromString(row.Source).Compile();
                Action run = () =>
                {
                    Microsoft.Scripting.Hosting.ScriptScope scope = iron.CreateScope();
                    compiled.Execute(scope);
                    object result;
                    if (!scope.TryGetVariable("result", out result))
                        throw new InvalidOperationException("no result");
                };
                OthersCpuPercent();
                row.IronRuns = RunsPerSample(run);
                row.Iron = MeasureSamples(run, row.IronRuns);
                row.IronOthers = OthersCpuPercent();
                Console.WriteLine(" x" + row.IronRuns.ToString(CultureInfo.InvariantCulture).PadRight(5) + FmtRange(row.Iron)
                    + "  others " + row.IronOthers.ToString("0", CultureInfo.InvariantCulture) + "%"
                    + "   " + CellMultiple(row) + (row.WithinNoise ? " (within noise)" : ""));
            }
            catch (Exception ex)
            {
                row.IronError = Shorten(ex.Message);
                Console.WriteLine(" FAIL " + row.IronError);
            }
        }

        // How many back-to-back runs make one sample of about SampleTargetMs; the first run is JIT.
        private static int RunsPerSample(Action run)
        {
            run();
            var sw = Stopwatch.StartNew();
            run();
            double one = Math.Max(sw.Elapsed.TotalMilliseconds, 0.01);
            return (int)Math.Max(1, Math.Min(MaxRunsPerSample, Math.Ceiling(SampleTargetMs / one)));
        }

        // Per-run times: every sample is `runs` back-to-back runs, started from a collected heap.
        private static BenchResult MeasureSamples(Action run, int runs)
        {
            for (int i = 0; i < MatrixWarmup; i++)
            {
                for (int j = 0; j < runs; j++)
                    run();
            }
            var times = new double[MatrixMeasured];
            var sw = new Stopwatch();
            for (int i = 0; i < MatrixMeasured; i++)
            {
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                sw.Restart();
                for (int j = 0; j < runs; j++)
                    run();
                sw.Stop();
                times[i] = sw.Elapsed.TotalMilliseconds / runs;
            }
            Array.Sort(times);
            return new BenchResult
            {
                MinMs = times[0],
                MedianMs = times[MatrixMeasured / 2],
                MaxMs = times[MatrixMeasured - 1],
                P95Ms = times[MatrixMeasured - 1],
                Iterations = MatrixMeasured * runs,
            };
        }

        private static string Verdict(MatrixRow r)
        {
            if (r.PyError != null || r.IronError != null)
                return "-";
            if (r.PyDisturbed || r.IronDisturbed)
                return "disturbed";   // before the NaN check, so an A/B run (--no-iron) still shows it
            if (double.IsNaN(r.Multiple))
                return "-";
            if (r.WithinNoise)
                return "within noise";
            return r.Multiple >= 1 ? "slower" : "faster";
        }

        private static string BuildMatrixReport(List<MatrixRow> rows, LoadVerdict load)
        {
            int slower = rows.Count(r => Verdict(r) == "slower");
            int faster = rows.Count(r => Verdict(r) == "faster");
            int noise = rows.Count(r => Verdict(r) == "within noise");
            int disturbed = rows.Count(r => Verdict(r) == "disturbed");
            int failed = rows.Count(r => Verdict(r) == "-");

            var sb = new StringBuilder();
            sb.AppendLine("# PERF-MATRIX — function-level PyLite vs IronPython 3.4.2");
            sb.AppendLine();
            sb.AppendLine("Generated " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                + "Z on " + Environment.MachineName + " at " + GitHead() + ". Informational — no gate.");
            sb.AppendLine();
            sb.AppendLine("Method: per-run milliseconds. One sample is K back-to-back runs, K chosen per engine so a sample");
            sb.AppendLine("lasts about " + SampleTargetMs + " ms; " + MatrixWarmup + " warm-up samples, " + MatrixMeasured
                + " measured, a forced collection before each. The whole PyLite arm runs before the");
            sb.AppendLine("IronPython arm. A row is **within noise** when the two engines' min–max ranges overlap: this box's");
            sb.AppendLine("background load moves wall-clock by tens of percent, and such a row says nothing either way.");
            sb.AppendLine("Load guard: a fixed PyLite script is measured every " + ControlEveryRows + " rows of each arm. Rows next to a");
            sb.AppendLine("control reading above x" + SpikeFactor.ToString("0.0", CultureInfo.InvariantCulture) + " the run's median, and rows during which other processes took more than "
                + MaxOthersPercent.ToString("0", CultureInfo.InvariantCulture) + "% of the machine, are **disturbed** and left out of the verdicts.");
            sb.AppendLine("This run: the arms' median controls differ x" + load.ArmBias.ToString("0.00", CultureInfo.InvariantCulture)
                + " (limit x" + MaxArmBias.ToString("0.00", CultureInfo.InvariantCulture) + "), " + load.Disturbed + " rows disturbed (limit "
                + (MaxDisturbedShare * 100).ToString("0", CultureInfo.InvariantCulture) + "%).");
            sb.AppendLine();
            sb.AppendLine("Rows: " + rows.Count + " — PyLite slower " + slower + ", PyLite faster " + faster
                + ", within noise " + noise + ", disturbed " + disturbed + (failed > 0 ? ", failed " + failed : "") + ".");
            sb.AppendLine();

            sb.AppendLine("## Slower than IronPython, outside noise");
            sb.AppendLine();
            AppendMatrixTable(sb, SortedByMultiple(rows.FindAll(r => Verdict(r) == "slower"), descending: true, top: int.MaxValue));
            sb.AppendLine("## Fastest relative to IronPython (top 15, outside noise)");
            sb.AppendLine();
            AppendMatrixTable(sb, SortedByMultiple(rows.FindAll(r => Verdict(r) == "faster"), descending: false, top: 15));

            sb.AppendLine("## Full matrix by category");
            sb.AppendLine();
            string cat = null;
            foreach (MatrixRow r in rows)
            {
                if (r.Cat != cat)
                {
                    if (cat != null)
                        sb.AppendLine();
                    cat = r.Cat;
                    sb.AppendLine("### " + cat);
                    sb.AppendLine();
                    sb.AppendLine("| function | PyLite ms | PyLite min–max | Iron ms | Iron min–max | PyLite/Iron | verdict |");
                    sb.AppendLine("|---|---:|---:|---:|---:|---:|---|");
                }
                sb.AppendLine("| " + r.Name + " | " + CellMs(r.Py, r.PyError) + " | " + CellRange(r.Py) + " | "
                    + CellMs(r.Iron, r.IronError) + " | " + CellRange(r.Iron) + " | " + CellMultiple(r) + " | " + Verdict(r) + " |");
            }
            return sb.ToString();
        }

        private static List<MatrixRow> SortedByMultiple(List<MatrixRow> rows, bool descending, int top)
        {
            var ok = rows.FindAll(r => !double.IsNaN(r.Multiple));
            ok.Sort((a, b) => descending ? b.Multiple.CompareTo(a.Multiple) : a.Multiple.CompareTo(b.Multiple));
            if (ok.Count > top)
                ok.RemoveRange(top, ok.Count - top);
            return ok;
        }

        private static void AppendMatrixTable(StringBuilder sb, List<MatrixRow> rows)
        {
            if (rows.Count == 0)
            {
                sb.AppendLine("(none)");
                sb.AppendLine();
                return;
            }
            sb.AppendLine("| category/function | PyLite ms | PyLite min–max | Iron ms | Iron min–max | PyLite/Iron |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|");
            foreach (MatrixRow r in rows)
                sb.AppendLine("| " + r.Cat + "/" + r.Name + " | " + CellMs(r.Py, r.PyError) + " | " + CellRange(r.Py) + " | "
                    + CellMs(r.Iron, r.IronError) + " | " + CellRange(r.Iron) + " | " + CellMultiple(r) + " |");
            sb.AppendLine();
        }

        private static string CellMs(BenchResult b, string error)
        {
            if (b != null)
                return FmtFine(b.MedianMs);
            return "FAIL: " + (error ?? "?");
        }

        private static string CellRange(BenchResult b)
        {
            return b == null ? "" : FmtFine(b.MinMs) + "–" + FmtFine(b.MaxMs);
        }

        private static string FmtRange(BenchResult b)
        {
            return FmtFine(b.MedianMs) + " ms [" + FmtFine(b.MinMs) + "–" + FmtFine(b.MaxMs) + "]";
        }

        private static string CellMultiple(MatrixRow r)
        {
            double m = r.Multiple;
            if (double.IsNaN(m))
                return "—";
            return m >= 1
                ? "×" + m.ToString("0.00", CultureInfo.InvariantCulture) + " slower"
                : "×" + (1 / m).ToString("0.00", CultureInfo.InvariantCulture) + " FASTER";
        }

        // Per-run times go down to microseconds, so the precision follows the magnitude.
        private static string FmtFine(double ms)
        {
            string f = ms >= 100 ? "0" : ms >= 10 ? "0.0" : ms >= 1 ? "0.00" : "0.000";
            return ms.ToString(f, CultureInfo.InvariantCulture);
        }

        private static string Shorten(string msg)
        {
            if (msg == null)
                return "?";
            msg = msg.Replace("\r", " ").Replace("\n", " ").Replace("|", "\\|");
            return msg.Length > 70 ? msg.Substring(0, 70) : msg;
        }
    }
}

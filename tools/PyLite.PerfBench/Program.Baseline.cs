using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Outbridge.PyLite.PerfBench
{
    internal static partial class Program
    {
        // ---------- report + baseline ----------

        private sealed class Row
        {
            public string Name;
            public string Kind;
            public bool Gated;      // participates in the baseline gate (and is written to the baseline)
            public BenchResult Py;
            public BenchResult Iron;
        }

        private static string BuildReport(List<Row> rows, int scale)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# PERF-REPORT — PyLite vs IronPython 3.4.2 (T-TST-18)");
            sb.AppendLine();
            sb.AppendLine("Generated " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                + "Z on " + Environment.MachineName + " | scale 1/" + scale + " of spec sizes | warmup " + _warmup
                + ", measured " + _measured + " (median). IronPython multiple is informational; the regression"
                + " gate is vs our own baseline (x" + GateFactor.ToString("0.0#", CultureInfo.InvariantCulture)
                + ", T-TST-18) and only compares runs of the same scale.");
            sb.AppendLine();
            sb.AppendLine("`cold_start` is the process-first run with JIT included: one sample, informational,"
                + " never gated. `engine_spinup` is the same work on a warm process over many samples and is"
                + " the gated startup number.");
            sb.AppendLine();
            sb.AppendLine("| bench | kind | PyLite med (ms) | PyLite p95 | Iron med (ms) | Iron p95 | PyLite/Iron |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            int faster = 0, slower = 0;
            double multipleSum = 0;
            foreach (Row r in rows)
            {
                double multiple = r.Iron.MedianMs > 0 ? r.Py.MedianMs / r.Iron.MedianMs : 0;
                if (r.Kind == "micro" || r.Kind == "macro")
                {
                    multipleSum += multiple;
                    if (multiple <= 1.0)
                        faster++;
                    else
                        slower++;
                }
                sb.AppendLine("| " + r.Name + " | " + r.Kind + " | " + Fmt(r.Py.MedianMs) + " | " + Fmt(r.Py.P95Ms)
                    + " | " + Fmt(r.Iron.MedianMs) + " | " + Fmt(r.Iron.P95Ms) + " | "
                    + (multiple >= 1 ? "×" + multiple.ToString("0.0", CultureInfo.InvariantCulture) + " slower"
                                     : "×" + (1 / multiple).ToString("0.0", CultureInfo.InvariantCulture) + " FASTER") + " |");
            }
            sb.AppendLine();
            sb.AppendLine("**Summary:** " + faster + " benches faster than IronPython, " + slower
                + " slower; average multiple ×" + (multipleSum / Math.Max(1, faster + slower)).ToString("0.0", CultureInfo.InvariantCulture)
                + " (micro and macro only). Startup is the separate pair of rows — PyLite has no DLR/AppDomain spin-up.");
            return sb.ToString();
        }

        private static string BaselinePath()
        {
            return Path.Combine(RepoRoot(), "tools", "perf-baseline.json");
        }

        // perf-baseline.json: flat "name": medianMs entries (json-shaped but hand-parsed — no extra deps
        // here). Keys starting with '_' are metadata about the run that produced the file; "_scale" is
        // the one the gate reads, the rest say how stale the numbers are.
        private static void SaveBaseline(List<Row> rows, int scale)
        {
            var lines = new List<string>
            {
                "  \"_scale\": " + scale.ToString(CultureInfo.InvariantCulture),
                "  \"_warmup\": " + _warmup.ToString(CultureInfo.InvariantCulture),
                "  \"_measured\": " + _measured.ToString(CultureInfo.InvariantCulture),
                "  \"_generated\": \"" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "Z\"",
                "  \"_machine\": \"" + Environment.OSVersion.VersionString + ", " + Environment.ProcessorCount
                    + " logical CPUs\"",   // what the numbers depend on, not the host name
                "  \"_commit\": \"" + (GitHead() ?? "unknown") + "\"",
            };
            int benches = 0;
            foreach (Row r in rows)
            {
                if (!r.Gated)
                    continue;
                lines.Add("  \"" + r.Name + "\": " + r.Py.MedianMs.ToString("0.###", CultureInfo.InvariantCulture));
                benches++;
            }
            File.WriteAllText(BaselinePath(), "{\n" + string.Join(",\n", lines) + "\n}\n", new UTF8Encoding(false));
            Console.WriteLine("baseline saved (scale 1/" + scale + ", " + benches + " benches) -> " + BaselinePath());
        }

        // Returns true when the run is clean: no regression, and the gate was actually able to run
        // whenever it had to. A skipped gate is a failure for --full because that is the CI path.
        private static bool CheckBaseline(List<Row> rows, int scale, bool full)
        {
            string path = BaselinePath();
            if (!File.Exists(path))
                return GateUnavailable("no baseline file at " + path, full);

            int baseScale;
            Dictionary<string, double> baseline = ParseBaseline(File.ReadAllLines(path), out baseScale);
            if (baseScale != scale)
            {
                return GateUnavailable("baseline is scale 1/" + (baseScale > 0 ? baseScale.ToString(CultureInfo.InvariantCulture) : "?")
                    + " but this run is scale 1/" + scale + "; rerun with --full or refresh with --save-baseline", full);
            }

            bool regression = false;
            int compared = 0;
            foreach (Row r in rows)
            {
                double baseMs;
                if (!r.Gated || !baseline.TryGetValue(r.Name, out baseMs) || baseMs <= 0)
                    continue;
                compared++;
                if (r.Py.MedianMs > baseMs * GateFactor)
                {
                    Console.WriteLine("REGRESSION " + r.Name + ": " + Fmt(baseMs) + " -> " + Fmt(r.Py.MedianMs)
                        + " (>x" + GateFactor.ToString("0.0#", CultureInfo.InvariantCulture) + ")");
                    regression = true;
                }
            }
            if (compared == 0)
                return GateUnavailable("baseline has no bench in common with this run", full);
            if (regression)
            {
                Console.WriteLine("PERF GATE FAILED (" + compared + " benches compared)");
                return false;
            }
            Console.WriteLine("no regression vs baseline (x" + GateFactor.ToString("0.0#", CultureInfo.InvariantCulture)
                + " gate, " + compared + " benches compared)");
            return true;
        }

        private static bool GateUnavailable(string why, bool full)
        {
            Console.WriteLine("GATE SKIPPED: " + why);
            if (!full)
                return true;
            Console.WriteLine("a --full run must be gated — save a full-scale baseline with: --full --save-baseline");
            return false;
        }

        private static Dictionary<string, double> ParseBaseline(string[] lines, out int scale)
        {
            var result = new Dictionary<string, double>(StringComparer.Ordinal);
            scale = 0;
            foreach (string line in lines)
            {
                int open = line.IndexOf('"');
                if (open < 0)
                    continue;
                int colon = line.IndexOf("\":", open, StringComparison.Ordinal);
                if (colon < 0)
                    continue;
                string name = line.Substring(open + 1, colon - open - 1);
                string raw = line.Substring(colon + 2).Trim().TrimEnd(',');
                double value;
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    continue;   // quoted metadata (date, machine, commit) — not a number, not a bench
                if (name.Length == 0)
                    continue;
                if (name == "_scale")
                    scale = (int)value;
                else if (name[0] != '_')
                    result[name] = value;
            }
            return result;
        }

        // Short HEAD sha read straight from .git — the baseline records what it was measured on, so a
        // stale file is visible without rerunning it. No git process, and absence is not an error.
        private static string GitHead()
        {
            try
            {
                string gitDir = Path.Combine(RepoRoot(), ".git");
                string head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
                if (head.StartsWith("ref:", StringComparison.Ordinal))
                {
                    string refPath = Path.Combine(gitDir, head.Substring(4).Trim().Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(refPath))
                        return null;
                    head = File.ReadAllText(refPath).Trim();
                }
                return head.Length >= 7 ? head.Substring(0, 7) : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}

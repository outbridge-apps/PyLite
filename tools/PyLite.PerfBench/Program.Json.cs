using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Outbridge.PyLite.Hosting;

namespace Outbridge.PyLite.PerfBench
{
    // benchmarks/: one pass as data (--json) and several passes as a page (--summarize). A pass keeps every
    // sample and the allocations per run, so medians and spreads can be recomputed; no host name is written.
    internal static partial class Program
    {
        private static void WriteJson(string path, List<Row> rows, int scale)
        {
            var env = new JObject
            {
                ["pylite"] = typeof(ScriptEngine).Assembly.GetName().Version.ToString(3),
                ["commit"] = GitCommit(),
                ["ironpython"] = typeof(IronPython.Runtime.PythonContext).Assembly.GetName().Version.ToString(3),
                ["os"] = Registry("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", "ProductName") ?? Environment.OSVersion.VersionString,
                ["osBuild"] = OsBuild(),
                ["cpu"] = (Registry("HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0", "ProcessorNameString") ?? "unknown").Trim(),
                ["logicalProcessors"] = Environment.ProcessorCount,
                ["dotnet"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                ["utc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            };
            var benches = new JArray();
            foreach (Row r in rows)
            {
                benches.Add(new JObject
                {
                    ["name"] = r.Name,
                    ["kind"] = r.Kind,
                    ["pylite"] = Sample(r.Py),
                    ["ironpython"] = Sample(r.Iron),
                });
            }
            var doc = new JObject
            {
                ["environment"] = env,
                ["method"] = new JObject { ["scale"] = scale, ["warmup"] = _warmup, ["measured"] = _measured },
                ["benchmarks"] = benches,
            };
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, doc.ToString(Formatting.Indented), new UTF8Encoding(false));
        }

        private static JObject Sample(BenchResult b)
        {
            return new JObject
            {
                ["medianMs"] = b.MedianMs,
                ["minMs"] = b.MinMs,
                ["maxMs"] = b.MaxMs,
                ["p95Ms"] = b.P95Ms,
                ["allocBytesPerRun"] = b.AllocBytesPerRun,
                ["gen0"] = b.Gen0,
                ["gen2"] = b.Gen2,
                ["samplesMs"] = new JArray(b.SamplesMs.Select(s => (object)Math.Round(s, 3)).ToArray()),
            };
        }

        private static string Registry(string key, string value)
        {
            object v = Microsoft.Win32.Registry.GetValue("HKEY_LOCAL_MACHINE\\" + key, value, null);
            return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        // Environment.OSVersion is capped at 6.2 for a process without a compatibility manifest.
        private static string OsBuild()
        {
            const string key = "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion";
            string build = Registry(key, "CurrentBuildNumber"), ubr = Registry(key, "UBR");
            return build == null ? Environment.OSVersion.Version.ToString() : ubr == null ? build : build + "." + ubr;
        }

        private static string GitCommit()
        {
            try
            {
                var psi = new ProcessStartInfo("git", "rev-parse --short HEAD")
                {
                    WorkingDirectory = RepoRoot(),
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (Process p = Process.Start(psi))
                {
                    string s = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit();
                    return p.ExitCode == 0 && s.Length > 0 ? s : null;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return null;   // no git on the path: the pass is still valid data
            }
        }

        // ---------- summary of several passes ----------

        private static int Summarize(string dir)
        {
            string[] files = Directory.GetFiles(dir, "pass-*.json").OrderBy(f => f, StringComparer.Ordinal).ToArray();
            if (files.Length == 0)
            {
                Console.Error.WriteLine("no pass-*.json in " + dir);
                return 2;
            }
            JObject[] passes = files.Select(Load).ToArray();
            JObject env = (JObject)passes[0]["environment"];
            JObject method = (JObject)passes[0]["method"];
            string[] names = passes[0]["benchmarks"].Select(b => (string)b["name"]).ToArray();
            int scale = (int)method["scale"];

            var sb = new StringBuilder();
            sb.Append("# Benchmarks: PyLite ").Append(env["pylite"]).Append(" and IronPython ").Append(env["ironpython"]).Append("\n\n");
            sb.Append(passes.Length).Append(scale == 1 ? " passes of `tools/PyLite.PerfBench --full`" : " passes of `tools/PyLite.PerfBench` at 1/" + scale + " scale")
                .Append(", each benchmark warmed up ").Append(method["warmup"]).Append(" times and measured ").Append(method["measured"])
                .Append(" times per pass. The samples are in the `pass-*.json` files next to this page.\n\n");
            sb.Append("| | |\n|---|---|\n");
            sb.Append("| PyLite | ").Append(env["pylite"]).Append(env["commit"] != null && env["commit"].Type != JTokenType.Null
                ? ", commit `" + env["commit"] + "`" : "").Append(" |\n");
            sb.Append("| IronPython | ").Append(env["ironpython"]).Append(" (NuGet) |\n");
            sb.Append("| OS | ").Append(env["os"]).Append(", build ").Append(env["osBuild"]).Append(" |\n");
            sb.Append("| CPU | ").Append(env["cpu"]).Append(", ").Append(env["logicalProcessors"]).Append(" logical processors |\n");
            sb.Append("| .NET | ").Append(env["dotnet"]).Append(" |\n");
            sb.Append("| passes | ").Append(string.Join(", ", passes.Select(p => (string)p["environment"]["utc"]))).Append(" |\n\n");

            sb.Append("Each cell gives the range across the passes. The median is the middle sample of a pass; the spread\n");
            sb.Append("is the fastest and the slowest single sample over all passes; allocations are the bytes the whole\n");
            sb.Append("process allocated per run. The verdict compares the medians pass by pass. Rows where PyLite is slower\n");
            sb.Append("stay in the table.\n\n");
            sb.Append("| benchmark | PyLite median | PyLite spread | PyLite allocated | IronPython median | IronPython spread | IronPython allocated | verdict |\n");
            sb.Append("|---|---:|---:|---:|---:|---:|---:|---|\n");
            foreach (string name in names)
            {
                JObject[] rows = passes.Select(p => (JObject)p["benchmarks"].First(b => (string)b["name"] == name)).ToArray();
                sb.Append("| `").Append(name).Append("` | ")
                    .Append(Cells(rows, "pylite")).Append(Cells(rows, "ironpython"))
                    .Append(Verdict(rows.Select(r => (double)r["ironpython"]["medianMs"] / (double)r["pylite"]["medianMs"]).ToArray()))
                    .Append(" |\n");
            }
            sb.Append("\n`cold_start` is one sample per pass: the first run in a fresh process, JIT included.\n");
            string outPath = Path.Combine(dir, "summary.md");
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine("summary -> " + outPath);
            return 0;
        }

        private static JObject Load(string path)
        {
            using (var reader = new JsonTextReader(new StringReader(File.ReadAllText(path))) { DateParseHandling = DateParseHandling.None })
                return JObject.Load(reader);
        }

        private static string Cells(JObject[] rows, string engine)
        {
            double[] medians = rows.Select(r => (double)r[engine]["medianMs"]).ToArray();
            double[] samples = rows.SelectMany(r => r[engine]["samplesMs"].Select(s => (double)s)).ToArray();
            long[] alloc = rows.Select(r => (long)r[engine]["allocBytesPerRun"]).ToArray();
            return Range(medians.Min(), medians.Max(), " ms") + " | " + Range(samples.Min(), samples.Max(), " ms") + " | "
                + Range(alloc.Min() / 1048576.0, alloc.Max() / 1048576.0, " MB") + " | ";
        }

        private static string Range(double lo, double hi, string unit)
        {
            string a = Num(lo), b = Num(hi);
            return (a == b ? a : a + "–" + b) + unit;
        }

        private static string Num(double v)
        {
            return v.ToString(v >= 100 ? "0" : v >= 1 ? "0.0" : "0.00", CultureInfo.InvariantCulture);
        }

        // ratio = IronPython / PyLite per pass: above 1 PyLite is faster.
        private static string Verdict(double[] ratios)
        {
            double lo = ratios.Min(), hi = ratios.Max();
            if (lo >= 1.05)
                return "×" + Ratio(lo, hi) + " faster";
            if (hi <= 1 / 1.05)
                return "×" + Ratio(1 / hi, 1 / lo) + " slower";
            return lo < 1 && hi > 1 && (lo < 1 / 1.05 || hi > 1.05) ? "about equal, direction varies" : "about equal";
        }

        private static string Ratio(double lo, double hi)
        {
            string fmt = hi >= 10 ? "0" : hi >= 2 ? "0.0" : "0.0#";
            string a = lo.ToString(fmt, CultureInfo.InvariantCulture), b = hi.ToString(fmt, CultureInfo.InvariantCulture);
            return a == b ? a : a + "–" + b;
        }
    }
}

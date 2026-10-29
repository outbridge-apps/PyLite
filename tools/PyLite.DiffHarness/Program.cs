using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Outbridge.PyLite.Hosting;

namespace Outbridge.PyLite.DiffHarness
{
    // T-TST-11/13: differential testing PyLite vs IronPython 3.4.2. Each corpus script sets `result`;
    // the harness runs both engines, normalizes the two values (float bitwise / dict-as-set), and
    // classifies divergences against known-differences.json. `--fail-on-bug` is the CI gate.
    internal static class Program
    {
        private static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            var opt = DiffOptions.Parse(args);
            if (opt == null)
                return 2;

            string[] scripts = Directory.Exists(opt.CorpusDir)
                ? Directory.GetFiles(opt.CorpusDir, "*.py", SearchOption.AllDirectories).OrderBy(p => p).ToArray()
                : Array.Empty<string>();
            if (scripts.Length == 0)
            {
                Console.Error.WriteLine("no .py scripts under " + opt.CorpusDir);
                return 2;
            }

            KnownDifferences known = KnownDifferences.Load(opt.KnownPath);
            var engine = new ScriptEngine(new EngineOptions
            {
                Clock = new SystemClock(TimeSpan.Zero, "UTC"),
                SleepPolicy = new NoOpSleepPolicy(),
                StrHashSeed = 0x12345678u,
                DefaultLimits = ResourceLimits.Batch,
            });
            var iron = new IronRunner();
            var rows = new List<DiffRow>();

            foreach (string path in scripts)
            {
                string name = path.Substring(opt.CorpusDir.Length).TrimStart('\\', '/').Replace('\\', '/');
                string source = File.ReadAllText(path).Replace("\r\n", "\n");
                EngineOutcome py = RunPyLite(engine, source);
                EngineOutcome ir = opt.Engine == "pylite" ? EngineOutcome.AsSkipped() : iron.Run(source);
                DiffRow row = Comparator.Compare(name, py, ir, known);
                rows.Add(row);
                Console.WriteLine(row.OneLine());
            }

            string report = opt.Report == "json" ? Reporter.Json(rows) : Reporter.Markdown(rows);
            if (opt.OutPath != null)
                File.WriteAllText(opt.OutPath, report, new UTF8Encoding(false));
            else
                Console.WriteLine(report);

            int bugs = rows.Count(r => r.Class == DiffClass.Bug || r.Class == DiffClass.EngineError);
            Console.WriteLine("\n" + rows.Count + " scripts | match=" + rows.Count(r => r.Class == DiffClass.Match)
                + " known=" + rows.Count(r => r.Class == DiffClass.Known)
                + " bug=" + rows.Count(r => r.Class == DiffClass.Bug)
                + " engineError=" + rows.Count(r => r.Class == DiffClass.EngineError));
            return opt.FailOnBug && bugs > 0 ? 1 : 0;
        }

        private static EngineOutcome RunPyLite(ScriptEngine engine, string source)
        {
            try
            {
                RunResult r = engine.Run(source, new RunRequest { ScalarOutputNames = new[] { "result" } });
                if (r.Succeeded)
                {
                    string v;
                    return r.ScalarsOut.TryGetValue("result", out v)
                        ? EngineOutcome.Ok(v)
                        : EngineOutcome.Ok("<no result>");
                }
                return EngineOutcome.Failure(r.Error.PythonType ?? r.Error.Kind.ToString(),
                    r.Error.Kind == ScriptErrorKind.EngineFault);
            }
            catch (Exception ex)
            {
                return EngineOutcome.Failure(ex.GetType().Name, engineFault: true);
            }
        }
    }
}

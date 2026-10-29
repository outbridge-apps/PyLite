using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Outbridge.PyLite.Hosting;

namespace Outbridge.PyLite.Stress
{
    // Runs every probe script in a directory under a fresh ScriptEngine and prints one line per probe:
    // outcome, wall ms, steps, allocated MB, peak call depth. A probe sets its own limits in header
    // comments: "# deadline: <ms>", "# allocmb: <MB>", "# repeat: <n>" (best of n), and for the limits
    // probes "# datadepth: <n>", "# sleepms: <ms>", "# steps: <n>" (a host raising or lowering them).
    //
    // The point is the steps column. A probe that does one operation over megabytes of data and leaves
    // a dozen steps on the counter has found a pass the budget does not see — and the deadline is
    // checked on the Step cadence, so the deadline does not see it either. The exit code is 1 when any
    // probe ends as HOSTERROR or a thrown host exception (a hang past the deadline, a crash); a clean
    // abort ("budget ...") or a script exception ("raise ...") is a pass — the sandbox held.
    //
    //   dotnet run -c Release -- bombs            # the whole corpus
    //   dotnet run -c Release -- bombs x7         # the probes whose name contains "x7"
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string dir = args.Length > 0 ? args[0] : "bombs";
            if (!Directory.Exists(dir))
            {
                Console.WriteLine("no probes dir: " + Path.GetFullPath(dir));
                return 2;
            }
            string filter = args.Length > 1 ? args[1] : null;

            string[] files = Directory.GetFiles(dir, "*.py");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            int failures = 0;
            Console.WriteLine("name                            outcome                             ms      cpu     steps    allocMB depth");
            foreach (string file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                string src = File.ReadAllText(file);
                int deadline = ReadInt(src, "# deadline:", 3000);
                long allocMb = ReadInt(src, "# allocmb:", 256);

                Console.Write(Pad(name, 31));
                Console.Out.Flush();

                ResourceLimits d = ResourceLimits.Default;
                var limits = d.With(deadlineMs: deadline, maxAllocBytes: allocMb << 20,
                    maxDataDepth: ReadInt(src, "# datadepth:", d.MaxDataDepth),
                    sleepTotalMs: ReadInt(src, "# sleepms:", d.SleepTotalMs),
                    maxSteps: ReadInt(src, "# steps:", (int)d.MaxSteps));
                var engine = new ScriptEngine(new EngineOptions
                {
                    DefaultLimits = limits,
                    CircuitBreakerLeakedThreads = 1000,
                    GlobalMemoryCeilingBytes = 2L << 30,
                });

                int repeat = ReadInt(src, "# repeat:", 1);
                RunResult r = null;
                string outcome = null;
                bool failed = false;
                long best = long.MaxValue;
                double bestCpu = double.MaxValue;
                Process proc = Process.GetCurrentProcess();
                for (int pass = 0; pass < repeat; pass++)
                {
                    // Two clocks: wall time, and the process's own CPU time, which other processes on
                    // the box (a scanner, a database) cannot inflate — the one to compare builds by.
                    TimeSpan cpu0 = proc.TotalProcessorTime;
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        r = engine.Run(src, new RunRequest { Limits = limits });
                        outcome = Describe(r, out failed);
                    }
                    catch (Exception ex)
                    {
                        r = null;
                        outcome = "THREW " + ex.GetType().Name;
                        failed = true;
                    }
                    sw.Stop();
                    proc.Refresh();
                    double cpuMs = (proc.TotalProcessorTime - cpu0).TotalMilliseconds;
                    if (sw.ElapsedMilliseconds < best)
                        best = sw.ElapsedMilliseconds;
                    if (cpuMs < bestCpu)
                        bestCpu = cpuMs;
                }
                if (failed)
                    failures++;

                RunStatistics st = r != null ? r.Stats : null;
                Console.WriteLine(Pad(outcome, 35)
                    + Pad(best.ToString(CultureInfo.InvariantCulture), 8)
                    + Pad(bestCpu.ToString("0", CultureInfo.InvariantCulture), 8)
                    + Pad(st != null ? st.Steps.ToString(CultureInfo.InvariantCulture) : "-", 11)
                    + Pad(st != null ? (st.AllocatedBytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture) : "-", 8)
                    + (st != null ? st.PeakCallDepth.ToString(CultureInfo.InvariantCulture) : "-"));
                if (r != null && !string.IsNullOrEmpty(r.PrintOutput))
                    Console.WriteLine(r.PrintOutput.TrimEnd());
                Console.Out.Flush();
            }
            Console.WriteLine(failures == 0 ? "sandbox held on every probe" : failures + " probe(s) escaped the sandbox");
            return failures == 0 ? 0 : 1;
        }

        private static string Describe(RunResult r, out bool failed)
        {
            failed = false;
            if (r.Succeeded)
                return "ok";
            ScriptError e = r.Error;
            switch (e.Kind)
            {
                case ScriptErrorKind.BudgetExceeded:
                    return "budget " + e.LimitName;
                case ScriptErrorKind.RuntimeError:
                    return "raise " + e.PythonType + ": " + Clip(e.Message, 20);
                case ScriptErrorKind.HostError:
                    failed = true;
                    return "HOSTERROR " + Clip(e.Message, 24);
                case ScriptErrorKind.SyntaxError:
                    return "SYNTAX " + Clip(e.Message, 200);
                default:
                    return e.Kind + " " + Clip(e.Message, 24);
            }
        }

        private static string Clip(string s, int n)
        {
            if (s == null)
                return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= n ? s : s.Substring(0, n);
        }

        private static string Pad(string s, int width)
        {
            if (s.Length >= width)
                return s.Substring(0, width - 1) + " ";
            return s + new string(' ', width - s.Length);
        }

        private static int ReadInt(string src, string tag, int fallback)
        {
            int i = src.IndexOf(tag, StringComparison.Ordinal);
            if (i < 0)
                return fallback;
            int j = i + tag.Length;
            while (j < src.Length && src[j] == ' ')
                j++;
            int k = j;
            while (k < src.Length && src[k] >= '0' && src[k] <= '9')
                k++;
            int v;
            return int.TryParse(src.Substring(j, k - j), out v) ? v : fallback;
        }
    }
}

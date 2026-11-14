using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Outbridge.PyLite.Hosting;

namespace Outbridge.PyLite.Stress
{
    // Runs every probe script in a directory under a fresh ScriptEngine and prints one line per probe:
    // outcome, wall ms, steps, allocated MB, peak call depth. A probe sets its own limits in header
    // comments: "# deadline: <ms>", "# allocmb: <MB>", "# repeat: <n>" (best of n), and for the limits
    // probes "# datadepth: <n>", "# sleepms: <ms>", "# steps: <n>", "# items: <n>" (a host raising or lowering them).
    //
    // The point is the steps column. A probe that does one operation over megabytes of data and leaves
    // a dozen steps on the counter has found a pass the budget does not see — and the deadline is
    // checked on the Step cadence, so the deadline does not see it either.
    //
    // A probe fails (exit code 1) when it ends as HOSTERROR, an engine fault or a thrown host exception,
    // returns more than OvershootSlackMs past its deadline, leaks its runner thread, grows the process past
    // PeakFactor times its memory limit (memory the budget never saw), keeps a core busy after it returned
    // (work that outlived the run), or ends in another
    // kind of outcome than bombs/expected.txt pins for it (ok / raise <Type> / budget / syntax; the limit a
    // budget probe hits first is the machine's, so it is not pinned, and a probe the deadline stops, or that
    // takes half of it, may finish on a faster machine: "ok|budget"). --tsv writes the rows for the gate.
    //
    //   dotnet run -c Release -- bombs                     # the whole corpus
    //   dotnet run -c Release -- bombs x7                  # the probes whose name contains "x7"
    //   dotnet run -c Release -- bombs --tsv rows.tsv      # also the rows, tab-separated
    //   dotnet run -c Release -- bombs --write-expected    # pin the outcomes of this run (review the diff)
    internal static class Program
    {
        internal const int OvershootSlackMs = 500;
        internal const long PeakFactor = 4;             // the working set may hold the charged bytes this many times over
        internal const long PeakSlackBytes = 512L << 20;
        internal const int AfterWindowMs = 250;         // the quiet window measured after the run returns
        internal const double BusyAfterShare = 0.6;     // CPU over that window, as a share of one core
        private const string ExpectedFile = "expected.txt";

        private static int Main(string[] args)
        {
            string dir = "bombs", filter = null, tsv = null;
            bool writeExpected = false, writePins = false;
            var positional = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--tsv" && i + 1 < args.Length)
                    tsv = args[++i];
                else if (args[i] == "--write-expected")
                    writeExpected = true;
                else if (args[i] == "--write-pins")
                    writePins = true;
                else
                    positional.Add(args[i]);
            }
            if (positional.Count > 0)
                dir = positional[0];
            if (positional.Count > 1)
                filter = positional[1];
            if (!Directory.Exists(dir))
            {
                Console.WriteLine("no probes dir: " + Path.GetFullPath(dir));
                return 2;
            }
            if (writePins || File.Exists(Path.Combine(dir, Bank.PinsFile)))
                return Bank.Run(dir, filter, tsv, writePins);   // the probe bank: pinned outputs, steps, charges (Bank.cs)

            string[] files = Directory.GetFiles(dir, "*.py");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            string expectedPath = Path.Combine(dir, ExpectedFile);
            Dictionary<string, string> expected = ReadExpected(expectedPath);
            var pinned = new SortedDictionary<string, string>(expected, StringComparer.Ordinal);

            int nameWidth = 5;
            foreach (string file in files)
                nameWidth = Math.Max(nameWidth, Path.GetFileNameWithoutExtension(file).Length + 2);

            var rows = new StringBuilder();
            rows.Append("name\toutcome\tms\tcpu\tsteps\tallocMB\tdepth\tflags\tpeakMB\n");
            int failures = 0;
            Console.WriteLine(Pad("name", nameWidth) + "outcome                             ms      cpu     steps    allocMB depth");
            foreach (string file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                string src = File.ReadAllText(file);
                int deadline = ReadInt(src, "# deadline:", 3000);
                long allocMb = ReadInt(src, "# allocmb:", 256);

                Console.Write(Pad(name, nameWidth));
                Console.Out.Flush();

                ResourceLimits d = ResourceLimits.Default;
                var limits = d.With(deadlineMs: deadline, maxAllocBytes: allocMb << 20,
                    maxDataDepth: ReadInt(src, "# datadepth:", d.MaxDataDepth),
                    sleepTotalMs: ReadInt(src, "# sleepms:", d.SleepTotalMs),
                    maxSteps: ReadInt(src, "# steps:", (int)d.MaxSteps),
                    maxCollectionItems: ReadInt(src, "# items:", d.MaxCollectionItems));
                int leaks = 0;
                var engine = new ScriptEngine(new EngineOptions
                {
                    DefaultLimits = limits,
                    CircuitBreakerLeakedThreads = 1000,
                    EventSink = ev => { if (ev.Kind == EngineEventKind.ThreadLeaked) Interlocked.Increment(ref leaks); },
                });

                int repeat = ReadInt(src, "# repeat:", 1);
                RunResult r = null;
                string outcome = null;
                string kind = null;
                string flags = "";
                long best = long.MaxValue, bestRun = long.MaxValue;   // bestRun: past the compile, where the deadline starts
                double bestCpu = double.MaxValue;
                Process proc = Process.GetCurrentProcess();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                proc.Refresh();
                var peak = new PeakSampler(proc.WorkingSet64);
                for (int pass = 0; pass < repeat; pass++)
                {
                    // Two clocks: wall time, and the process's own CPU time, which other processes on
                    // the box (a scanner, a database) cannot inflate — the one to compare builds by.
                    TimeSpan cpu0 = proc.TotalProcessorTime;
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        r = engine.Run(src, new RunRequest { Limits = limits });
                        outcome = Describe(r, out kind);
                    }
                    catch (Exception ex)
                    {
                        r = null;
                        outcome = "THREW " + ex.GetType().Name;
                        kind = "THREW";
                    }
                    sw.Stop();
                    proc.Refresh();
                    double cpuMs = (proc.TotalProcessorTime - cpu0).TotalMilliseconds;
                    if (sw.ElapsedMilliseconds < best)
                        best = sw.ElapsedMilliseconds;
                    long compileMs = r != null && r.Stats != null ? (long)r.Stats.CompileTime.TotalMilliseconds : 0;
                    if (sw.ElapsedMilliseconds - compileMs < bestRun)
                        bestRun = sw.ElapsedMilliseconds - compileMs;
                    if (cpuMs < bestCpu)
                        bestCpu = cpuMs;
                }
                long peakBytes = peak.Stop();
                GC.Collect();   // the collector's own work on what the probe dropped is not work that outlived it
                GC.WaitForPendingFinalizers();
                proc.Refresh();
                TimeSpan cpuIdle = proc.TotalProcessorTime;
                Thread.Sleep(AfterWindowMs);
                proc.Refresh();
                double busyAfter = (proc.TotalProcessorTime - cpuIdle).TotalMilliseconds / AfterWindowMs;

                if (kind == "HOSTERROR" || kind == "ENGINEFAULT" || kind == "THREW")
                    flags += " " + kind;
                if (bestRun > deadline + OvershootSlackMs)
                    flags += " OVERSHOOT";
                if (Volatile.Read(ref leaks) > 0)
                    flags += " LEAK";
                if (peakBytes > PeakFactor * (allocMb << 20) + PeakSlackBytes)
                    flags += " PEAKMEM";
                if (busyAfter > BusyAfterShare)
                    flags += " BUSYAFTER";
                string want;
                if (writeExpected)
                    pinned[name] = DeadlineBound(r, kind, bestRun, deadline) ? "ok|budget" : kind;
                else if (!expected.TryGetValue(name, out want))
                    flags += " UNPINNED";
                else if (Array.IndexOf(want.Split('|'), kind) < 0)
                    flags += " EXPECTED(" + want + ")";
                if (flags.Length > 0)
                    failures++;

                RunStatistics st = r != null ? r.Stats : null;
                string steps = st != null ? st.Steps.ToString(CultureInfo.InvariantCulture) : "-";
                string mb = st != null ? (st.AllocatedBytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture) : "-";
                string depth = st != null ? st.PeakCallDepth.ToString(CultureInfo.InvariantCulture) : "-";
                Console.WriteLine(Pad(outcome, 35)
                    + Pad(best.ToString(CultureInfo.InvariantCulture), 8)
                    + Pad(bestCpu.ToString("0", CultureInfo.InvariantCulture), 8)
                    + Pad(steps, 11)
                    + Pad(mb, 8)
                    + depth + flags);
                if (r != null && !string.IsNullOrEmpty(r.PrintOutput))
                    Console.WriteLine(r.PrintOutput.TrimEnd());
                Console.Out.Flush();
                rows.Append(name).Append('\t').Append(outcome.Replace('\t', ' ')).Append('\t')
                    .Append(best.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(bestCpu.ToString("0", CultureInfo.InvariantCulture)).Append('\t')
                    .Append(steps).Append('\t').Append(mb).Append('\t').Append(depth).Append('\t')
                    .Append(flags.Trim()).Append('\t')
                    .Append((peakBytes >> 20).ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
            if (tsv != null)
                File.WriteAllText(tsv, rows.ToString(), new UTF8Encoding(false));
            if (writeExpected)
            {
                var sb = new StringBuilder("# The kind of outcome each probe must end in (written by --write-expected).\n");
                foreach (KeyValuePair<string, string> kv in pinned)
                    sb.Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
                File.WriteAllText(expectedPath, sb.ToString(), new UTF8Encoding(false));
                Console.WriteLine("pinned " + pinned.Count + " outcomes -> " + expectedPath);
            }
            Console.WriteLine(failures == 0 ? "sandbox held on every probe" : failures + " probe(s) escaped the sandbox");
            return failures == 0 ? 0 : 1;
        }

        // The line's outcome, and its kind as pinned: ok, raise <Type>, budget, syntax; HOSTERROR, ENGINEFAULT
        // and THREW fail whatever is pinned.
        internal static string Describe(RunResult r, out string kind)
        {
            if (r.Succeeded)
            {
                kind = "ok";
                return "ok";
            }
            ScriptError e = r.Error;
            switch (e.Kind)
            {
                case ScriptErrorKind.BudgetExceeded:
                    kind = "budget";
                    return "budget " + e.LimitName;
                case ScriptErrorKind.RuntimeError:
                    kind = "raise " + e.PythonType;
                    return "raise " + e.PythonType + ": " + Clip(e.Message, 20);
                case ScriptErrorKind.HostError:
                    kind = "HOSTERROR";
                    return "HOSTERROR " + Clip(e.Message, 24);
                case ScriptErrorKind.SyntaxError:
                    kind = "syntax";
                    return "SYNTAX " + Clip(e.Message, 200);
                default:
                    kind = "ENGINEFAULT";
                    return e.Kind + " " + Clip(e.Message, 24);
            }
        }

        internal static bool DeadlineBound(RunResult r, string kind, long ms, int deadline)
        {
            if (kind == "budget")
                return r != null && r.Error != null && r.Error.LimitName == "DeadlineMs";
            return kind == "ok" && ms > deadline / 2;
        }

        private static Dictionary<string, string> ReadExpected(string path)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(path))
                return map;
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.Length == 0 || line[0] == '#')
                    continue;
                int tab = line.IndexOf('\t');
                if (tab > 0)
                    map[line.Substring(0, tab)] = line.Substring(tab + 1).Trim();
            }
            return map;
        }

        private static string Clip(string s, int n)
        {
            if (s == null)
                return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= n ? s : s.Substring(0, n);
        }

        // The process's working set above where the probe began, sampled every 20 ms while it runs.
        internal sealed class PeakSampler
        {
            private readonly long _base;
            private long _peak;
            private volatile bool _stop;
            private readonly Thread _thread;

            internal PeakSampler(long baseBytes)
            {
                _base = baseBytes;
                _peak = baseBytes;
                _thread = new Thread(Sample) { IsBackground = true, Name = "stress-peak" };
                _thread.Start();
            }

            private void Sample()
            {
                Process p = Process.GetCurrentProcess();
                while (!_stop)
                {
                    p.Refresh();
                    if (p.WorkingSet64 > _peak)
                        _peak = p.WorkingSet64;
                    Thread.Sleep(20);
                }
            }

            internal long Stop()
            {
                _stop = true;
                _thread.Join();
                return Math.Max(0, _peak - _base);
            }
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Outbridge.PyLite.Hosting;

namespace Outbridge.PyLite.Stress
{
    // The probe bank: every probe the pre-release reviews wrote (rounds 8-10, `bank/`), each pinned in `bank/pinned.tsv` to
    // what the approved build gave - the kind of outcome, a hash of its print output, its steps and charged KB - and, for
    // the probes 1.1.0 ran to the end, to 1.1.0's steps and MB as a ceiling. A row fails when:
    //   - the kind of outcome is not the pinned one (ok / raise <Type> / budget / syntax; "ok|budget" near the deadline),
    //     or the run faults, overshoots, leaks, outgrows its memory limit or stays busy (as the Stress corpus);
    //   - the output differs from the pinned hash (a wrong answer);
    //   - steps or charged memory grow past 1.25 times the pin (a new false limit or a hidden slowdown on its way);
    //   - a probe 1.1.0 ran is refused, or needs more than 1.5 times 1.1.0's steps or memory, unless its note says why
    //     (a documented charge).
    // Steps and charges are deterministic, so the gate does not flicker; a probe whose output, steps or charge moved
    // between the two runs of --write-pins is pinned "-" in that column. Steps and timings depend on nothing else.
    //
    //   dotnet run -c Release -- bank                    # check every probe
    //   dotnet run -c Release -- bank x11                # the probes whose name contains "x11"
    //   dotnet run -c Release -- bank --write-pins       # pin this build (keeps the 1.1.0 columns and notes; review the diff)
    internal static class Bank
    {
        internal const string PinsFile = "pinned.tsv";
        private const double GrowthFactor = 1.25, OldFactor = 1.5;
        private const long StepSlack = 20000, KbSlack = 512, OldStepSlack = 50000, OldMbSlack = 2;

        private sealed class Pin
        {
            public string Kind = "-", Out = "-", Steps = "-", Kb = "-", Steps110 = "-", Mb110 = "-", Note = "-";
        }

        private sealed class Result
        {
            public string Outcome, Kind, Hash, Flags = "";
            public long Steps = -1, Kb = -1, Ms, PeakBytes;
            public bool DeadlineBound;
        }

        internal static int Run(string dir, string filter, string tsv, bool writePins)
        {
            string[] files = Directory.GetFiles(dir, "*.py");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            string pinsPath = Path.Combine(dir, PinsFile);
            SortedDictionary<string, Pin> pins = Read(pinsPath);
            var rows = new StringBuilder("name\toutcome\tms\tsteps\tKB\tflags\tpeakMB\n");
            int failures = 0, ran = 0;
            foreach (string file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                ran++;
                string src = File.ReadAllText(file);
                Result r = RunOne(src, name);
                Pin pin;
                if (!pins.TryGetValue(name, out pin))
                    pins[name] = pin = new Pin();
                string flags = r.Flags;
                if (writePins)
                {
                    Result again = RunOne(src, name);
                    pin.Kind = r.DeadlineBound || again.DeadlineBound || again.Kind != r.Kind ? "ok|budget" : r.Kind;
                    // a probe that prints its own timings has no output to pin (two runs can still agree by chance)
                    bool timed = src.IndexOf("perf_counter", StringComparison.Ordinal) >= 0 || src.IndexOf("time.time", StringComparison.Ordinal) >= 0
                        || src.IndexOf("monotonic", StringComparison.Ordinal) >= 0 || src.IndexOf("process_time", StringComparison.Ordinal) >= 0;
                    pin.Out = r.Kind == "ok" && !timed && again.Hash == r.Hash ? r.Hash : "-";
                    pin.Steps = Stable(r.Steps, again.Steps, 0.02) ? r.Steps.ToString(CultureInfo.InvariantCulture) : "-";
                    pin.Kb = Stable(r.Kb, again.Kb, 0.05) ? r.Kb.ToString(CultureInfo.InvariantCulture) : "-";
                }
                else
                {
                    flags += Check(r, pin);
                }
                if (flags.Length > 0)
                    failures++;
                Console.WriteLine(name + "  " + r.Outcome + "  steps=" + r.Steps + " KB=" + r.Kb + " ms=" + r.Ms + flags);
                Console.Out.Flush();
                rows.Append(name).Append('\t').Append(r.Outcome.Replace('\t', ' ')).Append('\t').Append(r.Ms).Append('\t')
                    .Append(r.Steps).Append('\t').Append(r.Kb).Append('\t').Append(flags.Trim()).Append('\t')
                    .Append(r.PeakBytes >> 20).Append('\n');
            }
            if (tsv != null)
                File.WriteAllText(tsv, rows.ToString(), new UTF8Encoding(false));
            if (writePins)
            {
                Write(pinsPath, pins);
                Console.WriteLine("pinned " + ran + " probes -> " + pinsPath);
                return 0;
            }
            Console.WriteLine(failures == 0 ? "the bank held on every probe (" + ran + ")" : failures + " probe(s) moved off their pins");
            return failures == 0 ? 0 : 1;
        }

        private static string Check(Result r, Pin pin)
        {
            string flags = "";
            if (pin.Kind == "-")
                return " UNPINNED";
            if (Array.IndexOf(pin.Kind.Split('|'), r.Kind) < 0)
                flags += " EXPECTED(" + pin.Kind + ")";
            if (pin.Out != "-" && r.Kind == "ok" && r.Hash != pin.Out)
                flags += " OUTPUT";
            long v;
            if (r.Steps >= 0 && Long(pin.Steps, out v) && r.Steps > v * GrowthFactor + StepSlack)
                flags += " STEPS(" + Ratio(r.Steps, v) + "x pin)";
            if (r.Kb >= 0 && Long(pin.Kb, out v) && r.Kb > v * GrowthFactor + KbSlack)
                flags += " ALLOC(" + Ratio(r.Kb, v) + "x pin)";
            if (pin.Note == "-" && Long(pin.Steps110, out v))
            {
                if (r.Kind != "ok" && pin.Kind.IndexOf("budget", StringComparison.Ordinal) < 0)
                    flags += " REFUSED(1.1.0 ran it)";
                else if (r.Steps > v * OldFactor + OldStepSlack)
                    flags += " STEPS(" + Ratio(r.Steps, v) + "x 1.1.0)";
                long mb;
                if (Long(pin.Mb110, out mb) && r.Kb >= 0 && (r.Kb >> 10) > mb * OldFactor + OldMbSlack)
                    flags += " ALLOC(" + Ratio(r.Kb >> 10, mb) + "x 1.1.0)";
            }
            return flags;
        }

        private static Result RunOne(string src, string name)
        {
            ResourceLimits d = ResourceLimits.Default;
            int deadline = Int(src, "# deadline:", 3000);
            long allocMb = Int(src, "# allocmb:", 256);
            ResourceLimits limits = d.With(deadlineMs: deadline, maxAllocBytes: allocMb << 20,
                maxSteps: Int(src, "# steps:", (int)Math.Min(int.MaxValue, d.MaxSteps)),
                maxCallDepth: Int(src, "# calldepth:", d.MaxCallDepth),
                maxDataDepth: Int(src, "# datadepth:", d.MaxDataDepth),
                maxStrChars: Int(src, "# strchars:", d.MaxStrChars),
                maxCollectionItems: Int(src, "# items:", d.MaxCollectionItems),
                maxIntBits: Int(src, "# intbits:", d.MaxIntBits),
                maxOutputBytes: Int(src, "# outputbytes:", d.MaxOutputBytes),
                regexPerOpTimeoutMs: Int(src, "# regexms:", d.RegexPerOpTimeoutMs),
                sleepTotalMs: Int(src, "# sleepms:", d.SleepTotalMs));
            int leaks = 0;
            var engine = new ScriptEngine(new EngineOptions
            {
                DefaultLimits = limits,
                CircuitBreakerLeakedThreads = 1000,
                EventSink = ev => { if (ev.Kind == EngineEventKind.ThreadLeaked) Interlocked.Increment(ref leaks); },
            });
            var res = new Result();
            Process proc = Process.GetCurrentProcess();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            proc.Refresh();
            var peak = new Program.PeakSampler(proc.WorkingSet64);
            int repeat = Math.Max(1, Int(src, "# repeat:", 1));
            RunResult rr = null;
            long bestRun = long.MaxValue;
            for (int pass = 0; pass < repeat; pass++)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    rr = engine.Run(src, new RunRequest { Limits = limits });
                    res.Outcome = Program.Describe(rr, out res.Kind);
                }
                catch (Exception ex)
                {
                    rr = null;
                    res.Outcome = "THREW " + ex.GetType().Name;
                    res.Kind = "THREW";
                }
                sw.Stop();
                long compileMs = rr != null && rr.Stats != null ? (long)rr.Stats.CompileTime.TotalMilliseconds : 0;
                bestRun = Math.Min(bestRun, sw.ElapsedMilliseconds - compileMs);
                res.Ms = sw.ElapsedMilliseconds;
            }
            res.PeakBytes = peak.Stop();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            proc.Refresh();
            TimeSpan cpuIdle = proc.TotalProcessorTime;
            Thread.Sleep(Program.AfterWindowMs);
            proc.Refresh();
            double busyAfter = (proc.TotalProcessorTime - cpuIdle).TotalMilliseconds / Program.AfterWindowMs;
            if (res.Kind == "HOSTERROR" || res.Kind == "ENGINEFAULT" || res.Kind == "THREW")
                res.Flags += " " + res.Kind;
            if (bestRun > deadline + Program.OvershootSlackMs)
                res.Flags += " OVERSHOOT";
            if (Volatile.Read(ref leaks) > 0)
                res.Flags += " LEAK";
            if (res.PeakBytes > Program.PeakFactor * (allocMb << 20) + Program.PeakSlackBytes)
                res.Flags += " PEAKMEM";
            if (busyAfter > Program.BusyAfterShare)
                res.Flags += " BUSYAFTER";
            res.DeadlineBound = Program.DeadlineBound(rr, res.Kind, bestRun, deadline);
            if (rr != null && rr.Stats != null)
            {
                res.Steps = rr.Stats.Steps;
                res.Kb = rr.Stats.AllocatedBytes >> 10;
            }
            res.Hash = Hash(rr != null ? rr.PrintOutput ?? "" : "");
            return res;
        }

        private static bool Stable(long a, long b, double share)
        {
            return a >= 0 && b >= 0 && Math.Abs(a - b) <= Math.Max(a, b) * share;
        }

        private static string Hash(string s)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++)
                    sb.Append(h[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        private static string Ratio(long a, long b)
        {
            return b <= 0 ? "inf" : ((double)a / b).ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static bool Long(string s, out long v)
        {
            return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        }

        private static int Int(string src, string tag, int fallback)
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
            return int.TryParse(src.Substring(j, k - j), NumberStyles.None, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static SortedDictionary<string, Pin> Read(string path)
        {
            var map = new SortedDictionary<string, Pin>(StringComparer.Ordinal);
            if (!File.Exists(path))
                return map;
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.Length == 0 || line[0] == '#')
                    continue;
                string[] f = line.Split('\t');
                if (f.Length < 8)
                    continue;
                map[f[0]] = new Pin { Kind = f[1], Out = f[2], Steps = f[3], Kb = f[4], Steps110 = f[5], Mb110 = f[6], Note = f[7] };
            }
            return map;
        }

        private static void Write(string path, SortedDictionary<string, Pin> pins)
        {
            var sb = new StringBuilder();
            sb.Append("# name\tkind\toutput hash\tsteps\tcharged KB\t1.1.0 steps\t1.1.0 MB\tnote (why a 1.1.0 ceiling does not apply)\n");
            sb.Append("# Written by `dotnet run -c Release -- bank --write-pins` (tools/PyLite.Stress); \"-\" = not pinned.\n");
            foreach (KeyValuePair<string, Pin> kv in pins)
            {
                Pin p = kv.Value;
                sb.Append(kv.Key).Append('\t').Append(p.Kind).Append('\t').Append(p.Out).Append('\t').Append(p.Steps).Append('\t')
                    .Append(p.Kb).Append('\t').Append(p.Steps110).Append('\t').Append(p.Mb110).Append('\t').Append(p.Note).Append('\n');
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}

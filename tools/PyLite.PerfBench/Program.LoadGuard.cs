using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Syntax;

namespace Outbridge.PyLite.PerfBench
{
    internal static partial class Program
    {
        // ---------- the load guard ----------
        //
        // This box is shared: another session's test battery can take most of the CPU for minutes, and the
        // two arms run minutes apart. A fixed PyLite script is measured every ControlEveryRows rows exactly
        // as a row is (median of samples, same runner thread, same GC). Three rules follow from it:
        //   * a row is "disturbed" when other processes took more than MaxOthersPercent of the machine while
        //     it was measured, or when a control reading next to it is above SpikeFactor x the run's median;
        //     disturbed rows are left out of every verdict, like rows within noise. The per-row share is what
        //     catches a burst of 10-20 s that falls between two control readings (seen 2026-10-27: math/* and
        //     random/random measured 2.5x slow in one of two runs while both controls next to them read normal);
        //   * if the two arms' median controls differ by more than MaxArmBias, the arms ran on different
        //     machines in effect, and the run is not comparable;
        //   * so is a run with more than MaxDisturbedShare of its rows disturbed.
        // Measured on this box (2026-10-27): a quiet stretch still moves the control by about 30%, so a
        // single max/min threshold fires on every run; another session's battery moves it 10-30x. A raw
        // C# loop timed best-of-N was tried first and missed a contention that tripled every row: the best
        // of N finds a quiet window, a median does not.

        private static int ControlEveryRows = 5;
        private const double SpikeFactor = 1.5;
        private const double MaxArmBias = 1.25;
        private const double MaxDisturbedShare = 0.20;
        private const double MaxOthersPercent = 25;   // quiet baseline here is 4-15% (a database server, the antivirus, the host application)
        private const string ControlScript =
            "s = 0\nfor i in range(20000):\n    s = s + i * 3\nd = {}\nfor i in range(2000):\n    d[str(i)] = i\n"
            + "lst = sorted(d.values(), reverse=True)\nresult = s + len(lst)\n";

        private sealed class ControlReading
        {
            public bool Iron;
            public int BeforeRow;   // the index of the row measured next; rows.Count for the reading at the end
            public double Ms;
        }

        private sealed class LoadVerdict
        {
            public double ArmBias = 1;
            public int Disturbed;
            public bool Changed;
            public string Why;
        }

        private static readonly List<ControlReading> _control = new List<ControlReading>();
        private static Action _controlRun;
        private static int _controlRuns;

        private static void SampleControl(bool iron, int beforeRow)
        {
            if (_controlRun == null)
            {
                var engine = new ScriptEngine(new EngineOptions { DefaultLimits = ResourceLimits.Batch });
                CompiledScript compiled = engine.Compile(ControlScript, "control");
                var request = new RunRequest();
                _controlRun = () =>
                {
                    RunResult r = engine.Run(compiled, request);
                    if (!r.Succeeded)
                        throw new InvalidOperationException("control script failed: " + r.Error.Message);
                };
                _controlRuns = RunsPerSample(_controlRun);   // fixed for the whole run, so readings compare
                MeasureSamples(_controlRun, _controlRuns);   // the first reading after calibration runs high: discard it
                OthersCpuPercent();
            }
            double median = MeasureSamples(_controlRun, _controlRuns).MedianMs;
            _control.Add(new ControlReading { Iron = iron, BeforeRow = beforeRow, Ms = median });
            Console.WriteLine("   [control: " + FmtFine(median) + " ms; other processes "
                + OthersCpuPercent().ToString("0", CultureInfo.InvariantCulture) + "% of the machine; heap "
                + (GC.GetTotalMemory(false) >> 20) + " MB]");
        }

        private static LoadVerdict JudgeLoad(List<MatrixRow> rows, bool noIron)
        {
            var load = new LoadVerdict();
            if (_control.Count == 0)
                return load;
            double all = Median(_control.Select(c => c.Ms));
            double spike = all * SpikeFactor;
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].PyDisturbed = rows[i].PyOthers > MaxOthersPercent || Neighbours(false, i).Any(c => c.Ms > spike);
                rows[i].IronDisturbed = !noIron && (rows[i].IronOthers > MaxOthersPercent || Neighbours(true, i).Any(c => c.Ms > spike));
            }
            load.Disturbed = rows.Count(r => r.PyDisturbed || r.IronDisturbed);
            if (!noIron)
            {
                double py = Median(_control.Where(c => !c.Iron).Select(c => c.Ms));
                double iron = Median(_control.Where(c => c.Iron).Select(c => c.Ms));
                load.ArmBias = iron / py;
            }
            if (load.ArmBias > MaxArmBias || load.ArmBias < 1 / MaxArmBias)
            {
                load.Changed = true;
                load.Why = "the arms' median controls differ x" + load.ArmBias.ToString("0.00", CultureInfo.InvariantCulture);
            }
            else if (rows.Count > 0 && load.Disturbed > MaxDisturbedShare * rows.Count)
            {
                load.Changed = true;
                load.Why = load.Disturbed + " of " + rows.Count + " rows were measured next to a control spike";
            }
            return load;
        }

        // The control readings on either side of row `row` in one arm.
        private static IEnumerable<ControlReading> Neighbours(bool iron, int row)
        {
            ControlReading before = null, after = null;
            foreach (ControlReading c in _control)
            {
                if (c.Iron != iron)
                    continue;
                if (c.BeforeRow <= row)
                    before = c;
                else if (after == null)
                    after = c;
            }
            if (before != null)
                yield return before;
            if (after != null)
                yield return after;
        }

        private static double Median(IEnumerable<double> values)
        {
            double[] a = values.ToArray();
            if (a.Length == 0)
                return 1;
            Array.Sort(a);
            return a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2;
        }

        private static Dictionary<int, double> _othersCpu;
        private static DateTime _othersAt;

        // CPU taken by every other process since the previous call, as a share of all cores: tells
        // another session's load apart from this process slowing itself down. Per process, so a process
        // that exits in between cannot make the share negative; one that starts counts in full.
        private static double OthersCpuPercent()
        {
            int self = Process.GetCurrentProcess().Id;
            var now = new Dictionary<int, double>();
            double delta = 0;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == self)
                        continue;
                    double cpu = p.TotalProcessorTime.TotalMilliseconds;
                    now[p.Id] = cpu;
                    double before;
                    if (_othersCpu != null && _othersCpu.TryGetValue(p.Id, out before) && cpu >= before)
                        delta += cpu - before;
                    else if (_othersCpu != null)
                        delta += cpu;
                }
                catch (Exception)
                {
                    // system processes refuse the query; their share is not what this looks for
                }
                finally
                {
                    p.Dispose();
                }
            }
            DateTime at = DateTime.UtcNow;
            double pct = 0;
            if (_othersCpu != null)
            {
                double wallMs = (at - _othersAt).TotalMilliseconds * Environment.ProcessorCount;
                pct = wallMs > 0 ? 100 * delta / wallMs : 0;
            }
            _othersCpu = now;
            _othersAt = at;
            return pct;
        }
    }
}

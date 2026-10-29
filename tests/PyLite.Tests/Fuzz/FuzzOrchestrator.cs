using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Syntax;

namespace Outbridge.PyLite.Tests.Fuzz
{
    // T-TST-09 mutator: token/line-level edits on valid corpus scripts. Deliberately may yield invalid
    // syntax — we test robustness, not validity. Deterministic by seed.
    internal sealed class CorpusMutator
    {
        private static readonly string[] BigNums = { "0", "-1", "10**9", "2**63", "10**500000", "1e308", "-0.0" };
        private static readonly string[] Ops = { "+", "*", "**", "<<", ">>", "//", "%", "&", "|" };
        private readonly Random _rng;

        internal CorpusMutator(int seed) { _rng = new Random(seed); }

        internal string Mutate(string source, int mutations = 3)
        {
            var lines = new List<string>(source.Replace("\r\n", "\n").Split('\n'));
            for (int i = 0; i < mutations && lines.Count > 0; i++)
                ApplyOne(lines);
            return string.Join("\n", lines);
        }

        private void ApplyOne(List<string> lines)
        {
            int idx = _rng.Next(lines.Count);
            switch (_rng.Next(9))
            {
                case 0: lines[idx] = ReplaceDigits(lines[idx], Pick(BigNums)); break;
                case 1: lines[idx] = lines[idx] + " " + Pick(Ops) + " " + Pick(BigNums); break;
                case 2: lines.Insert(idx, lines[idx]); break;                       // duplicate a line
                case 3: lines.RemoveAt(idx); break;                                 // delete a line
                case 4: lines[idx] = ReplaceOp(lines[idx]); break;
                case 5: lines[idx] = "(" + lines[idx].TrimStart() + ")"; break;     // wrap (may break indent)
                case 6: lines[idx] = "    " + lines[idx]; break;                    // extra indent
                case 7: lines[idx] = lines[idx].Replace("=", "=="); break;
                default: lines[idx] = "try:\n    " + lines[idx].TrimStart() + "\nexcept:\n    pass"; break;
            }
        }

        private static string ReplaceDigits(string line, string repl)
        {
            var sb = new StringBuilder();
            bool replaced = false;
            for (int i = 0; i < line.Length; i++)
            {
                if (!replaced && char.IsDigit(line[i]))
                {
                    while (i < line.Length && char.IsDigit(line[i]))
                        i++;
                    sb.Append(repl);
                    replaced = true;
                    i--;
                }
                else
                    sb.Append(line[i]);
            }
            return replaced ? sb.ToString() : line;
        }

        private string ReplaceOp(string line)
        {
            foreach (string op in Ops)
                if (line.Contains(op))
                    return ReplaceFirst(line, op, Pick(Ops));
            return line;
        }

        private static string ReplaceFirst(string s, string find, string repl)
        {
            int i = s.IndexOf(find, StringComparison.Ordinal);
            return i < 0 ? s : s.Substring(0, i) + repl + s.Substring(i + find.Length);
        }

        private string Pick(string[] a) { return a[_rng.Next(a.Length)]; }
    }

    // T-TST-09 delta minimizer: line-wise ddmin shrinking a crashing source while the predicate holds.
    internal static class DeltaMinimizer
    {
        internal static string Minimize(string source, Func<string, bool> stillCrashes)
        {
            string[] lines = source.Replace("\r\n", "\n").Split('\n');
            var kept = new List<string>(lines);
            bool changed = true;
            int guard = 0;
            while (changed && guard++ < 200)
            {
                changed = false;
                for (int i = 0; i < kept.Count; i++)
                {
                    var trial = new List<string>(kept);
                    trial.RemoveAt(i);
                    if (trial.Count > 0 && stillCrashes(string.Join("\n", trial)))
                    {
                        kept = trial;
                        changed = true;
                        break;
                    }
                }
            }
            return string.Join("\n", kept);
        }
    }

    internal sealed class FuzzReport
    {
        public long Iterations;
        public int Crashes;
        public int Hangs;
        public int EngineFaults;
        public readonly List<string> CrashReproSources = new List<string>();
        public long LegalSyntaxErrors, LegalRuntimeErrors, LegalBudgetExceeded, LegalHostErrors, LegalSuccesses;

        public override string ToString()
        {
            return "iters=" + Iterations + " crashes=" + Crashes + " faults=" + EngineFaults + " hangs=" + Hangs
                + " | syntax=" + LegalSyntaxErrors + " runtime=" + LegalRuntimeErrors
                + " budget=" + LegalBudgetExceeded + " host=" + LegalHostErrors + " ok=" + LegalSuccesses;
        }
    }

    // T-TST-10: the fuzzing loop + the crash detector (R-13). A crash = a C# exception other than
    // PyLiteSyntaxException out of Compile/Run, OR Error.Kind==EngineFault. Everything else (Syntax/
    // Runtime/Budget/Host) is a LEGAL outcome.
    internal sealed class FuzzOrchestrator
    {
        private readonly ScriptEngine _engine;
        private readonly FuzzConfig _config;
        private readonly string[] _corpus;

        internal FuzzOrchestrator(ScriptEngine engine, FuzzConfig config, string[] corpus)
        {
            _engine = engine;
            _config = config;
            _corpus = corpus ?? Array.Empty<string>();
        }

        internal FuzzReport Run(TimeSpan timeBudget, int maxIterations)
        {
            var report = new FuzzReport();
            var sw = Stopwatch.StartNew();
            var mutator = new CorpusMutator(_config.Seed);
            int genSeed = _config.Seed;

            while (report.Iterations < maxIterations && sw.Elapsed < timeBudget)
            {
                report.Iterations++;
                string source;
                if (_corpus.Length > 0 && report.Iterations % 3 == 0)
                    source = mutator.Mutate(_corpus[genSeed % _corpus.Length], 1 + (int)(report.Iterations % 4));
                else
                    source = new GrammarFuzzer(new FuzzConfig
                    {
                        Seed = genSeed,
                        MaxDepth = _config.MaxDepth,
                        MaxStatements = _config.MaxStatements,
                        AllowForbidden = _config.AllowForbidden,
                    }).GenModule();
                genSeed++;

                if (Classify(source, report))
                    report.CrashReproSources.Add(source);
            }
            return report;
        }

        // Returns true if this source CRASHED the engine (R-13). Tallies legal outcomes too.
        internal bool Classify(string source, FuzzReport report)
        {
            RunResult result;
            try
            {
                result = _engine.Run(source, new RunRequest
                {
                    Limits = ResourceLimits.Default.With(deadlineMs: 2000, maxSteps: 5_000_000),
                });
            }
            catch (PyLiteSyntaxException)
            {
                report.LegalSyntaxErrors++;   // Compile threw the ONE allowed exception
                return false;
            }
            catch (Exception)
            {
                report.Crashes++;             // any other throw out of Run/Compile = a crash
                return true;
            }

            if (result.Succeeded)
            {
                report.LegalSuccesses++;
                return false;
            }
            switch (result.Error.Kind)
            {
                case ScriptErrorKind.SyntaxError: report.LegalSyntaxErrors++; return false;
                case ScriptErrorKind.RuntimeError: report.LegalRuntimeErrors++; return false;
                case ScriptErrorKind.BudgetExceeded: report.LegalBudgetExceeded++; return false;
                case ScriptErrorKind.HostError: report.LegalHostErrors++; return false;
                default:
                    report.EngineFaults++;    // EngineFault (code 5) = an internal bug = a crash
                    report.Crashes++;
                    return true;
            }
        }
    }
}

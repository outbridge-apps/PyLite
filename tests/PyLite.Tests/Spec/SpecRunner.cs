using System;
using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Hosting;

namespace Outbridge.PyLite.Tests.Spec
{
    // T-TST-02: executes one parsed golden file through the public ScriptEngine and returns a readable
    // verdict (null = pass; otherwise the full expected/actual diff — ALL divergences, not just the first).
    internal static class SpecRunner
    {
        internal static string RunCase(GoldenFile gf)
        {
            var engine = new ScriptEngine(new EngineOptions
            {
                DefaultLimits = ApplyLimits(ResourceLimits.Default, gf.Limits),
                Clock = new SystemClock(TimeSpan.Zero, "UTC"),
                SleepPolicy = new NoOpSleepPolicy(),
            });
            string source = gf.Seed != null
                ? "import random\nrandom.seed(" + gf.Seed.Value + ")\n" + gf.Source
                : gf.Source;
            var request = new RunRequest
            {
                ScalarsIn = gf.ScalarsIn.Count > 0 ? gf.ScalarsIn : null,
                JsonIn = gf.JsonIn.Count > 0 ? gf.JsonIn : null,
                ScalarOutputNames = Keys(gf.ScalarsOut),
                JsonOutputNames = Keys(gf.JsonOut),
            };
            RunResult result = engine.Run(source, request);
            var diffs = new StringBuilder();

            if (gf.ExpectsError)
            {
                if (result.Succeeded)
                    diffs.AppendLine("expected an error (" + (gf.ErrorType ?? gf.ErrorKind) + ") but the run succeeded");
                else
                    CheckError(gf, result.Error, diffs);
            }
            else if (!result.Succeeded)
            {
                diffs.AppendLine("expected success but got " + result.Error.Kind
                    + (result.Error.PythonType != null ? "/" + result.Error.PythonType : "")
                    + " at line " + result.Error.Line + ": " + result.Error.Message);
            }
            else
            {
                foreach (KeyValuePair<string, string> kv in gf.ScalarsOut)
                    CheckValue("out." + kv.Key, kv.Value, Lookup(result.ScalarsOut, kv.Key), diffs);
                foreach (KeyValuePair<string, string> kv in gf.JsonOut)
                    CheckValue("json_out." + kv.Key, kv.Value, Lookup(result.JsonOut, kv.Key), diffs);
                CheckPrint(gf, result, diffs);
            }
            return diffs.Length == 0 ? null : diffs.ToString().TrimEnd();
        }

        private static void CheckError(GoldenFile gf, ScriptError err, StringBuilder diffs)
        {
            if (gf.ErrorType != null && !string.Equals(err.PythonType, gf.ErrorType, StringComparison.Ordinal))
                diffs.AppendLine("error type\n  expected: " + gf.ErrorType + "\n  actual:   " + (err.PythonType ?? "<none>"));
            if (gf.ErrorMessage != null && (err.Message == null || !err.Message.Contains(gf.ErrorMessage)))
                diffs.AppendLine("error message\n  expected: " + gf.ErrorMessage + "\n  actual:   " + err.Message);
            if (gf.ErrorKind != null && !err.Kind.ToString().StartsWith(gf.ErrorKind, StringComparison.OrdinalIgnoreCase)
                && !(gf.ErrorKind == "Budget" && err.Kind == ScriptErrorKind.BudgetExceeded)
                && !(gf.ErrorKind == "Runtime" && err.Kind == ScriptErrorKind.RuntimeError)
                && !(gf.ErrorKind == "Syntax" && err.Kind == ScriptErrorKind.SyntaxError)
                && !(gf.ErrorKind == "Host" && err.Kind == ScriptErrorKind.HostError)
                && !(gf.ErrorKind == "Fault" && err.Kind == ScriptErrorKind.EngineFault))
                diffs.AppendLine("error kind\n  expected: " + gf.ErrorKind + "\n  actual:   " + err.Kind);
            if (gf.ErrorLine != null && err.Line != gf.ErrorLine.Value)
                diffs.AppendLine("error line\n  expected: " + gf.ErrorLine.Value + "\n  actual:   " + err.Line);
        }

        private static void CheckPrint(GoldenFile gf, RunResult result, StringBuilder diffs)
        {
            if (gf.PrintLines.Count == 0)
                return;
            string printed = (result.PrintOutput ?? "").Replace("\r\n", "\n");
            string[] actual = printed.TrimEnd('\n').Split('\n');
            int n = Math.Max(actual.Length, gf.PrintLines.Count);
            for (int i = 0; i < n; i++)
            {
                string exp = i < gf.PrintLines.Count ? gf.PrintLines[i] : "<no line>";
                string act = i < actual.Length ? actual[i] : "<no line>";
                if (!string.Equals(exp, act, StringComparison.Ordinal))
                    diffs.AppendLine("print line " + (i + 1) + "\n  expected: " + exp + "\n  actual:   " + act);
            }
        }

        private static void CheckValue(string what, string expected, string actual, StringBuilder diffs)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                diffs.AppendLine(what + "\n  expected: " + expected + "\n  actual:   " + (actual ?? "<missing>"));
        }

        private static string Lookup(IReadOnlyDictionary<string, string> d, string key)
        {
            string v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        private static string[] Keys(Dictionary<string, string> d)
        {
            if (d.Count == 0)
                return null;
            var keys = new string[d.Count];
            d.Keys.CopyTo(keys, 0);
            return keys;
        }

        // snake_case limit names -> ResourceLimits.With overrides (impl/12 frontmatter grammar).
        private static ResourceLimits ApplyLimits(ResourceLimits baseLimits, Dictionary<string, long> overrides)
        {
            ResourceLimits lim = baseLimits;
            foreach (KeyValuePair<string, long> kv in overrides)
            {
                switch (kv.Key)
                {
                    case "deadline_ms": lim = lim.With(deadlineMs: (int)kv.Value); break;
                    case "steps": lim = lim.With(maxSteps: kv.Value); break;
                    case "alloc_bytes": lim = lim.With(maxAllocBytes: kv.Value); break;
                    case "call_depth": lim = lim.With(maxCallDepth: (int)kv.Value); break;
                    case "str_chars": lim = lim.With(maxStrChars: (int)kv.Value); break;
                    case "collection_items": lim = lim.With(maxCollectionItems: (int)kv.Value); break;
                    case "int_bits": lim = lim.With(maxIntBits: (int)kv.Value); break;
                    case "data_depth": lim = lim.With(maxDataDepth: (int)kv.Value); break;
                    case "output_bytes": lim = lim.With(maxOutputBytes: (int)kv.Value); break;
                    case "sleep_total_ms": lim = lim.With(sleepTotalMs: (int)kv.Value); break;
                    case "regex_per_op_ms": lim = lim.With(regexPerOpTimeoutMs: (int)kv.Value); break;
                    default:
                        throw new FormatException("unknown limit override '" + kv.Key + "'");
                }
            }
            return lim;
        }
    }
}

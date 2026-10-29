using System;
using System.Collections.Generic;
using System.IO;
using Outbridge.PyLite.Hosting;

namespace Outbridge.PyLite.Conformance
{
    // Conformance runner: executes every dialect script under conformance/cases/ on the PyLite engine.
    // A case passes when it runs to completion (its asserts are the test). Header directives:
    //   # skip: <reason>            -- do not run; counted and reported as skipped
    //   # expect: <ErrorType>       -- the case MUST fail, with ScriptError.PythonType or Kind == ErrorType
    // Usage: dotnet run [-v] [filter-substring]
    internal static class Program
    {
        private static int Main(string[] args)
        {
            bool verbose = false;
            string filter = null;
            foreach (string a in args)
            {
                if (a == "-v")
                    verbose = true;
                else
                    filter = a;
            }

            string casesDir = FindCasesDir();
            if (casesDir == null)
            {
                Console.WriteLine("cases directory not found (walked up from " + AppDomain.CurrentDomain.BaseDirectory + ")");
                return 2;
            }

            var engine = new ScriptEngine(new EngineOptions
            {
                DefaultLimits = ResourceLimits.Default.With(
                    deadlineMs: 30_000, maxAllocBytes: 256L << 20, maxCallDepth: 512)
            });

            string[] files = Directory.GetFiles(casesDir, "*.py", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            int pass = 0, fail = 0, skip = 0;
            var failures = new List<string>();

            foreach (string file in files)
            {
                string rel = file.Substring(casesDir.Length + 1).Replace('\\', '/');
                if (filter != null && rel.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string source = File.ReadAllText(file);
                string skipReason, expectError;
                ParseDirectives(source, out skipReason, out expectError);

                if (skipReason != null)
                {
                    skip++;
                    Console.WriteLine("  skip " + rel + "   (" + skipReason + ")");
                    continue;
                }

                RunResult r = engine.Run(source, new RunRequest());

                bool ok;
                string detail;
                if (expectError != null)
                {
                    ok = !r.Succeeded && ErrorMatches(r.Error, expectError);
                    detail = r.Succeeded
                        ? "expected " + expectError + " but the script succeeded"
                        : "expected " + expectError + " but got " + Describe(r.Error);
                }
                else
                {
                    ok = r.Succeeded;
                    detail = r.Succeeded ? null : Describe(r.Error);
                }

                if (ok)
                {
                    pass++;
                    if (verbose)
                        Console.WriteLine("  ok   " + rel);
                }
                else
                {
                    fail++;
                    Console.WriteLine("  FAIL " + rel + "   [" + detail + "]");
                    failures.Add(rel);
                    if (verbose && r.Error != null && r.Error.ScriptTraceback != null)
                        Console.WriteLine("       " + r.Error.ScriptTraceback.Replace("\n", "\n       "));
                }
            }

            Console.WriteLine();
            Console.WriteLine("PASS " + pass + " / FAIL " + fail + " / SKIP " + skip);
            return fail == 0 ? 0 : 1;
        }

        private static void ParseDirectives(string source, out string skipReason, out string expectError)
        {
            skipReason = null;
            expectError = null;
            foreach (string raw in source.Split('\n'))
            {
                string line = raw.TrimEnd('\r').Trim();
                if (line.Length == 0)
                    continue;
                if (!line.StartsWith("#", StringComparison.Ordinal))
                    break;   // directives live in the leading comment block only
                if (line.StartsWith("# skip:", StringComparison.Ordinal))
                    skipReason = line.Substring(7).Trim();
                else if (line.StartsWith("# expect:", StringComparison.Ordinal))
                    expectError = line.Substring(9).Trim();
            }
        }

        private static bool ErrorMatches(ScriptError e, string expected)
        {
            if (e == null)
                return false;
            if (e.PythonType != null && string.Equals(e.PythonType, expected, StringComparison.Ordinal))
                return true;
            return string.Equals(e.Kind.ToString(), expected, StringComparison.Ordinal);
        }

        private static string Describe(ScriptError e)
        {
            if (e == null)
                return "<no error>";
            return (e.PythonType ?? e.Kind.ToString())
                + (e.Line > 0 ? " at line " + e.Line : string.Empty)
                + ": " + e.Message;
        }

        // The runner may execute from bin/<config>; walk up until a sibling "cases" directory appears.
        private static string FindCasesDir()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, "cases");
                if (Directory.Exists(candidate))
                    return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}

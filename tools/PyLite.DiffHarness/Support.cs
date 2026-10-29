using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Outbridge.PyLite.DiffHarness
{
    // T-TST-12: hosts IronPython 3.4.2 as the differential oracle. Runs the script, then reprs `result`
    // the CPython way so the two engines' outputs are directly comparable strings.
    internal sealed class IronRunner
    {
        private readonly Microsoft.Scripting.Hosting.ScriptEngine _engine;

        internal IronRunner()
        {
            _engine = IronPython.Hosting.Python.CreateEngine();
            _engine.SetSearchPaths(new[] { StdLib() });
        }

        internal EngineOutcome Run(string source)
        {
            try
            {
                Microsoft.Scripting.Hosting.ScriptScope scope = _engine.CreateScope();
                _engine.Execute(source, scope);
                object result;
                if (!scope.TryGetVariable("result", out result))
                    return EngineOutcome.Ok("<no result>");
                return EngineOutcome.Ok(Repr(result));
            }
            catch (Microsoft.Scripting.SyntaxErrorException)
            {
                return EngineOutcome.Failure("SyntaxError", engineFault: false);
            }
            catch (IronPython.Runtime.Exceptions.ImportException)
            {
                return EngineOutcome.Failure("ImportError", engineFault: false);
            }
            catch (DivideByZeroException)
            {
                return EngineOutcome.Failure("ZeroDivisionError", engineFault: false);   // IronPython leaks the .NET type
            }
            catch (Exception ex)
            {
                // A raised Python exception surfaces as a .NET exception; take the Python type name if any.
                string t = PythonTypeName(ex);
                if (t != null)
                    return EngineOutcome.Failure(t, engineFault: false);
                // A host/runner failure (missing stdlib etc.) is NOT a PyLite bug — flagged separately.
                var o = EngineOutcome.Failure("IronRunnerError", engineFault: false);
                o.Value = ex.GetType().Name + ": " + ex.Message;
                return o;
            }
        }

        private string Repr(object o)
        {
            try
            {
                // repr() via the hosted engine, no dynamic (avoids the Microsoft.CSharp binder dependency).
                Microsoft.Scripting.Hosting.ScriptScope s = _engine.CreateScope();
                s.SetVariable("__v", o);
                return _engine.Execute<string>("repr(__v)", s);
            }
            catch
            {
                return Convert.ToString(o, CultureInfo.InvariantCulture);
            }
        }

        private static string PythonTypeName(Exception ex)
        {
            // IronPython maps Python exceptions to .NET types like ...ValueError; strip to the leaf name.
            string n = ex.GetType().Name;
            string[] known = { "ValueError", "TypeError", "KeyError", "IndexError", "ZeroDivisionError",
                "AttributeError", "OverflowError", "RuntimeError", "StopIteration", "NotImplementedError",
                "ImportError", "AssertionError", "NameError" };
            return known.FirstOrDefault(k => n.Contains(k));
        }

        private static string StdLib()
        {
            string next = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(next))
                return next;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".nuget", "packages", "ironpython.stdlib", "3.4.2", "content", "lib");
        }
    }

    // T-TST-13: known-differences registry (documented, non-bug divergences). Format: one "name|kind"
    // pair per line (kind = outcome|error-type|value), '#' comments allowed. A name is a substring match
    // on the script's relative path so a whole topic can be gated.
    internal sealed class KnownDifferences
    {
        private readonly List<KeyValuePair<string, string>> _entries = new List<KeyValuePair<string, string>>();

        internal static KnownDifferences Load(string path)
        {
            var kd = new KnownDifferences();
            if (path == null || !File.Exists(path))
                return kd;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;
                int bar = line.IndexOf('|');
                if (bar > 0)
                    kd._entries.Add(new KeyValuePair<string, string>(line.Substring(0, bar).Trim(), line.Substring(bar + 1).Trim()));
            }
            return kd;
        }

        internal bool IsKnown(string name, string kind)
        {
            return _entries.Any(e => name.Contains(e.Key) && (e.Value == kind || e.Value == "*"));
        }
    }

    internal sealed class DiffOptions
    {
        public string CorpusDir;
        public string Engine = "both";
        public string Report = "md";
        public string OutPath;
        public string Filter;
        public string KnownPath;
        public bool FailOnBug;

        internal static DiffOptions Parse(string[] args)
        {
            var o = new DiffOptions();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--corpus": o.CorpusDir = args[++i]; break;
                    case "--engine": o.Engine = args[++i]; break;
                    case "--report": o.Report = args[++i]; break;
                    case "--out": o.OutPath = args[++i]; break;
                    case "--filter": o.Filter = args[++i]; break;
                    case "--known": o.KnownPath = args[++i]; break;
                    case "--fail-on-bug": o.FailOnBug = true; break;
                    default:
                        Console.Error.WriteLine("unknown arg: " + args[i]);
                        return null;
                }
            }
            if (o.CorpusDir == null)
            {
                Console.Error.WriteLine("usage: Outbridge.PyLite.DiffHarness --corpus <dir> [--engine both|pylite|iron] "
                    + "[--report md|json] [--out path] [--known file] [--fail-on-bug]");
                return null;
            }
            if (o.KnownPath == null)
            {
                string next = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "known-differences.txt");
                o.KnownPath = File.Exists(next) ? next : null;
            }
            return o;
        }
    }

    internal static class Reporter
    {
        internal static string Markdown(List<DiffRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# DiffHarness report — PyLite vs IronPython 3.4.2");
            sb.AppendLine();
            sb.AppendLine("Generated " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "Z");
            sb.AppendLine();
            sb.AppendLine("| script | class | detail |");
            sb.AppendLine("|---|---|---|");
            foreach (DiffRow r in rows.OrderBy(r => r.Class == DiffClass.Match ? 1 : 0))
                sb.AppendLine("| " + r.Name + " | " + r.Class + " | " + (r.Detail ?? "").Replace("|", "\\|") + " |");
            sb.AppendLine();
            sb.AppendLine("**" + rows.Count + " scripts** — match " + rows.Count(r => r.Class == DiffClass.Match)
                + ", known " + rows.Count(r => r.Class == DiffClass.Known)
                + ", bug " + rows.Count(r => r.Class == DiffClass.Bug)
                + ", engineError " + rows.Count(r => r.Class == DiffClass.EngineError)
                + ", ironError " + rows.Count(r => r.Class == DiffClass.IronError) + ".");
            return sb.ToString();
        }

        internal static string Json(List<DiffRow> rows)
        {
            var sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < rows.Count; i++)
            {
                DiffRow r = rows[i];
                if (i > 0)
                    sb.Append(",");
                sb.Append("\n  {\"name\":\"" + Esc(r.Name) + "\",\"class\":\"" + r.Class
                    + "\",\"detail\":\"" + Esc(r.Detail ?? "") + "\"}");
            }
            sb.Append("\n]\n");
            return sb.ToString();
        }

        private static string Esc(string s) { return s.Replace("\\", "\\\\").Replace("\"", "\\\""); }
    }
}

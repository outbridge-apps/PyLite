using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Outbridge.PyLite.Tests.Spec
{
    // T-TST-02: a golden .py file = an executable script + frontmatter directives in '#' comments.
    // Parsing is strict: an unknown directive, a missing space after ':', or a duplicate out.* fails
    // the case loudly (never silently). Directives may appear anywhere; usually at the top.
    internal sealed class GoldenFile
    {
        public string Source;                                     // the full file text (directives are comments)
        public readonly Dictionary<string, string> ScalarsIn = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> JsonIn = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> ScalarsOut = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> JsonOut = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly List<string> PrintLines = new List<string>();
        public readonly Dictionary<string, long> Limits = new Dictionary<string, long>(StringComparer.Ordinal);
        public string ErrorType;        // '# error: <PyType>[: <message>]'
        public string ErrorMessage;
        public string ErrorKind;        // '# error_kind: Syntax|Runtime|Budget|Host|Fault'
        public int? ErrorLine;
        public long? Seed;
        public string Skip;
        public string Tags;

        public bool ExpectsError { get { return ErrorType != null || ErrorKind != null; } }

        public static GoldenFile Parse(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
            var gf = new GoldenFile { Source = text };
            int lineNo = 0;
            foreach (string raw in text.Split('\n'))
            {
                lineNo++;
                string line = raw.TrimStart();
                if (!line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                string body = line.Substring(1).TrimStart();
                ParseDirective(gf, body, path, lineNo);
            }
            return gf;
        }

        private static void ParseDirective(GoldenFile gf, string body, string path, int lineNo)
        {
            // Only lines matching a known directive prefix are directives; other comments pass through.
            string[] prefixes = { "in.", "json_in.", "out.", "json_out.", "print:", "error:", "error_kind:", "error_line:", "limits.", "seed:", "skip:", "tags:" };
            string matched = null;
            foreach (string p in prefixes)
                if (body.StartsWith(p, StringComparison.Ordinal))
                {
                    matched = p;
                    break;
                }
            if (matched == null)
                return;   // an ordinary comment

            int colon = body.IndexOf(':');
            string key = body.Substring(0, colon);
            string rest = body.Substring(colon + 1);
            string value;
            if (rest.Length == 0)
                value = "";   // '# out.x:' would be an error; but 'print:'/'skip:' with nothing => empty
            else if (rest[0] != ' ')
                throw Bad(path, lineNo, "missing space after ':' in directive '" + key + "'");
            else
                value = Unescape(rest.Substring(1));

            if (matched == "in.")
                gf.ScalarsIn[key.Substring(3)] = value;
            else if (matched == "json_in.")
                gf.JsonIn[key.Substring(8)] = value;
            else if (matched == "out.")
            {
                string name = key.Substring(4);
                if (gf.ScalarsOut.ContainsKey(name))
                    throw Bad(path, lineNo, "duplicate 'out." + name + "' directive");
                gf.ScalarsOut[name] = value;
            }
            else if (matched == "json_out.")
            {
                string name = key.Substring(9);
                if (gf.JsonOut.ContainsKey(name))
                    throw Bad(path, lineNo, "duplicate 'json_out." + name + "' directive");
                gf.JsonOut[name] = value;
            }
            else if (matched == "print:")
                gf.PrintLines.Add(value);
            else if (matched == "error:")
            {
                int sep = value.IndexOf(": ", StringComparison.Ordinal);
                if (sep >= 0)
                {
                    gf.ErrorType = value.Substring(0, sep);
                    gf.ErrorMessage = value.Substring(sep + 2);
                }
                else
                    gf.ErrorType = value;
            }
            else if (matched == "error_kind:")
                gf.ErrorKind = value;
            else if (matched == "error_line:")
                gf.ErrorLine = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            else if (matched == "limits.")
                gf.Limits[key.Substring(7)] = long.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            else if (matched == "seed:")
                gf.Seed = long.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            else if (matched == "skip:")
                gf.Skip = value;
            else // tags:
                gf.Tags = value;
        }

        private static Exception Bad(string path, int line, string why)
        {
            return new FormatException("bad golden file " + Path.GetFileName(path) + ":" + line + ": " + why);
        }

        // literal \n -> newline, \t -> tab, \r -> CR, \\ -> backslash.
        private static string Unescape(string v)
        {
            if (v.IndexOf('\\') < 0)
                return v;
            var sb = new StringBuilder(v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i] != '\\' || i + 1 >= v.Length)
                {
                    sb.Append(v[i]);
                    continue;
                }
                i++;
                switch (v[i])
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '\\': sb.Append('\\'); break;
                    default:
                        sb.Append('\\').Append(v[i]);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Spec
{
    // Review 3, P6: 300 broken (and some valid) snippets of the review's lexer/parser fuzzing, with what CPython
    // 3.14.2 said about each (Oracle/syntax-errors.tsv): the type, the line, the column and the text must match.
    [Trait("Category", "Compatibility")]
    public class SyntaxErrorOracleTests
    {
        // What the dialect answers differently, on purpose (docs/deviations.md) or not yet.
        private static readonly Dictionary<string, string> Known = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "s026", "no complex numbers" },
            { "s156", "no async" },
            { "s163", "no except*" },
            { "s190", "no import *" },
            { "s191", "no relative imports" },
            { "s246", "no type statement" },
            { "s247", "no type parameters" },
            { "s248", "no type parameters" },
            { "s281", "no generator functions" },
            { "s208", "'<>' is two tokens: the error is at '>'" },
            { "s254", "CPython gives a null byte no position" },
            { "s294", "CPython reads a token past the field before the error" },
            { "s432", "a repeated keyword is reported at once: CPython reports a later __debug__ keyword first" },
            { "s453", "a repeated keyword is reported at once: CPython reports a later positional argument first" },
        };

        [Fact]
        public void EachSnippetFailsAsCPythonFails()
        {
            var wrong = new StringBuilder();
            int known = 0;
            foreach (string row in File.ReadAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Oracle", "syntax-errors.tsv")))
            {
                if (row.Length == 0 || row[0] == '#')
                    continue;
                string[] f = row.Split('\t');
                string expected = string.Join("\t", f, 2, f.Length - 2);
                string actual = Outcome(Unescape(f[1]));
                if (Known.ContainsKey(f[0]))
                {
                    Assert.True(actual != expected, f[0] + " now matches CPython: drop it from Known");
                    known++;
                }
                else if (actual != expected)
                    wrong.Append(f[0]).Append("\n  CPython ").Append(expected).Append("\n  PyLite  ").Append(actual).Append('\n');
            }
            Assert.True(wrong.Length == 0, wrong.ToString());
            Assert.Equal(Known.Count, known);
        }

        private static string Outcome(string source)
        {
            try
            {
                Compiler.Compile(source, "<snippet>", SyntaxLimits.Default);
                return "OK";
            }
            catch (PySyntaxErrorException e)
            {
                return e.PythonType + "\t" + e.Line.ToString(CultureInfo.InvariantCulture) + "\t"
                    + e.Col.ToString(CultureInfo.InvariantCulture) + "\t" + e.Message;
            }
        }

        // Python's unicode_escape, as the snippets were written
        private static string Unescape(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length)
                {
                    sb.Append(s[i]);
                    continue;
                }
                char n = s[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'v': sb.Append('\v'); break;
                    case 'a': sb.Append('\a'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'x': sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 2), 16)); i += 2; break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; break;
                    case 'U': sb.Append(char.ConvertFromUtf32(Convert.ToInt32(s.Substring(i + 1, 8), 16))); i += 8; break;
                    default: sb.Append(n); break;   // \\ \' \"
                }
            }
            return sb.ToString();
        }
    }
}

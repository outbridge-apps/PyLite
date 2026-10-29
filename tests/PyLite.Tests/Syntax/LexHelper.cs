using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Syntax;

namespace Outbridge.PyLite.Tests.Syntax
{
    internal static class L
    {
        internal static List<Token> Go(string s) { return Lexer.Tokenize(s, SyntaxLimits.Default); }

        internal static List<Token> Go(string s, SyntaxLimits limits) { return Lexer.Tokenize(s, limits); }

        // Space-joined kind names, including the trailing EndOfFile.
        internal static string Kinds(string s)
        {
            var sb = new StringBuilder();
            foreach (var t in Go(s))
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(t.Kind.ToString());
            }
            return sb.ToString();
        }
    }
}

using System;
using System.Text;
using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Fuzz")]
    public class LexerStressTests
    {
        [Fact]
        public void TooManyTokens()
        {
            var src = new StringBuilder();
            for (int i = 0; i < 250001; i++)
                src.Append("a;");
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go(src.ToString()));
            Assert.Equal("too many tokens (limit is 500000)", e.Message);
        }

        [Fact]
        public void TooManyIndentLevels()
        {
            var src = new StringBuilder();
            for (int i = 0; i < 102; i++)
                src.Append(new string (' ', i)).Append("if a:\n");
            src.Append(new string(' ', 102)).Append("pass\n");
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go(src.ToString()));
            Assert.Equal("IndentationError", e.PythonType);
            Assert.Equal("too many levels of indentation", e.Message);
        }

        [Fact]
        public void HugeTripleStringCompletes()
        {
            string body = new string('x', 400000);
            var t = L.Go("'''" + body + "'''");
            Assert.Equal(TokenKind.StringLiteral, t[0].Kind);
            Assert.Equal(400000, t[0].Text.Length);
        }

        // Seeded fuzz: no foreign exceptions, no hangs — only clean tokens or PySyntaxErrorException.
        [Fact]
        public void FuzzNoForeignExceptions()
        {
            const string alphabet = "abc09 \t\n()[]{}:;,.+-*/%=<>!&|^~'\"\\#fru{}αΩ";
            var rnd = new Random(20260706);
            for (int iter = 0; iter < 1000; iter++)
            {
                int n = rnd.Next(0, 200);
                var sb = new StringBuilder(n);
                for (int i = 0; i < n; i++)
                    sb.Append(alphabet[rnd.Next(alphabet.Length)]);
                string src = sb.ToString().Replace('\0', ' ');
                try
                {
                    L.Go(src);
                }
                catch (PySyntaxErrorException)
                { /* expected class */
                }
            }
        }
    }
}

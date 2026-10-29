using System;
using System.Text;
using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Fuzz")]
    public class ParserFuzzTests
    {
        private static void CompileSwallowSyntax(string src)
        {
            try
            {
                Compiler.Compile(src, "<fuzz>", SyntaxLimits.Default);
            }
            catch (PySyntaxErrorException)
            { /* the only acceptable failure */
            }
        }

        private static readonly string[] Pool =
        {
            "if ", "elif ", "else", "def ", "for ", "while ", "try", "except ", "finally", "return ", "raise ",
            "assert ", "del ", "pass", "break", "continue", "import ", "from ", "as ", "global ", "nonlocal ",
            "lambda ", "and ", "or ", "not ", "in ", "is ", "None", "True", "False",
            "x", "y", "foo", "1", "2", "3.5", "'s'", "f'{x}'", "0xFF",
            "(", ")", "[", "]", "{", "}", ",", ":", ";", ".", "=", "+", "-", "*", "**", "/", "//", "%",
            "@", "|", "&", "^", "~", "<", ">", "==", "!=", "<=", ">=", "+=", "*=", ":=", "->",
            "\n", " ", "  ", "\t"
        };

        [Fact]
        public void FuzzTokenStreamsNoForeignExceptions()
        {
            var rnd = new Random(20260707);
            for (int iter = 0; iter < 8000; iter++)
            {
                int n = rnd.Next(0, 25);
                var sb = new StringBuilder();
                for (int i = 0; i < n; i++)
                    sb.Append(Pool[rnd.Next(Pool.Length)]);
                CompileSwallowSyntax(sb.ToString());
            }
        }

        [Fact]
        public void FuzzRandomCharsNoForeignExceptions()
        {
            const string alphabet = "abc09 \t\n()[]{}:;,.+-*/%=<>!&|^~'\"\\#fru@";
            var rnd = new Random(1234567);
            for (int iter = 0; iter < 4000; iter++)
            {
                int n = rnd.Next(0, 120);
                var sb = new StringBuilder(n);
                for (int i = 0; i < n; i++)
                    sb.Append(alphabet[rnd.Next(alphabet.Length)]);
                CompileSwallowSyntax(sb.ToString().Replace('\0', ' '));
            }
        }

        [Fact]
        public void DeepParensSyntaxErrorNotOverflow()
        {
            string src = new string('(', 10000) + "x" + new string(')', 10000);
            var ex = Assert.Throws<PySyntaxErrorException>(() => Compiler.Compile(src, "<f>", SyntaxLimits.Default));
            Assert.Contains("too deeply nested", ex.Message);
        }

        [Fact]
        public void DeepStatementsSyntaxError()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 70; i++)
                sb.Append(new string (' ', i)).Append("if x:\n");
            sb.Append(new string(' ', 70)).Append("pass\n");
            var ex = Assert.Throws<PySyntaxErrorException>(() => Compiler.Compile(sb.ToString(), "<f>", SyntaxLimits.Default));
            Assert.Contains("too deeply nested", ex.Message);
        }

        [Fact]
        public void WideListNoOverflow()
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < 50000; i++)
                sb.Append("1,");
            sb.Append("1]\n");
            var cs = Compiler.Compile(sb.ToString(), "<f>", SyntaxLimits.Default);
            Assert.Single(cs.Program.Module.Body);
        }

        [Fact]
        public void MutationFuzzHoldsInvariant()
        {
            string[] seeds =
            {
                "x = [i for i in range(3) if i > 0]\n",
                "def f(a, b=1, *c, d, **e):\n return a + b\n",
                "try:\n f()\nexcept E as x:\n raise\nfinally:\n g()\n",
                "y = {k: v for k, v in items}\n",
                "z = f'{a!r:>{w}}' + 'lit'\n"
            };
            var rnd = new Random(99);
            foreach (var seed in seeds)
            {
                for (int m = 0; m < 500; m++)
                {
                    var chars = seed.ToCharArray();
                    int muts = rnd.Next(1, 4);
                    for (int k = 0; k < muts; k++)
                    {
                        int pos = rnd.Next(chars.Length);
                        chars[pos] = "()[]{}:,;=+* \n".ToCharArray()[rnd.Next(14)];
                    }

                    CompileSwallowSyntax(new string (chars));
                }
            }
        }

        [Fact]
        public void DeterministicDump()
        {
            const string src = "def f(a):\n return [a for _ in range(3)]\n";
            Assert.Equal(AstDump.OfModule(src), AstDump.OfModule(src));
        }
    }
}

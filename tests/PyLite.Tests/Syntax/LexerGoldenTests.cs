using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Compatibility")]
    public class LexerGoldenTests
    {
        private static string Dump(string src) => TokenDump.DumpAll(L.Go(src));

        [Fact]
        public void HelloWorld()
            => Assert.Equal(
                "1:1 Name print\n1:6 LeftParen\n1:7 StringLiteral \"hi\"\n1:11 RightParen\n1:12 NewLine\n1:12 EndOfFile\n",
                Dump("print('hi')"));

        [Fact]
        public void HexInteger()
            => Assert.Equal(
                "1:1 Name a\n1:2 Assign\n1:3 IntLiteral 255\n1:7 NewLine\n1:7 EndOfFile\n",
                Dump("a=0xFF"));

        [Fact]
        public void FloatBits()
            => Assert.Equal(
                "1:1 Name x\n1:2 Assign\n1:3 FloatLiteral 0x3FE0000000000000\n1:6 NewLine\n1:6 EndOfFile\n",
                Dump("x=0.5"));

        [Fact]
        public void FStringDump()
            => Assert.Equal(
                "1:1 FStringStart f'\n1:3 FStringMiddle \"a\"\n1:4 LeftBrace\n1:5 Name x\n1:6 RightBrace\n1:7 FStringEnd '\n1:8 NewLine\n1:8 EndOfFile\n",
                Dump("f'a{x}'"));

        [Fact]
        public void EscapeInStringDump()
            => Assert.Equal(
                "1:1 StringLiteral \"a\\nb\"\n1:7 NewLine\n1:7 EndOfFile\n",
                Dump("'a\\nb'"));

        [Fact]
        public void Deterministic()
            => Assert.Equal(Dump("if a:\n    b\nc\n"), Dump("if a:\n    b\nc\n"));
    }
}

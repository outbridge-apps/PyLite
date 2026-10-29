using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Compatibility")]
    public class LexerNameTests
    {
        [Fact]
        public void NamesAndTexts()
        {
            var t = L.Go("foo bar _baz x1");
            Assert.Equal("foo", t[0].Text);
            Assert.Equal("bar", t[1].Text);
            Assert.Equal("_baz", t[2].Text);
            Assert.Equal("x1", t[3].Text);
        }

        [Fact]
        public void Keywords()
            => Assert.Equal("KwWhile KwTrue Colon KwPass NewLine EndOfFile", L.Kinds("while True: pass"));

        [Fact]
        public void ReservedButBanned()
            => Assert.Equal("KwAsync KwAwait KwClass KwYield KwWith NewLine EndOfFile", L.Kinds("async await class yield with"));

        [Fact]
        public void PrintIsName()
            => Assert.Equal("Name LeftParen Name RightParen NewLine EndOfFile", L.Kinds("print(x)"));

        [Fact]
        public void NonAsciiName()
        {
            var t = L.Go("αβγ = 1");
            Assert.Equal(TokenKind.Name, t[0].Kind);
            Assert.Equal("αβγ", t[0].Text);
        }

        [Theory]
        [InlineData("b'x'")]
        [InlineData("rb'x'")]
        [InlineData("bR'x'")]
        public void BytesLiteralLexes(string src)
            => Assert.Equal("BytesLiteral NewLine EndOfFile", L.Kinds(src));

        [Fact]
        public void UPrefixIsPlainString()
        {
            var t = L.Go("u'x'");
            Assert.Equal(TokenKind.StringLiteral, t[0].Kind);
            Assert.Equal("x", t[0].Text);
        }

        [Fact]
        public void NotAPrefix_NameThenString()
        {
            var t = L.Go("x'a'");
            Assert.Equal(TokenKind.Name, t[0].Kind);
            Assert.Equal("x", t[0].Text);
            Assert.Equal(TokenKind.StringLiteral, t[1].Kind);
            Assert.Equal("a", t[1].Text);
        }

        [Fact]
        public void UrIsNotAPrefix()
        {
            var t = L.Go("ur'a'");
            Assert.Equal(TokenKind.Name, t[0].Kind);
            Assert.Equal("ur", t[0].Text);
            Assert.Equal(TokenKind.StringLiteral, t[1].Kind);
        }

        [Fact]
        public void RawStringKeepsBackslash()
        {
            var t = L.Go("R'\\n'");
            Assert.Equal(TokenKind.StringLiteral, t[0].Kind);
            Assert.Equal("\\n", t[0].Text); // two characters
        }

        [Fact]
        public void InvalidIdentifierChar()
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go("€"));
            Assert.Equal("invalid character in identifier", e.Message);
            Assert.Equal(1, e.Col);
        }

        [Fact]
        public void GreedyNameNotKeyword()
        {
            var t = L.Go("Trueχ");
            Assert.Equal(TokenKind.Name, t[0].Kind);
            Assert.Equal("Trueχ", t[0].Text);
        }
    }
}

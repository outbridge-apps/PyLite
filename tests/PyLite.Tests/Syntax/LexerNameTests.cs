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

        [Theory]
        [InlineData("ur'a'", "'u' and 'r' prefixes are incompatible")]
        [InlineData("bf'a'", "'b' and 'f' prefixes are incompatible")]
        [InlineData("t'a'", "t-strings are not supported in this dialect")]
        public void PrefixesThatDoNotCombine(string src, string message)
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go(src));
            Assert.Equal(message, e.Message);
            Assert.Equal(1, e.Col);
        }

        [Fact]
        public void RawStringKeepsBackslash()
        {
            var t = L.Go("R'\\n'");
            Assert.Equal(TokenKind.StringLiteral, t[0].Kind);
            Assert.Equal("\\n", t[0].Text); // two characters
        }

        // CPython 3.14's verify_identifier: the message and the column of the first char that is not
        // XID_Start/XID_Continue (or printable).
        [Theory]
        [InlineData("€ = 1", "invalid character '€' (U+20AC)", 1, 1)]
        [InlineData("x² = 1", "invalid character '²' (U+00B2)", 1, 2)]
        [InlineData("x = 1 → 2", "invalid character '→' (U+2192)", 1, 7)]
        [InlineData("x = 1 ", "invalid non-printable character U+00A0", 1, 6)]
        [InlineData("x = 1", "invalid non-printable character U+00A0", 1, 2)]
        [InlineData("print(1 ​+ 2)", "invalid non-printable character U+200B", 1, 9)]
        [InlineData("́a = 1", "invalid character '́' (U+0301)", 1, 1)]
        [InlineData("def f²(): pass", "invalid character '²' (U+00B2)", 1, 6)]
        [InlineData("x = 1\nééé² = 2", "invalid character '²' (U+00B2)", 2, 4)]
        public void InvalidIdentifierChar(string src, string message, int line, int col)
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go(src));
            Assert.Equal(message, e.Message);
            Assert.Equal(line, e.Line);
            Assert.Equal(col, e.Col);
        }

        // PEP 3131: any XID name, NFKC-normalized; a keyword is matched on the raw text, as in CPython.
        [Theory]
        [InlineData("a·b", "a·b")]
        [InlineData("x‿y", "x‿y")]
        [InlineData("á", "á")]
        [InlineData("ℌ", "H")]
        [InlineData("ﬁx", "fix")]
        [InlineData("\U0001d518\U0001d52b", "Un")]
        [InlineData("ｉｆ", "if")]
        public void NonAsciiNamesAreNfkc(string src, string name)
        {
            var t = L.Go(src + " = 1");
            Assert.Equal(TokenKind.Name, t[0].Kind);
            Assert.Equal(name, t[0].Text);
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

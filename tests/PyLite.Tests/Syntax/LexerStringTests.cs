using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Compatibility")]
    public class LexerStringTests
    {
        private static string Str(string src)
        {
            var t = L.Go(src);
            Assert.Equal(TokenKind.StringLiteral, t[0].Kind);
            return t[0].Text;
        }

        [Theory]
        [InlineData("'abc'", "abc")]
        [InlineData("\"a'b\"", "a'b")]
        [InlineData("'don\\'t'", "don't")]
        [InlineData("'a\\nb'", "a\nb")]
        [InlineData("'\\x41\\x42'", "AB")]
        [InlineData("'\\d'", "\\d")]
        [InlineData("'\\0'", "\0")]
        [InlineData("r'\\n'", "\\n")]
        [InlineData("r'\\''", "\\'")]
        public void Cooked(string src, string expected) => Assert.Equal(expected, Str(src));

        [Fact] public void Octal() => Assert.Equal("ǿ", Str("'\\777'"));
        [Fact] public void Unicode16() => Assert.Equal("Ω", Str("'Ω'"));

        [Fact]
        public void Astral()
        {
            string s = Str("'\\U0001F600'");
            Assert.Equal(2, s.Length); // surrogate pair
            Assert.Equal("😀", s);
        }

        [Fact]
        public void LoneSurrogate()
        {
            string s = Str("'\\ud800'");
            Assert.Equal(1, s.Length);
            Assert.Equal('\ud800', s[0]);
        }

        [Fact]
        public void TripleQuotedNewlineNormalized()
        {
            Assert.Equal("a\nb", Str("'''a\r\nb'''"));
            Assert.Equal("a\nb", Str("'''a\nb'''"));
        }

        [Fact]
        public void Continuation() => Assert.Equal("ab", Str("'a\\\nb'"));

        [Fact]
        public void AdjacentLiterals()
            => Assert.Equal("StringLiteral StringLiteral NewLine EndOfFile", L.Kinds("'a' 'b'"));

        [Fact]
        public void UnterminatedSingle()
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go("'abc"));
            Assert.Equal("EOL while scanning string literal", e.Message);
        }

        [Fact]
        public void NewlineInSingle()
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go("'a\nb'"));
            Assert.Equal("EOL while scanning string literal", e.Message);
        }

        [Fact]
        public void UnterminatedTriple()
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go("'''abc"));
            Assert.Equal("EOF in multi-line string", e.Message);
        }

        [Fact]
        public void FourQuotesAfterTriple()
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go("'''ab''''"));
            Assert.Equal("EOL while scanning string literal", e.Message);
        }

        [Theory]
        [InlineData("'\\x4'", "0-2", "truncated \\xXX escape")]
        [InlineData("'\\u12'", "0-3", "truncated \\uXXXX escape")]
        [InlineData("'a\\U00110000'", "1-10", "illegal Unicode character")]
        [InlineData("'\\UFFFFFFFF'", "0-9", "illegal Unicode character")]   // was accepted: the int wrapped (D18)
        public void UnicodeErrors(string src, string pos, string tail)
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go(src));
            Assert.Equal("(unicode error) 'unicodeescape' codec can't decode bytes in position " + pos + ": " + tail, e.Message);
        }

        // The position counts from the literal's body, not from the start of the source (D18).
        [Theory]
        [InlineData("b'\\x4'", 0)]
        [InlineData("x = b'ab\\xZ1'", 2)]
        [InlineData("b'''\\x'''", 0)]
        public void BytesHexEscapeErrors(string src, int pos)
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go(src));
            Assert.Equal("(value error) invalid \\x escape at position " + pos, e.Message);
        }


        [Theory]
        [InlineData("'\\N{EURO SIGN}'", "€")]
        [InlineData("'\\N{euro sign}'", "€")]                          // matched case-insensitively
        [InlineData("'a\\N{EURO SIGN}b'", "a€b")]
        [InlineData("'\\N{NUL}'", "\0")]                                    // an abbreviation alias
        [InlineData("'\\N{LATIN CAPITAL LETTER GHA}'", "Ƣ")]           // a correction alias
        [InlineData("'\\N{KEYCAP DIGIT ZERO}'", "0️⃣")]           // a named sequence
        [InlineData("'\\N{HANGUL SYLLABLE GA}'", "가")]                 // composed from its jamo
        [InlineData("'\\N{HANGUL SYLLABLE GGWALS}'", "꽔")]
        [InlineData("'\\N{HANGUL SYLLABLE HIH}'", "힣")]
        [InlineData("'\\N{CJK UNIFIED IDEOGRAPH-4E00}'", "一")]         // a range rule
        [InlineData("'\\N{CJK COMPATIBILITY IDEOGRAPH-F900}'", "豈")]   // a prefix rule
        [InlineData("'\\N{NUSHU CHARACTER-1B170}'", "\U0001B170")]
        [InlineData("'\\N{GRINNING FACE}'", "\U0001F600")]
        [InlineData("'\\N{GREEK SMALL LETTER ALPHA}\\N{COMBINING ACUTE ACCENT}'", "ά")]
        [InlineData("r'\\N{EURO SIGN}'", "\\N{EURO SIGN}")]
        public void NamedEscapes(string src, string expected) => Assert.Equal(expected, Str(src));

        [Fact]
        public void NamedEscapeIsNoEscapeInBytes()
        {
            var t = L.Go("b'\\N{EURO SIGN}'");
            Assert.Equal(TokenKind.BytesLiteral, t[0].Kind);
            Assert.Equal("\\N{EURO SIGN}", System.Text.Encoding.ASCII.GetString((byte[])t[0].Value));
        }

        [Theory]
        [InlineData("'\\N{NOT A CHARACTER}'", "0-18", "unknown Unicode character name")]
        [InlineData("'\\N{HANGUL SYLLABLE QQ}'", "0-21", "unknown Unicode character name")]
        [InlineData("'\\N{CJK UNIFIED IDEOGRAPH-0041}'", "0-29", "unknown Unicode character name")]
        [InlineData("'\\N{EGYPTIAN HIEROGLYPH-13000}'", "0-28", "unknown Unicode character name")]
        [InlineData("'\\N{}'", "0-2", "malformed \\N character escape")]
        [InlineData("'\\Nx'", "0-1", "malformed \\N character escape")]
        [InlineData("'\\N{EURO SIGN'", "0-11", "malformed \\N character escape")]
        public void NamedEscapeErrors(string src, string pos, string tail)
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go(src));
            Assert.Equal("(unicode error) 'unicodeescape' codec can't decode bytes in position " + pos + ": " + tail, e.Message);
        }

        [Fact]
        public void LengthCap()
        {
            var lim = new SyntaxLimits { MaxStringLiteralChars = 16 };
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go("'" + new string('x', 20) + "'", lim));
            Assert.Equal("string literal too long (limit is 16 characters)", e.Message);
        }
    }
}

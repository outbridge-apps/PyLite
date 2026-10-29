using System.Collections.Generic;
using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Compatibility")]
    public class LexerFStringTests
    {
        private static List<Token> Mids(string src)
        {
            var r = new List<Token>();
            foreach (var t in L.Go(src))
                if (t.Kind == TokenKind.FStringMiddle)
                    r.Add(t);
            return r;
        }

        [Theory]
        [InlineData("f''", "FStringStart FStringEnd NewLine EndOfFile")]
        [InlineData("f'a{x}b'", "FStringStart FStringMiddle LeftBrace Name RightBrace FStringMiddle FStringEnd NewLine EndOfFile")]
        [InlineData("f'{x}'", "FStringStart LeftBrace Name RightBrace FStringEnd NewLine EndOfFile")]
        [InlineData("f'{{x}}'", "FStringStart FStringMiddle FStringEnd NewLine EndOfFile")]
        [InlineData("f'{x!r}'", "FStringStart LeftBrace Name Bang Name RightBrace FStringEnd NewLine EndOfFile")]
        [InlineData("f'{x:>10}'", "FStringStart LeftBrace Name Colon FStringMiddle RightBrace FStringEnd NewLine EndOfFile")]
        [InlineData("f'{x:{w}}'", "FStringStart LeftBrace Name Colon LeftBrace Name RightBrace RightBrace FStringEnd NewLine EndOfFile")]
        [InlineData("f'{x:}'", "FStringStart LeftBrace Name Colon RightBrace FStringEnd NewLine EndOfFile")]
        [InlineData("f'{f\"{1}\"}'", "FStringStart LeftBrace FStringStart LeftBrace IntLiteral RightBrace FStringEnd RightBrace FStringEnd NewLine EndOfFile")]
        [InlineData("f'{ {1:2} }'", "FStringStart LeftBrace LeftBrace IntLiteral Colon IntLiteral RightBrace RightBrace FStringEnd NewLine EndOfFile")]
        [InlineData("rf'\\d{x}'", "FStringStart FStringMiddle LeftBrace Name RightBrace FStringEnd NewLine EndOfFile")]
        [InlineData("f'\\{x}'", "FStringStart FStringMiddle LeftBrace Name RightBrace FStringEnd NewLine EndOfFile")]
        [InlineData("f'{a!=b}'", "FStringStart LeftBrace Name NotEqual Name RightBrace FStringEnd NewLine EndOfFile")]
        [InlineData("F\"x{ y }z\"", "FStringStart FStringMiddle LeftBrace Name RightBrace FStringMiddle FStringEnd NewLine EndOfFile")]
        [InlineData("f'{x:%Y-%m}'", "FStringStart LeftBrace Name Colon FStringMiddle RightBrace FStringEnd NewLine EndOfFile")]
        [InlineData("f'''{'a'}'''", "FStringStart LeftBrace StringLiteral RightBrace FStringEnd NewLine EndOfFile")]
        public void Kinds(string src, string expected) => Assert.Equal(expected, L.Kinds(src));

        // The braces of \N{...} belong to the escape, not to a replacement field.
        [Fact]
        public void NamedEscapeIsNotAField()
        {
            Assert.Equal("FStringStart FStringMiddle LeftBrace Name RightBrace FStringEnd NewLine EndOfFile",
                L.Kinds("f'a\\N{EURO SIGN}{x}'"));
            Assert.Equal("a€", Mids("f'a\\N{EURO SIGN}{x}'")[0].Text);
            Assert.Equal("\\N", Mids("rf'\\N{x}'")[0].Text);   // raw: no escape, so the braces ARE a field
        }

        [Fact]
        public void MiddleTexts()
        {
            Assert.Equal("{x}", Mids("f'{{x}}'")[0].Text);
            Assert.Equal("\\d", Mids("rf'\\d{x}'")[0].Text);
            Assert.Equal("\\", Mids("f'\\{x}'")[0].Text);
            Assert.Equal(">10", Mids("f'{x:>10}'")[0].Text);
            Assert.Equal("%Y-%m", Mids("f'{x:%Y-%m}'")[0].Text);
        }

        [Fact]
        public void NameColumnInsideField()
        {
            var t = L.Go("f'a{x}b'");
            var name = t[3]; // FStringStart, Middle(a), LBrace, Name(x)
            Assert.Equal(TokenKind.Name, name.Kind);
            Assert.Equal("x", name.Text);
            Assert.Equal(1, name.Line);
            Assert.Equal(5, name.Col); // f ' a { x  -> x at column 5
        }

        [Fact]
        public void NoNewlineInMultilineField()
            => Assert.Equal("FStringStart LeftBrace IntLiteral Plus IntLiteral RightBrace FStringEnd NewLine EndOfFile",
                            L.Kinds("f'''{1 +\n2}'''"));

        [Fact]
        public void NestedDepth3Legal()
        {
            var t = L.Go("f'{f\"{f'{1}'}\"}'"); // three levels
            Assert.Equal(TokenKind.EndOfFile, t[t.Count - 1].Kind);
        }

        [Fact]
        public void SameQuoteNestingLegal()
            => Assert.Equal("FStringStart LeftBrace Name LeftBracket StringLiteral RightBracket RightBrace FStringEnd NewLine EndOfFile",
                            L.Kinds("f'{d['k']}'"));

        [Theory]
        [InlineData("f'{}'", "f-string: empty expression not allowed")]
        [InlineData("f'{!r}'", "f-string: empty expression not allowed")]
        [InlineData("f'}'", "f-string: single '}' is not allowed")]
        [InlineData("f'{a\\b}'", "f-string expression part cannot include a backslash")]
        [InlineData("f'{#x}'", "f-string expression part cannot include '#'")]
        [InlineData("f'{a)}'", "f-string: unmatched ')'")]
        [InlineData("f'{x:{y:{z}}}'", "f-string: expressions nested too deeply")]
        [InlineData("f'\\x7b}'", "f-string: single '}' is not allowed")]
        [InlineData("f'{x'", "EOL while scanning string literal")]
        [InlineData("f'{d['", "EOL while scanning string literal")]
        public void Errors(string src, string message)
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go(src));
            Assert.Equal(message, e.Message);
        }

        [Fact]
        public void FourLevelsTooDeep()
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go("f'{f\"{f'{f\"{1}\"}'}\"}'"));
            Assert.Equal("f-string: f-strings nested too deeply (limit is 3)", e.Message);
        }

        [Fact]
        public void FieldBracketsDoNotSuppressOuterNewline()
        {
            // f'({x})' has no top-level bracket: the following statement's NEWLINE is still emitted.
            Assert.Equal(
                "FStringStart FStringMiddle LeftBrace Name RightBrace FStringMiddle FStringEnd NewLine Name NewLine EndOfFile",
                L.Kinds("f'({x})'\ny\n"));
        }
    }
}

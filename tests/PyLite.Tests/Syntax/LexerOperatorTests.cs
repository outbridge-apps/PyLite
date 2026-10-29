using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Compatibility")]
    public class LexerOperatorTests
    {
        [Theory]
        [InlineData("a**=b", "Name DoubleStarAssign Name")]
        [InlineData("a**b", "Name DoubleStar Name")]
        [InlineData("a//=b", "Name DoubleSlashAssign Name")]
        [InlineData("a<=b<c", "Name LessEqual Name Less Name")]
        [InlineData("a!=b", "Name NotEqual Name")]
        [InlineData("x:=1", "Name ColonEqual IntLiteral")]
        [InlineData("x:1", "Name Colon IntLiteral")]
        [InlineData("a<>b", "Name Less Greater Name")]
        [InlineData("x...y", "Name Ellipsis Name")]
        [InlineData("a.b..c", "Name Dot Name Dot Dot Name")]
        [InlineData("@dec", "At Name")]
        [InlineData("a@=b", "Name At Assign Name")]
        [InlineData("a&b|c^d~e", "Name Ampersand Name Pipe Name Caret Name Tilde Name")]
        [InlineData("a<<=b", "Name LeftShiftAssign Name")]
        [InlineData("a>>=b", "Name RightShiftAssign Name")]
        public void OperatorSequences(string src, string head)
            => Assert.Equal(head + " NewLine EndOfFile", L.Kinds(src));

        [Fact]
        public void Arrow()
            => Assert.Equal("KwDef Name LeftParen Name RightParen Arrow Name Colon NewLine EndOfFile", L.Kinds("def f(x)->int:"));

        [Theory]
        [InlineData("a!b", 2)]
        [InlineData("a $ b", 3)]
        [InlineData("a`b", 2)]
        public void InvalidSyntaxAt(string src, int col)
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go(src));
            Assert.Equal("invalid syntax", e.Message);
            Assert.Equal(col, e.Col);
        }
    }
}

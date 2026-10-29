using System.Numerics;
using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Compatibility")]
    public class TokenTests
    {
        [Theory]
        [InlineData("while", "KwWhile")]
        [InlineData("async", "KwAsync")]
        [InlineData("await", "KwAwait")]
        [InlineData("True", "KwTrue")]
        public void TryGet_Known(string name, string expectedKind)
        {
            Assert.True(Keywords.TryGet(name, out var kind));
            Assert.Equal(expectedKind, kind.ToString());
        }

        [Theory]
        [InlineData("print")]
        [InlineData("exec")]
        [InlineData("match")]
        [InlineData("true")]
        public void TryGet_NotKeyword(string name)
        {
            Assert.False(Keywords.TryGet(name, out _));
        }

        [Fact]
        public void KeywordTable_Has35()
        {
            Assert.Equal(35, Keywords.Table.Count);
        }

        [Fact]
        public void Token_FieldsRoundTrip()
        {
            var t = new Token(TokenKind.IntLiteral, "255", (BigInteger)255, 1, 1);
            Assert.Equal(TokenKind.IntLiteral, t.Kind);
            Assert.Equal("255", t.Text);
            Assert.Equal((BigInteger)255, (BigInteger)t.Value);
            Assert.Equal(1, t.Line);
            Assert.Equal(1, t.Col);
        }

        [Fact]
        public void SyntaxError_Factories()
        {
            var tab = PySyntaxErrorException.Tab(3, 1);
            Assert.Equal("TabError", tab.PythonType);
            Assert.Equal("inconsistent use of tabs and spaces in indentation", tab.Message);
            Assert.Equal(3, tab.Line);
            Assert.Equal(1, tab.Col);

            var syn = PySyntaxErrorException.Syntax("invalid token", 2, 5);
            Assert.Equal("SyntaxError", syn.PythonType);
            Assert.Equal(2, syn.Line);
            Assert.Equal(5, syn.Col);
        }
    }
}

using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Compatibility")]
    public class LexerIndentTests
    {
        [Fact]
        public void SimpleBlock()
            => Assert.Equal(
                "KwIf Name Colon NewLine Indent Name NewLine Dedent EndOfFile",
                L.Kinds("if x:\n    y\n"));

        [Fact]
        public void NestedThenFullDedent()
            => Assert.Equal(
                "KwIf Name Colon NewLine Indent KwIf Name Colon NewLine Indent Name NewLine Dedent Dedent Name NewLine EndOfFile",
                L.Kinds("if a:\n    if b:\n        c\nd\n"));

        [Fact]
        public void DedentAtEof()
            => Assert.Equal("KwIf Name Colon NewLine Indent Name NewLine Dedent EndOfFile", L.Kinds("if x:\n    y\n"));

        [Fact]
        public void UnindentMismatch()
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go("if a:\n    b\n  c\n"));
            Assert.Equal("IndentationError", e.PythonType);
            Assert.Equal("unindent does not match any outer indentation level", e.Message);
        }

        [Fact]
        public void MixedTabsAndSpacesInLine()
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go("if a:\n \tb\n"));
            Assert.Equal("TabError", e.PythonType);
            Assert.Equal("inconsistent use of tabs and spaces in indentation", e.Message);
        }

        [Fact]
        public void TabVsSpaceAcrossStack()
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go("if a:\n    b\n\tc\n"));
            Assert.Equal("TabError", e.PythonType);
        }

        [Fact]
        public void BlankAndCommentLinesDoNotIndent()
            => Assert.Equal("Name NewLine Name NewLine EndOfFile", L.Kinds("a\n\n   # c\nb\n"));
    }
}

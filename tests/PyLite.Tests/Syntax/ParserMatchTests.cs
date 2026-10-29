using System.Text;
using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Compatibility")]
    public class ParserMatchTests
    {
        private static string Err(string src)
        {
            var ex = Assert.Throws<PySyntaxErrorException>(() => AstDump.OfModule(src));
            return ex.Message;
        }

        // "a.b.b.…" with `dots` attribute accesses (dotted-name depth = dots + 1).
        private static string Dotted(int dots)
        {
            var sb = new StringBuilder("a");
            for (int i = 0; i < dots; i++)
                sb.Append(".b");
            return sb.ToString();
        }

        [Theory]
        [InlineData("match x:\n    case 1:\n        pass\n", "[(Match x (case 1 [(Pass)]))]")]
        [InlineData("match x:\n    case n:\n        pass\n", "[(Match x (case n [(Pass)]))]")]
        [InlineData("match x:\n    case _:\n        pass\n", "[(Match x (case _ [(Pass)]))]")]
        [InlineData("match x:\n    case -1:\n        pass\n", "[(Match x (case (USub 1) [(Pass)]))]")]
        [InlineData("match x:\n    case a.b:\n        pass\n", "[(Match x (case (Attr a b) [(Pass)]))]")]
        [InlineData("match x:\n    case [a, *b]:\n        pass\n", "[(Match x (case [a *b] [(Pass)]))]")]
        [InlineData("match x:\n    case 1 | 2 | 3:\n        pass\n", "[(Match x (case (| 1 2 3) [(Pass)]))]")]
        [InlineData("match x:\n    case [1, y] as w:\n        pass\n", "[(Match x (case (as [1 y] w) [(Pass)]))]")]
        [InlineData("match x:\n    case {1: a, **r}:\n        pass\n", "[(Match x (case {1: a **r} [(Pass)]))]")]
        [InlineData("match x:\n    case n if n:\n        pass\n", "[(Match x (case n if n [(Pass)]))]")]
        [InlineData("match a, b:\n    case [1, 2]:\n        pass\n", "[(Match (Tuple a b) (case [1 2] [(Pass)]))]")]
        public void MatchShapes(string src, string expected) => Assert.Equal(expected, AstDump.OfModule(src));

        [Fact] public void ClassPatternShape() => Assert.Equal("[(Match x (case (cls Point 0 y=b) [(Pass)]))]", AstDump.OfModule("match x:\n    case Point(0, y=b):\n        pass\n"));
        [Fact] public void EmptyMatchRejected() => Assert.Equal("expected 'case' block", Err("match x:\n    pass\n"));
        [Fact] public void MatchAsIdentifierAssign() => Assert.Equal("[(Assign [match] 5)]", AstDump.OfModule("match = 5\n"));
        [Fact] public void MatchAsIdentifierCall() => Assert.Equal("[(Expr (Call (Attr match group) 0))]", AstDump.OfModule("match.group(0)\n"));

        // A dotted name inside a match pattern (value / class head / mapping key) grows an AttributeNode chain
        // iteratively, just like a postfix trailer. Without a depth guard the resolver's recursive walk of that
        // chain overflows the C# stack (uncatchable StackOverflow → host crash). The parser's fold/trailer guard
        // is now applied to these three pattern paths too, so an over-deep dotted name is a SyntaxError.
        [Fact]
        public void DeepValuePatternDottedNameRejected()
        {
            string msg = Err("match x:\n    case " + Dotted(1500) + ":\n        pass\n");
            Assert.Contains("expression too deeply nested", msg);
        }

        [Fact]
        public void DeepClassPatternDottedNameRejected()
        {
            string msg = Err("match x:\n    case " + Dotted(1500) + "():\n        pass\n");
            Assert.Contains("expression too deeply nested", msg);
        }

        [Fact]
        public void DeepMappingKeyDottedNameRejected()
        {
            string msg = Err("match x:\n    case {" + Dotted(1500) + ": 1}:\n        pass\n");
            Assert.Contains("expression too deeply nested", msg);
        }

        // Guard bounds depth only: an ordinary short dotted pattern still parses.
        [Fact]
        public void ShortDottedValuePatternParses()
        {
            Assert.Equal("[(Match x (case (Attr (Attr a b) b) [(Pass)]))]",
                AstDump.OfModule("match x:\n    case " + Dotted(2) + ":\n        pass\n"));
        }
    }
}

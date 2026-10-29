using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Compatibility")]
    public class ParserAtomTests
    {
        private static string Err(string src)
        {
            var ex = Assert.Throws<PySyntaxErrorException>(() => AstDump.Expr(src));
            return ex.Message;
        }

        [Theory]
        [InlineData("x[1:2:3]", "(Index x (Slice 1 2 3))")]
        [InlineData("x[:]", "(Index x (Slice _ _ _))")]
        [InlineData("x[::2]", "(Index x (Slice _ _ 2))")]
        [InlineData("x[1:]", "(Index x (Slice 1 _ _))")]
        [InlineData("x[a,b]", "(Index x (Tuple a b))")]
        [InlineData("x[1:2, 3]", "(Index x (Tuple (Slice 1 2 _) 3))")]
        [InlineData("f(a)(b)", "(Call (Call f a) b)")]
        [InlineData("a.b.c", "(Attr (Attr a b) c)")]
        [InlineData("a.b(c)[d]", "(Index (Call (Attr a b) c) d)")]
        [InlineData("()", "(Tuple)")]
        [InlineData("(1,)", "(Tuple 1)")]
        [InlineData("(1)", "1")]
        [InlineData("{}", "(Dict)")]
        [InlineData("{1}", "(Set 1)")]
        [InlineData("{1:2}", "(Dict 1 2)")]
        [InlineData("{1, 2, 3}", "(Set 1 2 3)")]
        [InlineData("[1, 2]", "(List 1 2)")]
        [InlineData("[1, *a, 2]", "(List 1 (Star a) 2)")]
        [InlineData("[*a, *b]", "(List (Star a) (Star b))")]
        [InlineData("(*a,)", "(Tuple (Star a))")]
        [InlineData("{*a, 2}", "(Set (Star a) 2)")]
        [InlineData("{**a}", "(Dict **a)")]
        [InlineData("{1: 2, **b}", "(Dict 1 2 **b)")]
        [InlineData("{**a, **b}", "(Dict **a **b)")]
        public void AtomShapes(string src, string expected) => Assert.Equal(expected, AstDump.Of(src));

        [Theory]
        [InlineData("[x for x in y]", "(Comp List x (for x y))")]
        [InlineData("{k:v for k,v in it}", "(Comp Dict k v (for (Tuple k v) it))")]
        [InlineData("(x for x in y)", "(Comp Generator x (for x y))")]
        [InlineData("{x for x in y}", "(Comp Set x (for x y))")]
        [InlineData("[x for x in a if x>0 for y in b]", "(Comp List x (for x a) (if (Cmp x Gt 0)) (for y b))")]
        public void Comprehensions(string src, string expected) => Assert.Equal(expected, AstDump.Of(src));

        [Theory]
        [InlineData("'a' 'b' 'c'", "'abc'")]
        [InlineData("f'a{x}b'", "(FStr 'a' {x} 'b')")]
        [InlineData("f'{x!r}'", "(FStr {x!r})")]
        [InlineData("f'{x:>{w}}'", "(FStr {x:'>'{w}})")]
        [InlineData("f'{x}' 'lit'", "(FStr {x} 'lit')")]
        [InlineData("f'{x=}'", "(FStr 'x=' {x!r})")]
        [InlineData("f'{x = }'", "(FStr 'x = ' {x!r})")]
        [InlineData("f'{x=!s}'", "(FStr 'x=' {x!s})")]
        public void FStrings(string src, string expected) => Assert.Equal(expected, AstDump.Of(src));

        [Fact] public void StarredInGroupingRejected() => Assert.Equal("can't use starred expression here", Err("(*x)"));
        [Fact] public void InvalidConversion() => Assert.Contains("invalid conversion character", Err("f'{x!q}'"));
    }
}

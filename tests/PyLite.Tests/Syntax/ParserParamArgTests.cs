using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Compatibility")]
    public class ParserParamArgTests
    {
        private static string Err(string src)
        {
            var ex = Assert.Throws<PySyntaxErrorException>(() => AstDump.Expr(src));
            return ex.Message;
        }

        // ---- parameters (via lambda, which shares ParseParamList) ----
        [Theory]
        [InlineData("lambda: x", "(Lambda [] x)")]
        [InlineData("lambda a, b: a", "(Lambda [a,b] a)")]
        [InlineData("lambda x, y=1: x", "(Lambda [x,y=1] x)")]
        [InlineData("lambda *a: a", "(Lambda [*a] a)")]
        [InlineData("lambda **k: k", "(Lambda [**k] k)")]
        [InlineData("lambda *, k: k", "(Lambda [*,k] k)")]
        [InlineData("lambda a, *b, c, **d: c", "(Lambda [a,*b,c,**d] c)")]
        [InlineData("lambda a, b=1, *c, d, e=2, **g: d", "(Lambda [a,b=1,*c,d,e=2,**g] d)")]
        [InlineData("lambda *a, k, **kw: k", "(Lambda [*a,k,**kw] k)")]
        public void Params(string src, string expected) => Assert.Equal(expected, AstDump.Of(src));

        [Fact] public void NonDefaultAfterDefault() => Assert.Equal("non-default argument follows default argument", Err("lambda a=1, b: a"));
        [Fact] public void DuplicateParam() => Assert.Equal("duplicate argument 'a' in function definition", Err("lambda a, a: a"));
        [Fact] public void BareStarNeedsKwOnly() => Assert.Equal("named arguments must follow bare *", Err("lambda *: x"));

        // ---- call arguments ----
        [Theory]
        [InlineData("f()", "(Call f)")]
        [InlineData("f(1, 2)", "(Call f 1 2)")]
        [InlineData("f(a=1)", "(Call f a=1)")]
        [InlineData("f(1, k=2)", "(Call f 1 k=2)")]
        [InlineData("f(*a)", "(Call f *a)")]
        [InlineData("f(**a)", "(Call f **a)")]
        [InlineData("f(1, *a, k=2, **b)", "(Call f 1 *a k=2 **b)")]
        [InlineData("f(x for x in xs)", "(Call f (Comp Generator x (for x xs)))")]
        [InlineData("f(*a, k=1)", "(Call f *a k=1)")]
        [InlineData("f(*a, *b)", "(Call f *a *b)")]
        [InlineData("f(**a, **b)", "(Call f **a **b)")]
        [InlineData("f(*a, 2)", "(Call f *a 2)")]
        [InlineData("f(*a, b=1, *c)", "(Call f *a b=1 *c)")]
        public void Args(string src, string expected) => Assert.Equal(expected, AstDump.Of(src));

        [Fact] public void PositionalAfterKeyword() => Assert.Equal("positional argument follows keyword argument", Err("f(1, k=2, 3)"));
        [Fact] public void PositionalAfterKwargs() => Assert.Equal("positional argument follows keyword argument", Err("f(**a, 2)"));
        [Fact] public void StarAfterKwargs() => Assert.Equal("iterable argument unpacking follows keyword argument unpacking", Err("f(**a, *b)"));
        [Fact] public void KeywordRepeated() => Assert.Equal("keyword argument repeated", Err("f(a=1, a=2)"));
        [Fact] public void KeywordNotExpression() => Assert.Equal("keyword can't be an expression", Err("f(1+2=3)"));
        [Fact] public void GenexpMustBeParenthesized() => Assert.Equal("Generator expression must be parenthesized", Err("f(x for x in y, z)"));
    }
}

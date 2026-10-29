using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Compatibility")]
    public class ParserAssignTests
    {
        private static string Err(string src)
        {
            var ex = Assert.Throws<PySyntaxErrorException>(() => AstDump.Stmt(src));
            return ex.Message;
        }

        [Theory]
        [InlineData("a = 1", "(Assign [a] 1)")]
        [InlineData("a = b = 1", "(Assign [a b] 1)")]
        [InlineData("a, b = 1, 2", "(Assign [(Tuple a b)] (Tuple 1 2))")]
        [InlineData("a, *b = xs", "(Assign [(Tuple a (Star b))] xs)")]
        [InlineData("*a, b = xs", "(Assign [(Tuple (Star a) b)] xs)")]
        [InlineData("*a, = xs", "(Assign [(Tuple (Star a))] xs)")]
        [InlineData("[a, b] = xs", "(Assign [(List a b)] xs)")]
        [InlineData("a.b = 1", "(Assign [(Attr a b)] 1)")]
        [InlineData("a[0] = 1", "(Assign [(Index a 0)] 1)")]
        [InlineData("a += 1", "(Aug a Add 1)")]
        [InlineData("a[0] += 1", "(Aug (Index a 0) Add 1)")]
        [InlineData("a <<= 2", "(Aug a LShift 2)")]
        [InlineData("del a, b[0]", "(Del a (Index b 0))")]
        [InlineData("del a", "(Del a)")]
        [InlineData("x", "(Expr x)")]
        [InlineData("f(x)", "(Expr (Call f x))")]
        public void StatementShapes(string src, string expected) => Assert.Equal(expected, AstDump.OfStmt(src));

        [Fact] public void TwoStarred() => Assert.Equal("two starred expressions in assignment", Err("a, *b, *c = xs"));
        [Fact] public void AssignLiteral() => Assert.Equal("can't assign to literal", Err("1 = x"));
        [Fact] public void AssignCall() => Assert.Equal("can't assign to function call", Err("f() = x"));
        [Fact] public void AssignOperator() => Assert.Equal("can't assign to operator", Err("a + b = x"));
        [Fact] public void TopLevelStarred() => Assert.Equal("starred assignment target must be in a list or tuple", Err("*x = xs"));
        [Fact] public void AugTupleIllegal() => Assert.Equal("illegal expression for augmented assignment", Err("a, b += 1"));
        [Fact] public void DelStarred() => Assert.Equal("can't use starred expression in del", Err("del *a"));
        [Fact] public void DelLiteral() => Assert.Equal("can't delete literal", Err("del 1"));
        [Fact] public void VariableAnnotation() => Assert.Equal("(Assign [x] 1)", AstDump.OfStmt("x: int = 1"));
        [Fact] public void BareVariableAnnotation() => Assert.Equal("(Pass)", AstDump.OfStmt("x: int"));
        [Fact] public void TupleAnnotationRejected() => Assert.Equal("only single target (not tuple) can be annotated", Err("a, b: int = 1, 2"));
        [Fact] public void Walrus() => Assert.Equal("(Expr (Named a 1))", AstDump.OfStmt("a := 1"));
        [Fact] public void MatMul() => Assert.Equal("the '@' (matrix multiply) operator is not supported in this dialect", Err("a @ b"));
    }
}

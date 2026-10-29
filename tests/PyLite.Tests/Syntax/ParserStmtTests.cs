using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Compatibility")]
    public class ParserStmtTests
    {
        private static string Err(string src)
        {
            var ex = Assert.Throws<PySyntaxErrorException>(() => AstDump.Module(src));
            return ex.Message;
        }

        [Theory]
        [InlineData("if x: a", "(If x [(Expr a)] [])")]
        [InlineData("if x:\n a\n b", "(If x [(Expr a) (Expr b)] [])")]
        [InlineData("if a: p\nelif b: q\nelse: r", "(If a [(Expr p)] [(If b [(Expr q)] [(Expr r)])])")]
        [InlineData("while x: pass", "(While x [(Pass)] [])")]
        [InlineData("while x: pass\nelse: y", "(While x [(Pass)] [(Expr y)])")]
        [InlineData("for i in xs: pass", "(For i xs [(Pass)] [])")]
        [InlineData("for a, b in xs: pass", "(For (Tuple a b) xs [(Pass)] [])")]
        [InlineData("for a, *b in xs: pass", "(For (Tuple a (Star b)) xs [(Pass)] [])")]
        [InlineData("try: a\nfinally: b", "(Try [(Expr a)] [] [] [(Expr b)])")]
        [InlineData("try: a\nexcept: b", "(Try [(Expr a)] [(except _ _ [(Expr b)])] [] [])")]
        [InlineData("try: a\nexcept E as e: b", "(Try [(Expr a)] [(except E e [(Expr b)])] [] [])")]
        [InlineData("try: a\nexcept A: b\nexcept B: c\nelse: d\nfinally: e",
            "(Try [(Expr a)] [(except A _ [(Expr b)]) (except B _ [(Expr c)])] [(Expr d)] [(Expr e)])")]
        [InlineData("def f(): pass", "(Def f [] [(Pass)])")]
        [InlineData("def f(a, b): return a", "(Def f [a,b] [(Return a)])")]
        [InlineData("def f(): return 1, 2", "(Def f [] [(Return (Tuple 1 2))])")]
        public void CompoundShapes(string src, string expected) => Assert.Equal(expected, AstDump.OfStmt(src));

        [Theory]
        [InlineData("import a", "[(Import a)]")]
        [InlineData("import a.b.c as x", "[(Import a.b.c=x)]")]
        [InlineData("import a, b as c", "[(Import a b=c)]")]
        [InlineData("from a.b import c", "[(From a.b c)]")]
        [InlineData("from a import b as c, d", "[(From a b=c d)]")]
        [InlineData("from a import (b, c,)", "[(From a b c)]")]
        [InlineData("global a, b", "[(Global a,b)]")]
        [InlineData("nonlocal x", "[(Nonlocal x)]")]
        [InlineData("assert x", "[(Assert x _)]")]
        [InlineData("assert x, 'm'", "[(Assert x 'm')]")]
        [InlineData("raise", "[(Raise _ _)]")]
        [InlineData("raise E('x')", "[(Raise (Call E 'x') _)]")]
        [InlineData("raise E from c", "[(Raise E c)]")]
        [InlineData("a; b; c", "[(Expr a) (Expr b) (Expr c)]")]
        [InlineData("pass;", "[(Pass)]")]
        public void SimpleShapes(string src, string expected) => Assert.Equal(expected, AstDump.OfModule(src));

        [Fact] public void BareExceptNotLast() => Assert.Equal("default 'except:' must be last", Err("try: a\nexcept: b\nexcept A: c"));
        [Fact] public void ElseWithoutExcept() => Assert.Equal("'else' without 'except'", Err("try: a\nelse: b"));
        [Fact] public void TryWithoutHandler() => Assert.Equal("expected 'except' or 'finally' block", Err("try: a"));

        [Fact] public void ClassParses() => Assert.Equal("[(Class C)]", AstDump.OfModule("class C: pass"));
        [Fact] public void YieldForbidden() => Assert.Equal("generators are not supported in this dialect; use a generator expression", Err("def f(): yield 1"));
        [Fact] public void WithLowersToTry() => Assert.Contains("(Try", AstDump.OfModule("with x: pass"));
        [Fact] public void AsyncWithForbidden() => Assert.Equal("async is not supported in this dialect", Err("async with x: pass"));
        [Fact] public void AsyncForbidden() => Assert.Equal("async is not supported in this dialect", Err("async def f(): pass"));
        [Fact] public void AwaitForbidden() => Assert.Equal("await is not supported in this dialect", Err("await x"));
        [Fact] public void DecoratedDef() => Assert.Equal("[(Def f @d [] [(Pass)])]", AstDump.OfModule("@d\ndef f(): pass"));
        [Fact] public void MultiDecoratedDef() => Assert.Equal("[(Def f @a @(Call b 1) [] [(Pass)])]", AstDump.OfModule("@a\n@b(1)\ndef f(): pass"));
        [Fact] public void DecoratedClass() => Assert.Equal("[(Class C @d)]", AstDump.OfModule("@d\nclass C: pass"));
        [Fact] public void StarImportForbidden() => Assert.Equal("from ... import * is not supported in this dialect; import names explicitly", Err("from a import *"));
        [Fact] public void RelativeImportForbidden() => Assert.Equal("relative imports are not supported in this dialect", Err("from . import x"));
        [Fact] public void ParamAnnotationDiscarded() => Assert.Equal("[(Def f [x] [(Pass)])]", AstDump.OfModule("def f(x: int): pass"));
        [Fact] public void ParamAnnotationWithDefault() => Assert.Equal("[(Def f [x=5] [(Pass)])]", AstDump.OfModule("def f(x: int = 5): pass"));
        [Fact] public void ReturnAnnotationDiscarded() => Assert.Equal("[(Def f [] [(Pass)])]", AstDump.OfModule("def f() -> int: pass"));
        [Fact] public void PositionalOnlyParams() => Assert.Equal("[(Def f [a,b,/,c] [(Pass)])]", AstDump.OfModule("def f(a, b, /, c): pass"));
        [Fact] public void SlashLeadingRejected() => Assert.Equal("at least one argument must precede /", Err("def f(/, a): pass"));

        [Fact]
        public void NestedComprehensionStatement()
            => Assert.Equal("[(Expr (Comp List x (for x a) (if (Cmp x Gt 0)) (for y b)))]",
                AstDump.OfModule("[x for x in a if x>0 for y in b]"));
    }
}

using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Compatibility")]
    public class ResolverValidateTests
    {
        private static string Err(string src)
        {
            var ex = Assert.Throws<PySyntaxErrorException>(() => Resolver.Resolve(AstDump.Module(src), SyntaxLimits.Default));
            return ex.Message;
        }
        private static void Ok(string src) { Resolver.Resolve(AstDump.Module(src), SyntaxLimits.Default); }

        [Fact] public void ReturnOutsideFunction() => Assert.Equal("'return' outside function", Err("return 1"));
        [Fact] public void BreakOutsideLoop() => Assert.Equal("'break' outside loop", Err("break"));
        [Fact] public void ContinueOutsideLoop() => Assert.Equal("'continue' not properly in loop", Err("continue"));

        [Fact] public void ReturnInFunctionOk() => Ok("def f(): return 1");
        [Fact] public void BreakInLoopOk() => Ok("for i in x:\n break");

        [Fact] public void BreakInNestedFunction() => Assert.Equal("'break' outside loop", Err("for i in x:\n def g(): break"));
        [Fact] public void ContinueInFinally() => Ok("while x:\n try: pass\n finally: continue");   // 3.8+ (RFC-003 §4)

        [Fact] public void NonlocalNoBinding() => Assert.Equal("no binding for nonlocal 'x' found", Err("def f(): nonlocal x"));
        [Fact] public void NonlocalAtModule() => Assert.Equal("nonlocal declaration not allowed at module level", Err("nonlocal x"));
        [Fact] public void AssignedBeforeGlobal() => Assert.Equal("name 'x' is assigned to before global declaration", Err("def f():\n x=1\n global x"));
        [Fact] public void UsedBeforeGlobal() => Assert.Equal("name 'x' is used prior to global declaration", Err("def f():\n print(x)\n global x"));
        [Fact] public void ParameterAndGlobal() => Assert.Equal("name 'x' is parameter and global", Err("def f(x):\n global x"));
        [Fact] public void NonlocalAndGlobal() => Assert.Equal("name 'x' is nonlocal and global", Err("def f():\n global x\n nonlocal x"));
        [Fact] public void ParameterAndNonlocal() => Assert.Equal("name 'x' is parameter and nonlocal", Err("def g(x):\n nonlocal x"));

        [Fact] public void NonlocalWriteOk() => Ok("def f():\n x=1\n def g():\n  nonlocal x\n  x=2");

        // What runs in the enclosing scope counts as a use there, as in CPython's symtable (D3: one AstWalker).
        [Theory]
        [InlineData("def f():\n print(f'{x}')\n global x")]
        [InlineData("def f():\n g = lambda a=x: a\n global x")]
        [InlineData("def f():\n [y for y in x]\n global x")]
        [InlineData("def f():\n def g(a=x): pass\n global x")]
        public void UseInEnclosingScopeBeforeGlobal(string src)
        {
            Assert.Equal("name 'x' is used prior to global declaration", Err(src));
        }

        // A nested scope's own reads do not count.
        [Theory]
        [InlineData("def f():\n g = lambda: x\n global x")]
        [InlineData("def f():\n [x for y in z]\n global x")]
        [InlineData("def f():\n class C:\n  def m(self, a=x): pass\n global x")]
        public void UseInNestedScopeIsNotAUseBeforeGlobal(string src)
        {
            Ok(src);
        }
    }
}

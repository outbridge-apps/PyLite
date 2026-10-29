using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Syntax.Ast;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Compatibility")]
    public class CompilerTests
    {
        private static CompiledScript Compile(string src) { return Compiler.Compile(src, "<test>", SyntaxLimits.Default); }
        private static string Err(string src) { return Assert.Throws<PySyntaxErrorException>(() => Compile(src)).Message; }

        [Fact]
        public void EmptySource()
        {
            var cs = Compile("");
            Assert.Empty(cs.Program.Module.Body);
        }

        [Fact] public void CommentOnly() => Assert.Empty(Compile("# comment\n").Program.Module.Body);

        [Fact]
        public void SingleAssign()
        {
            var cs = Compile("x = 1\n");
            Assert.Single(cs.Program.Module.Body);
            Assert.IsType<AssignNode>(cs.Program.Module.Body[0]);
            Assert.NotNull(cs.Program.ModuleInfo);
        }

        [Fact]
        public void FunctionCompiles()
        {
            var cs = Compile("def f(): return x\n");
            var f = (FuncDefNode)cs.Program.Module.Body[0];
            var xr = (NameNode)((ReturnNode)f.Body[0]).Value;
            Assert.Equal(NameKind.Builtin, cs.Program.GetBinding(xr).Kind);   // undefined -> builtin lookup
        }

        [Fact]
        public void ClassCompiles()
        {
            var cs = Compile("class C:\n    def __init__(self):\n        self.x = 1\n");
            Assert.IsType<ClassDefNode>(cs.Program.Module.Body[0]);
        }
        [Fact] public void ReturnOutsidePropagates() => Assert.Equal("'return' outside function", Err("return 1\n"));

        [Fact]
        public void IncompleteExpression()
        {
            Assert.Throws<PySyntaxErrorException>(() => Compile("1 +\n"));
        }

        [Fact]
        public void RepeatedCompileIndependent()
        {
            var a = Compile("x=1\n");
            var b = Compile("x=1\n");
            Assert.NotSame(a, b);
            Assert.NotSame(a.Program, b.Program);
        }

        [Fact] public void DialectVersion() => Assert.Equal(DialectConstants.Version, Compile("x=1\n").DialectVersion);
    }
}

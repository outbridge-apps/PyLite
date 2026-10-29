using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Syntax.Ast;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Compatibility")]
    public class ResolverTests
    {
        private static ResolvedProgram Rp(string src)
        {
            var mod = AstDump.Module(src);
            return Resolver.Resolve(mod, SyntaxLimits.Default);
        }
        private static ModuleNode Mod(string src) { return AstDump.Module(src); }

        [Fact]
        public void LocalName()
        {
            var mod = Mod("def f():\n x=1\n return x");
            var rp = Resolver.Resolve(mod, SyntaxLimits.Default);
            var f = (FuncDefNode)mod.Body[0];
            var xt = (NameNode)((AssignNode)f.Body[0]).Targets[0];
            var xr = (NameNode)((ReturnNode)f.Body[1]).Value;
            Assert.Equal(NameKind.Local, rp.GetBinding(xt).Kind);
            Assert.Equal(NameKind.Local, rp.GetBinding(xr).Kind);
        }

        [Fact]
        public void GlobalFromModule()
        {
            var mod = Mod("x=1\ndef f():\n return x");
            var rp = Resolver.Resolve(mod, SyntaxLimits.Default);
            var f = (FuncDefNode)mod.Body[1];
            var xr = (NameNode)((ReturnNode)f.Body[0]).Value;
            Assert.Equal(NameKind.Global, rp.GetBinding(xr).Kind);
        }

        [Fact]
        public void CellAndFree()
        {
            var mod = Mod("def f():\n x=1\n def g():\n  return x");
            var rp = Resolver.Resolve(mod, SyntaxLimits.Default);
            var f = (FuncDefNode)mod.Body[0];
            var g = (FuncDefNode)f.Body[1];
            var xInG = (NameNode)((ReturnNode)g.Body[0]).Value;
            var xInF = (NameNode)((AssignNode)f.Body[0]).Targets[0];
            Assert.Equal(NameKind.Free, rp.GetBinding(xInG).Kind);
            Assert.Equal(NameKind.Cell, rp.GetBinding(xInF).Kind);
            Assert.Contains("x", rp.GetFunctionInfo(f).CellVars);
            Assert.Contains("x", rp.GetFunctionInfo(g).FreeVars);
        }

        [Fact]
        public void GlobalDeclaration()
        {
            var mod = Mod("def f():\n global x\n x=1");
            var rp = Resolver.Resolve(mod, SyntaxLimits.Default);
            var f = (FuncDefNode)mod.Body[0];
            var xt = (NameNode)((AssignNode)f.Body[1]).Targets[0];
            Assert.Equal(NameKind.Global, rp.GetBinding(xt).Kind);
        }

        [Fact]
        public void NonlocalWrite()
        {
            var mod = Mod("def f():\n x=1\n def g():\n  nonlocal x\n  x=2");
            var rp = Resolver.Resolve(mod, SyntaxLimits.Default);
            var f = (FuncDefNode)mod.Body[0];
            var g = (FuncDefNode)f.Body[1];
            var xt = (NameNode)((AssignNode)g.Body[1]).Targets[0];
            Assert.Equal(NameKind.Free, rp.GetBinding(xt).Kind);
        }

        [Fact]
        public void BuiltinName()
        {
            var mod = Mod("def f():\n return len");
            var rp = Resolver.Resolve(mod, SyntaxLimits.Default);
            var f = (FuncDefNode)mod.Body[0];
            var lenr = (NameNode)((ReturnNode)f.Body[0]).Value;
            Assert.Equal(NameKind.Builtin, rp.GetBinding(lenr).Kind);
        }

        [Fact]
        public void UseBeforeAssignIsLocal()
        {
            var mod = Mod("def f():\n print(y)\n y=1");
            var rp = Resolver.Resolve(mod, SyntaxLimits.Default);
            var f = (FuncDefNode)mod.Body[0];
            var call = (CallNode)((ExprStmtNode)f.Body[0]).Value;
            var y = (NameNode)call.Args[0].Value;
            Assert.Equal(NameKind.Local, rp.GetBinding(y).Kind);
        }

        [Fact]
        public void ComprehensionLocalDoesNotLeak()
        {
            var mod = Mod("[x for x in r]");
            var rp = Resolver.Resolve(mod, SyntaxLimits.Default);
            var comp = (ComprehensionNode)((ExprStmtNode)mod.Body[0]).Value;
            var xElem = (NameNode)comp.Element;
            Assert.Equal(NameKind.Local, rp.GetBinding(xElem).Kind);
            Assert.Contains("x", rp.GetFunctionInfo(comp).LocalNames);
        }

        [Fact]
        public void ComprehensionClosesOverParam()
        {
            var mod = Mod("def f(a):\n return [a for _ in range(3)]");
            var rp = Resolver.Resolve(mod, SyntaxLimits.Default);
            var f = (FuncDefNode)mod.Body[0];
            var comp = (ComprehensionNode)((ReturnNode)f.Body[0]).Value;
            Assert.Contains("a", rp.GetFunctionInfo(comp).FreeVars);
            Assert.Contains("a", rp.GetFunctionInfo(f).CellVars);
        }

        [Fact]
        public void ShadowBuiltinAtModule()
        {
            var mod = Mod("list=[1]\ndef f():\n return list");
            var rp = Resolver.Resolve(mod, SyntaxLimits.Default);
            var f = (FuncDefNode)mod.Body[1];
            var lr = (NameNode)((ReturnNode)f.Body[0]).Value;
            Assert.Equal(NameKind.Global, rp.GetBinding(lr).Kind);
        }

        [Fact]
        public void FreeThreadedThroughIntermediate()
        {
            var mod = Mod("def f():\n def g():\n  def h(): return v\n  return h\n v=1");
            var rp = Resolver.Resolve(mod, SyntaxLimits.Default);
            var f = (FuncDefNode)mod.Body[0];
            var g = (FuncDefNode)f.Body[0];
            var h = (FuncDefNode)g.Body[0];
            Assert.Contains("v", rp.GetFunctionInfo(h).FreeVars);
            Assert.Contains("v", rp.GetFunctionInfo(g).FreeVars);   // threaded
            Assert.Contains("v", rp.GetFunctionInfo(f).CellVars);
        }

        // ---- FunctionInfo layout ----

        [Fact]
        public void SimpleParams()
        {
            var mod = Mod("def f(a, b): return a+b");
            var rp = Resolver.Resolve(mod, SyntaxLimits.Default);
            var fi = rp.GetFunctionInfo((FuncDefNode)mod.Body[0]);
            Assert.Equal(2, fi.ParamCount);
            Assert.Equal(2, fi.LocalSlotCount);
            Assert.Empty(fi.CellVars);
            Assert.Empty(fi.FreeVars);
            Assert.Equal(new[] { 0, 1 }, fi.ParamSlots);
        }

        [Fact]
        public void StarKwParams()
        {
            var mod = Mod("def f(*a, **k): pass");
            var rp = Resolver.Resolve(mod, SyntaxLimits.Default);
            var fi = rp.GetFunctionInfo((FuncDefNode)mod.Body[0]);
            Assert.True(fi.HasStarArg);
            Assert.True(fi.HasKwArg);
            Assert.Equal(new[] { "a", "k" }, fi.ParamNames);
        }

        [Fact]
        public void KeywordOnlyCounts()
        {
            var mod = Mod("def f(a, *, b): pass");
            var rp = Resolver.Resolve(mod, SyntaxLimits.Default);
            var fi = rp.GetFunctionInfo((FuncDefNode)mod.Body[0]);
            Assert.Equal(1, fi.PositionalCount);
            Assert.Equal(1, fi.KwOnlyCount);
        }

        [Fact]
        public void ParamIsCellForClosure()
        {
            var mod = Mod("def f(x):\n return lambda: x");
            var rp = Resolver.Resolve(mod, SyntaxLimits.Default);
            var f = (FuncDefNode)mod.Body[0];
            var lam = (LambdaNode)((ReturnNode)f.Body[0]).Value;
            var fi = rp.GetFunctionInfo(f);
            Assert.Equal(new[] { "x" }, fi.CellVars);
            Assert.Equal(new[] { true }, fi.ParamIsCell);
            Assert.Equal(new[] { "x" }, rp.GetFunctionInfo(lam).FreeVars);
        }

        [Fact]
        public void ModuleInfoKind()
        {
            var rp = Rp("x=1\ny=2");
            Assert.Equal(ScopeKind.Module, rp.ModuleInfo.Kind);
            Assert.Equal(0, rp.ModuleInfo.LocalSlotCount);
        }

        [Fact]
        public void FullSignature()
        {
            var mod = Mod("def f(a, b=1, *c, d, **e): pass");
            var rp = Resolver.Resolve(mod, SyntaxLimits.Default);
            var fi = rp.GetFunctionInfo((FuncDefNode)mod.Body[0]);
            Assert.Equal(new[] { "a", "b", "c", "d", "e" }, fi.ParamNames);
            Assert.Equal(2, fi.PositionalCount);
            Assert.Equal(1, fi.KwOnlyCount);
            Assert.True(fi.HasStarArg);
            Assert.True(fi.HasKwArg);
        }
    }
}

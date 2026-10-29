using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    [Trait("Category", "Compatibility")]
    public class EnvironmentTests
    {
        private static Environment Mod(EvalContext ctx, HostFunctionTable builtins = null)
        {
            return Environment.CreateModule(ctx, ctx.Values.Dict(4), builtins ?? HostFunctionTable.Empty);
        }

        // A mutable table seeded with a single name (replaces the removed Builder in these tests).
        private static HostFunctionTable TableWith(string name, ScriptValue value)
        {
            var t = HostFunctionTable.Empty.Clone();
            t.SetValue(name, value);
            return t;
        }

        private static Environment Fn(EvalContext ctx, Environment mod, int locals, int cells, Cell[] free)
        {
            return Environment.CreateFunction(ctx, locals, cells, free ?? new Cell[0], mod);
        }

        [Fact]
        public void LocalSetGet()
        {
            var ctx = Rt.Ctx();
            var fn = Fn(ctx, Mod(ctx), 2, 0, null);
            var v = ctx.Values.Int(7);
            fn.SetLocal(0, v);
            Assert.Same(v, fn.GetLocal(0, "x"));
        }

        [Fact]
        public void UnboundLocalGetThrows()
        {
            var ctx = Rt.Ctx();
            var fn = Fn(ctx, Mod(ctx), 2, 0, null);
            var ex = Assert.Throws<ScriptException>(() => fn.GetLocal(1, "y"));
            Assert.Same(PyExceptionTypes.UnboundLocalError, ex.Value.ExcType);
            Assert.Equal("local variable 'y' referenced before assignment", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void DelThenGetIsUnbound()
        {
            var ctx = Rt.Ctx();
            var fn = Fn(ctx, Mod(ctx), 1, 0, null);
            fn.SetLocal(0, ctx.Values.Int(1));
            fn.DelLocal(0, "x");
            Assert.Throws<ScriptException>(() => fn.GetLocal(0, "x"));
        }

        [Fact]
        public void DelTwiceThrows()
        {
            var ctx = Rt.Ctx();
            var fn = Fn(ctx, Mod(ctx), 1, 0, null);
            fn.SetLocal(0, ctx.Values.Int(1));
            fn.DelLocal(0, "x");
            Assert.Throws<ScriptException>(() => fn.DelLocal(0, "x"));
        }

        [Fact]
        public void GlobalMissingIsNameError()
        {
            var ctx = Rt.Ctx();
            var mod = Mod(ctx);
            var ex = Assert.Throws<ScriptException>(() => mod.GetGlobal("foo"));
            Assert.Same(PyExceptionTypes.NameError, ex.Value.ExcType);
            Assert.Equal("name 'foo' is not defined", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void GlobalPresentWins()
        {
            var ctx = Rt.Ctx();
            var mod = Mod(ctx);
            var v = ctx.Values.Int(11);
            mod.SetGlobal("x", v);
            Assert.Same(v, mod.GetGlobal("x"));
        }

        [Fact]
        public void BuiltinFallback()
        {
            var ctx = Rt.Ctx();
            var lenVal = ctx.Values.Int(99);
            var table = TableWith("len", lenVal);
            var mod = Mod(ctx, table);
            Assert.Same(lenVal, mod.GetGlobal("len"));
        }

        [Fact]
        public void ShadowingBuiltin()
        {
            var ctx = Rt.Ctx();
            var lenVal = ctx.Values.Int(99);
            var table = TableWith("len", lenVal);
            var mod = Mod(ctx, table);
            var five = ctx.Values.Int(5);
            mod.SetGlobal("len", five);
            Assert.Same(five, mod.GetGlobal("len"));
        }

        [Fact]
        public void DelGlobalRestoresBuiltin()
        {
            var ctx = Rt.Ctx();
            var lenVal = ctx.Values.Int(99);
            var mod = Mod(ctx, TableWith("len", lenVal));
            mod.SetGlobal("len", ctx.Values.Int(5));
            mod.DelGlobal("len");
            Assert.Same(lenVal, mod.GetGlobal("len"));
        }

        [Fact]
        public void DelGlobalMissingIsNameError()
        {
            var ctx = Rt.Ctx();
            var mod = Mod(ctx, TableWith("len", ctx.Values.Int(99)));
            Assert.Throws<ScriptException>(() => mod.DelGlobal("len"));   // present only in builtins
        }

        [Fact]
        public void CellSharedAcrossClosures()
        {
            var ctx = Rt.Ctx();
            var mod = Mod(ctx);
            var parent = Fn(ctx, mod, 0, 1, null);
            var child = Fn(ctx, mod, 0, 0, new[] { parent.CellAt(0) });
            var v = ctx.Values.Int(5);
            parent.SetCell(0, v);
            Assert.Same(v, child.GetFree(0, "c"));
        }

        [Fact]
        public void FreeUnboundIsNameError()
        {
            var ctx = Rt.Ctx();
            var mod = Mod(ctx);
            var fn = Fn(ctx, mod, 0, 0, new[] { new Cell() });
            var ex = Assert.Throws<ScriptException>(() => fn.GetFree(0, "z"));
            Assert.Same(PyExceptionTypes.NameError, ex.Value.ExcType);
            Assert.Equal("free variable 'z' referenced before assignment in enclosing scope", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void BuiltinRemovedIsNameError()
        {
            var ctx = Rt.Ctx();
            var mod = Mod(ctx);   // empty builtins
            Assert.Throws<ScriptException>(() => mod.GetGlobal("print"));
        }
    }
}

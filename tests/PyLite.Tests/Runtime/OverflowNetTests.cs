using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // The net under the call boundary: an arithmetic overflow that escapes a builtin (a cast of a
    // script's int past the C range that no guard caught) is the script's OverflowError, catchable and
    // with a line, never an EngineFault. The audit closed the known sites; this holds for the next one.
    [Trait("Category", "Sandbox")]
    public class OverflowNetTests
    {
        private static ScriptValue Boom(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext c)
        {
            long big = long.MaxValue;
            return c.Values.Int(checked((int)big));
        }

        private static DictValue Run(string src)
        {
            var ctx = Rt.Ctx();
            CompiledScript script = Compiler.Compile(src, "<test>", SyntaxLimits.Default);
            DictValue globals = ctx.Values.Dict(8);
            globals.SetItem(ctx.Values.Str("boom"), ctx.Values.BuiltinFunction("boom", Boom), ctx);
            var env = Outbridge.PyLite.Runtime.Environment.CreateModule(ctx, globals, HostFunctionTable.CreateDefault(), script.Program.ModuleInfo);
            new Outbridge.PyLite.Runtime.Evaluator.Evaluator(script.Program).RunModule(env, ctx);
            return globals;
        }

        [Fact]
        public void AnOverflowEscapingABuiltinIsAScriptOverflowError()
        {
            var ctx = Rt.Ctx();
            DictValue g = Run("try:\n    boom()\n    r = 'no error'\nexcept OverflowError as e:\n    r = str(e)\n");
            ScriptValue r;
            Assert.True(g.TryGet(ctx.Values.Str("r"), ctx, out r));
            Assert.Equal("Python int too large to convert to C int", ((StrValue)r).Value);
        }

        [Fact]
        public void TheErrorCarriesTheCallersLine()
        {
            var ex = Assert.Throws<Outbridge.PyLite.Runtime.Errors.ScriptException>(() => Run("x = 1\n\nboom()\n"));
            Assert.Equal("OverflowError", ex.Value.ExcType.Name);
            ScriptError err = ScriptErrorBuilder.Build(ex, Rt.Ctx());
            Assert.Equal(ScriptErrorKind.RuntimeError, err.Kind);
            Assert.Equal(3, err.Line);
        }

        [Fact]
        public void AnOverflowOutsideAnyCallIsStillNotAFault()
        {
            ScriptError err = ScriptErrorBuilder.Build(new System.OverflowException(), Rt.Ctx());
            Assert.Equal(ScriptErrorKind.RuntimeError, err.Kind);
            Assert.Equal("OverflowError", err.PythonType);
        }
    }
}

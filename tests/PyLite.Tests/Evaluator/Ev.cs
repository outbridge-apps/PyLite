using System.Collections.Generic;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Tests;

namespace Outbridge.PyLite.Tests.Evaluator
{
    // Minimal end-to-end harness for the evaluator: compile source, run the module body against a fresh
    // module environment, and expose the module globals for assertions (ScriptEngine is the real host).
    internal sealed class EvResult
    {
        public EvalContext Ctx;
        public DictValue Globals;

        public ScriptValue Get(string name)
        {
            return Globals.GetItemOrThrow(Ctx.Values.Str(name), Ctx);
        }

        public bool Has(string name)
        {
            ScriptValue ignored;
            return Globals.TryGet(Ctx.Values.Str(name), Ctx, out ignored);
        }

        public string Repr(string name)
        {
            return Get(name).Repr(Ctx);
        }

        // The text captured by print() during the run (impl/06 T-BLT-11 buffered sink).
        public string PrintOutput
        {
            get { return Ctx.PrintBuffer == null ? "" : Ctx.PrintBuffer.ToString(); }
        }
    }

    internal static class Ev
    {
        public static EvResult Run(string source, ResourceLimits limits = null, IScriptClock clock = null, ISleepPolicy sleepPolicy = null)
        {
            var ctx = MakeCtx(limits ?? ResourceLimits.Default, clock, sleepPolicy);
            CompiledScript script = Compiler.Compile(source, "<test>", SyntaxLimits.Default);
            DictValue globals = ctx.Values.Dict(8);
            ctx.Builtins = HostFunctionTable.CreateDefault();   // as ScriptEngine does: Python-source modules (string.Template) read them from the context
            Outbridge.PyLite.Runtime.Environment moduleEnv = Outbridge.PyLite.Runtime.Environment.CreateModule(ctx, globals, ctx.Builtins, script.Program.ModuleInfo);
            var ev = new Outbridge.PyLite.Runtime.Evaluator.Evaluator(script.Program);
            ev.RunModule(moduleEnv, ctx);
            return new EvResult { Ctx = ctx, Globals = globals };
        }

        // A run context with the engine's own module registry, so import tests see what a host sees.
        private static EvalContext MakeCtx(ResourceLimits lim, IScriptClock clock = null, ISleepPolicy sleepPolicy = null)
        {
            var budget = new Budget(lim, System.Diagnostics.Stopwatch.StartNew(), System.Threading.CancellationToken.None);
            var modules = new ModuleRegistry();
            DefaultModules.RegisterBuiltins(modules);
            modules.Freeze();
            var ctx = new EvalContext(budget, lim, 0x12345678u, modules, HostFunctionTable.Empty,
                clock ?? new SystemClock(System.TimeSpan.Zero, "UTC"), sleepPolicy ?? new DefaultSleepPolicy(), new RunStatistics());
            ctx.Format = Outbridge.PyLite.Modules.Support.PyFormatServices.Instance;
            return ctx;
        }

        // Repr of a single expression, evaluated as `__r__ = (expr)`.
        public static string ReprOf(string expr, ResourceLimits limits = null)
        {
            return ReprOf("", expr, limits);
        }

        // The same after a prelude (imports, definitions) the expression reads.
        public static string ReprOf(string prelude, string expr, ResourceLimits limits = null, IScriptClock clock = null)
        {
            EvResult r = Run(prelude + "__r__ = (" + expr + ")\n", limits, clock);
            return r.Repr("__r__");
        }

        // The message / type of the script exception the source raises.
        public static string Err(string src)
        {
            return Xunit.Assert.Throws<ScriptException>(() => Run(src)).Value.GetMessageText(Rt.Ctx());
        }

        public static PyExceptionType ErrType(string src)
        {
            return Xunit.Assert.Throws<ScriptException>(() => Run(src)).Value.ExcType;
        }
    }
}

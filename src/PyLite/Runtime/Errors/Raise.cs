using System;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Runtime.Errors
{
    // The single way to throw a script error from engine C# code. Every path
    // charges Step(Raise) and allocates args through the ValueFactory. Message is truncated before
    // the string allocation.
    public static class Raise
    {
        public static ScriptException Make(EvalContext ctx, PyExceptionType t, string message)
        {
            if (message.Length > EngineHardLimits.MaxErrorMessageChars)
                message = message.Substring(0, EngineHardLimits.MaxErrorMessageChars);
            return Make(ctx, t, new ScriptValue[] { ctx.Values.Str(message) });
        }

        public static ScriptException Make(EvalContext ctx, PyExceptionType t, ScriptValue[] args)
        {
            ctx.Budget.Step(BudgetCost.Raise);
            ExceptionValue ev = ctx.Values.RaisedException(t, args);   // charged if a handler keeps it
            ev.RaiseLine = ctx.CurrentLine;
            if (ctx.CurrentHandledException != null)
                Evaluator.Evaluator.SetContextChain(ev, ctx.CurrentHandledException);   // as `raise` does: raised inside an except block
            return new ScriptException(ev);
        }

        public static ScriptException TypeError(EvalContext ctx, string m) => Make(ctx, PyExceptionTypes.TypeError, m);
        public static ScriptException ValueError(EvalContext ctx, string m) => Make(ctx, PyExceptionTypes.ValueError, m);
        public static ScriptException RuntimeError(EvalContext ctx, string m) => Make(ctx, PyExceptionTypes.RuntimeError, m);
        public static ScriptException RecursionError(EvalContext ctx, string m) => Make(ctx, PyExceptionTypes.RecursionError, m);
        public static ScriptException Overflow(EvalContext ctx, string m) => Make(ctx, PyExceptionTypes.OverflowError, m);
        public static ScriptException ZeroDivision(EvalContext ctx, string m) => Make(ctx, PyExceptionTypes.ZeroDivisionError, m);
        public static ScriptException IndexError(EvalContext ctx, string m) => Make(ctx, PyExceptionTypes.IndexError, m);
        public static ScriptException NotImplemented(EvalContext ctx, string m) => Make(ctx, PyExceptionTypes.NotImplementedError, m);
        public static ScriptException KeyError(EvalContext ctx, ScriptValue key) => Make(ctx, PyExceptionTypes.KeyError, new[] { key });
        public static ScriptException StopIteration(EvalContext ctx) => Make(ctx, PyExceptionTypes.StopIteration, Array.Empty<ScriptValue>());

        // The missing name rides along as e.name (a string field, not a dict: getattr with a default raises these in loops).
        public static ScriptException Attribute(EvalContext ctx, string typeName, string attr)
            => Named(Make(ctx, PyExceptionTypes.AttributeError, "'" + typeName + "' object has no attribute '" + attr + "'"), attr);

        public static ScriptException NameError(EvalContext ctx, string name)
            => Named(Make(ctx, PyExceptionTypes.NameError, "name '" + name + "' is not defined"), name);

        private static ScriptException Named(ScriptException e, string name)
        {
            e.Value.MissingName = name;
            return e;
        }

        public static ScriptException UnboundLocal(EvalContext ctx, string name)
            => Make(ctx, PyExceptionTypes.UnboundLocalError, "cannot access local variable '" + name + "' where it is not associated with a value");

        public static ScriptException FreeVariable(EvalContext ctx, string name)
            => Make(ctx, PyExceptionTypes.NameError, "cannot access free variable '" + name + "' where it is not associated with a value in enclosing scope");

        public static ScriptException Import(EvalContext ctx, string m) => Make(ctx, PyExceptionTypes.ImportError, m);
    }
}

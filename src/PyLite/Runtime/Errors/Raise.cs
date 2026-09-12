using System;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Runtime.Errors
{
    // The single way to throw a script error from engine C# code. Every path
    // charges Step(Raise) and allocates args through the ValueFactory. Message is truncated before
    // the string allocation (A8-#23).
    public static class Raise
    {
        public static ScriptException Make(EvalContext ctx, PyExceptionType t, string message)
        {
            ctx.Budget.Step(BudgetCost.Raise);
            if (message.Length > EngineHardLimits.MaxErrorMessageChars)
                message = message.Substring(0, EngineHardLimits.MaxErrorMessageChars);
            ScriptValue[] args = { ctx.Values.Str(message) };
            var ev = new ExceptionValue(t, args) { RaiseLine = ctx.CurrentLine };
            return new ScriptException(ev);
        }

        public static ScriptException Make(EvalContext ctx, PyExceptionType t, ScriptValue[] args)
        {
            ctx.Budget.Step(BudgetCost.Raise);
            var ev = new ExceptionValue(t, args) { RaiseLine = ctx.CurrentLine };
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

        public static ScriptException Attribute(EvalContext ctx, string typeName, string attr)
            => Make(ctx, PyExceptionTypes.AttributeError, "'" + typeName + "' object has no attribute '" + attr + "'");

        public static ScriptException NameError(EvalContext ctx, string name)
            => Make(ctx, PyExceptionTypes.NameError, "name '" + name + "' is not defined");

        public static ScriptException UnboundLocal(EvalContext ctx, string name)
            => Make(ctx, PyExceptionTypes.UnboundLocalError, "local variable '" + name + "' referenced before assignment");

        public static ScriptException FreeVariable(EvalContext ctx, string name)
            => Make(ctx, PyExceptionTypes.NameError, "free variable '" + name + "' referenced before assignment in enclosing scope");

        public static ScriptException Import(EvalContext ctx, string m) => Make(ctx, PyExceptionTypes.ImportError, m);
    }
}

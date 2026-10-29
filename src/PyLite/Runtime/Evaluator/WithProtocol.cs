using System;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Runtime.Evaluator
{
    // The two engine helpers the parser's lowering of `with` calls (see Parser.ParseWith). Their names
    // start with '$', so only that lowering can reach them; Environment resolves them after the host's
    // builtins, which therefore cannot remove or replace them.
    internal static class WithProtocol
    {
        private static readonly BuiltinFunctionValue Enter = BuiltinFunctionValue.Make("$with_enter", EnterImpl);
        private static readonly BuiltinFunctionValue Exit = BuiltinFunctionValue.Make("$with_exit", ExitImpl);

        internal static bool TryGetHelper(string name, out ScriptValue helper)
        {
            helper = null;
            if (name.Length == 0 || name[0] != '$')
                return false;
            if (name == "$with_enter")
                helper = Enter;
            else if (name == "$with_exit")
                helper = Exit;
            return helper != null;
        }

        // Both methods are looked up before __enter__ runs, as CPython does, with CPython's messages.
        private static ScriptValue EnterImpl(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ScriptValue mgr = a[0];
            IContextManager engineMgr = mgr as IContextManager;
            if (engineMgr != null)
                return engineMgr.Enter(ctx);
            ScriptValue enter = Special(mgr, "__enter__", ctx);
            if (enter == null)
                throw Raise.TypeError(ctx, "'" + mgr.PyTypeName + "' object does not support the context manager protocol");
            if (Special(mgr, "__exit__", ctx) == null)
            {
                throw Raise.TypeError(ctx, "'" + mgr.PyTypeName
                    + "' object does not support the context manager protocol (missed __exit__ method)");
            }
            return ctx.CallHook(enter, Array.Empty<ScriptValue>(), KwArgs.Empty);
        }

        // $with_exit(mgr, failed): failed => __exit__(type(e), e, None) for the exception being handled,
        // answering whether it is suppressed; else __exit__(None, None, None). The traceback argument is
        // None: the dialect has no traceback objects. While the run is ending on a resource limit the
        // manager is not called at all — the limit is not an exception a script may see or swallow.
        private static ScriptValue ExitImpl(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            if (ctx.Budget.IsTerminating)
                return ctx.Values.Bool(false);
            ExceptionValue e = a[1].IsTruthy(ctx) ? ctx.CurrentHandledException : null;
            IContextManager engineMgr = a[0] as IContextManager;
            if (engineMgr != null)
                return ctx.Values.Bool(engineMgr.Exit(ctx, e));
            ScriptValue exit = PyOps.GetAttr(a[0], "__exit__", ctx);
            ScriptValue none = ctx.Values.None;
            if (e == null)
            {
                ctx.CallHook(exit, new[] { none, none, none }, KwArgs.Empty);
                return ctx.Values.Bool(false);
            }
            ScriptValue type = (ScriptValue)e.ExcType.ClassValue ?? Modules.Builtins.TypeObjectFor(e.PyTypeName);
            ScriptValue r = ctx.CallHook(exit, new[] { type, e, none }, KwArgs.Empty);
            return ctx.Values.Bool(r.IsTruthy(ctx));
        }

        private static ScriptValue Special(ScriptValue obj, string name, EvalContext ctx)
        {
            try
            {
                return PyOps.GetAttr(obj, name, ctx);
            }
            catch (ScriptException se) when (se.Value.ExcType.IsSubtypeOf(PyExceptionTypes.AttributeError))
            {
                return null;
            }
        }
    }
}

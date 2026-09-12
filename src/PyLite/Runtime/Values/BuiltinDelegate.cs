using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // Contract signature for built-in methods and free functions; kwargs are
    // threaded through here. For a slot method, `self` is the receiver and `args` does NOT contain self; for a
    // free builtin, `self` is null. `kw` carries the call's keyword arguments (KwArgs.Empty when none) —
    // each builtin binds them via ArgSpec; builtins that take no kwargs reject a non-empty kw.
    public delegate ScriptValue BuiltinDelegate(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx);
}

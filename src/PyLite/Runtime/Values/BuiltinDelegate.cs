using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // Contract signature for built-in methods and free functions; kwargs are
    // threaded through here. For a slot method, `self` is the receiver and `args` does NOT contain self; for a
    // free builtin, `self` is null. `kw` carries the call's keyword arguments (KwArgs.Empty when none) —
    // each builtin binds them via ArgSpec; builtins that take no kwargs reject a non-empty kw.
    public delegate ScriptValue BuiltinDelegate(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx);

    // A builtin's quiet twin: the failure scripts catch (int('x'), next() at the end) comes back in `error`
    // instead of a throw, for a statement whose whole value is the call to raise as a signal; anything else
    // it answers or throws as the builtin does.
    internal delegate ScriptValue QuietBuiltin(ScriptValue[] args, KwArgs kw, EvalContext ctx, out ScriptException error);
}

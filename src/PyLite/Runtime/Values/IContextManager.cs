using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // An engine-made value usable in `with` (the in-memory files, and the managers later modules add).
    // Engine slot tables carry no dunders, so the `with` lowering asks for this interface first.
    internal interface IContextManager
    {
        ScriptValue Enter(EvalContext ctx);

        // pending: the exception leaving the body, or null. True suppresses it.
        bool Exit(EvalContext ctx, ExceptionValue pending);
    }
}

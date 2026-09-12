using System;

namespace Outbridge.PyLite.Runtime.Errors
{
    // A host delegate (a builtin override or the print sink) threw something other than EngineAbort/
    // ScriptException. It lives OUTSIDE the script exception hierarchy, so the evaluator's
    // except dispatcher never catches it — it flies straight to ScriptEngine.Run and becomes a HostError.
    internal sealed class HostCallFailure : Exception
    {
        public string FunctionName { get; }

        public HostCallFailure(string functionName, Exception inner)
            : base("host function failed", inner)
        {
            FunctionName = functionName;
        }
    }
}

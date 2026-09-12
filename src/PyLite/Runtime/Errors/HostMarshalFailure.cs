using System;

namespace Outbridge.PyLite.Runtime.Errors
{
    // Input/output JSON (or scalar) marshaling failed at the host boundary: broken JSON
    // input, or a non-serializable declared output. Outside the script exception hierarchy — the script
    // cannot catch it; ScriptEngine.Run maps it to a HostError. A budget EngineAbort during marshaling stays
    // an EngineAbort (BudgetExceeded), so only script-level parse/serialize errors become this.
    internal sealed class HostMarshalFailure : Exception
    {
        public HostMarshalFailure(string message)
            : base(message)
        {
        }
    }
}

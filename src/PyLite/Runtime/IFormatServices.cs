using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Runtime
{
    // The Runtime-side seam to the format engine. The Evaluator (f-strings) reaches PyFormat
    // through this; the concrete PyFormatServices lives in Modules.Support and is installed on EvalContext
    // by the host/harness before a run.
    internal interface IFormatServices
    {
        ScriptValue FormatValue(EvalContext ctx, ScriptValue value, string spec);
        StrValue StrDotFormat(EvalContext ctx, StrValue self, ScriptValue[] args, KwArgs kwargs);
        StrValue StrFormatMap(EvalContext ctx, StrValue self, ScriptValue mapping);
        StrValue PercentFormat(EvalContext ctx, StrValue format, ScriptValue rhs);
        ScriptValue PercentFormatBytes(EvalContext ctx, BytesValue format, ScriptValue rhs);
    }
}

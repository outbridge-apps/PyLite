using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // Installs the format engine on EvalContext. Stateless -> a singleton.
    internal sealed class PyFormatServices : IFormatServices
    {
        internal static readonly PyFormatServices Instance = new PyFormatServices();

        public ScriptValue FormatValue(EvalContext ctx, ScriptValue value, string spec)
        {
            return PyFormat.FormatValue(ctx, value, spec);
        }

        public StrValue StrDotFormat(EvalContext ctx, StrValue self, ScriptValue[] args, KwArgs kwargs)
        {
            return PyFormat.StrDotFormat(ctx, self, args, kwargs);   // implemented in PyFormat.Markup.cs
        }

        public StrValue StrFormatMap(EvalContext ctx, StrValue self, ScriptValue mapping)
        {
            return PyFormat.StrFormatMap(ctx, self, mapping);
        }

        public StrValue PercentFormat(EvalContext ctx, StrValue format, ScriptValue rhs)
        {
            return PyFormat.PercentFormat(ctx, format, rhs);   // implemented in PyFormat.Percent.cs
        }

        public ScriptValue PercentFormatBytes(EvalContext ctx, BytesValue format, ScriptValue rhs)
        {
            return PyFormat.PercentFormatBytes(ctx, format, rhs);
        }
    }
}

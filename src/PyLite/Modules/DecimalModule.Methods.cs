using System;
using System.Collections.Generic;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    internal static partial class DecimalModule
    {
        // getcontext() returns a per-run Context view (reads/writes ctx.DecimalCtx). setcontext/localcontext/
        // Context are v2 (not exported -> AttributeError).
        internal static void AddGetContext(EvalContext ctx, Dictionary<string, ScriptValue> m)
        {
            m["getcontext"] = BuiltinFunctionValue.Make("getcontext", (self, args, kw, c) => ContextValue.Instance);
        }
    }

    // A live view of the per-run DecimalContext. prec and rounding are both r/w and drive
    // every arithmetic result; prec above 28 is refused because the backend cannot hold it.
    // The view holds no state of its own (everything lives in ctx.DecimalCtx), so one shared instance keeps
    // `getcontext() is getcontext()` true as in CPython.
    internal sealed class ContextValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = BuildType();
        internal static readonly ContextValue Instance = new ContextValue();

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        protected internal override bool TrySetAttrCore(string name, ScriptValue value, EvalContext ctx)
        {
            if (name == "prec")
            {
                if (value.Kind != ValueKind.Int && value.Kind != ValueKind.Bool)
                    throw Raise.TypeError(ctx, "prec must be an integer");
                System.Numerics.BigInteger n = NumericOps.AsBigInteger(value);
                if (n < 1 || n > DecimalContext.MaxPrec)
                    throw Raise.ValueError(ctx, "valid range for prec is [1, " + DecimalContext.MaxPrec
                        + "] on the System.Decimal backend");
                ctx.DecimalCtx.Prec = (int)n;
                return true;
            }
            if (name == "rounding")
            {
                StrValue s = value as StrValue;
                if (s == null || Array.IndexOf(RoundNames, s.Value) < 0)
                    throw Raise.ValueError(ctx, "rounding must be one of the ROUND_* constants");
                ctx.DecimalCtx.Rounding = s.Value;
                return true;
            }
            return false;
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("Context(prec=" + ctx.DecimalCtx.Prec + ", rounding=" + ctx.DecimalCtx.Rounding + ")");
        }

        private static readonly string[] RoundNames =
        {
            "ROUND_CEILING", "ROUND_FLOOR", "ROUND_UP", "ROUND_DOWN",
            "ROUND_HALF_UP", "ROUND_HALF_DOWN", "ROUND_HALF_EVEN", "ROUND_05UP",
        };

        private static ScriptTypeInfo BuildType()
        {
            var d = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal)
            {
                ["prec"] = SlotDescriptor.MakeProperty("prec", (self, c) => c.Values.Int(c.DecimalCtx.Prec)),
                ["rounding"] = SlotDescriptor.MakeProperty("rounding", (self, c) => c.Values.Str(c.DecimalCtx.Rounding)),
            };
            return new ScriptTypeInfo("decimal.Context", d);
        }
    }
}

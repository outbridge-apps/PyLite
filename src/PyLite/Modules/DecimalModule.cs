using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // The decimal module. NOT shared between runs (its context reads per-run rounding).
    // quantize/normalize/predicates/__format__/getcontext land in the next phase.
    internal static partial class DecimalModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["Decimal"] = DecimalType(ctx),
                ["DecimalException"] = ExcType(ctx, "DecimalException", PyExceptionTypes.DecimalException),
                ["InvalidOperation"] = ExcType(ctx, "InvalidOperation", PyExceptionTypes.DecimalInvalidOperation),
                ["DivisionByZero"] = ExcType(ctx, "DivisionByZero", PyExceptionTypes.DecimalDivisionByZero),
                ["Overflow"] = ExcType(ctx, "Overflow", PyExceptionTypes.DecimalOverflow),
                ["Underflow"] = ExcType(ctx, "Underflow", PyExceptionTypes.DecimalUnderflow),
                ["ROUND_CEILING"] = ctx.Values.Str("ROUND_CEILING"),
                ["ROUND_FLOOR"] = ctx.Values.Str("ROUND_FLOOR"),
                ["ROUND_UP"] = ctx.Values.Str("ROUND_UP"),
                ["ROUND_DOWN"] = ctx.Values.Str("ROUND_DOWN"),
                ["ROUND_HALF_UP"] = ctx.Values.Str("ROUND_HALF_UP"),
                ["ROUND_HALF_DOWN"] = ctx.Values.Str("ROUND_HALF_DOWN"),
                ["ROUND_HALF_EVEN"] = ctx.Values.Str("ROUND_HALF_EVEN"),
                ["ROUND_05UP"] = ctx.Values.Str("ROUND_05UP"),
            };
            AddGetContext(ctx, m);
            return ctx.Values.Module("decimal", m);
        }

        private static TypeValue DecimalType(EvalContext ctx)
        {
            var slots = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["from_float"] = BuiltinFunctionValue.Make("from_float", (self, args, kw, c) => DecFromFloat(c, args)),
            };
            BuiltinDelegate ctor = (self, args, kw, c) => DecimalValue.Construct(c, args, kw);
            return ctx.Values.Type("decimal.Decimal", ctor, null, slots);
        }

        private static ScriptValue DecFromFloat(EvalContext ctx, ScriptValue[] args)
        {
            if (args.Length < 1)
                throw Raise.TypeError(ctx, "from_float() missing 1 required positional argument");
            FloatValue fv = args[0] as FloatValue;
            if (fv != null)
                return DecimalValue.FromFloat(ctx, fv.Value);
            IntValue iv = args[0] as IntValue;
            if (iv != null)
                return DecimalValue.FromDecimal(ctx, DecimalValue.IntToDecimal(ctx, iv.Value));
            throw Raise.TypeError(ctx, "argument must be int or float.");
        }

        private static TypeValue ExcType(EvalContext ctx, string name, PyExceptionType pt)
        {
            BuiltinDelegate ctor = (self, args, kw, c) => c.Values.Exception(pt, args);
            return ctx.Values.Type(name, ctor, null, null, pt);
        }
    }
}

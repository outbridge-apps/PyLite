using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // The fractions module. Immutable, sharable. Exports only Fraction (fractions.gcd was
    // removed in 3.9 -> AttributeError). from_decimal lands with DecimalValue.
    internal static class FractionsModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["Fraction"] = FractionType(ctx),
            };
            return ctx.Values.Module("fractions", m);
        }

        private static TypeValue FractionType(EvalContext ctx)
        {
            var slots = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["from_float"] = BuiltinFunctionValue.Make("from_float", (self, args, kw, c) => FromFloat(c, args)),
                ["from_decimal"] = BuiltinFunctionValue.Make("from_decimal", (self, args, kw, c) => FromDecimal(c, args)),
            };
            BuiltinDelegate ctor = (self, args, kw, c) => FractionValue.Construct(c, args, kw);
            return ctx.Values.Type("fractions.Fraction", ctor, null, slots);
        }

        private static ScriptValue FromFloat(EvalContext ctx, ScriptValue[] args)
        {
            if (args.Length < 1)
                throw Raise.TypeError(ctx, "from_float() missing 1 required positional argument");
            FloatValue fv = args[0] as FloatValue;
            if (fv != null)
                return FractionValue.FromFloat(ctx, fv.Value);
            IntValue iv = args[0] as IntValue;
            if (iv != null)
                return FractionValue.Create(ctx, iv.Value, System.Numerics.BigInteger.One);
            BoolValue bv = args[0] as BoolValue;
            if (bv != null)
                return FractionValue.Create(ctx, bv.Value ? 1 : 0, System.Numerics.BigInteger.One);
            throw Raise.TypeError(ctx, "from_float() only takes floats, not " + args[0].PyTypeName);
        }

        private static ScriptValue FromDecimal(EvalContext ctx, ScriptValue[] args)
        {
            if (args.Length < 1)
                throw Raise.TypeError(ctx, "from_decimal() missing 1 required positional argument");
            DecimalValue dv = args[0] as DecimalValue;
            if (dv == null)
                throw Raise.TypeError(ctx, "from_decimal() only takes Decimals, not " + args[0].PyTypeName);
            System.Numerics.BigInteger num, den;
            dv.ToRational(out num, out den);
            return FractionValue.Create(ctx, num, den);
        }
    }
}

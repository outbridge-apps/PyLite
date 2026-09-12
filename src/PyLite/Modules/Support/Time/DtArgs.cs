using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // Shared numeric-argument coercion for the datetime/time constructors. date/time/
    // datetime fields are ints (bool counts as int); a float is a hard TypeError; an int outside int32 raises
    // the field's own range error (not a raw OverflowException).
    internal static class DtArgs
    {
        public static int IntField(EvalContext ctx, ScriptValue v, string rangeError)
        {
            IntValue iv = v as IntValue;
            if (iv != null)
            {
                // Fast path: date/time fields are always small ints — read the long directly and skip the
                // three BigInteger materializations the `.Value` property would do (P1 pattern).
                if (iv.IsSmall)
                {
                    long s = iv.Small;
                    if (s < int.MinValue || s > int.MaxValue)
                        throw Raise.ValueError(ctx, rangeError);
                    return (int)s;
                }
                if (iv.Value < int.MinValue || iv.Value > int.MaxValue)
                    throw Raise.ValueError(ctx, rangeError);
                return (int)iv.Value;
            }
            BoolValue bv = v as BoolValue;
            if (bv != null)
                return bv.Value ? 1 : 0;
            if (v is FloatValue)
                throw Raise.TypeError(ctx, "integer argument expected, got float");
            throw Raise.TypeError(ctx, "'" + v.PyTypeName + "' object cannot be interpreted as an integer");
        }

        // tzinfo argument: None -> naive (null); a TimezoneValue -> aware; anything else -> TypeError.
        // Callers distinguish "not passed" (ArgSpec.MISSING) from None BEFORE calling this.
        public static TzInfoValue TzArg(EvalContext ctx, ScriptValue v)
        {
            if (v.Kind == ValueKind.None)
                return null;
            TzInfoValue tz = v as TzInfoValue;
            if (tz != null)
                return tz;
            throw Raise.TypeError(ctx, "tzinfo argument must be None or of a tzinfo subclass, not type '" + v.PyTypeName + "'");
        }

        // The format string of a strftime(fmt) call.
        public static string StrftimeArg(EvalContext ctx, ScriptValue[] args)
        {
            Args.AtLeast(ctx, args, "strftime", 1);
            StrValue s = args[0] as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, "strftime() argument 1 must be str, not " + args[0].PyTypeName);
            return s.Value;
        }

        // int|bool|float -> double, for fromtimestamp/utcfromtimestamp.
        public static double Timestamp(EvalContext ctx, ScriptValue v)
        {
            IntValue iv = v as IntValue;
            if (iv != null)
                return (double)iv.Value;
            BoolValue bv = v as BoolValue;
            if (bv != null)
                return bv.Value ? 1.0 : 0.0;
            FloatValue fv = v as FloatValue;
            if (fv != null)
                return fv.Value;
            throw Raise.TypeError(ctx, "an integer is required (got type " + v.PyTypeName + ")");
        }
    }
}

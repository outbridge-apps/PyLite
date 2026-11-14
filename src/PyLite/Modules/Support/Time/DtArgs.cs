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
        // CPython 3.14's check_date_args / check_time_args texts
        public static void CheckDate(EvalContext ctx, int y, int mo, int d)
        {
            if (y < 1 || y > 9999)
                throw Raise.ValueError(ctx, "year must be in 1..9999, not " + Num(y));
            if (mo < 1 || mo > 12)
                throw Raise.ValueError(ctx, "month must be in 1..12, not " + Num(mo));
            int dim = CalendarMath.DaysInMonth(y, mo);
            if (d < 1 || d > dim)
                throw Raise.ValueError(ctx, "day " + Num(d) + " must be in range 1.." + Num(dim) + " for month " + Num(mo) + " in year " + Num(y));
        }

        public static void CheckTime(EvalContext ctx, int h, int mi, int s, int us, int fold)
        {
            if (h < 0 || h > 23)
                throw Raise.ValueError(ctx, "hour must be in 0..23, not " + Num(h));
            if (mi < 0 || mi > 59)
                throw Raise.ValueError(ctx, "minute must be in 0..59, not " + Num(mi));
            if (s < 0 || s > 59)
                throw Raise.ValueError(ctx, "second must be in 0..59, not " + Num(s));
            if (us < 0 || us > 999999)
                throw Raise.ValueError(ctx, "microsecond must be in 0..999999, not " + Num(us));
            if (fold != 0 && fold != 1)
                throw Raise.ValueError(ctx, "fold must be either 0 or 1, not " + Num(fold));
        }

        private static string Num(int n) { return n.ToString(System.Globalization.CultureInfo.InvariantCulture); }

        // replace()'s fields go through Argument Clinic's int converter, which words the overflow its own way.
        public static int IntFieldReplace(EvalContext ctx, ScriptValue v)
        {
            IntValue iv = v as IntValue;
            if (iv != null && (!iv.IsSmall || iv.Small < int.MinValue || iv.Small > int.MaxValue))
                throw Raise.Overflow(ctx, "Python int too large to convert to C int");
            return IntField(ctx, v, null);
        }

        public static int IntField(EvalContext ctx, ScriptValue v, string rangeError)
        {
            IntValue iv = v as IntValue;
            if (iv != null)
            {
                // Fast path: date/time fields are always small ints — read the long directly and skip the
                // three BigInteger materializations the `.Value` property would do.
                if (iv.IsSmall)
                {
                    long s = iv.Small;
                    if (s < int.MinValue || s > int.MaxValue)
                        throw Raise.Overflow(ctx, s > 0 ? "signed integer is greater than maximum" : "signed integer is less than minimum");
                    return (int)s;
                }
                throw Raise.Overflow(ctx, "Python int too large to convert to C long");   // CPython's "i" past a long
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
            if (iv != null && double.IsInfinity(iv.ToDouble()))
                throw Raise.Overflow(ctx, "timestamp out of range for platform time_t");
            double d;
            if (Coerce.TryToReal(ctx, v, false, out d))
                return d;
            throw Raise.TypeError(ctx, "an integer is required (got type " + v.PyTypeName + ")");
        }
    }
}

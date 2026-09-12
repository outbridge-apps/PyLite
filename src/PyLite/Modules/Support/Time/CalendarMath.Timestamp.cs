using System;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Modules.Support
{
    // Timestamp <-> broken-down-time conversions. Half-even microsecond rounding
    // (CPython 3.3+), floored epoch division (never a truncated C# % on negative epochs). These take ctx
    // because the out-of-range/NaN/Inf paths are script-level ValueError/OverflowError.
    internal static partial class CalendarMath
    {
        public const long UnixEpochTicks = 621355968000000000L;   // ticks at 1970-01-01T00:00:00Z
        public const long EpochNaiveUs = (long)EpochOrdinal * UsPerDay;

        // Largest |t| that survives a checked (long) cast; 2^63 as a double is exactly this.
        private const double TimestampLongLimit = 9223372036854775808.0;

        // double epoch seconds -> (sec, us in 0..999999). Half-even on microseconds; negatives via floor.
        public static void SplitTimestamp(EvalContext ctx, double t, out long sec, out int us)
        {
            if (double.IsNaN(t))
                throw Raise.ValueError(ctx, "cannot convert float NaN to integer");
            if (double.IsInfinity(t))
                throw Raise.Overflow(ctx, "cannot convert float infinity to integer");

            double intpart = Math.Truncate(t);
            if (intpart >= TimestampLongLimit || intpart < -TimestampLongLimit)
                throw Raise.Overflow(ctx, "timestamp out of range");

            double frac = t - intpart;
            double usD = Math.Round(frac * 1e6, MidpointRounding.ToEven);
            sec = (long)intpart;
            us = (int)usD;
            if (us >= 1000000)
            {
                sec += 1;
                us -= 1000000;
            }
            else if (us < 0)
            {
                sec -= 1;
                us += 1000000;
            }
        }

        // (epoch seconds, us) -> broken-down UTC fields. Result year outside 1..9999 -> OverflowError.
        public static void EpochToYmdHms(EvalContext ctx, long sec, int us,
            out int y, out int mo, out int d, out int h, out int mi, out int s, out int usOut)
        {
            long days = FloorDiv(sec, SecondsPerDay);
            long rem = FloorMod(sec, SecondsPerDay);
            long ordinal = EpochOrdinal + days;
            if (ordinal < 1 || ordinal > MaxOrdinal)
                throw Raise.Overflow(ctx, "timestamp out of range");

            Ord2Ymd((int)ordinal, out y, out mo, out d);
            h = (int)(rem / 3600);
            mi = (int)(rem % 3600 / 60);
            s = (int)(rem % 60);
            usOut = us;
        }

        // Broken-down UTC fields -> epoch seconds. INPUT MUST ALREADY BE VALID.
        public static long YmdHmsToEpoch(int y, int mo, int d, int h, int mi, int s)
        {
            return (long)(Ymd2Ord(y, mo, d) - EpochOrdinal) * SecondsPerDay + h * 3600 + mi * 60 + s;
        }

        // Clock reading -> epoch seconds (double). No DateTime.Now: utcNow comes from IScriptClock.
        public static double UtcNowToEpoch(DateTime utcNow)
        {
            return (utcNow.Ticks - UnixEpochTicks) / 1e7;
        }
    }
}

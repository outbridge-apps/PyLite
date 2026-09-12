using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // The time module. Per-run: its tz attributes read ctx.Clock, so the ModuleValue is
    // NOT shared between runs. All wall-clock reads go through ctx.Clock / ctx.MonotonicClock — never DateTime.Now.
    internal static class TimeModule
    {
        // asctime/ctime index this Sun=0 table via (tm_wday_py + 1) % 7 (CPython gettmarg convention).
        private static readonly string[] Sun0Days = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

        public static ModuleValue Create(EvalContext ctx)
        {
            long localOffsetSeconds = (long)ctx.Clock.LocalOffset.TotalSeconds;
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["struct_time"] = StructTimeType(ctx),
                ["time"] = BuiltinFunctionValue.Make("time", (s, a, kw, c) => c.Values.Float(CalendarMath.UtcNowToEpoch(c.Clock.UtcNow))),
                ["monotonic"] = BuiltinFunctionValue.Make("monotonic", (s, a, kw, c) => c.Values.Float(c.MonotonicClock.Elapsed.TotalSeconds)),
                ["perf_counter"] = BuiltinFunctionValue.Make("perf_counter", (s, a, kw, c) => c.Values.Float(c.MonotonicClock.Elapsed.TotalSeconds)),
                ["gmtime"] = BuiltinFunctionValue.Make("gmtime", (s, a, kw, c) => BrokenDownTime(c, a, 0)),
                ["localtime"] = BuiltinFunctionValue.Make("localtime", (s, a, kw, c) => BrokenDownTime(c, a, (long)c.Clock.LocalOffset.TotalSeconds)),
                ["mktime"] = BuiltinFunctionValue.Make("mktime", (s, a, kw, c) => Mktime(c, a)),
                ["asctime"] = BuiltinFunctionValue.Make("asctime", (s, a, kw, c) => c.Values.Str(Asctime(c, a))),
                ["ctime"] = BuiltinFunctionValue.Make("ctime", (s, a, kw, c) => c.Values.Str(Ctime(c, a))),
                ["sleep"] = BuiltinFunctionValue.Make("sleep", (s, a, kw, c) => Sleep(c, a)),
                ["strftime"] = BuiltinFunctionValue.Make("strftime", (s, a, kw, c) => c.Values.Str(Strftime(c, a))),
                ["strptime"] = BuiltinFunctionValue.Make("strptime", (s, a, kw, c) => Strptime(c, a)),
                // tz attributes: seconds WEST of UTC (UTC+2 => -7200).
                ["timezone"] = ctx.Values.Int(-localOffsetSeconds),
                ["altzone"] = ctx.Values.Int(-localOffsetSeconds),
                ["daylight"] = ctx.Values.Int(0),
                ["tzname"] = ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Str(ctx.Clock.LocalName), ctx.Values.Str(ctx.Clock.LocalName) }),
            };
            return ctx.Values.Module("time", m);
        }

        private static TypeValue StructTimeType(EvalContext ctx)
        {
            BuiltinDelegate ctor = (self, args, kw, c) => StructTimeValue.Construct(c, args, kw);
            return ctx.Values.Type("time.struct_time", ctor, null, null);
        }

        // ---- gmtime / localtime ----

        private static ScriptValue BrokenDownTime(EvalContext ctx, ScriptValue[] args, long shiftSeconds)
        {
            long sec = ReadSecsFloor(ctx, args) + shiftSeconds;
            int y, mo, d, h, mi, s, us;
            CalendarMath.EpochToYmdHms(ctx, sec, 0, out y, out mo, out d, out h, out mi, out s, out us);
            int ord = CalendarMath.Ymd2Ord(y, mo, d);
            return StructTimeValue.Create(ctx, y, mo, d, h, mi, s,
                CalendarMath.Weekday(ord), CalendarMath.DayOfYear(y, mo, d), 0);
        }

        // secs argument (int|float|None) floored to whole seconds; None/absent => now.
        private static long ReadSecsFloor(EvalContext ctx, ScriptValue[] args)
        {
            if (args.Length == 0 || args[0].Kind == ValueKind.None)
                return (long)Math.Floor(CalendarMath.UtcNowToEpoch(ctx.Clock.UtcNow));
            ScriptValue v = args[0];
            IntValue iv = v as IntValue;
            if (iv != null)
            {
                if (iv.Value < long.MinValue || iv.Value > long.MaxValue)
                    throw Raise.Overflow(ctx, "timestamp out of range");
                return (long)iv.Value;
            }
            BoolValue bv = v as BoolValue;
            if (bv != null)
                return bv.Value ? 1 : 0;
            FloatValue fv = v as FloatValue;
            if (fv != null)
            {
                if (double.IsNaN(fv.Value))
                    throw Raise.ValueError(ctx, "cannot convert float NaN to integer");
                if (double.IsInfinity(fv.Value))
                    throw Raise.Overflow(ctx, "cannot convert float infinity to integer");
                double f = Math.Floor(fv.Value);
                if (f < -9.2e18 || f > 9.2e18)
                    throw Raise.Overflow(ctx, "timestamp out of range");
                return (long)f;
            }
            throw Raise.TypeError(ctx, "an integer is required (got type " + v.PyTypeName + ")");
        }

        // ---- mktime ----

        private static ScriptValue Mktime(EvalContext ctx, ScriptValue[] args)
        {
            Args.AtLeast(ctx, args, "mktime", 1);
            CheckedTm t = CheckTm(ctx, args[0]);
            if (t.Year < 1 || t.Year > 9999)
                throw Raise.Overflow(ctx, "mktime argument out of range");
            long epoch = CalendarMath.YmdHmsToEpoch((int)t.Year, t.Mon, t.MDay, t.Hour, t.Min, t.Sec)
                - (long)ctx.Clock.LocalOffset.TotalSeconds;
            return ctx.Values.Float(epoch);
        }

        // ---- asctime / ctime ----

        private static string Asctime(EvalContext ctx, ScriptValue[] args)
        {
            CheckedTm t = args.Length == 0 || args[0].Kind == ValueKind.None
                ? CheckTm(ctx, BrokenDownTime(ctx, Array.Empty<ScriptValue>(), (long)ctx.Clock.LocalOffset.TotalSeconds))
                : CheckTm(ctx, args[0]);
            return Format(t);
        }

        private static string Ctime(EvalContext ctx, ScriptValue[] args)
        {
            ScriptValue local = BrokenDownTime(ctx, args, (long)ctx.Clock.LocalOffset.TotalSeconds);
            return Format(CheckTm(ctx, local));
        }

        private static string Format(CheckedTm t)
        {
            return Sun0Days[t.WdaySun0] + " " + DtNames.AbbrMonths[t.Mon - 1] + " "
                + t.MDay.ToString(CultureInfo.InvariantCulture).PadLeft(2, ' ') + " "
                + t.Hour.ToString("00", CultureInfo.InvariantCulture) + ":"
                + t.Min.ToString("00", CultureInfo.InvariantCulture) + ":"
                + t.Sec.ToString("00", CultureInfo.InvariantCulture) + " "
                + t.Year.ToString(CultureInfo.InvariantCulture);
        }

        // --- sleep (quantized cooperative wait) ----

        private static ScriptValue Sleep(EvalContext ctx, ScriptValue[] args)
        {
            Args.AtLeast(ctx, args, "sleep", 1);
            double secs = ToSeconds(ctx, args[0]);
            if (double.IsNaN(secs))
                throw Raise.ValueError(ctx, "Invalid value NaN (not a number)");
            if (secs < 0)
                throw Raise.ValueError(ctx, "sleep length must be non-negative");

            double capped = secs > 1e9 ? 1e9 : secs;   // guard FromSeconds against 2**63 / +Inf overflow
            TimeSpan requested = TimeSpan.FromSeconds(capped);
            TimeSpan eff = ctx.SleepPolicy.Clamp(requested, ctx.SleptTotal);
            TimeSpan rem = ctx.Budget.TimeRemaining;
            if (eff > rem)
                eff = rem;

            if (eff <= TimeSpan.Zero)
            {
                ctx.Budget.Step();   // sleep(0)/no-op is still charged (anti tight-loop)
                ctx.Budget.CheckDeadlineNow();
                return ctx.Values.None;
            }

            TimeSpan quantum = ctx.SleepPolicy.Quantum;
            TimeSpan waited = TimeSpan.Zero;
            using (var mre = new System.Threading.ManualResetEventSlim(false))
            {
                while (waited < eff)
                {
                    ctx.Budget.CheckDeadlineNow();
                    TimeSpan left = eff - waited;
                    TimeSpan q = quantum < left ? quantum : left;
                    mre.Wait(q);           // never signaled; cancellation is caught by CheckDeadlineNow
                    waited += q;
                    ctx.SleptTotal += q;
                    ctx.Budget.Step();
                    ctx.Budget.CheckDeadlineNow();
                }
            }
            return ctx.Values.None;
        }

        private static double ToSeconds(EvalContext ctx, ScriptValue v)
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
            throw Raise.TypeError(ctx, "must be real number, not " + v.PyTypeName);
        }

        // --- strftime / strptime ----

        private static string Strftime(EvalContext ctx, ScriptValue[] args)
        {
            Args.AtLeast(ctx, args, "strftime", 1);
            StrValue fmt = args[0] as StrValue;
            if (fmt == null)
                throw Raise.TypeError(ctx, "strftime() argument 1 must be str, not " + args[0].PyTypeName);
            ScriptValue tupleArg = args.Length >= 2 && args[1].Kind != ValueKind.None
                ? args[1]
                : BrokenDownTime(ctx, Array.Empty<ScriptValue>(), (long)ctx.Clock.LocalOffset.TotalSeconds);
            CheckedTm t = CheckTm(ctx, tupleArg);
            var bt = new BrokenTime
            {
                Year = (int)t.Year, Month = t.Mon, Day = t.MDay,
                Hour = t.Hour, Minute = t.Min, Second = t.Sec, Microsecond = 0,
                WeekdayMon0 = t.WdayMon0, DayOfYear = t.Yday1,
                OffsetUs = null, TzName = null, HasMicroseconds = false,
            };
            return StrftimeFormatter.Format(ctx, fmt.Value, bt);
        }

        private const string DefaultStrptimeFormat = "%a %b %d %H:%M:%S %Y";

        private static ScriptValue Strptime(EvalContext ctx, ScriptValue[] args)
        {
            Args.AtLeast(ctx, args, "strptime", 1);
            StrValue data = args[0] as StrValue;
            if (data == null)
                throw Raise.TypeError(ctx, "strptime() argument 1 must be str, not " + args[0].PyTypeName);
            string fmt = DefaultStrptimeFormat;
            if (args.Length >= 2)
            {
                StrValue f = args[1] as StrValue;
                if (f == null)
                    throw Raise.TypeError(ctx, "strptime() argument 2 must be str, not " + args[1].PyTypeName);
                fmt = f.Value;
            }
            StrptimeResult r = StrptimeParser.Parse(ctx, data.Value, fmt);
            int yday = r.DayOfYear ?? CalendarMath.DayOfYear(r.Year, r.Month, r.Day);
            return StructTimeValue.Create(ctx, r.Year, r.Month, r.Day, r.Hour, r.Minute, r.Second,
                r.WeekdayMon0.Value, yday, r.TmIsdst);
        }

        // --- CheckTm (port of Modules/timemodule.c checks; Python-level values, texts) ----

        internal struct CheckedTm
        {
            public long Year;
            public int Mon;        // 1..12 (0 normalized to 1)
            public int MDay;       // 1..31 (0 normalized to 1)
            public int Hour, Min, Sec;
            public int WdaySun0;   // (tm_wday_py + 1) % 7, validated >= 0
            public int WdayMon0;   // back to Mon=0 for strftime
            public int Yday1;      // 1..366 for strftime (%j uses tm_yday)
            public int Isdst;
        }

        internal static CheckedTm CheckTm(EvalContext ctx, ScriptValue arg)
        {
            long[] f = Read9(ctx, arg);
            var t = new CheckedTm { Year = f[0], Isdst = (int)f[8] };

            long mon = f[1];
            if (mon == 0)
                t.Mon = 1;
            else if (mon < 1 || mon > 12)
                throw Raise.ValueError(ctx, "month out of range");
            else
                t.Mon = (int)mon;

            long mday = f[2];
            if (mday == 0)
                t.MDay = 1;
            else if (mday < 1 || mday > 31)
                throw Raise.ValueError(ctx, "day of month out of range");
            else
                t.MDay = (int)mday;

            if (f[3] < 0 || f[3] > 23)
                throw Raise.ValueError(ctx, "hour out of range");
            t.Hour = (int)f[3];
            if (f[4] < 0 || f[4] > 59)
                throw Raise.ValueError(ctx, "minute out of range");
            t.Min = (int)f[4];
            if (f[5] < 0 || f[5] > 61)
                throw Raise.ValueError(ctx, "seconds out of range");
            t.Sec = (int)f[5];

            long cw = (f[6] + 1) % 7;   // C-truncated %; tm_wday_py = -1 -> 0
            if (cw < 0)
                throw Raise.ValueError(ctx, "day of week out of range");
            t.WdaySun0 = (int)cw;
            t.WdayMon0 = (int)((cw + 6) % 7);

            long cyday = f[7] - 1;
            if (cyday == -1)
                t.Yday1 = 1;
            else if (cyday < 0 || cyday > 365)
                throw Raise.ValueError(ctx, "day of year out of range");
            else
                t.Yday1 = (int)cyday + 1;

            return t;
        }

        private static long[] Read9(EvalContext ctx, ScriptValue arg)
        {
            ScriptValue[] items;
            StructTimeValue st = arg as StructTimeValue;
            if (st != null)
                items = st.Items;
            else if (arg.Kind == ValueKind.Tuple)
                items = ((TupleValue)arg).Items;
            else if (arg.Kind == ValueKind.List)
                items = ((ListValue)arg).Items.ToArray();
            else
                throw Raise.TypeError(ctx, "Tuple or struct_time argument required");
            if (items.Length != 9)
                throw Raise.TypeError(ctx, "argument must be a sequence of length 9, not " + items.Length);
            var r = new long[9];
            for (int i = 0; i < 9; i++)
                r[i] = FieldLong(ctx, items[i]);
            return r;
        }

        private static long FieldLong(EvalContext ctx, ScriptValue v)
        {
            IntValue iv = v as IntValue;
            if (iv != null)
            {
                if (iv.Value > long.MaxValue)
                    return long.MaxValue;
                if (iv.Value < long.MinValue)
                    return long.MinValue;
                return (long)iv.Value;
            }
            BoolValue bv = v as BoolValue;
            if (bv != null)
                return bv.Value ? 1 : 0;
            throw Raise.TypeError(ctx, "an integer is required (got type " + v.PyTypeName + ")");
        }
    }
}

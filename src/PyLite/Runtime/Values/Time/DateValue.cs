using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // datetime.date. Immutable proleptic Gregorian date. Arithmetic with timedelta uses
    // ONLY td.Days; date - date yields a whole-day timedelta. strftime/__format__ land with the formatter.
    internal sealed class DateValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = BuildType();

        internal const int TypeTag = 1001;   // hash tag, distinguishes date hashes from bare tuples

        public readonly int Year;
        public readonly byte Month;
        public readonly byte Day;

        private DateValue(int year, byte month, byte day)
        {
            Year = year;
            Month = month;
            Day = day;
        }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        // ---- construction ----

        internal static DateValue Construct(EvalContext ctx, int year, int month, int day)
        {
            if (year < 1 || year > 9999)
                throw Raise.ValueError(ctx, "year is out of range");
            if (month < 1 || month > 12)
                throw Raise.ValueError(ctx, "month must be in 1..12");
            if (day < 1 || day > CalendarMath.DaysInMonth(year, month))
                throw Raise.ValueError(ctx, "day is out of range for month");
            ctx.Values.PreCharge(48);
            return new DateValue(year, (byte)month, (byte)day);
        }

        private static readonly ArgSpec CtorSpec = ArgSpec.Create("date").Req("year").Req("month").Req("day").Seal();

        internal static DateValue ConstructFromArgs(EvalContext ctx, ScriptValue[] args, KwArgs kw)
        {
            ctx.Budget.Step();
            var slots = new ScriptValue[CtorSpec.SlotCount];
            ScriptValue[] star;
            CtorSpec.Bind(ctx, args, kw, slots, out star);
            int y = DtArgs.IntField(ctx, slots[0], "year is out of range");
            int m = DtArgs.IntField(ctx, slots[1], "month must be in 1..12");
            int d = DtArgs.IntField(ctx, slots[2], "day is out of range for month");
            return Construct(ctx, y, m, d);
        }

        internal int ToOrdinalInt() { return CalendarMath.Ymd2Ord(Year, Month, Day); }

        internal static DateValue FromOrdinal(EvalContext ctx, BigInteger n)
        {
            if (n < 1)
                throw Raise.ValueError(ctx, "ordinal must be >= 1");
            if (n > CalendarMath.MaxOrdinal)
                throw Raise.ValueError(ctx, "year is out of range");
            int y, m, d;
            CalendarMath.Ord2Ymd((int)n, out y, out m, out d);
            return Construct(ctx, y, m, d);
        }

        // ---- static factories (today/fromtimestamp/fromordinal) ----

        internal static DateValue Today(EvalContext ctx)
        {
            return FromTimestamp(ctx, CalendarMath.UtcNowToEpoch(ctx.Clock.UtcNow));
        }

        internal static DateValue FromTimestamp(EvalContext ctx, double t)
        {
            long sec;
            int us;
            CalendarMath.SplitTimestamp(ctx, t, out sec, out us);
            long localSec = sec + (long)ctx.Clock.LocalOffset.TotalSeconds;
            int y, mo, d, h, mi, s, usOut;
            CalendarMath.EpochToYmdHms(ctx, localSec, us, out y, out mo, out d, out h, out mi, out s, out usOut);
            return Construct(ctx, y, mo, d);
        }

        // ---- arithmetic ----

        protected internal override ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        {
            TimeDeltaValue td = other as TimeDeltaValue;
            switch (op)
            {
                case PyBinOp.Add:
                    return td != null ? AddDays(ctx, td.Days) : null;
                case PyBinOp.Sub:
                    if (reflected)
                        return null;
                    if (td != null)
                        return AddDays(ctx, -td.Days);
                    DateValue od = other as DateValue;
                    if (od != null)
                        return TimeDeltaValue.FromNormalized(ctx, ToOrdinalInt() - od.ToOrdinalInt(), 0, 0);
                    return null;
                default:
                    return null;
            }
        }

        private DateValue AddDays(EvalContext ctx, int days)
        {
            long ord = (long)ToOrdinalInt() + days;
            if (ord < 1 || ord > CalendarMath.MaxOrdinal)
                throw Raise.Overflow(ctx, "date value out of range");
            int y, m, d;
            CalendarMath.Ord2Ymd((int)ord, out y, out m, out d);
            return Construct(ctx, y, m, d);
        }

        // ---- comparisons / hash ----

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            DateValue o = other as DateValue;
            return o != null && Year == o.Year && Month == o.Month && Day == o.Day;
        }

        protected internal override bool TryCompareLeafCore(ScriptValue other, EvalContext ctx, int depth, out int cmp)
        {
            DateValue o = other as DateValue;
            if (o == null)
            {
                if (other is DateTimeValue)
                    throw Raise.TypeError(ctx, "can't compare datetime.datetime to datetime.date");
                cmp = 0;
                return false;
            }
            cmp = Year != o.Year ? Year.CompareTo(o.Year)
                : Month != o.Month ? Month.CompareTo(o.Month)
                : Day.CompareTo(o.Day);
            return true;
        }

        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            var t = ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Int(TypeTag), ctx.Values.Int(Year), ctx.Values.Int(Month), ctx.Values.Int(Day) });
            return PyOps.Hash(t, ctx, depth);
        }

        // ---- formatting ----

        internal string IsoFormat()
        {
            return Year.ToString("0000", CultureInfo.InvariantCulture) + "-"
                + ((int)Month).ToString("00", CultureInfo.InvariantCulture) + "-"
                + ((int)Day).ToString("00", CultureInfo.InvariantCulture);
        }

        internal string CtimeString(int hour, int minute, int second)
        {
            int wd = CalendarMath.Weekday(ToOrdinalInt());
            return DtNames.AbbrDays[wd] + " " + DtNames.AbbrMonths[Month - 1] + " "
                + ((int)Day).ToString(CultureInfo.InvariantCulture).PadLeft(2, ' ') + " "
                + hour.ToString("00", CultureInfo.InvariantCulture) + ":"
                + minute.ToString("00", CultureInfo.InvariantCulture) + ":"
                + second.ToString("00", CultureInfo.InvariantCulture) + " "
                + Year.ToString(CultureInfo.InvariantCulture);
        }

        internal string Strftime(EvalContext ctx, string fmt)
        {
            int ord = ToOrdinalInt();
            var t = new BrokenTime
            {
                Year = Year, Month = Month, Day = Day,
                Hour = 0, Minute = 0, Second = 0, Microsecond = 0,
                WeekdayMon0 = CalendarMath.Weekday(ord),
                DayOfYear = CalendarMath.DayOfYear(Year, Month, Day),
                OffsetUs = null, TzName = null, HasMicroseconds = true,
            };
            return StrftimeFormatter.Format(ctx, fmt, t);
        }

        protected internal override ScriptValue FormatSpecCore(string spec, EvalContext ctx)
        {
            return ctx.Values.Str(Strftime(ctx, spec));
        }

        protected internal override void StrLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append(IsoFormat()); }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("datetime.date(" + Year.ToString(CultureInfo.InvariantCulture) + ", "
                + ((int)Month).ToString(CultureInfo.InvariantCulture) + ", "
                + ((int)Day).ToString(CultureInfo.InvariantCulture) + ")");
        }

        // ---- instance slots ----

        private static readonly ArgSpec ReplaceSpec = ArgSpec.Create("replace")
            .Opt("year", ArgSpec.MISSING).Opt("month", ArgSpec.MISSING).Opt("day", ArgSpec.MISSING).Seal();

        private ScriptValue Replace(EvalContext ctx, ScriptValue[] args, KwArgs kw)
        {
            var slots = new ScriptValue[ReplaceSpec.SlotCount];
            ScriptValue[] star;
            ReplaceSpec.Bind(ctx, args, kw, slots, out star);
            int y = ReferenceEquals(slots[0], ArgSpec.MISSING) ? Year : DtArgs.IntField(ctx, slots[0], "year is out of range");
            int m = ReferenceEquals(slots[1], ArgSpec.MISSING) ? Month : DtArgs.IntField(ctx, slots[1], "month must be in 1..12");
            int d = ReferenceEquals(slots[2], ArgSpec.MISSING) ? Day : DtArgs.IntField(ctx, slots[2], "day is out of range for month");
            return Construct(ctx, y, m, d);
        }

        private ScriptValue TimeTuple(EvalContext ctx)
        {
            int ord = ToOrdinalInt();
            return StructTimeValue.Create(ctx, Year, Month, Day, 0, 0, 0,
                CalendarMath.Weekday(ord), CalendarMath.DayOfYear(Year, Month, Day), -1);
        }

        private ScriptValue IsoCalendarTuple(EvalContext ctx)
        {
            int iy, iw, id;
            CalendarMath.IsoCalendar(Year, Month, Day, out iy, out iw, out id);
            return ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Int(iy), ctx.Values.Int(iw), ctx.Values.Int(id) });
        }

        private static ScriptTypeInfo BuildType()
        {
            var d = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal)
            {
                ["year"] = SlotDescriptor.MakeProperty("year", (self, c) => c.Values.Int(((DateValue)self).Year)),
                ["month"] = SlotDescriptor.MakeProperty("month", (self, c) => c.Values.Int(((DateValue)self).Month)),
                ["day"] = SlotDescriptor.MakeProperty("day", (self, c) => c.Values.Int(((DateValue)self).Day)),
                ["replace"] = SlotDescriptor.MakeMethod("replace", (self, a, kw, c) => ((DateValue)self).Replace(c, a, kw)),
                ["timetuple"] = SlotDescriptor.MakeMethod("timetuple", (self, a, kw, c) => ((DateValue)self).TimeTuple(c)),
                ["toordinal"] = SlotDescriptor.MakeMethod("toordinal", (self, a, kw, c) => c.Values.Int(((DateValue)self).ToOrdinalInt())),
                ["weekday"] = SlotDescriptor.MakeMethod("weekday", (self, a, kw, c) => c.Values.Int(CalendarMath.Weekday(((DateValue)self).ToOrdinalInt()))),
                ["isoweekday"] = SlotDescriptor.MakeMethod("isoweekday", (self, a, kw, c) => c.Values.Int(CalendarMath.Weekday(((DateValue)self).ToOrdinalInt()) + 1)),
                ["isocalendar"] = SlotDescriptor.MakeMethod("isocalendar", (self, a, kw, c) => ((DateValue)self).IsoCalendarTuple(c)),
                ["isoformat"] = SlotDescriptor.MakeMethod("isoformat", (self, a, kw, c) => c.Values.Str(((DateValue)self).IsoFormat())),
                ["ctime"] = SlotDescriptor.MakeMethod("ctime", (self, a, kw, c) => c.Values.Str(((DateValue)self).CtimeString(0, 0, 0))),
                ["strftime"] = SlotDescriptor.MakeMethod("strftime", (self, a, kw, c) => c.Values.Str(((DateValue)self).Strftime(c, DtArgs.StrftimeArg(c, a)))),
            };
            return new ScriptTypeInfo("datetime.date", d);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // datetime.datetime. Immutable naive/aware timestamp. Field arithmetic with timedelta
    // keeps the tz and does NOT convert to UTC (CPython parity for fixed offsets). All wall-clock reads go
    // through ctx.Clock — never DateTime.Now.
    internal sealed class DateTimeValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = BuildType();

        internal const int TypeTag = 1002;

        public readonly int Year;
        public readonly byte Month;
        public readonly byte Day;
        public readonly byte Hour;
        public readonly byte Minute;
        public readonly byte Second;
        public readonly int Microsecond;
        public readonly TzInfoValue Tz;      // null <=> naive
        public readonly int Fold;            // 0/1: which of two occurrences of an ambiguous wall time (PEP 495)

        private DateTimeValue(int y, byte mo, byte d, byte h, byte mi, byte s, int us, TzInfoValue tz, int fold)
        {
            Year = y; Month = mo; Day = d; Hour = h; Minute = mi; Second = s; Microsecond = us; Tz = tz; Fold = fold;
        }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        internal long UsOfDay() { return ((Hour * 60L + Minute) * 60L + Second) * 1000000L + Microsecond; }
        internal int ToOrdinalInt() { return CalendarMath.Ymd2Ord(Year, Month, Day); }
        internal BigInteger ToNaiveUs() { return (BigInteger)ToOrdinalInt() * CalendarMath.UsPerDay + UsOfDay(); }
        internal long ToNaiveUsLong() { return ToOrdinalInt() * CalendarMath.UsPerDay + UsOfDay(); }   // <= ~3.2e17, always fits
        private long OffsetUsLong() { return Tz.OffsetUsAt(this).Value; }   // aware only (Tz != null)
        private BigInteger OffsetUs() { return (BigInteger)OffsetUsLong(); }

        // ---- construction ----

        internal static DateTimeValue Construct(EvalContext ctx, int y, int mo, int d, int h, int mi, int s, int us, TzInfoValue tz, int fold = 0)
        {
            if (fold != 0 && fold != 1)
                throw Raise.ValueError(ctx, "fold must be either 0 or 1");
            if (y < 1 || y > 9999)
                throw Raise.ValueError(ctx, "year is out of range");
            if (mo < 1 || mo > 12)
                throw Raise.ValueError(ctx, "month must be in 1..12");
            if (d < 1 || d > CalendarMath.DaysInMonth(y, mo))
                throw Raise.ValueError(ctx, "day is out of range for month");
            if (h < 0 || h > 23)
                throw Raise.ValueError(ctx, "hour must be in 0..23");
            if (mi < 0 || mi > 59)
                throw Raise.ValueError(ctx, "minute must be in 0..59");
            if (s < 0 || s > 59)
                throw Raise.ValueError(ctx, "second must be in 0..59");
            if (us < 0 || us > 999999)
                throw Raise.ValueError(ctx, "microsecond must be in 0..999999");
            ctx.Values.PreCharge(64);
            return new DateTimeValue(y, (byte)mo, (byte)d, (byte)h, (byte)mi, (byte)s, us, tz, fold);
        }

        private static readonly ArgSpec CtorSpec = ArgSpec.Create("datetime")
            .Req("year").Req("month").Req("day")
            .Opt("hour", ArgSpec.MISSING).Opt("minute", ArgSpec.MISSING).Opt("second", ArgSpec.MISSING)
            .Opt("microsecond", ArgSpec.MISSING).Opt("tzinfo", ArgSpec.MISSING).Opt("fold", ArgSpec.MISSING).Seal();

        // fold= is keyword-only in CPython; a ninth positional would land here too, which is accepted.
        private static int FoldArg(EvalContext ctx, ScriptValue v)
        {
            if (ReferenceEquals(v, ArgSpec.MISSING))
                return 0;
            if (v.Kind != ValueKind.Int && v.Kind != ValueKind.Bool)
                throw Raise.TypeError(ctx, "'" + v.PyTypeName + "' object cannot be interpreted as an integer");
            System.Numerics.BigInteger b = NumericOps.AsBigInteger(v);
            if (b != 0 && b != 1)
                throw Raise.ValueError(ctx, "fold must be either 0 or 1");
            return (int)b;
        }

        internal static DateTimeValue ConstructFromArgs(EvalContext ctx, ScriptValue[] args, KwArgs kw)
        {
            ctx.Budget.Step();
            // Fast path: the common all-positional call (year..tzinfo in order, no kwargs) reads args
            // directly, skipping ArgSpec.Bind and its 8-slot scratch array. The datetime signature is fixed
            // Python API; anything else (kwargs, too few/many args) falls through to the general binder,
            // which raises the exact same errors.
            int n = args == null ? 0 : args.Length;
            if (kw.Count == 0 && n >= 3 && n <= CtorSpec.SlotCount)
            {
                int fy = DtArgs.IntField(ctx, args[0], "year is out of range");
                int fmo = DtArgs.IntField(ctx, args[1], "month must be in 1..12");
                int fd = DtArgs.IntField(ctx, args[2], "day is out of range for month");
                int fh = n > 3 ? DtArgs.IntField(ctx, args[3], "hour must be in 0..23") : 0;
                int fmi = n > 4 ? DtArgs.IntField(ctx, args[4], "minute must be in 0..59") : 0;
                int fs = n > 5 ? DtArgs.IntField(ctx, args[5], "second must be in 0..59") : 0;
                int fus = n > 6 ? DtArgs.IntField(ctx, args[6], "microsecond must be in 0..999999") : 0;
                TzInfoValue ftz = n > 7 ? DtArgs.TzArg(ctx, args[7]) : null;
                return Construct(ctx, fy, fmo, fd, fh, fmi, fs, fus, ftz);
            }

            var slots = new ScriptValue[CtorSpec.SlotCount];
            ScriptValue[] star;
            CtorSpec.Bind(ctx, args, kw, slots, out star);
            int y = DtArgs.IntField(ctx, slots[0], "year is out of range");
            int mo = DtArgs.IntField(ctx, slots[1], "month must be in 1..12");
            int d = DtArgs.IntField(ctx, slots[2], "day is out of range for month");
            int h = Field(ctx, slots[3], "hour must be in 0..23");
            int mi = Field(ctx, slots[4], "minute must be in 0..59");
            int s = Field(ctx, slots[5], "second must be in 0..59");
            int us = Field(ctx, slots[6], "microsecond must be in 0..999999");
            TzInfoValue tz = ReferenceEquals(slots[7], ArgSpec.MISSING) ? null : DtArgs.TzArg(ctx, slots[7]);
            return Construct(ctx, y, mo, d, h, mi, s, us, tz, FoldArg(ctx, slots[8]));
        }

        private static int Field(EvalContext ctx, ScriptValue v, string rangeError)
        {
            return ReferenceEquals(v, ArgSpec.MISSING) ? 0 : DtArgs.IntField(ctx, v, rangeError);
        }

        // Long twin of FromNaiveUs (the whole valid datetime range fits in long).
        private static DateTimeValue FromNaiveUsLong(EvalContext ctx, long naiveUs, TzInfoValue tz)
        {
            long ordinal = naiveUs / CalendarMath.UsPerDay;
            long rem = naiveUs - ordinal * CalendarMath.UsPerDay;
            if (rem < 0)
            {
                ordinal--;
                rem += CalendarMath.UsPerDay;
            }
            if (ordinal < 1 || ordinal > CalendarMath.MaxOrdinal)
                throw Raise.Overflow(ctx, "date value out of range");
            int y, mo, d;
            CalendarMath.Ord2Ymd((int)ordinal, out y, out mo, out d);
            long sec = rem / 1000000L;
            int us = (int)(rem % 1000000L);
            return Construct(ctx, y, mo, d, (int)(sec / 3600), (int)(sec % 3600 / 60), (int)(sec % 60), us, tz);
        }

        // (ordinal, us-of-day) from naive microseconds; range-checked.
        private static DateTimeValue FromNaiveUs(EvalContext ctx, BigInteger naiveUs, TzInfoValue tz)
        {
            BigInteger ordinalB = TimeDeltaValue.FloorDivBig(naiveUs, CalendarMath.UsPerDay);
            if (ordinalB < 1 || ordinalB > CalendarMath.MaxOrdinal)
                throw Raise.Overflow(ctx, "date value out of range");
            long usOfDay = (long)(naiveUs - ordinalB * CalendarMath.UsPerDay);
            int y, mo, d;
            CalendarMath.Ord2Ymd((int)ordinalB, out y, out mo, out d);
            long sec = usOfDay / 1000000L;
            int us = (int)(usOfDay % 1000000L);
            return Construct(ctx, y, mo, d, (int)(sec / 3600), (int)(sec % 3600 / 60), (int)(sec % 60), us, tz);
        }

        // ---- clock factories (all via ctx.Clock) ----

        private static long LocalOffsetSeconds(EvalContext ctx) { return (long)ctx.Clock.LocalOffset.TotalSeconds; }

        internal static DateTimeValue UtcNow(EvalContext ctx)
        {
            return FromEpoch(ctx, CalendarMath.UtcNowToEpoch(ctx.Clock.UtcNow), 0, null);
        }

        internal static DateTimeValue Now(EvalContext ctx, TzInfoValue tz)
        {
            return FromEpochIn(ctx, CalendarMath.UtcNowToEpoch(ctx.Clock.UtcNow), tz);
        }

        internal static DateTimeValue FromTimestamp(EvalContext ctx, double t, TzInfoValue tz)
        {
            return FromEpochIn(ctx, t, tz);
        }

        // An epoch timestamp read in tz (the local clock when tz is None). A zone answers for the instant,
        // so its offset - and the fold of the wall time it lands on - come from the UTC moment itself.
        private static DateTimeValue FromEpochIn(EvalContext ctx, double t, TzInfoValue tz)
        {
            if (tz == null)
                return FromEpoch(ctx, t, LocalOffsetSeconds(ctx), null);
            long sec;
            int us;
            CalendarMath.SplitTimestamp(ctx, t, out sec, out us);
            long utcNaiveUs = CalendarMath.EpochNaiveUs + sec * 1000000L + us;
            long offsetUs = tz.OffsetUsForUtc(utcNaiveUs);
            DateTimeValue wall = FromNaiveUsLong(ctx, utcNaiveUs + offsetUs, tz);
            int fold = tz.FoldForUtc(utcNaiveUs);
            return fold == 0 ? wall : Construct(ctx, wall.Year, wall.Month, wall.Day, wall.Hour, wall.Minute, wall.Second, wall.Microsecond, tz, fold);
        }

        internal static DateTimeValue UtcFromTimestamp(EvalContext ctx, double t)
        {
            return FromEpoch(ctx, t, 0, null);
        }

        private static DateTimeValue FromEpoch(EvalContext ctx, double t, long shiftSeconds, TzInfoValue tz)
        {
            long sec;
            int us;
            CalendarMath.SplitTimestamp(ctx, t, out sec, out us);
            int y, mo, d, h, mi, s, usOut;
            CalendarMath.EpochToYmdHms(ctx, sec + shiftSeconds, us, out y, out mo, out d, out h, out mi, out s, out usOut);
            return Construct(ctx, y, mo, d, h, mi, s, usOut, tz);
        }

        internal static DateTimeValue FromOrdinal(EvalContext ctx, BigInteger n)
        {
            if (n < 1)
                throw Raise.ValueError(ctx, "ordinal must be >= 1");
            if (n > CalendarMath.MaxOrdinal)
                throw Raise.ValueError(ctx, "year is out of range");
            int y, mo, d;
            CalendarMath.Ord2Ymd((int)n, out y, out mo, out d);
            return Construct(ctx, y, mo, d, 0, 0, 0, 0, null);
        }

        internal static DateTimeValue Strptime(EvalContext ctx, string data, string fmt)
        {
            StrptimeResult r = StrptimeParser.Parse(ctx, data, fmt);
            TzInfoValue tz = null;
            if (r.OffsetSeconds != null)
            {
                long us = r.OffsetSeconds.Value * 1000000L + r.OffsetMicros;
                if (us <= -86400000000L || us >= 86400000000L)
                    throw Raise.ValueError(ctx, "offset must be a timedelta strictly between -timedelta(hours=24) and timedelta(hours=24).");
                tz = us == 0 ? TimezoneValue.Utc : TimezoneValue.FromOffsetUs(ctx, us, null);   // timezone(timedelta(0)) is utc
            }
            return Construct(ctx, r.Year, r.Month, r.Day, r.Hour, r.Minute, r.Second, r.Microsecond, tz);
        }

        internal static DateTimeValue Combine(EvalContext ctx, ScriptValue dateArg, ScriptValue timeArg)
        {
            int y, mo, d;
            DateValue dv = dateArg as DateValue;
            DateTimeValue dtv = dateArg as DateTimeValue;
            if (dv != null)
            {
                y = dv.Year; mo = dv.Month; d = dv.Day;
            }
            else if (dtv != null)
            {
                y = dtv.Year; mo = dtv.Month; d = dtv.Day;
            }
            else
                throw Raise.TypeError(ctx, "combine() argument 1 must be datetime.date, not " + dateArg.PyTypeName);
            TimeValue tv = timeArg as TimeValue;
            if (tv == null)
                throw Raise.TypeError(ctx, "combine() argument 2 must be datetime.time, not " + timeArg.PyTypeName);
            return Construct(ctx, y, mo, d, tv.Hour, tv.Minute, tv.Second, tv.Microsecond, tv.Tz);
        }

        // ---- arithmetic ----

        protected internal override ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        {
            TimeDeltaValue td = other as TimeDeltaValue;
            switch (op)
            {
                case PyBinOp.Add:
                    if (td == null)
                        return null;
                    // Naive datetime us always fits in long (<= ~3.2e17); the delta fits when |Days| is
                    // bounded so the sum stays clear of long.MaxValue. Huge deltas keep the exact path.
                    if (td.Days >= -100000000 && td.Days <= 100000000)
                        return FromNaiveUsLong(ctx, ToNaiveUsLong() + td.ToUsLong(), Tz);
                    return FromNaiveUs(ctx, ToNaiveUs() + td.ToUs(), Tz);
                case PyBinOp.Sub:
                    if (reflected)
                        return null;
                    if (td != null)
                    {
                        if (td.Days >= -100000000 && td.Days <= 100000000)
                            return FromNaiveUsLong(ctx, ToNaiveUsLong() - td.ToUsLong(), Tz);
                        return FromNaiveUs(ctx, ToNaiveUs() - td.ToUs(), Tz);
                    }
                    DateTimeValue o = other as DateTimeValue;
                    if (o != null)
                        return Difference(ctx, o);
                    return null;
                default:
                    return null;
            }
        }

        private ScriptValue Difference(EvalContext ctx, DateTimeValue o)
        {
            bool aAware = Tz != null, bAware = o.Tz != null;
            if (aAware != bAware)
                throw Raise.TypeError(ctx, "can't subtract offset-naive and offset-aware datetimes");
            BigInteger a = aAware ? ToNaiveUs() - OffsetUs() : ToNaiveUs();
            BigInteger b = bAware ? o.ToNaiveUs() - o.OffsetUs() : o.ToNaiveUs();
            return TimeDeltaValue.FromUs(ctx, a - b);
        }

        // ---- comparisons / hash ----

        // The hash reads the offset as if fold were 0, so the two occurrences of an ambiguous wall time
        // hash alike (they compare equal under the same tzinfo), as CPython's __hash__ does.
        private BigInteger HashKey()
        {
            if (Tz == null)
                return ToNaiveUs();
            if (Fold == 0)
                return ToNaiveUs() - OffsetUs();
            var twin = new DateTimeValue(Year, (byte)Month, (byte)Day, (byte)Hour, (byte)Minute, (byte)Second, Microsecond, Tz, 0);
            return ToNaiveUs() - (BigInteger)Tz.OffsetUsAt(twin).Value;
        }

        // The same tzinfo object on both sides compares the wall-clock fields (fold ignored), as CPython;
        // otherwise the UTC instants.
        private BigInteger CmpKey(DateTimeValue o)
        {
            return Tz == null || ReferenceEquals(Tz, o.Tz) ? ToNaiveUs() : ToNaiveUs() - OffsetUs();
        }

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            DateTimeValue o = other as DateTimeValue;
            if (o == null)
                return false;
            if ((Tz == null) != (o.Tz == null))
                return false;
            return CmpKey(o) == o.CmpKey(this);
        }

        protected internal override bool TryCompareLeafCore(ScriptValue other, EvalContext ctx, int depth, out int cmp)
        {
            DateTimeValue o = other as DateTimeValue;
            if (o != null)
            {
                if ((Tz == null) != (o.Tz == null))
                    throw Raise.TypeError(ctx, "can't compare offset-naive and offset-aware datetimes");
                cmp = CmpKey(o).CompareTo(o.CmpKey(this));
                return true;
            }
            if (other is DateValue)
                throw Raise.TypeError(ctx, "can't compare datetime.datetime to datetime.date");
            cmp = 0;
            return false;
        }

        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            if (Tz != null)
                return NumericHash.HashBigInteger(HashKey());
            var t = ctx.Values.Tuple(new ScriptValue[]
            {
                ctx.Values.Int(TypeTag), ctx.Values.Int(Year), ctx.Values.Int(Month), ctx.Values.Int(Day),
                ctx.Values.Int(Hour), ctx.Values.Int(Minute), ctx.Values.Int(Second), ctx.Values.Int(Microsecond),
            });
            return PyOps.Hash(t, ctx, depth);
        }

        // ---- formatting ----

        internal string IsoFormat(char sep) { return IsoFormat(sep, "auto"); }

        internal string IsoFormat(char sep, string timespec)
        {
            string core = Year.ToString("0000", CultureInfo.InvariantCulture) + "-" + Pad2(Month) + "-" + Pad2(Day)
                + sep + TimeValue.IsoTime(Hour, Minute, Second, Microsecond, timespec);
            if (Tz != null)
                core += TimezoneValue.IsoOffset(OffsetUsLong());
            return core;
        }

        internal string CtimeString()
        {
            int wd = CalendarMath.Weekday(ToOrdinalInt());
            return DtNames.AbbrDays[wd] + " " + DtNames.AbbrMonths[Month - 1] + " "
                + ((int)Day).ToString(CultureInfo.InvariantCulture).PadLeft(2, ' ') + " "
                + Pad2(Hour) + ":" + Pad2(Minute) + ":" + Pad2(Second) + " "
                + Year.ToString(CultureInfo.InvariantCulture);
        }

        internal string Strftime(EvalContext ctx, string fmt)
        {
            int ord = ToOrdinalInt();
            var t = new BrokenTime
            {
                Year = Year, Month = Month, Day = Day,
                Hour = Hour, Minute = Minute, Second = Second, Microsecond = Microsecond,
                WeekdayMon0 = CalendarMath.Weekday(ord),
                DayOfYear = CalendarMath.DayOfYear(Year, Month, Day),
                OffsetUs = Tz == null ? (long?)null : Tz.OffsetUsAt(this),
                TzName = Tz == null ? null : Tz.TzNameAt(this),
                HasMicroseconds = true,
            };
            return StrftimeFormatter.Format(ctx, fmt, t);
        }

        protected internal override ScriptValue FormatSpecCore(string spec, EvalContext ctx)
        {
            return ctx.Values.Str(Strftime(ctx, spec));
        }

        protected internal override void StrLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth) { sb.Append(IsoFormat(' ')); }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            string body = Fmt(Year) + ", " + Fmt(Month) + ", " + Fmt(Day) + ", " + Fmt(Hour) + ", " + Fmt(Minute);
            if (Microsecond != 0)
                body += ", " + Fmt(Second) + ", " + Fmt(Microsecond);
            else if (Second != 0)
                body += ", " + Fmt(Second);
            if (Tz != null)
                body += ", tzinfo=" + Tz.Repr(ctx);
            if (Fold != 0)
                body += ", fold=1";
            sb.Append("datetime.datetime(" + body + ")");
        }

        private static string Fmt(int n) { return n.ToString(CultureInfo.InvariantCulture); }
        private static string Pad2(int n) { return n.ToString("00", CultureInfo.InvariantCulture); }

        // ---- instance slots ----

        private static readonly ArgSpec ReplaceSpec = ArgSpec.Create("replace")
            .Opt("year", ArgSpec.MISSING).Opt("month", ArgSpec.MISSING).Opt("day", ArgSpec.MISSING)
            .Opt("hour", ArgSpec.MISSING).Opt("minute", ArgSpec.MISSING).Opt("second", ArgSpec.MISSING)
            .Opt("microsecond", ArgSpec.MISSING).Opt("tzinfo", ArgSpec.MISSING).Opt("fold", ArgSpec.MISSING).Seal();

        private ScriptValue Replace(EvalContext ctx, ScriptValue[] args, KwArgs kw)
        {
            var slots = new ScriptValue[ReplaceSpec.SlotCount];
            ScriptValue[] star;
            ReplaceSpec.Bind(ctx, args, kw, slots, out star);
            int y = Keep(ctx, slots[0], Year, "year is out of range");
            int mo = Keep(ctx, slots[1], Month, "month must be in 1..12");
            int d = Keep(ctx, slots[2], Day, "day is out of range for month");
            int h = Keep(ctx, slots[3], Hour, "hour must be in 0..23");
            int mi = Keep(ctx, slots[4], Minute, "minute must be in 0..59");
            int s = Keep(ctx, slots[5], Second, "second must be in 0..59");
            int us = Keep(ctx, slots[6], Microsecond, "microsecond must be in 0..999999");
            TzInfoValue tz = ReferenceEquals(slots[7], ArgSpec.MISSING) ? Tz : DtArgs.TzArg(ctx, slots[7]);
            int fold = ReferenceEquals(slots[8], ArgSpec.MISSING) ? Fold : FoldArg(ctx, slots[8]);
            return Construct(ctx, y, mo, d, h, mi, s, us, tz, fold);
        }

        private static int Keep(EvalContext ctx, ScriptValue v, int current, string rangeError)
        {
            return ReferenceEquals(v, ArgSpec.MISSING) ? current : DtArgs.IntField(ctx, v, rangeError);
        }

        private ScriptValue TimeTuple(EvalContext ctx, int isdst)
        {
            int ord = ToOrdinalInt();
            return StructTimeValue.Create(ctx, Year, Month, Day, Hour, Minute, Second,
                CalendarMath.Weekday(ord), CalendarMath.DayOfYear(Year, Month, Day), isdst);
        }

        private ScriptValue UtcTimeTuple(EvalContext ctx)
        {
            if (Tz == null)
                return TimeTuple(ctx, 0);
            // shift to UTC, then a struct_time with tm_isdst=0
            BigInteger utcNaive = ToNaiveUs() - OffsetUs();
            BigInteger ordinalB = TimeDeltaValue.FloorDivBig(utcNaive, CalendarMath.UsPerDay);
            if (ordinalB < 1 || ordinalB > CalendarMath.MaxOrdinal)
                throw Raise.Overflow(ctx, "date value out of range");
            long usOfDay = (long)(utcNaive - ordinalB * CalendarMath.UsPerDay);
            int y, mo, d;
            CalendarMath.Ord2Ymd((int)ordinalB, out y, out mo, out d);
            long sec = usOfDay / 1000000L;
            int h = (int)(sec / 3600), mi = (int)(sec % 3600 / 60), s = (int)(sec % 60);
            return StructTimeValue.Create(ctx, y, mo, d, h, mi, s,
                CalendarMath.Weekday((int)ordinalB), CalendarMath.DayOfYear(y, mo, d), 0);
        }

        // isoformat(sep='T', timespec='auto')
        private ScriptValue IsoFormatMethod(EvalContext ctx, ScriptValue[] args, KwArgs kw)
        {
            char sep = 'T';
            ScriptValue sepArg = args.Length >= 1 ? args[0] : null;
            ScriptValue sepKw;
            if (kw.TryGet("sep", out sepKw))
                sepArg = sepKw;
            if (sepArg != null)
            {
                StrValue s = sepArg as StrValue;
                if (s == null || s.Value.Length != 1)
                    throw Raise.TypeError(ctx, "isoformat() argument 'sep' must be a single character");
                sep = s.Value[0];
            }
            return ctx.Values.Str(IsoFormat(sep, TimeValue.TimespecArg(ctx, args, kw, 1)));
        }

        private ScriptValue IsoCalendarTuple(EvalContext ctx)
        {
            int iy, iw, id;
            CalendarMath.IsoCalendar(Year, Month, Day, out iy, out iw, out id);
            return ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Int(iy), ctx.Values.Int(iw), ctx.Values.Int(id) });
        }

        private ScriptValue Timestamp(EvalContext ctx)
        {
            BigInteger num = Tz != null
                ? ToNaiveUs() - OffsetUs() - CalendarMath.EpochNaiveUs
                : ToNaiveUs() - (BigInteger)LocalOffsetSeconds(ctx) * 1000000L - CalendarMath.EpochNaiveUs;
            return ctx.Values.Float((double)num / 1e6);
        }

        // astimezone(tz=None): the same instant under tz. A naive self is read as local time (the host
        // clock's offset), and tz=None means the local zone as timezone(offset, name), as CPython does.
        private ScriptValue Astimezone(EvalContext ctx, ScriptValue[] a, KwArgs kw)
        {
            Args.AtMost(ctx, a, "astimezone", 1);
            ScriptValue tzArg = a.Length == 1 ? a[0] : null;
            ScriptValue kwTz;
            if (kw.TryGet("tz", out kwTz))
                tzArg = kwTz;
            TzInfoValue target;
            if (tzArg == null || tzArg.Kind == ValueKind.None)
                target = TimezoneValue.FromOffsetUs(ctx, LocalOffsetSeconds(ctx) * 1000000L, ctx.Clock.LocalName);
            else
            {
                target = tzArg as TzInfoValue;
                if (target == null)
                    throw Raise.TypeError(ctx, "tz argument must be an instance of tzinfo");
            }
            if (Tz != null && ReferenceEquals(Tz, target))
                return this;
            long selfOffset = Tz != null ? OffsetUsLong() : LocalOffsetSeconds(ctx) * 1000000L;
            long utc = ToNaiveUsLong() - selfOffset;
            DateTimeValue wall = FromNaiveUsLong(ctx, utc + target.OffsetUsForUtc(utc), target);
            int fold = target.FoldForUtc(utc);
            return fold == 0 ? wall : Construct(ctx, wall.Year, wall.Month, wall.Day, wall.Hour, wall.Minute, wall.Second, wall.Microsecond, target, fold);
        }

        private ScriptValue TzInfoValue(EvalContext ctx) { return Tz == null ? (ScriptValue)ctx.Values.None : Tz; }

        private static ScriptTypeInfo BuildType()
        {
            var d = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal)
            {
                ["year"] = SlotDescriptor.MakeProperty("year", (self, c) => c.Values.Int(((DateTimeValue)self).Year)),
                ["month"] = SlotDescriptor.MakeProperty("month", (self, c) => c.Values.Int(((DateTimeValue)self).Month)),
                ["day"] = SlotDescriptor.MakeProperty("day", (self, c) => c.Values.Int(((DateTimeValue)self).Day)),
                ["hour"] = SlotDescriptor.MakeProperty("hour", (self, c) => c.Values.Int(((DateTimeValue)self).Hour)),
                ["minute"] = SlotDescriptor.MakeProperty("minute", (self, c) => c.Values.Int(((DateTimeValue)self).Minute)),
                ["second"] = SlotDescriptor.MakeProperty("second", (self, c) => c.Values.Int(((DateTimeValue)self).Second)),
                ["microsecond"] = SlotDescriptor.MakeProperty("microsecond", (self, c) => c.Values.Int(((DateTimeValue)self).Microsecond)),
                ["tzinfo"] = SlotDescriptor.MakeProperty("tzinfo", (self, c) => ((DateTimeValue)self).TzInfoValue(c)),
                ["fold"] = SlotDescriptor.MakeProperty("fold", (self, c) => c.Values.Int(((DateTimeValue)self).Fold)),
                ["date"] = SlotDescriptor.MakeMethod("date", (self, a, kw, c) => DateValue.Construct(c, ((DateTimeValue)self).Year, ((DateTimeValue)self).Month, ((DateTimeValue)self).Day)),
                ["time"] = SlotDescriptor.MakeMethod("time", (self, a, kw, c) => TimeValue.Construct(c, ((DateTimeValue)self).Hour, ((DateTimeValue)self).Minute, ((DateTimeValue)self).Second, ((DateTimeValue)self).Microsecond, null)),
                ["timetz"] = SlotDescriptor.MakeMethod("timetz", (self, a, kw, c) => TimeValue.Construct(c, ((DateTimeValue)self).Hour, ((DateTimeValue)self).Minute, ((DateTimeValue)self).Second, ((DateTimeValue)self).Microsecond, ((DateTimeValue)self).Tz)),
                ["replace"] = SlotDescriptor.MakeMethod("replace", (self, a, kw, c) => ((DateTimeValue)self).Replace(c, a, kw)),
                ["toordinal"] = SlotDescriptor.MakeMethod("toordinal", (self, a, kw, c) => c.Values.Int(((DateTimeValue)self).ToOrdinalInt())),
                ["weekday"] = SlotDescriptor.MakeMethod("weekday", (self, a, kw, c) => c.Values.Int(CalendarMath.Weekday(((DateTimeValue)self).ToOrdinalInt()))),
                ["isoweekday"] = SlotDescriptor.MakeMethod("isoweekday", (self, a, kw, c) => c.Values.Int(CalendarMath.Weekday(((DateTimeValue)self).ToOrdinalInt()) + 1)),
                ["isocalendar"] = SlotDescriptor.MakeMethod("isocalendar", (self, a, kw, c) => ((DateTimeValue)self).IsoCalendarTuple(c)),
                ["timetuple"] = SlotDescriptor.MakeMethod("timetuple", (self, a, kw, c) => ((DateTimeValue)self).TimeTuple(c, -1)),
                ["utctimetuple"] = SlotDescriptor.MakeMethod("utctimetuple", (self, a, kw, c) => ((DateTimeValue)self).UtcTimeTuple(c)),
                ["isoformat"] = SlotDescriptor.MakeMethod("isoformat", (self, a, kw, c) => ((DateTimeValue)self).IsoFormatMethod(c, a, kw)),
                ["ctime"] = SlotDescriptor.MakeMethod("ctime", (self, a, kw, c) => c.Values.Str(((DateTimeValue)self).CtimeString())),
                ["strftime"] = SlotDescriptor.MakeMethod("strftime", (self, a, kw, c) => c.Values.Str(((DateTimeValue)self).Strftime(c, DtArgs.StrftimeArg(c, a)))),
                ["timestamp"] = SlotDescriptor.MakeMethod("timestamp", (self, a, kw, c) => ((DateTimeValue)self).Timestamp(c)),
                ["astimezone"] = SlotDescriptor.MakeMethod("astimezone", (self, a, kw, c) => ((DateTimeValue)self).Astimezone(c, a, kw)),
                ["utcoffset"] = SlotDescriptor.MakeMethod("utcoffset", (self, a, kw, c) =>
                {
                    DateTimeValue dt = (DateTimeValue)self;
                    return dt.Tz == null ? (ScriptValue)c.Values.None : dt.Tz.OffsetTd(c, dt.OffsetUsLong());
                }),
                ["dst"] = SlotDescriptor.MakeMethod("dst", (self, a, kw, c) =>
                {
                    DateTimeValue dt = (DateTimeValue)self;
                    long? dst = dt.Tz == null ? (long?)null : dt.Tz.DstUsAt(dt);
                    return dst == null ? (ScriptValue)c.Values.None : dt.Tz.OffsetTd(c, dst.Value);
                }),
                ["tzname"] = SlotDescriptor.MakeMethod("tzname", (self, a, kw, c) =>
                {
                    DateTimeValue dt = (DateTimeValue)self;
                    return dt.Tz == null ? (ScriptValue)c.Values.None : c.Values.Str(dt.Tz.TzNameAt(dt));
                }),
            };
            return new ScriptTypeInfo("datetime.datetime", d);
        }
    }
}

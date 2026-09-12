using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // datetime.time. Immutable time-of-day, optionally aware (fixed-offset tz). Always
    // truthy (3.5+, no midnight-falsy bug). Naive/aware compare per CPython: == is False across
    // the divide, ordering is a TypeError.
    internal sealed class TimeValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = BuildType();

        internal const int TypeTag = 1003;

        public readonly byte Hour;
        public readonly byte Minute;
        public readonly byte Second;
        public readonly int Microsecond;
        public readonly TzInfoValue Tz;      // null <=> naive

        private TimeValue(byte hour, byte minute, byte second, int us, TzInfoValue tz)
        {
            Hour = hour;
            Minute = minute;
            Second = second;
            Microsecond = us;
            Tz = tz;
        }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        internal long UsOfDay()
        {
            return ((Hour * 60L + Minute) * 60L + Second) * 1000000L + Microsecond;
        }

        // ---- construction ----

        internal static TimeValue Construct(EvalContext ctx, int hour, int minute, int second, int us, TzInfoValue tz)
        {
            if (hour < 0 || hour > 23)
                throw Raise.ValueError(ctx, "hour must be in 0..23");
            if (minute < 0 || minute > 59)
                throw Raise.ValueError(ctx, "minute must be in 0..59");
            if (second < 0 || second > 59)
                throw Raise.ValueError(ctx, "second must be in 0..59");
            if (us < 0 || us > 999999)
                throw Raise.ValueError(ctx, "microsecond must be in 0..999999");
            ctx.Values.PreCharge(48);
            return new TimeValue((byte)hour, (byte)minute, (byte)second, us, tz);
        }

        private static readonly ArgSpec CtorSpec = ArgSpec.Create("time")
            .Opt("hour", ArgSpec.MISSING).Opt("minute", ArgSpec.MISSING).Opt("second", ArgSpec.MISSING)
            .Opt("microsecond", ArgSpec.MISSING).Opt("tzinfo", ArgSpec.MISSING).Seal();

        internal static TimeValue ConstructFromArgs(EvalContext ctx, ScriptValue[] args, KwArgs kw)
        {
            ctx.Budget.Step();
            var slots = new ScriptValue[CtorSpec.SlotCount];
            ScriptValue[] star;
            CtorSpec.Bind(ctx, args, kw, slots, out star);
            int h = Field(ctx, slots[0], "hour must be in 0..23");
            int mi = Field(ctx, slots[1], "minute must be in 0..59");
            int s = Field(ctx, slots[2], "second must be in 0..59");
            int us = Field(ctx, slots[3], "microsecond must be in 0..999999");
            TzInfoValue tz = ReferenceEquals(slots[4], ArgSpec.MISSING) ? null : DtArgs.TzArg(ctx, slots[4]);
            return Construct(ctx, h, mi, s, us, tz);
        }

        private static int Field(EvalContext ctx, ScriptValue v, string rangeError)
        {
            return ReferenceEquals(v, ArgSpec.MISSING) ? 0 : DtArgs.IntField(ctx, v, rangeError);
        }

        // ---- comparisons / hash ----

        // A zone answers utcoffset(None) as None, so a time carrying a ZoneInfo compares as naive (CPython).
        private long? OffsetUsOrNull() { return Tz == null ? (long?)null : Tz.OffsetUsAt(null); }

        private long CmpKey()
        {
            long? off = OffsetUsOrNull();
            return off == null ? UsOfDay() : UsOfDay() - off.Value;
        }

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            TimeValue o = other as TimeValue;
            if (o == null)
                return false;
            if ((Tz == null) != (o.Tz == null))
                return false;
            return CmpKey() == o.CmpKey();
        }

        protected internal override bool TryCompareLeafCore(ScriptValue other, EvalContext ctx, int depth, out int cmp)
        {
            TimeValue o = other as TimeValue;
            if (o == null)
            {
                cmp = 0;
                return false;
            }
            if ((Tz == null) != (o.Tz == null))
                throw Raise.TypeError(ctx, "can't compare offset-naive and offset-aware times");
            cmp = CmpKey().CompareTo(o.CmpKey());
            return true;
        }

        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            if (Tz != null)
                return NumericHash.HashBigInteger(new BigInteger(CmpKey()));
            var t = ctx.Values.Tuple(new ScriptValue[]
            {
                ctx.Values.Int(TypeTag), ctx.Values.Int(Hour), ctx.Values.Int(Minute),
                ctx.Values.Int(Second), ctx.Values.Int(Microsecond),
            });
            return PyOps.Hash(t, ctx, 0);
        }

        // ---- formatting ----

        internal string IsoFormat() { return IsoFormat("auto"); }

        internal string IsoFormat(string timespec)
        {
            string core = IsoTime(Hour, Minute, Second, Microsecond, timespec);
            long? off = OffsetUsOrNull();
            if (off != null)
                core += TimezoneValue.IsoOffset(off.Value);
            return core;
        }

        // The HH:MM:SS[.ffffff] part under a 3.6+ timespec ('auto' prints microseconds only when non-zero).
        internal static string IsoTime(int hour, int minute, int second, int microsecond, string timespec)
        {
            switch (timespec)
            {
                case "hours": return Pad2(hour);
                case "minutes": return Pad2(hour) + ":" + Pad2(minute);
                case "seconds": return Pad2(hour) + ":" + Pad2(minute) + ":" + Pad2(second);
                case "milliseconds":
                    return Pad2(hour) + ":" + Pad2(minute) + ":" + Pad2(second) + "." + (microsecond / 1000).ToString("000", CultureInfo.InvariantCulture);
                case "microseconds":
                    return Pad2(hour) + ":" + Pad2(minute) + ":" + Pad2(second) + "." + microsecond.ToString("000000", CultureInfo.InvariantCulture);
                default:
                    string core = Pad2(hour) + ":" + Pad2(minute) + ":" + Pad2(second);
                    if (microsecond != 0)
                        core += "." + microsecond.ToString("000000", CultureInfo.InvariantCulture);
                    return core;
            }
        }

        // isoformat's timespec: positional slot `pos` or the keyword; validated against the 3.6+ names.
        internal static string TimespecArg(EvalContext ctx, ScriptValue[] a, Outbridge.PyLite.Runtime.Evaluator.KwArgs kw, int pos)
        {
            ScriptValue v = a.Length > pos ? a[pos] : null;
            ScriptValue kv;
            if (kw.TryGet("timespec", out kv))
                v = kv;
            if (v == null)
                return "auto";
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, "isoformat() argument 'timespec' must be str, not " + v.PyTypeName);
            switch (s.Value)
            {
                case "auto": case "hours": case "minutes": case "seconds": case "milliseconds": case "microseconds":
                    return s.Value;
                default:
                    throw Raise.ValueError(ctx, "Unknown timespec value");
            }
        }

        internal string Strftime(EvalContext ctx, string fmt)
        {
            // CPython formats time via timetuple(1900, 1, 1, H, M, S, 0, 1, -1); %z/%Z use this time's tz.
            var t = new BrokenTime
            {
                Year = 1900, Month = 1, Day = 1,
                Hour = Hour, Minute = Minute, Second = Second, Microsecond = Microsecond,
                WeekdayMon0 = 0, DayOfYear = 1,
                OffsetUs = OffsetUsOrNull(),
                TzName = Tz == null ? null : Tz.TzNameAt(null),
                HasMicroseconds = true,
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
            string body;
            if (Microsecond != 0)
                body = Fmt(Hour) + ", " + Fmt(Minute) + ", " + Fmt(Second) + ", " + Fmt(Microsecond);
            else if (Second != 0)
                body = Fmt(Hour) + ", " + Fmt(Minute) + ", " + Fmt(Second);
            else
                body = Fmt(Hour) + ", " + Fmt(Minute);
            if (Tz != null)
                body += ", tzinfo=" + Tz.Repr(ctx);
            sb.Append("datetime.time(" + body + ")");
        }

        private static string Fmt(int n) { return n.ToString(CultureInfo.InvariantCulture); }
        private static string Pad2(int n) { return n.ToString("00", CultureInfo.InvariantCulture); }

        // ---- instance slots ----

        private static readonly ArgSpec ReplaceSpec = ArgSpec.Create("replace")
            .Opt("hour", ArgSpec.MISSING).Opt("minute", ArgSpec.MISSING).Opt("second", ArgSpec.MISSING)
            .Opt("microsecond", ArgSpec.MISSING).Opt("tzinfo", ArgSpec.MISSING).Seal();

        private ScriptValue Replace(EvalContext ctx, ScriptValue[] args, KwArgs kw)
        {
            var slots = new ScriptValue[ReplaceSpec.SlotCount];
            ScriptValue[] star;
            ReplaceSpec.Bind(ctx, args, kw, slots, out star);
            int h = ReferenceEquals(slots[0], ArgSpec.MISSING) ? Hour : DtArgs.IntField(ctx, slots[0], "hour must be in 0..23");
            int mi = ReferenceEquals(slots[1], ArgSpec.MISSING) ? Minute : DtArgs.IntField(ctx, slots[1], "minute must be in 0..59");
            int s = ReferenceEquals(slots[2], ArgSpec.MISSING) ? Second : DtArgs.IntField(ctx, slots[2], "second must be in 0..59");
            int us = ReferenceEquals(slots[3], ArgSpec.MISSING) ? Microsecond : DtArgs.IntField(ctx, slots[3], "microsecond must be in 0..999999");
            TzInfoValue tz = ReferenceEquals(slots[4], ArgSpec.MISSING) ? Tz : DtArgs.TzArg(ctx, slots[4]);
            return Construct(ctx, h, mi, s, us, tz);
        }

        private static ScriptTypeInfo BuildType()
        {
            var d = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal)
            {
                ["hour"] = SlotDescriptor.MakeProperty("hour", (self, c) => c.Values.Int(((TimeValue)self).Hour)),
                ["minute"] = SlotDescriptor.MakeProperty("minute", (self, c) => c.Values.Int(((TimeValue)self).Minute)),
                ["second"] = SlotDescriptor.MakeProperty("second", (self, c) => c.Values.Int(((TimeValue)self).Second)),
                ["microsecond"] = SlotDescriptor.MakeProperty("microsecond", (self, c) => c.Values.Int(((TimeValue)self).Microsecond)),
                ["tzinfo"] = SlotDescriptor.MakeProperty("tzinfo", (self, c) =>
                {
                    TzInfoValue tz = ((TimeValue)self).Tz;
                    return tz == null ? (ScriptValue)c.Values.None : tz;
                }),
                ["replace"] = SlotDescriptor.MakeMethod("replace", (self, a, kw, c) => ((TimeValue)self).Replace(c, a, kw)),
                ["isoformat"] = SlotDescriptor.MakeMethod("isoformat", (self, a, kw, c) => c.Values.Str(((TimeValue)self).IsoFormat(TimespecArg(c, a, kw, 0)))),
                ["strftime"] = SlotDescriptor.MakeMethod("strftime", (self, a, kw, c) => c.Values.Str(((TimeValue)self).Strftime(c, DtArgs.StrftimeArg(c, a)))),
                ["utcoffset"] = SlotDescriptor.MakeMethod("utcoffset", (self, a, kw, c) =>
                {
                    TimeValue t = (TimeValue)self;
                    long? off = t.OffsetUsOrNull();
                    return off == null ? (ScriptValue)c.Values.None : t.Tz.OffsetTd(c, off.Value);
                }),
                ["dst"] = SlotDescriptor.MakeMethod("dst", (self, a, kw, c) => c.Values.None),
                ["tzname"] = SlotDescriptor.MakeMethod("tzname", (self, a, kw, c) =>
                {
                    TzInfoValue tz = ((TimeValue)self).Tz;
                    string name = tz == null ? null : tz.TzNameAt(null);
                    return name == null ? (ScriptValue)c.Values.None : c.Values.Str(name);
                }),
            };
            return new ScriptTypeInfo("datetime.time", d);
        }
    }
}

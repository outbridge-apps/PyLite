using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // zoneinfo.ZoneInfo over the Windows time zone database: the IANA key goes through the CLDR map
    // (WindowsZones) to a Windows id, and offsets/DST come from TimeZoneInfo. Immutable and cached per
    // key, so ZoneInfo(key) is ZoneInfo(key) as in CPython. Rules are the OS's, not tzdata's: identical for
    // current rules, approximate for historical dates; tzname() answers tzdata abbreviations (ZoneAbbrev).
    internal sealed class ZoneInfoValue : TzInfoValue
    {
        private static readonly ScriptTypeInfo Type = BuildType();
        private static readonly ConcurrentDictionary<string, ZoneInfoValue> Cache =
            new ConcurrentDictionary<string, ZoneInfoValue>(StringComparer.Ordinal);

        public readonly string Key;
        private readonly TimeZoneInfo _zone;
        private readonly TimeZoneInfo.AdjustmentRule[] _rules;

        private ZoneInfoValue(string key, TimeZoneInfo zone)
        {
            Key = key;
            _zone = zone;
            _rules = zone.GetAdjustmentRules();
        }

        private ZoneInfoValue(string key, TimeZoneInfo zone, TimeZoneInfo.AdjustmentRule[] rules)
        {
            Key = key;
            _zone = zone;
            _rules = rules;
        }

        // AdjustmentRule.BaseUtcOffsetDelta is internal on .NET Framework; without it the standard offset is
        // today's base, as before.
        private static readonly Func<TimeZoneInfo.AdjustmentRule, TimeSpan> BaseDelta = MakeBaseDelta();

        private static Func<TimeZoneInfo.AdjustmentRule, TimeSpan> MakeBaseDelta()
        {
            try
            {
                PropertyInfo p = typeof(TimeZoneInfo.AdjustmentRule).GetProperty("BaseUtcOffsetDelta",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo get = p == null || p.PropertyType != typeof(TimeSpan) ? null : p.GetGetMethod(true);
                return get == null ? null : (Func<TimeZoneInfo.AdjustmentRule, TimeSpan>)Delegate.CreateDelegate(
                    typeof(Func<TimeZoneInfo.AdjustmentRule, TimeSpan>), get);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }

        // ZoneInfo(key): the cached instance; ZoneInfoNotFoundError (a KeyError) when the key is unknown to
        // the CLDR map or its Windows zone is absent from this machine.
        internal static ZoneInfoValue Get(EvalContext ctx, string key, bool cached)
        {
            ZoneInfoValue z;
            if (cached && Cache.TryGetValue(key, out z))
                return z;
            // A no_cache instance is a new object over the same rules: a copy of them per call was megabytes, uncharged.
            if (!cached && Cache.TryGetValue(key, out z))
                return new ZoneInfoValue(key, z._zone, z._rules);
            string windowsId;
            if (!WindowsZones.TryMap(key, out windowsId))
                throw NotFound(ctx, key);
            TimeZoneInfo zone;
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(windowsId);
            }
            catch (TimeZoneNotFoundException)
            {
                throw NotFound(ctx, key);
            }
            catch (InvalidTimeZoneException)
            {
                throw NotFound(ctx, key);
            }
            z = new ZoneInfoValue(key, zone);
            ZoneInfoValue shared = Cache.GetOrAdd(key, z);
            return cached ? shared : new ZoneInfoValue(key, shared._zone, shared._rules);
        }

        private static ScriptException NotFound(EvalContext ctx, string key)
        {
            return Raise.Make(ctx, PyExceptionTypes.ZoneInfoNotFoundError, "No time zone found with key " + key);
        }

        // available_timezones(): the CLDR keys whose Windows zone this machine has. The registry sweep
        // (~140 ids) runs once per process; the machine's zone set does not change under a run.
        private static volatile List<string> _available;

        internal static List<string> Available()
        {
            List<string> cached = _available;
            if (cached != null)
                return cached;
            var ok = new Dictionary<string, bool>(StringComparer.Ordinal);
            var keys = new List<string>();
            foreach (KeyValuePair<string, string> kv in WindowsZones.All())
            {
                bool present;
                if (!ok.TryGetValue(kv.Value, out present))
                {
                    try
                    {
                        TimeZoneInfo.FindSystemTimeZoneById(kv.Value);
                        present = true;
                    }
                    catch (TimeZoneNotFoundException) { present = false; }
                    catch (InvalidTimeZoneException) { present = false; }
                    ok[kv.Value] = present;
                }
                if (present)
                    keys.Add(kv.Key);
            }
            _available = keys;
            return keys;
        }

        // ---- the tzinfo contract ----

        // Naive microseconds count from ordinal 1 = 0001-01-01, one day after DateTime's tick origin.
        private static DateTime Wall(long naiveUs)
        {
            long ticks = (naiveUs - CalendarMath.UsPerDay) * 10;
            if (ticks < 0)
                ticks = 0;
            else if (ticks > DateTime.MaxValue.Ticks)
                ticks = DateTime.MaxValue.Ticks;
            return new DateTime(ticks, DateTimeKind.Unspecified);
        }

        // The offset for a wall time. In the ambiguous hour fold=0 is the first occurrence (the larger,
        // daylight offset) and fold=1 the second; in a gap fold=0 answers the pre-transition offset and
        // fold=1 the post-transition one, as CPython's ZoneInfo does.
        private long OffsetUsForWall(long naiveUs, int fold)
        {
            DateTime wall = Wall(naiveUs);
            TimeSpan off;
            if (_zone.IsAmbiguousTime(wall))
            {
                TimeSpan[] both = _zone.GetAmbiguousTimeOffsets(wall);
                TimeSpan lo = both[0], hi = both[0];
                for (int i = 1; i < both.Length; i++)
                {
                    if (both[i] > hi)
                        hi = both[i];
                    if (both[i] < lo)
                        lo = both[i];
                }
                off = fold == 0 ? hi : lo;
            }
            else if (fold != 0 && _zone.IsInvalidTime(wall))
                off = _zone.GetUtcOffset(wall.AddHours(3));   // past the gap: the post-transition offset
            else
                off = _zone.GetUtcOffset(wall);
            return off.Ticks / 10;
        }

        internal override long? OffsetUsAt(DateTimeValue dt)
        {
            return dt == null ? (long?)null : OffsetUsForWall(dt.ToNaiveUsLong(), dt.Fold);
        }

        internal override long OffsetUsForUtc(long utcNaiveUs)
        {
            DateTime utc = DateTime.SpecifyKind(Wall(utcNaiveUs), DateTimeKind.Utc);
            return _zone.GetUtcOffset(utc).Ticks / 10;
        }

        internal override int FoldForUtc(long utcNaiveUs)
        {
            long off = OffsetUsForUtc(utcNaiveUs);
            DateTime wall = Wall(utcNaiveUs + off);
            if (!_zone.IsAmbiguousTime(wall))
                return 0;
            TimeSpan[] both = _zone.GetAmbiguousTimeOffsets(wall);
            TimeSpan lo = both[0];
            for (int i = 1; i < both.Length; i++)
                if (both[i] < lo)
                    lo = both[i];
            return off == lo.Ticks / 10 ? 1 : 0;
        }

        // The standard offset in force at a wall time whose offset is known: the base plus the delta of the rule that
        // covers it (Moscow kept +04 as standard time in 2012, Asuncion -04 in 2020; the base alone is today's).
        // Windows keeps a mid-year change of standard time as "daylight" of a whole-year rule: such an interval touches
        // the year's start or end, and runs on from the standard offset of the year beside it (Pyongyang's +09 until
        // August 2015, Minsk's +03 from March 2011, Asuncion's -03 from October 2024). Its offset is the standard one.
        private long StandardUsAt(long naiveUs, long offsetUs)
        {
            if (BaseDelta == null)
                return _zone.BaseUtcOffset.Ticks / 10;
            DateTime day = Wall(naiveUs).Date;
            TimeZoneInfo.AdjustmentRule r = RuleAt(day);
            long std = RuleStandardUs(r);
            if (r == null || offsetUs == std)
                return std;
            TimeZoneInfo.TransitionTime s = r.DaylightTransitionStart, e = r.DaylightTransitionEnd;
            bool wraps = s.Month > e.Month;   // a southern year: daylight at both of its ends
            bool touchesStart = StartsTheYear(s) || (wraps && day.Month <= e.Month);
            bool touchesEnd = StartsTheYear(e) || (wraps && day.Month >= s.Month);
            if (touchesStart && r.DateStart > DateTime.MinValue && RuleStandardUs(RuleAt(r.DateStart.AddDays(-1))) == offsetUs)
                return offsetUs;
            if (touchesEnd && r.DateEnd < DateTime.MaxValue.Date && RuleStandardUs(RuleAt(r.DateEnd.AddDays(1))) == offsetUs)
                return offsetUs;
            return std;
        }

        private static bool StartsTheYear(TimeZoneInfo.TransitionTime t)
        {
            return t.Month == 1 && t.TimeOfDay.TimeOfDay == TimeSpan.Zero && !t.IsFixedDateRule && t.Week == 1;
        }

        private TimeZoneInfo.AdjustmentRule RuleAt(DateTime day)
        {
            foreach (TimeZoneInfo.AdjustmentRule r in _rules)
            {
                if (r.DateStart <= day && day <= r.DateEnd)
                    return r;
            }
            return null;
        }

        private long RuleStandardUs(TimeZoneInfo.AdjustmentRule r)
        {
            return (_zone.BaseUtcOffset.Ticks + (r == null ? 0 : BaseDelta(r).Ticks)) / 10;
        }

        private bool IsDaylightAt(DateTimeValue dt)
        {
            return DstUsAt(dt) != 0;
        }

        // tzdata's abbreviation (CET/CEST, EST/EDT, ...) where it has one, else the numeric form (+04).
        internal override string TzNameAt(DateTimeValue dt)
        {
            if (dt == null)
                return null;
            long off = OffsetUsForWall(dt.ToNaiveUsLong(), dt.Fold);
            return ZoneAbbrev.For(Key, IsDaylightAt(dt), off);
        }

        internal override long? DstUsAt(DateTimeValue dt)
        {
            if (dt == null)
                return null;
            long naive = dt.ToNaiveUsLong();
            long offset = OffsetUsForWall(naive, dt.Fold);
            return offset - StandardUsAt(naive, offset);
        }

        // ---- equality / hash / repr ----

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            ZoneInfoValue o = other as ZoneInfoValue;
            return o != null && Key == o.Key;
        }

        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            return NumericHash.HashString(Key, ctx.StrHashSeed);   // the engine's str hash: string.GetHashCode differs per process
        }

        protected internal override void StrLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append(Key);
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("zoneinfo.ZoneInfo(key=" + ctx.Values.Str(Key).Repr(ctx) + ")");
        }

        private static DateTimeValue DtArg(EvalContext ctx, ScriptValue[] a, string method)
        {
            Args.Exactly(ctx, a, method, 1);
            if (a[0].Kind == ValueKind.None)
                return null;
            DateTimeValue dt = a[0] as DateTimeValue;
            if (dt == null)
                throw Raise.TypeError(ctx, method + "(dt) argument must be a datetime instance or None");
            return dt;
        }

        private static ScriptTypeInfo BuildType()
        {
            var d = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal)
            {
                ["key"] = SlotDescriptor.MakeProperty("key", (self, c) => c.Values.Str(((ZoneInfoValue)self).Key)),
                ["utcoffset"] = SlotDescriptor.MakeMethod("utcoffset", Args.Positional("utcoffset", 1, 1, (self, a, kw, c) =>
                {
                    long? off = ((ZoneInfoValue)self).OffsetUsAt(DtArg(c, a, "utcoffset"));
                    return off == null ? (ScriptValue)c.Values.None : ((ZoneInfoValue)self).OffsetTd(c, off.Value);
                })),
                ["dst"] = SlotDescriptor.MakeMethod("dst", Args.Positional("dst", 1, 1, (self, a, kw, c) =>
                {
                    long? dst = ((ZoneInfoValue)self).DstUsAt(DtArg(c, a, "dst"));
                    return dst == null ? (ScriptValue)c.Values.None : ((ZoneInfoValue)self).OffsetTd(c, dst.Value);
                })),
                ["tzname"] = SlotDescriptor.MakeMethod("tzname", Args.Positional("tzname", 1, 1, (self, a, kw, c) =>
                {
                    string name = ((ZoneInfoValue)self).TzNameAt(DtArg(c, a, "tzname"));
                    return name == null ? (ScriptValue)c.Values.None : c.Values.Str(name);
                })),
                ["fromutc"] = SlotDescriptor.MakeMethod("fromutc", Args.Positional("fromutc", 1, 1, (self, a, kw, c) =>
                {
                    DateTimeValue dt = DtArg(c, a, "fromutc");
                    if (dt == null || !ReferenceEquals(dt.Tz, self))
                        throw Raise.ValueError(c, "fromutc: dt.tzinfo is not self");
                    long utc = dt.ToNaiveUsLong();
                    return PyOps.BinaryOp(PyBinOp.Add, dt, ((ZoneInfoValue)self).OffsetTd(c, ((ZoneInfoValue)self).OffsetUsForUtc(utc)), c);
                })),
            };
            return new ScriptTypeInfo("zoneinfo.ZoneInfo", d);
        }
    }
}

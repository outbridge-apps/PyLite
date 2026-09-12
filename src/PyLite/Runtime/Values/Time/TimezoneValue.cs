using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // datetime.timezone — a fixed-offset tzinfo. Immutable; equality/hash by offset only
    // (the name is ignored, CPython parity). The single permitted static is the immutable Utc singleton.
    internal sealed class TimezoneValue : TzInfoValue
    {
        private static readonly ScriptTypeInfo Type = BuildType();

        // INVARIANT: |OffsetUs| < 24h in microseconds (3.7+: sub-minute offsets allowed). Name == null =>
        // auto "UTC" / "UTC±HH:MM[:SS[.ffffff]]".
        public readonly long OffsetUs;
        public readonly string Name;
        public readonly bool IsUtcSingleton;

        // Immutable process-wide singleton (offset 0, auto name). Safe to share: no ctx-dependent state.
        public static readonly TimezoneValue Utc = new TimezoneValue(0, null, true);

        private TimezoneValue(long offsetUs, string name, bool isUtc)
        {
            OffsetUs = offsetUs;
            Name = name;
            IsUtcSingleton = isUtc;
        }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }

        // A fixed offset answers the same for every instant, and for time objects (dt == null) too.
        internal override long? OffsetUsAt(DateTimeValue dt) { return OffsetUs; }
        internal override long OffsetUsForUtc(long utcNaiveUs) { return OffsetUs; }
        internal override string TzNameAt(DateTimeValue dt) { return TzName(); }
        internal override long? DstUsAt(DateTimeValue dt) { return null; }

        // ---- factories ----

        // Trusted construction (offset already validated). name may be null (=> auto format).
        internal static TimezoneValue FromOffsetUs(EvalContext ctx, long offsetUs, string name)
        {
            ctx.Values.PreCharge(48);
            return new TimezoneValue(offsetUs, name, false);
        }

        // Script-level timezone(offset[, name]).
        internal static TimezoneValue Construct(EvalContext ctx, ScriptValue[] args, KwArgs kw)
        {
            ctx.Budget.Step();
            if (args.Length < 1 || args.Length > 2)
                throw Raise.TypeError(ctx, "timezone() takes from 1 to 2 positional arguments but " + args.Length + " were given");
            TimeDeltaValue offset = args[0] as TimeDeltaValue;
            if (offset == null)
                throw Raise.TypeError(ctx, "timezone() argument 'offset' must be a timedelta, not " + args[0].PyTypeName);

            string name = null;
            if (args.Length == 2)
            {
                StrValue s = args[1] as StrValue;
                if (s == null)
                    throw Raise.TypeError(ctx, "timezone() argument 'name' must be str, not " + args[1].PyTypeName);
                name = s.Value;
            }

            BigInteger us = offset.ToUs();
            if (us <= -86400000000L || us >= 86400000000L)
                throw Raise.ValueError(ctx, "offset must be a timedelta strictly between -timedelta(hours=24) and timedelta(hours=24).");
            if (us.IsZero && name == null)
                return Utc;   // timezone(timedelta(0)) is the utc singleton in CPython

            ctx.Values.PreCharge(48);
            return new TimezoneValue((long)us, name, false);
        }

        // ---- broken-down helpers ----

        // "±HH:MM[:SS[.ffffff]]" suffix for isoformat (time/datetime) and the auto tzname. Colon-separated
        // (strftime %z uses ±HHMM[SS[.ffffff]], no colons).
        internal static string IsoOffset(long offsetUs)
        {
            return FormatOffset(offsetUs, ":");
        }

        internal static string FormatOffset(long offsetUs, string sep)
        {
            char sign = offsetUs < 0 ? '-' : '+';
            long abs = Math.Abs(offsetUs);
            long secs = abs / 1000000L;
            long frac = abs % 1000000L;
            string s = sign + (secs / 3600).ToString("00", CultureInfo.InvariantCulture)
                + sep + (secs / 60 % 60).ToString("00", CultureInfo.InvariantCulture);
            if (secs % 60 != 0 || frac != 0)
                s += sep + (secs % 60).ToString("00", CultureInfo.InvariantCulture);
            if (frac != 0)
                s += "." + frac.ToString("000000", CultureInfo.InvariantCulture);
            return s;
        }

        public string TzName()
        {
            if (Name != null)
                return Name;
            if (OffsetUs == 0)
                return "UTC";
            return "UTC" + IsoOffset(OffsetUs);
        }

        internal TimeDeltaValue UtcOffsetTd(EvalContext ctx)
        {
            return TimeDeltaValue.FromUs(ctx, (BigInteger)OffsetUs);
        }

        // ---- equality / hash ----

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            TimezoneValue o = other as TimezoneValue;
            return o != null && OffsetUs == o.OffsetUs;
        }

        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            return NumericHash.HashBigInteger(new BigInteger(OffsetUs));
        }

        // ---- str / repr ----

        protected internal override void StrLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append(TzName());
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            if (IsUtcSingleton)
            {
                sb.Append("datetime.timezone.utc");
                return;
            }
            string tdRepr = UtcOffsetTd(ctx).Repr(ctx);
            if (Name == null)
                sb.Append("datetime.timezone(" + tdRepr + ")");
            else
                sb.Append("datetime.timezone(" + tdRepr + ", " + ctx.Values.Str(Name).Repr(ctx) + ")");
        }

        // fromutc(dt): dt must be an aware datetime whose tzinfo is self; returns dt + utcoffset.
        private ScriptValue FromUtc(EvalContext ctx, ScriptValue[] args)
        {
            Args.AtLeast(ctx, args, "fromutc", 1);
            DateTimeValue dt = args[0] as DateTimeValue;
            if (dt == null)
                throw Raise.TypeError(ctx, "fromutc: argument must be a datetime");
            if (!ReferenceEquals(dt.Tz, this))
                throw Raise.ValueError(ctx, "fromutc: dt.tzinfo is not self");
            return PyOps.BinaryOp(PyBinOp.Add, dt, UtcOffsetTd(ctx), ctx);
        }

        // ---- instance slots ----

        private static ScriptTypeInfo BuildType()
        {
            var d = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal)
            {
                ["utcoffset"] = SlotDescriptor.MakeMethod("utcoffset", (self, a, kw, c) => ((TimezoneValue)self).UtcOffsetTd(c)),
                ["tzname"] = SlotDescriptor.MakeMethod("tzname", (self, a, kw, c) => c.Values.Str(((TimezoneValue)self).TzName())),
                ["dst"] = SlotDescriptor.MakeMethod("dst", (self, a, kw, c) => c.Values.None),
                ["fromutc"] = SlotDescriptor.MakeMethod("fromutc", (self, a, kw, c) => ((TimezoneValue)self).FromUtc(c, a)),
            };
            return new ScriptTypeInfo("datetime.timezone", d);
        }
    }
}

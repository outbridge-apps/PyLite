using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // The datetime module. Assembled incrementally as the value types land; each class is a
    // TypeValue whose Constructor is the script-level call and whose StaticSlots carry classmethods/constants.
    internal static class DateTimeModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["MINYEAR"] = ctx.Values.Int(1),
                ["MAXYEAR"] = ctx.Values.Int(9999),
                ["timedelta"] = TimedeltaType(ctx),
                ["timezone"] = TimezoneType(ctx),
                ["date"] = DateType(ctx),
                ["time"] = TimeType(ctx),
                ["datetime"] = DatetimeType(ctx),
                ["tzinfo"] = TzInfoType(ctx),
            };
            return ctx.Values.Module("datetime", m);
        }

        // Abstract base for isinstance(timezone.utc, tzinfo); not instantiable in this dialect.
        private static TypeValue TzInfoType(EvalContext ctx)
        {
            BuiltinDelegate ctor = (self, args, kw, c) =>
            {
                throw Runtime.Errors.Raise.TypeError(c, "cannot instantiate 'datetime.tzinfo'");
            };
            return ctx.Values.Type("datetime.tzinfo", ctor, null, null);
        }

        private static TypeValue DatetimeType(EvalContext ctx)
        {
            var slots = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["today"] = BuiltinFunctionValue.Make("today", (self, args, kw, c) => DateTimeValue.Now(c, null)),
                ["now"] = BuiltinFunctionValue.Make("now", (self, args, kw, c) => DateTimeValue.Now(c, NowTz(c, args, kw))),
                ["utcnow"] = BuiltinFunctionValue.Make("utcnow", (self, args, kw, c) => DateTimeValue.UtcNow(c)),
                ["fromtimestamp"] = BuiltinFunctionValue.Make("fromtimestamp",
                    (self, args, kw, c) => DateTimeValue.FromTimestamp(c, Support.DtArgs.Timestamp(c, Arg1(c, args, "fromtimestamp")), TsTz(c, args, kw))),
                ["utcfromtimestamp"] = BuiltinFunctionValue.Make("utcfromtimestamp",
                    (self, args, kw, c) => DateTimeValue.UtcFromTimestamp(c, Support.DtArgs.Timestamp(c, Arg1(c, args, "utcfromtimestamp")))),
                ["fromordinal"] = BuiltinFunctionValue.Make("fromordinal",
                    (self, args, kw, c) => DateTimeValue.FromOrdinal(c, OrdinalArg(c, args))),
                ["combine"] = BuiltinFunctionValue.Make("combine",
                    (self, args, kw, c) => DateTimeValue.Combine(c, Arg1(c, args, "combine"), Arg2(c, args, "combine"))),
                ["strptime"] = BuiltinFunctionValue.Make("strptime",
                    (self, args, kw, c) => DateTimeValue.Strptime(c, StrArg(c, args, 0, "strptime"), StrArg(c, args, 1, "strptime"))),
                ["fromisoformat"] = BuiltinFunctionValue.Make("fromisoformat",
                    (self, args, kw, c) => Support.IsoFormatParser.DateTime(c, IsoArg(c, args, kw))),
                ["min"] = DateTimeValue.Construct(ctx, 1, 1, 1, 0, 0, 0, 0, null),
                ["max"] = DateTimeValue.Construct(ctx, 9999, 12, 31, 23, 59, 59, 999999, null),
                ["resolution"] = TimeDeltaValue.FromNormalized(ctx, 0, 0, 1),
            };
            BuiltinDelegate ctor = (self, args, kw, c) => DateTimeValue.ConstructFromArgs(c, args, kw);
            return ctx.Values.Type("datetime.datetime", ctor, null, slots);
        }

        // The optional tz argument for now()/fromtimestamp() (positional arg index 1 for fromtimestamp, 0 for now).
        private static TzInfoValue NowTz(EvalContext c, ScriptValue[] args, Outbridge.PyLite.Runtime.Evaluator.KwArgs kw)
        {
            ScriptValue tz = args.Length >= 1 ? args[0] : null;
            ScriptValue tzKw;
            if (kw.TryGet("tz", out tzKw))
                tz = tzKw;
            return tz != null ? Support.DtArgs.TzArg(c, tz) : null;
        }

        private static TzInfoValue TsTz(EvalContext c, ScriptValue[] args, Outbridge.PyLite.Runtime.Evaluator.KwArgs kw)
        {
            ScriptValue tz = args.Length >= 2 ? args[1] : null;
            ScriptValue tzKw;
            if (kw.TryGet("tz", out tzKw))
                tz = tzKw;
            return tz != null ? Support.DtArgs.TzArg(c, tz) : null;
        }

        private static ScriptValue Arg2(EvalContext c, ScriptValue[] args, string fn)
        {
            if (args.Length < 2)
                throw Runtime.Errors.Raise.TypeError(c, fn + "() missing required argument");
            return args[1];
        }

        // fromisoformat(date_string): one positional str, CPython's own wording for anything else.
        private static string IsoArg(EvalContext c, ScriptValue[] args, Outbridge.PyLite.Runtime.Evaluator.KwArgs kw)
        {
            Args.Exactly(c, args, kw, "fromisoformat", 1);
            StrValue s = args[0] as StrValue;
            if (s == null)
                throw Runtime.Errors.Raise.TypeError(c, "fromisoformat: argument must be str");
            return s.Value;
        }

        private static string StrArg(EvalContext c, ScriptValue[] args, int idx, string fn)
        {
            if (args.Length <= idx)
                throw Runtime.Errors.Raise.TypeError(c, fn + "() missing required argument");
            StrValue s = args[idx] as StrValue;
            if (s == null)
                throw Runtime.Errors.Raise.TypeError(c, "strptime() argument " + (idx + 1) + " must be str, not " + args[idx].PyTypeName);
            return s.Value;
        }

        private static TypeValue TimeType(EvalContext ctx)
        {
            var slots = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["fromisoformat"] = BuiltinFunctionValue.Make("fromisoformat",
                    (self, args, kw, c) => Support.IsoFormatParser.Time(c, IsoArg(c, args, kw))),
                ["min"] = TimeValue.Construct(ctx, 0, 0, 0, 0, null),
                ["max"] = TimeValue.Construct(ctx, 23, 59, 59, 999999, null),
                ["resolution"] = TimeDeltaValue.FromNormalized(ctx, 0, 0, 1),
            };
            BuiltinDelegate ctor = (self, args, kw, c) => TimeValue.ConstructFromArgs(c, args, kw);
            return ctx.Values.Type("datetime.time", ctor, null, slots);
        }

        private static TypeValue DateType(EvalContext ctx)
        {
            var slots = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["today"] = BuiltinFunctionValue.Make("today", (self, args, kw, c) => DateValue.Today(c)),
                ["fromtimestamp"] = BuiltinFunctionValue.Make("fromtimestamp",
                    (self, args, kw, c) => DateValue.FromTimestamp(c, Support.DtArgs.Timestamp(c, Arg1(c, args, "fromtimestamp")))),
                ["fromordinal"] = BuiltinFunctionValue.Make("fromordinal",
                    (self, args, kw, c) => DateValue.FromOrdinal(c, OrdinalArg(c, args))),
                ["fromisoformat"] = BuiltinFunctionValue.Make("fromisoformat",
                    (self, args, kw, c) => Support.IsoFormatParser.Date(c, IsoArg(c, args, kw))),
                ["min"] = DateValue.Construct(ctx, 1, 1, 1),
                ["max"] = DateValue.Construct(ctx, 9999, 12, 31),
                ["resolution"] = TimeDeltaValue.FromNormalized(ctx, 1, 0, 0),
            };
            BuiltinDelegate ctor = (self, args, kw, c) => DateValue.ConstructFromArgs(c, args, kw);
            return ctx.Values.Type("datetime.date", ctor, null, slots);
        }

        private static ScriptValue Arg1(EvalContext c, ScriptValue[] args, string fn)
        {
            if (args.Length < 1)
                throw Runtime.Errors.Raise.TypeError(c, fn + "() missing 1 required positional argument");
            return args[0];
        }

        private static System.Numerics.BigInteger OrdinalArg(EvalContext c, ScriptValue[] args)
        {
            ScriptValue v = Arg1(c, args, "fromordinal");
            IntValue iv = v as IntValue;
            if (iv != null)
                return iv.Value;
            BoolValue bv = v as BoolValue;
            if (bv != null)
                return bv.Value ? 1 : 0;
            throw Runtime.Errors.Raise.TypeError(c, "'" + v.PyTypeName + "' object cannot be interpreted as an integer");
        }

        private static TypeValue TimezoneType(EvalContext ctx)
        {
            var slots = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["utc"] = TimezoneValue.Utc,
                ["min"] = TimezoneValue.FromOffsetUs(ctx, -86340000000L, null),
                ["max"] = TimezoneValue.FromOffsetUs(ctx, 86340000000L, null),
            };
            BuiltinDelegate ctor = (self, args, kw, c) => TimezoneValue.Construct(c, args, kw);
            return ctx.Values.Type("datetime.timezone", ctor, null, slots);
        }

        private static TypeValue TimedeltaType(EvalContext ctx)
        {
            var slots = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["min"] = TimeDeltaValue.FromNormalized(ctx, -999999999, 0, 0),
                ["max"] = TimeDeltaValue.FromNormalized(ctx, 999999999, 86399, 999999),
                ["resolution"] = TimeDeltaValue.FromNormalized(ctx, 0, 0, 1),
            };
            BuiltinDelegate ctor = (self, args, kw, c) => TimeDeltaValue.Construct(c, args, kw);
            return ctx.Values.Type("datetime.timedelta", ctor, null, slots);
        }
    }
}

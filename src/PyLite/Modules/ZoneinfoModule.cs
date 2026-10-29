using System;
using System.Collections.Generic;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // zoneinfo: ZoneInfo(key) over the Windows time zone database through the CLDR IANA map (see
    // ZoneInfoValue). No tzdata files, so from_file/TZPATH are absent; ZoneInfoNotFoundError is a KeyError.
    internal static class ZoneinfoModule
    {
        // clear_cache(*, only_keys=None): the zone cache is shared by every run of the process, so a script may
        // not clear it; the call checks its arguments as CPython does and leaves the cache alone.
        private static ScriptValue ClearCache(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext c)
        {
            Args.None(c, a, "clear_cache");
            var r = new KwReader(c, kw, "clear_cache");
            r.GetOrNull("only_keys");
            r.RejectInvalid();
            return c.Values.None;
        }

        public static ModuleValue Create(EvalContext ctx)
        {
            var slots = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["no_cache"] = BuiltinFunctionValue.Make("no_cache", (s, a, kw, c) => ZoneInfoValue.Get(c, KeyArg(c, a, kw, "no_cache"), false)),
                ["clear_cache"] = BuiltinFunctionValue.Make("clear_cache", ClearCache),
            };
            BuiltinDelegate ctor = (s, a, kw, c) => ZoneInfoValue.Get(c, KeyArg(c, a, kw, "ZoneInfo"), true);
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["ZoneInfo"] = ctx.Values.Type("zoneinfo.ZoneInfo", ctor, null, slots),
                ["available_timezones"] = BuiltinFunctionValue.Make("available_timezones", AvailableTimezones),
                ["ZoneInfoNotFoundError"] = ModuleKit.ExceptionClass(PyExceptionTypes.ZoneInfoNotFoundError),
                ["TZPATH"] = ctx.Values.Tuple(new ScriptValue[0]),
            };
            return ctx.Values.Module("zoneinfo", m);
        }

        private static string KeyArg(EvalContext ctx, ScriptValue[] a, KwArgs kw, string fn)
        {
            ScriptValue v = a.Length >= 1 ? a[0] : null;
            ScriptValue kv;
            if (kw.TryGet("key", out kv))
                v = kv;
            if (v == null || a.Length > 1)
                throw Args.ExactlyError(ctx, fn, 1, a.Length);
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, "ZoneInfo keys must be strings, not " + v.PyTypeName);
            return s.Value;
        }

        private static ScriptValue AvailableTimezones(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            if (a.Length != 0 || kw.Count != 0)
                throw Raise.TypeError(ctx, "available_timezones() takes no arguments");
            List<string> keys = ZoneInfoValue.Available();
            SetValue set = ctx.Values.Set(keys.Count);
            foreach (string k in keys)
                set.AddItem(ctx.Values.Str(k), ctx);
            return set;
        }
    }
}

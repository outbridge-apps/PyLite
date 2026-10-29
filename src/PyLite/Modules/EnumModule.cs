using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // enum: Enum, IntEnum, StrEnum, auto and unique. A class statement with one of them (or an enum without
    // members) as its base builds an EnumClassValue; Enum('Name', names) is the functional form. No Flag.
    internal static class EnumModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            var plain = new EnumClassValue("Enum", EnumFlavor.Plain, null, null, ctx);
            m["Enum"] = plain;
            m["IntEnum"] = new EnumClassValue("IntEnum", EnumFlavor.Int, plain, null, ctx);
            m["StrEnum"] = new EnumClassValue("StrEnum", EnumFlavor.Str, plain, null, ctx);
            m["auto"] = BuiltinFunctionValue.Make("auto", (self, a, kw, c) =>
            {
                if (a.Length > 0 || kw.Count > 0)
                    throw Raise.TypeError(c, "auto() takes no arguments in this dialect");
                c.Values.PreCharge(24);
                return new AutoValue();
            });
            m["unique"] = BuiltinFunctionValue.Make("unique", Unique);
            return ctx.Values.Module("enum", m);
        }

        // @unique: CPython's ValueError naming every alias and the member it repeats.
        private static ScriptValue Unique(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext c)
        {
            EnumClassValue cls = a.Length == 1 && kw.Count == 0 ? a[0] as EnumClassValue : null;
            if (cls == null)
                throw Raise.TypeError(c, "unique() takes one enumeration class");
            var dups = new List<string>();
            foreach (string n in cls.NameOrder)
            {
                EnumMemberValue member = cls.ByName[n];
                if (member.Name != n)
                    dups.Add(n + " -> " + member.Name);
            }
            if (dups.Count > 0)
                throw Raise.ValueError(c, "duplicate values found in <enum '" + cls.Name + "'>: " + string.Join(", ", dups));
            return cls;
        }
    }
}

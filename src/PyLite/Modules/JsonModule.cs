using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // The json module. loads/dumps over PyJson (Newtonsoft reader + our writer): the formatting
    // kwargs, dumps(default=), loads(parse_float=, parse_int=, strict=). cls, object_hook,
    // object_pairs_hook and parse_constant are rejected when passed non-None.
    internal static class JsonModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["loads"] = BuiltinFunctionValue.Make("loads", (s, a, kw, c) => Loads(c, a, kw)),
                ["dumps"] = BuiltinFunctionValue.Make("dumps", (s, a, kw, c) => Dumps(c, a, kw)),
                ["load"] = BuiltinFunctionValue.Make("load", (s, a, kw, c) => Load(c, a, kw)),
                ["dump"] = BuiltinFunctionValue.Make("dump", (s, a, kw, c) => DumpTo(c, a, kw)),
                ["JSONDecodeError"] = DecodeErrorType(),
            };
            return ctx.Values.Module("json", m);
        }

        // JSONDecodeError(msg, doc, pos), with CPython's derived message and the five attributes.
        private static TypeValue DecodeErrorType()
        {
            BuiltinDelegate ctor = (self, args, kw, c) =>
            {
                if (args.Length != 3 || kw.Count != 0)
                    throw Raise.TypeError(c, "JSONDecodeError() takes exactly 3 positional arguments: msg, doc, pos");
                StrValue msg = args[0] as StrValue;
                StrValue doc = args[1] as StrValue;
                IntValue pos = args[2] as IntValue;
                if (msg == null || doc == null || pos == null || !pos.IsSmall)
                    throw Raise.TypeError(c, "JSONDecodeError() expects (str, str, int)");
                return JsonScan.Error(c, doc.Value, doc, msg.Value, (int)pos.Small).Value;
            };
            return TypeValue.Make("JSONDecodeError", ctor, null, null, PyExceptionTypes.JSONDecodeError);
        }

        private static ScriptValue Loads(EvalContext c, ScriptValue[] a, KwArgs kw)
        {
            if (a.Length < 1)
                throw Raise.TypeError(c, "loads() missing 1 required positional argument: 's'");
            StrValue s = a[0] as StrValue;
            if (s == null)
                throw Raise.TypeError(c, "the JSON object must be str, not '" + a[0].PyTypeName + "'");
            PyJson.JsonLoadHooks hooks;
            bool strict = ReadLoadKw(c, kw, out hooks);
            return PyJson.Parse(c, s.Value, strict, hooks, s);
        }

        // The decode keywords shared by loads() and load(). parse_float / parse_int take any callable,
        // handed the number's text as the document spells it, and the builtin they default to is a no-op.
        // object_pairs_hook and cls are the two that are not here.
        private static bool ReadLoadKw(EvalContext c, KwArgs kw, out PyJson.JsonLoadHooks hooks)
        {
            hooks = new PyJson.JsonLoadHooks();
            bool strict = true;
            for (int j = 0; j < kw.Count; j++)
            {
                string name = kw.NameAt(j);
                ScriptValue val = kw.ValueAt(j);
                if (name == "strict")
                {
                    strict = val.IsTruthy(c);   // CPython's own knob: False allows control characters in strings
                    continue;
                }
                if (val.Kind == ValueKind.None)
                    continue;
                TypeValue tv = val as TypeValue;
                switch (name)
                {
                    case "parse_float": hooks.ParseFloat = tv != null && tv.Name == "float" ? null : val; continue;
                    case "parse_int": hooks.ParseInt = tv != null && tv.Name == "int" ? null : val; continue;
                    case "parse_constant": hooks.ParseConstant = val; continue;
                    case "object_hook": hooks.ObjectHook = val; continue;
                }
                throw Raise.TypeError(c, "'" + name + "' argument is not supported in this dialect");
            }
            return strict;
        }

        // load(fp, **kw) / dump(obj, fp, **kw). There are no files here, so `fp` is any object with read()
        // or write() — io.StringIO is the one the engine ships. dump writes the document in one call
        // rather than in chunks: the text exists in full either way, and a sink that only sees one write
        // is the friendlier contract for a host-supplied object.
        private static ScriptValue Load(EvalContext c, ScriptValue[] a, KwArgs kw)
        {
            Args.Exactly(c, a, "load", 1);
            ScriptValue read = PyOps.GetAttr(a[0], "read", c);
            ScriptValue text = c.CallHook(read, System.Array.Empty<ScriptValue>(), KwArgs.Empty);
            StrValue s = text as StrValue;
            if (s == null)
            {
                BytesValue b = text as BytesValue;   // a UTF-8 document, the only encoding the sandbox decodes
                if (b == null)
                    throw Raise.TypeError(c, "load(): read() must return str or bytes, not " + text.PyTypeName);
                s = c.Values.Str(new System.Text.UTF8Encoding(false, true).GetString(b.Data));
            }
            PyJson.JsonLoadHooks hooks;
            bool strict = ReadLoadKw(c, kw, out hooks);
            return PyJson.Parse(c, s.Value, strict, hooks, s);
        }

        private static ScriptValue DumpTo(EvalContext c, ScriptValue[] a, KwArgs kw)
        {
            Args.Exactly(c, a, "dump", 2);
            ScriptValue text = Dumps(c, new[] { a[0] }, kw);
            ScriptValue write = PyOps.GetAttr(a[1], "write", c);
            c.CallHook(write, new[] { text }, KwArgs.Empty);
            return c.Values.None;
        }

        private static ScriptValue Dumps(EvalContext c, ScriptValue[] a, KwArgs kw)
        {
            if (a.Length < 1)
                throw Raise.TypeError(c, "dumps() missing 1 required positional argument: 'obj'");

            bool sortKeys = false, ensureAscii = true, allowNan = true, skipKeys = false;
            ScriptValue indent = null, separators = null, defaultHook = null;

            for (int j = 0; j < kw.Count; j++)
            {
                string name = kw.NameAt(j);
                ScriptValue val = kw.ValueAt(j);
                switch (name)
                {
                    case "sort_keys": sortKeys = val.IsTruthy(c); break;
                    case "ensure_ascii": ensureAscii = val.IsTruthy(c); break;
                    case "allow_nan": allowNan = val.IsTruthy(c); break;
                    case "check_circular": break;   // accepted, ignored — detection is always on
                    case "indent": indent = val; break;
                    case "separators": separators = val; break;
                    case "skipkeys": skipKeys = val.IsTruthy(c); break;
                    case "default":
                        if (val.Kind == ValueKind.None)
                            break;
                        if (!val.IsCallable)
                            throw Raise.TypeError(c, "'" + val.PyTypeName + "' object is not callable");
                        defaultHook = val;
                        break;
                    case "cls":
                        if (val.Kind != ValueKind.None)
                            throw Raise.TypeError(c, "'" + name + "' argument is not supported in this dialect");
                        break;
                    default:
                        throw KwReader.InvalidError(c, "dumps", name);
                }
            }

            string indentUnit = ResolveIndent(c, indent);
            string itemSep, keySep;
            ResolveSeparators(c, separators, indentUnit, out itemSep, out keySep);

            var opts = new JsonDumpOptions(sortKeys, ensureAscii, allowNan, itemSep, keySep, indentUnit, defaultHook, skipKeys);
            return c.Values.Str(PyJson.Dump(c, a[0], opts));
        }

        private static string ResolveIndent(EvalContext c, ScriptValue indent)
        {
            if (indent == null || indent.Kind == ValueKind.None)
                return null;
            if (indent.Kind == ValueKind.Int || indent.Kind == ValueKind.Bool)
            {
                BigInteger n = NumericOps.AsBigInteger(indent);
                int count = n <= 0 ? 0 : (n > 1024 ? 1024 : (int)n);   // indent=0/negative -> newlines, no spaces
                return new string(' ', count);
            }
            StrValue s = indent as StrValue;
            if (s != null)
                return s.Value;
            throw Raise.TypeError(c, "indent must be int or str, not " + indent.PyTypeName);
        }

        private static void ResolveSeparators(EvalContext c, ScriptValue separators, string indentUnit,
            out string itemSep, out string keySep)
        {
            if (separators != null && separators.Kind != ValueKind.None)
            {
                ScriptValue[] pair = AsPair(c, separators);
                itemSep = AsSepStr(c, pair[0]);
                keySep = AsSepStr(c, pair[1]);
                return;
            }
            // CPython defaults: (', ', ': ') without indent; (',', ': ') with indent.
            itemSep = indentUnit != null ? "," : ", ";
            keySep = ": ";
        }

        private static ScriptValue[] AsPair(EvalContext c, ScriptValue v)
        {
            TupleValue t = v as TupleValue;
            if (t != null && t.Items.Length == 2)
                return t.Items;
            ListValue l = v as ListValue;
            if (l != null && l.Items.Count == 2)
                return new[] { l.Items[0], l.Items[1] };
            throw Raise.TypeError(c, "separators must be a (item, key) tuple of two strings");
        }

        private static string AsSepStr(EvalContext c, ScriptValue v)
        {
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(c, "separators must be strings");
            return s.Value;
        }
    }
}

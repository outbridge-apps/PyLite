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
    // kwargs, dumps(default=), loads(parse_float=, parse_int=, parse_constant=, object_hook=, strict=).
    // cls and object_pairs_hook are rejected when passed non-None.
    internal static class JsonModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["loads"] = BuiltinFunctionValue.Make("loads", (s, a, kw, c) => Loads(c, Lead(c, a, ref kw, "loads", "s"), kw)),
                ["dumps"] = BuiltinFunctionValue.Make("dumps", (s, a, kw, c) => Dumps(c, Lead(c, a, ref kw, "dumps", "obj"), kw)),
                ["load"] = BuiltinFunctionValue.Make("load", (s, a, kw, c) => Load(c, Lead(c, a, ref kw, "load", "fp"), kw)),
                ["dump"] = BuiltinFunctionValue.Make("dump", (s, a, kw, c) => DumpTo(c, Lead(c, a, ref kw, "dump", "obj", "fp"), kw)),
                ["JSONDecodeError"] = ModuleKit.ExceptionClass(PyExceptionTypes.JSONDecodeError),
            };
            return ctx.Values.Module("json", m);
        }

        // JSONDecodeError(msg, doc, pos), with CPython's derived message and the five attributes. The builtins
        // table makes the class with this constructor (JSONDecodeError is in its exception tree), so the
        // class json exports is that same object.
        internal static readonly BuiltinDelegate DecodeErrorCtor = (self, args, kw, c) =>
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

        // The leading parameters by name (loads(s=...), dump(obj, fp=...)) moved into their positions; the
        // other keywords stay for the function's own reader.
        private static ScriptValue[] Lead(EvalContext c, ScriptValue[] a, ref KwArgs kw, string fn, params string[] names)
        {
            if (kw.Count == 0)
                return Required(c, a, fn, names);
            ScriptValue[] lead = null;
            var restNames = new List<string>(kw.Count);
            var restValues = new List<ScriptValue>(kw.Count);
            for (int i = 0; i < kw.Count; i++)
            {
                int at = Array.IndexOf(names, kw.NameAt(i));
                if (at < 0)
                {
                    restNames.Add(kw.NameAt(i));
                    restValues.Add(kw.ValueAt(i));
                    continue;
                }
                if (at < a.Length)
                    throw Raise.TypeError(c, fn + "() got multiple values for argument '" + names[at] + "'");
                if (lead == null)
                {
                    lead = new ScriptValue[names.Length];
                    Array.Copy(a, lead, a.Length);
                }
                lead[at] = kw.ValueAt(i);
            }
            if (lead == null)
                return Required(c, a, fn, names);
            int n = 0;
            while (n < lead.Length && lead[n] != null)
                n++;
            for (int i = n; i < lead.Length; i++)
            {
                if (lead[i] != null)
                    throw Raise.TypeError(c, fn + "() missing 1 required positional argument: '" + names[n] + "'");
            }
            kw = KwArgs.FromArrays(restNames.ToArray(), restValues.ToArray());
            var result = new ScriptValue[n];
            Array.Copy(lead, result, n);
            return Required(c, result, fn, names);
        }

        // Every lead parameter is required: CPython's "missing N required positional argument(s): 'a' and 'b'".
        private static ScriptValue[] Required(EvalContext c, ScriptValue[] a, string fn, string[] names)
        {
            if (a.Length >= names.Length)
                return a;
            int missing = names.Length - a.Length;
            var quoted = new List<string>(missing);
            for (int i = a.Length; i < names.Length; i++)
                quoted.Add("'" + names[i] + "'");
            string list = missing == 1 ? quoted[0] : string.Join(", ", quoted.GetRange(0, missing - 1)) + " and " + quoted[missing - 1];
            throw Raise.TypeError(c, fn + "() missing " + missing + " required positional argument" + (missing == 1 ? "" : "s") + ": " + list);
        }

        private static ScriptValue Loads(EvalContext c, ScriptValue[] a, KwArgs kw)
        {
            if (a.Length < 1)
                throw Raise.TypeError(c, "loads() missing 1 required positional argument: 's'");
            if (a.Length > 1)
                throw Raise.TypeError(c, "loads() takes 1 positional argument but " + a.Length + " were given");   // the rest are keyword-only
            StrValue s = Document(c, a[0]);
            PyJson.JsonLoadHooks hooks;
            bool strict = ReadLoadKw(c, kw, out hooks);
            return PyJson.Parse(c, s.Value, strict, hooks, s);
        }

        // What loads() reads: a str (a leading BOM refused, as CPython's), or bytes in the encoding
        // json.detect_encoding picks from the BOM or the zero bytes of the first four.
        private static StrValue Document(EvalContext c, ScriptValue v)
        {
            StrValue s = v as StrValue;
            if (s != null)
            {
                if (s.Value.Length > 0 && s.Value[0] == '\uFEFF')
                    throw JsonScan.Error(c, s.Value, s, "Unexpected UTF-8 BOM (decode using utf-8-sig)", 0);
                return s;
            }
            BytesValue b = v as BytesValue;
            if (b == null)
                throw Raise.TypeError(c, "the JSON object must be str, bytes or bytearray, not " + v.PyTypeName);
            string enc = BytesCodec.Normalize(c, c.Values.Str(DetectEncoding(b.Data)));
            return c.Values.Str(BytesCodec.Decode(c, b.Data, enc, "strict"));
        }

        private static string DetectEncoding(byte[] b)
        {
            if (b.Length >= 4 && ((b[0] == 0 && b[1] == 0 && b[2] == 0xFE && b[3] == 0xFF)
                || (b[0] == 0xFF && b[1] == 0xFE && b[2] == 0 && b[3] == 0)))
                return "utf-32";
            if (b.Length >= 2 && ((b[0] == 0xFE && b[1] == 0xFF) || (b[0] == 0xFF && b[1] == 0xFE)))
                return "utf-16";
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
                return "utf-8-sig";
            if (b.Length >= 4)
            {
                if (b[0] == 0)
                    return b[1] != 0 ? "utf-16-be" : "utf-32-be";
                if (b[1] == 0)
                    return b[2] != 0 || b[3] != 0 ? "utf-16-le" : "utf-32-le";
            }
            else if (b.Length == 2)
            {
                if (b[0] == 0)
                    return "utf-16-be";
                if (b[1] == 0)
                    return "utf-16-le";
            }
            return "utf-8";
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
                    case "parse_float": hooks.ParseFloat = tv != null && !tv.IsNamedByScript && tv.Name == "float" ? null : val; continue;
                    case "parse_int": hooks.ParseInt = tv != null && !tv.IsNamedByScript && tv.Name == "int" ? null : val; continue;
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
            StrValue s = Document(c, c.CallHook(read, System.Array.Empty<ScriptValue>(), KwArgs.Empty));
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
            if (a.Length > 1)
                throw Raise.TypeError(c, "dumps() takes 1 positional argument but " + a.Length + " were given");

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

using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // collections.namedtuple: a C# factory (no Python source generation). verbose is
    // accepted-and-ignored; _source -> "" (a documented divergence). The returned type is a TypeValue.
    public static partial class CollectionsModule
    {
        internal static ScriptValue NamedTuple(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (args.Length < 2)
                throw Raise.TypeError(ctx, "namedtuple() missing required arguments 'typename' and 'field_names'");
            string typename = AsFieldString(ctx, args[0]);
            List<string> fields = ParseFieldNames(ctx, args[1]);
            bool rename = BoolKw(ctx, kw, "rename");

            if (!IsPyIdentifier(typename))
                throw Raise.ValueError(ctx, "Type names and field names must be valid identifiers: " + Repr(ctx, typename));
            if (IsKeyword(typename))
                throw Raise.ValueError(ctx, "Type names and field names cannot be a keyword: " + Repr(ctx, typename));

            if (rename)
                ApplyRename(fields);
            else
                ValidateFields(ctx, fields);

            string[] fieldArr = fields.ToArray();
            ScriptTypeInfo instanceType = NamedTupleValue.BuildInstanceType(typename, fieldArr);

            // defaults=: an iterable applied to the RIGHTMOST fields (3.7+), as CPython does
            var defaults = new ScriptValue[fieldArr.Length];
            DictValue fieldDefaults = ctx.Values.Dict(4);
            ScriptValue defaultsArg;
            if (kw.TryGet("defaults", out defaultsArg) && defaultsArg.Kind != ValueKind.None)
            {
                var given = new List<ScriptValue>();
                IScriptIterator it = PyOps.GetIterator(defaultsArg, ctx);
                ScriptValue d;
                while (it.MoveNext(ctx, out d))
                    given.Add(d);
                if (given.Count > fieldArr.Length)
                    throw Raise.TypeError(ctx, "Got more default values than field names");
                int first = fieldArr.Length - given.Count;
                for (int i = 0; i < given.Count; i++)
                {
                    defaults[first + i] = given[i];
                    fieldDefaults.SetItem(ctx.Values.Str(fieldArr[first + i]), given[i], ctx);
                }
            }
            NamedTupleInfo info = new NamedTupleInfo(typename, fieldArr, instanceType, defaults);

            var statics = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["_fields"] = NamedTupleValue.FieldsTuple(info, ctx),
                ["_field_defaults"] = fieldDefaults,
                ["_source"] = ctx.Values.Str(""),
                ["_make"] = BuiltinFunctionValue.Make("_make", (s, a, k, c) =>
                {
                    Args.Exactly(c, a, "_make", 1);
                    return NamedTupleValue.Make(info, a[0], c);
                }),
            };
            return ctx.Values.Type(typename, (s, a, k, c) => NamedTupleValue.Construct(info, a, k, c), instanceType, statics);
        }

        private static List<string> ParseFieldNames(EvalContext ctx, ScriptValue arg)
        {
            var result = new List<string>();
            if (arg.Kind == ValueKind.Str)
            {
                string s = ((StrValue)arg).Value.Replace(',', ' ');
                foreach (string part in s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                    result.Add(part);
            }
            else
            {
                IScriptIterator it = PyOps.GetIterator(arg, ctx);
                ScriptValue v;
                while (it.MoveNext(ctx, out v))
                    result.Add(v.Kind == ValueKind.Str ? ((StrValue)v).Value : v.Str(ctx));
            }
            return result;
        }

        private static void ValidateFields(EvalContext ctx, List<string> fields)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in fields)
            {
                if (!IsPyIdentifier(name))
                    throw Raise.ValueError(ctx, "Type names and field names must be valid identifiers: " + Repr(ctx, name));
                if (IsKeyword(name))
                    throw Raise.ValueError(ctx, "Type names and field names cannot be a keyword: " + Repr(ctx, name));
                if (name[0] == '_')
                    throw Raise.ValueError(ctx, "Field names cannot start with an underscore: " + Repr(ctx, name));
                if (seen.Contains(name))
                    throw Raise.ValueError(ctx, "Encountered duplicate field name: " + Repr(ctx, name));
                seen.Add(name);
            }
        }

        private static void ApplyRename(List<string> fields)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < fields.Count; i++)
            {
                string name = fields[i];
                if (!IsPyIdentifier(name) || IsKeyword(name) || name.Length == 0 || name[0] == '_' || seen.Contains(name))
                    fields[i] = "_" + i;
                seen.Add(name);   // the ORIGINAL name (CPython parity)
            }
        }

        private static string AsFieldString(EvalContext ctx, ScriptValue v)
        {
            return v.Kind == ValueKind.Str ? ((StrValue)v).Value : v.Str(ctx);
        }

        private static bool BoolKw(EvalContext ctx, KwArgs kw, string name)
        {
            ScriptValue v;
            return kw.TryGet(name, out v) && v.IsTruthy(ctx);
        }

        private static string Repr(EvalContext ctx, string s)
        {
            return ctx.Values.Str(s).Repr(ctx);
        }

        private static bool IsKeyword(string s)
        {
            TokenKind kind;
            return Keywords.TryGet(s, out kind);
        }

        // ASCII isidentifier (the tested field/type names are ASCII); no keyword check here.
        private static bool IsPyIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            char c0 = s[0];
            if (!(c0 == '_' || (c0 >= 'A' && c0 <= 'Z') || (c0 >= 'a' && c0 <= 'z')))
                return false;
            for (int i = 1; i < s.Length; i++)
            {
                char c = s[i];
                if (!(c == '_' || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')))
                    return false;
            }
            return true;
        }
    }
}

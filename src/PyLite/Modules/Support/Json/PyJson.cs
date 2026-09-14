using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using Newtonsoft.Json;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;

namespace Outbridge.PyLite.Modules.Support
{
    // JSON over ScriptValue. HYBRID: loads uses Newtonsoft's streaming JsonTextReader — NOT
    // JToken/DeserializeObject (which recurse and would SOE on deep input) — and builds the value graph with an
    // explicit Stack (SOE-safe); dumps is our own iterative writer so floats go through FloatRepr and
    // separators/escaping match Python exactly. Every token/node charges Budget. Shared by the json module and
    // hosting marshaling.
    internal static class PyJson
    {
        // ---- loads ----

        // strict: CPython's own json.loads parameter — False lets a raw control character stand inside a
        // string. Everything else the grammar refuses is refused whatever it is set to.
        // parseFloat / parseInt: CPython's hooks, called with the number's text as the document spells
        // it (null for the default conversion). doc: the document as the script's own str, if the
        // caller has it, so an error can carry it without a copy.
        // The decode hooks, as one record so the parser's signature stays readable.
        internal sealed class JsonLoadHooks
        {
            public ScriptValue ParseFloat;      // called with the number's text as the document spells it
            public ScriptValue ParseInt;
            public ScriptValue ParseConstant;   // called with 'NaN', 'Infinity' or '-Infinity'
            public ScriptValue ObjectHook;      // called with each finished dict; its answer takes its place
        }

        public static ScriptValue Parse(EvalContext ctx, string text, bool strict = true,
            ScriptValue parseFloat = null, ScriptValue parseInt = null, StrValue doc = null)
        {
            return Parse(ctx, text, strict, new JsonLoadHooks { ParseFloat = parseFloat, ParseInt = parseInt }, doc);
        }

        internal static ScriptValue Parse(EvalContext ctx, string text, bool strict, JsonLoadHooks hooks, StrValue doc)
        {
            text = text ?? "";
            // The reader is lenient by design and cannot be told otherwise, so the grammar is checked
            // against the raw text alongside the token stream; any doubt goes to JsonScan for the verdict.
            var guard = new JsonGuard(ctx, text, doc, strict, ctx.Limits.MaxDataDepth);
            using (var sr = new StringReader(text))
            using (var reader = new JsonTextReader(sr))
            {
                reader.DateParseHandling = DateParseHandling.None;      // "2020-01-01" stays a str, not DateTime
                reader.FloatParseHandling = FloatParseHandling.Double;
                reader.MaxDepth = ctx.Limits.MaxDataDepth;              // second guard beside our stack
                try
                {
                    return ParseCore(ctx, reader, guard, text, hooks ?? new JsonLoadHooks());
                }
                catch (JsonReaderException ex)
                {
                    // the token the reader gave up on starts where the guard's bookkeeping says the next
                    // one does; the reader's own position is past it
                    int at = guard.NextStart;
                    guard.Verdict();   // CPython's message when the text is at fault ...
                    // ... and the reader's own limit otherwise (a number beyond double, a 400-digit int)
                    throw guard.Fail("Expecting value", at >= 0 ? at : Offset(text, ex.LineNumber, ex.LinePosition));
                }
            }
        }

        // The reader's (line, column) as an offset, for when the guard has lost its place: the column
        // counts from the line start and sits past the character it stopped on.
        private static int Offset(string text, int line, int column)
        {
            int pos = 0;
            for (int l = 1; l < line && pos < text.Length; pos++)
            {
                if (text[pos] == '\n')
                    l++;
            }
            pos += column - 1;
            return pos < 0 ? 0 : pos > text.Length ? text.Length : pos;
        }

        private sealed class Frame
        {
            public ScriptValue Container;   // ListValue or DictValue
            public bool IsObject;
            public string PendingKey;       // the last PropertyName, awaiting its value
            public string OwnKey;           // the key THIS container goes under, when its parent is an object
        }

        private static ScriptValue ParseCore(EvalContext ctx, JsonTextReader reader, JsonGuard guard, string text,
            JsonLoadHooks hooks)
        {
            var stack = new Stack<Frame>();
            ScriptValue root = null;
            bool haveRoot = false;

            while (reader.Read())
            {
                ctx.Budget.Step();
                guard.Token(reader);
                switch (reader.TokenType)
                {
                    // A container joins its parent when it CLOSES, not when it opens: object_hook may
                    // replace it with something else entirely, and the parent must receive that answer.
                    case JsonToken.StartObject:
                        stack.Push(new Frame { Container = ctx.Values.Dict(8), IsObject = true, OwnKey = TakeKey(stack) });
                        break;
                    case JsonToken.StartArray:
                        stack.Push(new Frame { Container = ctx.Values.List(8), IsObject = false, OwnKey = TakeKey(stack) });
                        break;
                    case JsonToken.EndObject:
                    case JsonToken.EndArray:
                        Frame done = stack.Pop();
                        ScriptValue built = done.Container;
                        if (done.IsObject && hooks.ObjectHook != null)
                            built = ctx.CallHook(hooks.ObjectHook, new[] { built }, KwArgs.Empty);
                        Store(ctx, guard, stack, built, done.OwnKey, ref root, ref haveRoot);
                        break;
                    case JsonToken.PropertyName:
                        stack.Peek().PendingKey = (string)reader.Value;
                        break;
                    default:
                        Store(ctx, guard, stack, Scalar(ctx, reader, guard, text, hooks), TakeKey(stack),
                            ref root, ref haveRoot);
                        break;
                }
            }

            // Newtonsoft returns false at EOF on an unterminated container (e.g. "[1,2,", "{", "{\"a\":1")
            // instead of throwing; without this a truncated document would silently yield a partial value.
            // The scan names the exact spot; the texts below stand only for a document it let through.
            if (!haveRoot || stack.Count != 0)
                guard.Verdict();
            if (!haveRoot)
                throw guard.Fail("Expecting value", 0);
            if (stack.Count != 0)
                throw guard.Fail(stack.Peek().IsObject ? "Expecting property name enclosed in double quotes or '}'"
                                                       : "Expecting value or ']' delimiter", text.Length);
            return root;
        }

        // The key the next value goes under, consumed from the enclosing object frame (null inside an array
        // or at the top level).
        private static string TakeKey(Stack<Frame> stack)
        {
            if (stack.Count == 0)
                return null;
            Frame f = stack.Peek();
            if (!f.IsObject)
                return null;
            string k = f.PendingKey;
            f.PendingKey = null;
            return k;
        }

        private static void Store(EvalContext ctx, JsonGuard guard, Stack<Frame> stack, ScriptValue v, string key,
            ref ScriptValue root, ref bool haveRoot)
        {
            if (stack.Count == 0)
            {
                if (haveRoot)
                {
                    guard.Verdict();
                    throw guard.Fail("Extra data", 0);
                }
                root = v;
                haveRoot = true;
                return;
            }
            Frame f = stack.Peek();
            if (f.IsObject)
                ((DictValue)f.Container).SetItem(ctx.Values.Str(key), v, ctx);   // duplicate key -> last wins
            else
                ((ListValue)f.Container).Add(v, ctx);
        }

        private static ScriptValue Scalar(EvalContext ctx, JsonTextReader reader, JsonGuard guard, string text,
            JsonLoadHooks hooks)
        {
            switch (reader.TokenType)
            {
                case JsonToken.String:
                    return ctx.Values.Str((string)reader.Value);
                case JsonToken.Boolean:
                    return ctx.Values.Bool((bool)reader.Value);
                case JsonToken.Null:
                    return ctx.Values.None;
                case JsonToken.Integer:
                    object iv = reader.Value;
                    if (hooks.ParseInt != null)
                    {
                        return Hook(ctx, hooks.ParseInt, guard, text, iv is BigInteger b
                            ? b.ToString(CultureInfo.InvariantCulture)
                            : Convert.ToInt64(iv, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                    }
                    return iv is BigInteger big
                        ? ctx.Values.Int(big)
                        : ctx.Values.Int(Convert.ToInt64(iv, CultureInfo.InvariantCulture));
                case JsonToken.Float:
                    double d = Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
                    // NaN and the infinities are parse_constant's in CPython, never parse_float's. They can
                    // only have come from the three literals: the reader refuses a finite one that overflows.
                    if (double.IsNaN(d) || double.IsInfinity(d))
                    {
                        if (hooks.ParseConstant == null)
                            return ctx.Values.Float(d);
                        string name = double.IsNaN(d) ? "NaN" : double.IsPositiveInfinity(d) ? "Infinity" : "-Infinity";
                        return ctx.CallHook(hooks.ParseConstant, new ScriptValue[] { ctx.Values.Str(name) }, KwArgs.Empty);
                    }
                    if (hooks.ParseFloat != null)
                        return Hook(ctx, hooks.ParseFloat, guard, text, FloatRepr.Repr(d));
                    return ctx.Values.Float(d);
                default:
                    guard.Verdict();
                    throw guard.Fail("Expecting value", 0);
            }
        }

        // The hook takes the number as the document spells it: the guard knows the span. Should the guard
        // have handed the document to the scan and switched off, the shortest text that reads back to the
        // same value stands in — the value is the same, only a trailing zero could be missing.
        private static ScriptValue Hook(EvalContext ctx, ScriptValue hook, JsonGuard guard, string text, string fallback)
        {
            string raw = guard.On ? text.Substring(guard.NumberStart, guard.NumberEnd - guard.NumberStart) : fallback;
            return ctx.CallHook(hook, new ScriptValue[] { ctx.Values.Str(raw) }, KwArgs.Empty);
        }

        // ---- dumps (own iterative writer; no JsonTextWriter, so floats/escaping/separators match Python) ----

        private sealed class Work
        {
            public ScriptValue Node;   // non-null => serialize this node at Depth
            public string Text;        // non-null => append verbatim
            public ScriptValue Close;  // non-null => also pop this container from the open set (cycle tracking)
            public int Depth;
        }

        public static string Dump(EvalContext ctx, ScriptValue value, JsonDumpOptions o)
        {
            var sb = new StringBuilder();
            var open = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var stack = new Stack<Work>();
            stack.Push(new Work { Node = value, Depth = 0 });

            while (stack.Count > 0)
            {
                ctx.Budget.Step();
                Work w = stack.Pop();
                if (w.Node == null)
                {
                    sb.Append(w.Text);
                    if (w.Close != null)
                        open.Remove(w.Close);
                    continue;
                }
                SerializeNode(ctx, w.Node, w.Depth, o, sb, open, stack);
            }
            return sb.ToString();
        }

        private static void SerializeNode(EvalContext ctx, ScriptValue node, int depth, JsonDumpOptions o,
            StringBuilder sb, HashSet<object> open, Stack<Work> stack)
        {
            switch (node.Kind)
            {
                case ValueKind.None:
                    sb.Append("null");
                    return;
                case ValueKind.Bool:
                    sb.Append(((BoolValue)node).Value ? "true" : "false");
                    return;
                case ValueKind.Int:
                    sb.Append(((IntValue)node).Value.ToString(CultureInfo.InvariantCulture));
                    return;
                case ValueKind.Float:
                    sb.Append(FloatToken(ctx, ((FloatValue)node).Value, o));
                    return;
                case ValueKind.Str:
                    WriteString(sb, ((StrValue)node).Value, o.EnsureAscii);
                    return;
                case ValueKind.List:
                    PushList(ctx, node, ((ListValue)node).Items, depth, o, sb, open, stack);
                    return;
                case ValueKind.Tuple:
                    PushList(ctx, node, ((TupleValue)node).Items, depth, o, sb, open, stack);
                    return;
                case ValueKind.Dict:
                    PushDict(ctx, (DictValue)node, depth, o, sb, open, stack);
                    return;
                default:
                    if (o.Default != null)
                    {
                        // default(o) answers a serializable stand-in; a stand-in that never bottoms out is
                        // caught by the same depth cap as a nested container.
                        if (depth >= ctx.Limits.MaxDataDepth)
                            throw Raise.ValueError(ctx, "Circular reference detected");
                        SerializeNode(ctx, ctx.CallHook(o.Default, new[] { node }, KwArgs.Empty), depth + 1, o, sb, open, stack);
                        return;
                    }
                    throw Raise.TypeError(ctx, "Object of type " + node.PyTypeName + " is not JSON serializable");
            }
        }

        private static void PushList(EvalContext ctx, ScriptValue node, IReadOnlyList<ScriptValue> items,
            int depth, JsonDumpOptions o, StringBuilder sb, HashSet<object> open, Stack<Work> stack)
        {
            EnterContainer(ctx, node, depth, open);
            if (items.Count == 0)
            {
                open.Remove(node);
                sb.Append("[]");
                return;
            }
            sb.Append('[');
            string childIndent = Newline(o, depth + 1);
            stack.Push(new Work { Text = Newline(o, depth) + "]", Close = node });
            for (int i = items.Count - 1; i >= 0; i--)
            {
                stack.Push(new Work { Node = items[i], Depth = depth + 1 });
                stack.Push(new Work { Text = i == 0 ? childIndent : o.ItemSeparator + childIndent });
            }
        }

        private static void PushDict(EvalContext ctx, DictValue node, int depth, JsonDumpOptions o,
            StringBuilder sb, HashSet<object> open, Stack<Work> stack)
        {
            var keys = new List<string>();
            var vals = new List<ScriptValue>();
            OrderedTable t = node.Table;
            int used = t.EntriesUsed;
            for (int pos = 0; pos < used; pos++)
            {
                long h;
                ScriptValue k, v;
                if (!t.TryGetEntryAt(pos, out h, out k, out v))
                    continue;
                string ks;
                if (!TryCoerceKey(ctx, k, o, out ks))
                    continue;                      // skipkeys=True: a key JSON cannot spell is dropped
                keys.Add(ks);
                vals.Add(v);
            }

            EnterContainer(ctx, node, depth, open);
            if (keys.Count == 0)
            {
                open.Remove(node);
                sb.Append("{}");
                return;
            }

            int[] order = SortOrder(keys, o.SortKeys);
            sb.Append('{');
            string childIndent = Newline(o, depth + 1);
            stack.Push(new Work { Text = Newline(o, depth) + "}", Close = node });
            for (int idx = order.Length - 1; idx >= 0; idx--)
            {
                int i = order[idx];
                stack.Push(new Work { Node = vals[i], Depth = depth + 1 });
                stack.Push(new Work { Text = o.KeySeparator });
                var keyBuf = new StringBuilder();
                WriteString(keyBuf, keys[i], o.EnsureAscii);
                stack.Push(new Work { Text = keyBuf.ToString() });
                stack.Push(new Work { Text = idx == 0 ? childIndent : o.ItemSeparator + childIndent });
            }
        }

        private static void EnterContainer(EvalContext ctx, ScriptValue node, int depth, HashSet<object> open)
        {
            if (!open.Add(node))
                throw Raise.ValueError(ctx, "Circular reference detected");
            if (depth >= ctx.Limits.MaxDataDepth)
            {
                open.Remove(node);
                throw Raise.ValueError(ctx, "Too deeply nested");
            }
        }

        private static int[] SortOrder(List<string> keys, bool sortKeys)
        {
            var order = new int[keys.Count];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;
            if (sortKeys)
                Array.Sort(order, (a, b) => string.CompareOrdinal(keys[a], keys[b]));
            return order;
        }

        // JSON object keys must be strings; Python coerces the scalar key types (str/bool/None/int/float).
        // false => this key has no JSON spelling, which skipkeys=True drops and the default refuses.
        private static bool TryCoerceKey(EvalContext ctx, ScriptValue key, JsonDumpOptions o, out string text)
        {
            switch (key.Kind)
            {
                case ValueKind.Str: text = ((StrValue)key).Value; return true;
                case ValueKind.Bool: text = ((BoolValue)key).Value ? "true" : "false"; return true;
                case ValueKind.None: text = "null"; return true;
                case ValueKind.Int: text = ((IntValue)key).Value.ToString(CultureInfo.InvariantCulture); return true;
                case ValueKind.Float: text = FloatToken(ctx, ((FloatValue)key).Value, o); return true;
                default:
                    text = null;
                    if (o.SkipKeys)
                        return false;
                    throw Raise.TypeError(ctx, "keys must be str, int, float, bool or None, not " + key.PyTypeName);
            }
        }

        private static string FloatToken(EvalContext ctx, double d, JsonDumpOptions o)
        {
            if (double.IsNaN(d))
                return o.AllowNan ? "NaN" : throw OutOfRange(ctx);
            if (double.IsPositiveInfinity(d))
                return o.AllowNan ? "Infinity" : throw OutOfRange(ctx);
            if (double.IsNegativeInfinity(d))
                return o.AllowNan ? "-Infinity" : throw OutOfRange(ctx);
            return FloatRepr.Repr(d);
        }

        private static ScriptException OutOfRange(EvalContext ctx)
        {
            return Raise.ValueError(ctx, "Out of range float values are not JSON compliant");
        }

        private static string Newline(JsonDumpOptions o, int depth)
        {
            if (o.IndentUnit == null)
                return "";
            var sb = new StringBuilder("\n", 1 + o.IndentUnit.Length * depth);
            for (int i = 0; i < depth; i++)
                sb.Append(o.IndentUnit);
            return sb.ToString();
        }

        private static void WriteString(StringBuilder sb, string s, bool ensureAscii)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || (ensureAscii && c > 0x7E))
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    // Immutable dumps options. MarshalDefault is the compact form hosting uses.
    internal sealed class JsonDumpOptions
    {
        public readonly bool SortKeys;
        public readonly bool EnsureAscii;
        public readonly bool AllowNan;
        public readonly string ItemSeparator;   // "," or ", "
        public readonly string KeySeparator;    // ":" or ": "
        public readonly string IndentUnit;      // null => compact; otherwise the per-level indent string
        public readonly ScriptValue Default;    // dumps(default=): called on an unserializable object; null => TypeError
        public readonly bool SkipKeys;          // dumps(skipkeys=True): a key JSON has no spelling for is dropped

        public JsonDumpOptions(bool sortKeys, bool ensureAscii, bool allowNan,
            string itemSeparator, string keySeparator, string indentUnit, ScriptValue defaultHook = null,
            bool skipKeys = false)
        {
            SortKeys = sortKeys;
            EnsureAscii = ensureAscii;
            AllowNan = allowNan;
            ItemSeparator = itemSeparator;
            KeySeparator = keySeparator;
            IndentUnit = indentUnit;
            Default = defaultHook;
            SkipKeys = skipKeys;
        }

        // Hosting JsonOut: compact, EnsureAscii=false (the host string is UTF-16), AllowNan=false, no indent.
        public static readonly JsonDumpOptions MarshalDefault =
            new JsonDumpOptions(false, false, false, ",", ":", null);
    }
}

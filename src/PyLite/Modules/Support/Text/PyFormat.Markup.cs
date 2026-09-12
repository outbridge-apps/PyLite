using System.Text;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // str.format markup: {field!conv:spec}, auto/manual numbering, one nesting level.
    internal static partial class PyFormat
    {
        private enum NumMode { Unknown, Auto, Manual }

        private sealed class AutoState
        {
            public NumMode Mode = NumMode.Unknown;
            public int AutoIndex;
            public ScriptValue Mapping;   // format_map: names resolve through mapping[name], never eagerly
        }

        internal static StrValue StrDotFormat(EvalContext ctx, StrValue self, ScriptValue[] args, KwArgs kwargs)
        {
            return Format(ctx, self, args, kwargs, null);
        }

        // format_map(mapping) is format(**mapping) minus the unpacking: each field name is looked up in
        // the mapping as it is met, which is what lets a defaultdict's __missing__ or a ChainMap answer.
        internal static StrValue StrFormatMap(EvalContext ctx, StrValue self, ScriptValue mapping)
        {
            return Format(ctx, self, System.Array.Empty<ScriptValue>(), KwArgs.Empty, mapping);
        }

        private static StrValue Format(EvalContext ctx, StrValue self, ScriptValue[] args, KwArgs kwargs, ScriptValue mapping)
        {
            string t = self.Value;
            var sb = new StringBuilder(t.Length + 16);
            var st = new AutoState { Mapping = mapping };
            int pos = 0, n = t.Length;
            while (pos < n)
            {
                char c = t[pos];
                if (c == '}')
                {
                    if (pos + 1 < n && t[pos + 1] == '}')
                    {
                        sb.Append('}');
                        pos += 2;
                    }
                    else
                    {
                        throw Raise.ValueError(ctx, "Single '}' encountered in format string");
                    }
                }
                else if (c == '{')
                {
                    if (pos + 1 < n && t[pos + 1] == '{')
                    {
                        sb.Append('{');
                        pos += 2;
                    }
                    else
                    {
                        ParseField(ctx, t, ref pos, args, kwargs, st, sb);
                    }
                }
                else
                {
                    sb.Append(c);
                    pos++;
                }
            }
            return (StrValue)ctx.Values.Str(sb.ToString());
        }

        private static void ParseField(EvalContext ctx, string t, ref int pos, ScriptValue[] args, KwArgs kwargs,
            AutoState st, StringBuilder sb)
        {
            int n = t.Length;
            pos++;   // past '{'
            int nameStart = pos;
            while (pos < n && t[pos] != '!' && t[pos] != ':' && t[pos] != '}')
                pos++;
            string fieldName = t.Substring(nameStart, pos - nameStart);

            char conv = '\0';
            if (pos < n && t[pos] == '!')
            {
                pos++;
                if (pos >= n)
                    throw Raise.ValueError(ctx, "Single '{' encountered in format string");
                conv = t[pos];
                pos++;
                if (conv != 's' && conv != 'r' && conv != 'a')
                    throw Raise.ValueError(ctx, "Unknown conversion specifier " + conv);
            }

            string spec = "";
            if (pos < n && t[pos] == ':')
            {
                pos++;
                int depth = 1, specStart = pos;
                while (pos < n)
                {
                    if (t[pos] == '{')
                        depth++;
                    else if (t[pos] == '}')
                    {
                        depth--;
                        if (depth == 0)
                            break;
                    }
                    pos++;
                }
                spec = t.Substring(specStart, pos - specStart);
            }
            if (pos >= n || t[pos] != '}')
                throw Raise.ValueError(ctx, "Single '{' encountered in format string");
            pos++;   // past '}'

            ScriptValue value = ResolveField(ctx, fieldName, args, kwargs, st);
            if (conv == 's')
                value = ctx.Values.Str(value.Str(ctx));
            else if (conv == 'r')
                value = ctx.Values.Str(value.Repr(ctx));
            else if (conv == 'a')
                value = ctx.Values.Str(AsciiEscape(value.Repr(ctx)));

            string resolvedSpec = spec.IndexOf('{') >= 0 ? ResolveNestedSpec(ctx, spec, args, kwargs, st) : spec;
            ScriptValue rendered = FormatValue(ctx, value, resolvedSpec);
            sb.Append(((StrValue)rendered).Value);
            ctx.Budget.Step();
        }

        private static readonly char[] FieldBreaks = { '.', '[' };

        // field_name ::= arg_name ("." attribute_name | "[" element_index "]")*. The accessors reach
        // exactly the surface the dot operator and [] reach (PyOps.GetAttr / PyOps.GetItem), so a format
        // string opens nothing the dialect does not already allow.
        private static ScriptValue ResolveField(EvalContext ctx, string fieldName, ScriptValue[] args, KwArgs kwargs, AutoState st)
        {
            int cut = fieldName.IndexOfAny(FieldBreaks);
            if (cut < 0)
                return ResolveArgName(ctx, fieldName, args, kwargs, st);
            ScriptValue value = ResolveArgName(ctx, fieldName.Substring(0, cut), args, kwargs, st);
            return ApplyAccessors(ctx, value, fieldName, cut);
        }

        private static ScriptValue ApplyAccessors(EvalContext ctx, ScriptValue value, string fieldName, int i)
        {
            int n = fieldName.Length;
            while (i < n)
            {
                ctx.Budget.Step();
                if (fieldName[i] == '.')
                {
                    int start = ++i;
                    while (i < n && fieldName[i] != '.' && fieldName[i] != '[')
                        i++;
                    if (i == start)
                        throw Raise.ValueError(ctx, "Empty attribute in format string");
                    value = PyOps.GetAttr(value, fieldName.Substring(start, i - start), ctx);
                    continue;
                }
                int keyStart = ++i;
                while (i < n && fieldName[i] != ']')
                    i++;
                if (i == n)
                    throw Raise.ValueError(ctx, "Missing ']' in format string");
                string key = fieldName.Substring(keyStart, i - keyStart);
                i++;   // past ']'
                // element_index is digits -> an integer index, anything else -> the literal string key
                // (unquoted: '{0[a]}' looks up 'a').
                int idx;
                ScriptValue k;
                if (key.Length > 0 && IsAllDigits(key)
                    && int.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out idx))
                    k = ctx.Values.Int(idx);
                else
                    k = ctx.Values.Str(key);
                value = PyOps.GetItem(value, k, ctx);
            }
            return value;
        }

        private static ScriptValue ResolveArgName(EvalContext ctx, string fieldName, ScriptValue[] args, KwArgs kwargs, AutoState st)
        {
            if (fieldName.Length == 0)
            {
                if (st.Mode == NumMode.Manual)
                    throw Raise.ValueError(ctx, "cannot switch from manual field specification to automatic field numbering");
                st.Mode = NumMode.Auto;
                return ByIndex(ctx, args, st.AutoIndex++);
            }
            if (IsAllDigits(fieldName))
            {
                if (st.Mode == NumMode.Auto)
                    throw Raise.ValueError(ctx, "cannot switch from automatic field numbering to manual field specification");
                st.Mode = NumMode.Manual;
                return ByIndex(ctx, args, int.Parse(fieldName, System.Globalization.CultureInfo.InvariantCulture));
            }
            if (st.Mapping != null)
                return PyOps.GetItem(st.Mapping, ctx.Values.Str(fieldName), ctx);
            ScriptValue v;
            if (kwargs.TryGet(fieldName, out v))
                return v;
            throw Raise.KeyError(ctx, ctx.Values.Str(fieldName));
        }

        private static ScriptValue ByIndex(EvalContext ctx, ScriptValue[] args, int idx)
        {
            if (idx < 0 || idx >= args.Length)
                throw Raise.IndexError(ctx, "tuple index out of range");
            return args[idx];
        }

        private static bool IsAllDigits(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < '0' || s[i] > '9')
                    return false;
            }
            return true;
        }

        // One nesting level: each {name} in the spec resolves to str(value); deeper markup -> error.
        private static string ResolveNestedSpec(EvalContext ctx, string spec, ScriptValue[] args, KwArgs kwargs, AutoState st)
        {
            var sb = new StringBuilder(spec.Length);
            int i = 0, n = spec.Length;
            while (i < n)
            {
                char c = spec[i];
                if (c == '{')
                {
                    if (i + 1 < n && spec[i + 1] == '{')
                    {
                        sb.Append('{');
                        i += 2;
                        continue;
                    }
                    int start = i + 1, j = i + 1;
                    while (j < n && spec[j] != '}' && spec[j] != '{' && spec[j] != ':' && spec[j] != '!')
                        j++;
                    if (j >= n || spec[j] != '}')
                        throw Raise.ValueError(ctx, "Max string recursion exceeded");
                    ScriptValue v = ResolveField(ctx, spec.Substring(start, j - start), args, kwargs, st);
                    sb.Append(v.Str(ctx));
                    i = j + 1;
                }
                else if (c == '}' && i + 1 < n && spec[i + 1] == '}')
                {
                    sb.Append('}');
                    i += 2;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            return sb.ToString();
        }
    }
}

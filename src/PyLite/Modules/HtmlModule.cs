using System;
using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // html: escape is our own 5-replacement pass; unescape is a port of CPython's
    // algorithm over the generated HTML5 entity table (Html5Entities).
    public static class HtmlModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["escape"] = BuiltinFunctionValue.Make("escape", Escape);
            m["unescape"] = BuiltinFunctionValue.Make("unescape", Unescape);
            return ctx.Values.Module("html", m);
        }

        // escape(s, quote=True): & first (avoids double-escaping); ' -> &#x27; (not &apos;).
        private static ScriptValue Escape(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, args, "escape", 1, 2);
            string s = AsStr(ctx, args[0]);
            ctx.Budget.ChargeLinear(s.Length);
            bool quote = true;
            if (args.Length == 2)
                quote = args[1].IsTruthy(ctx);
            ScriptValue qkw;
            if (kw.TryGet("quote", out qkw))
                quote = qkw.IsTruthy(ctx);

            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                if ((i & 63) == 0)
                    ctx.Budget.Step();
                char ch = s[i];
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append(quote ? "&quot;" : "\""); break;
                    case '\'': sb.Append(quote ? "&#x27;" : "'"); break;
                    default: sb.Append(ch); break;
                }
            }
            return ctx.Values.Str(sb.ToString());
        }

        // unescape(s): CPython html.unescape (Lib/html/__init__.py). Numeric references decode with or
        // without ';' (cp1252 remap of 0x80-0x9F, U+FFFD for 0/surrogates/out-of-range, control points
        // dropped); named references need ';' except the HTML5 legacy names, which take the longest
        // known prefix (the HTML5 table, generated into Html5Entities).
        private static ScriptValue Unescape(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, args, "unescape", 1);
            string s = AsStr(ctx, args[0]);
            ctx.Budget.ChargeLinear(s.Length);
            if (s.IndexOf('&') < 0)
                return args[0].Kind == ValueKind.Str ? args[0] : ctx.Values.Str(s);
            ctx.Budget.PreCharge(2L * s.Length);
            var sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c != '&' || i + 1 >= s.Length)
                {
                    sb.Append(c);
                    i++;
                    continue;
                }
                int consumed = TryCharRef(s, i + 1, sb);
                if (consumed == 0)
                {
                    sb.Append('&');
                    i++;
                    continue;
                }
                i += 1 + consumed;
            }
            return ctx.Values.Str(sb.ToString());
        }

        // Decodes the reference starting after '&' at s[p]; appends the replacement and returns the number of
        // characters consumed after the '&', or 0 when there is no reference there.
        private static int TryCharRef(string s, int p, StringBuilder sb)
        {
            if (s[p] == '#')
                return TryNumericRef(s, p, sb);
            int q = p;
            while (q < s.Length && q - p < 32 && IsNameChar(s[q]))
                q++;
            if (q == p)
                return 0;
            string name = s.Substring(p, q - p);
            bool semi = q < s.Length && s[q] == ';';
            string decoded;
            if (semi && TryNamed(name + ";", out decoded))
            {
                sb.Append(decoded);
                return q - p + 1;
            }
            // Legacy names (the HTML5 table entries without ';'): longest prefix wins, the rest stays literal.
            for (int x = name.Length; x > 1; x--)
            {
                string head = name.Substring(0, x);
                if (TryNamed(head, out decoded))
                {
                    sb.Append(decoded);
                    return x;
                }
            }
            return 0;
        }

        private static bool IsNameChar(char c)
        {
            return c != '\t' && c != '\n' && c != '\f' && c != ' ' && c != '<' && c != '&' && c != '#' && c != ';';
        }

        private static bool TryNamed(string name, out string decoded)
        {
            return Html5Entities.Table.TryGetValue(name, out decoded);
        }

        private static int TryNumericRef(string s, int p, StringBuilder sb)
        {
            int q = p + 1;
            bool hex = q < s.Length && (s[q] == 'x' || s[q] == 'X');
            if (hex)
                q++;
            int digitsStart = q;
            long num = 0;
            while (q < s.Length)
            {
                int dv = hex ? HexVal(s[q]) : (s[q] >= '0' && s[q] <= '9' ? s[q] - '0' : -1);
                if (dv < 0)
                    break;
                if (num <= 0x110000)
                    num = num * (hex ? 16 : 10) + dv;
                q++;
            }
            if (q == digitsStart)
                return 0;
            if (q < s.Length && s[q] == ';')
                q++;
            AppendCodePoint(sb, num);
            return q - p;
        }

        private static int HexVal(char c)
        {
            if (c >= '0' && c <= '9')
                return c - '0';
            if (c >= 'a' && c <= 'f')
                return c - 'a' + 10;
            if (c >= 'A' && c <= 'F')
                return c - 'A' + 10;
            return -1;
        }

        private static void AppendCodePoint(StringBuilder sb, long num)
        {
            if (num == 0 || (num >= 0xD800 && num <= 0xDFFF) || num > 0x10FFFF)
            {
                sb.Append('�');
                return;
            }
            if (num == 0x0D)
            {
                sb.Append('\r');
                return;
            }
            if (num >= 0x80 && num <= 0x9F)
            {
                sb.Append(Encoding.GetEncoding(1252).GetString(new[] { (byte)num }));
                return;
            }
            if ((num >= 0x1 && num <= 0x8) || num == 0xB || (num >= 0xE && num <= 0x1F) || num == 0x7F
                || (num >= 0xFDD0 && num <= 0xFDEF) || (num & 0xFFFE) == 0xFFFE)
                return;   // invalid code points are dropped
            sb.Append(char.ConvertFromUtf32((int)num));
        }

        private static string AsStr(EvalContext ctx, ScriptValue v)
        {
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, "argument must be str, not " + v.PyTypeName);
            return s.Value;
        }
    }
}

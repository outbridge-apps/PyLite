using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // textwrap: a port of Lib/textwrap.py's functions (wrap/fill/shorten with TextWrapper's keyword
    // options, dedent, indent). The TextWrapper class itself is absent (nothing to subclass here).
    public static class TextwrapModule
    {
        private const string Whitespace = "\t\n\x0b\x0c\r ";

        // TextWrapper.wordsep_re, transcribed: whitespace runs, em-dashes between words, and words that
        // may break after an inner hyphen; wordsep_simple_re when break_on_hyphens is off.
        private static readonly Regex WordSep = new Regex(
            @"([\t\n\x0b\x0c\r ]+|(?<=[\w!""'&.,?])-{2,}(?=\w)|[^\t\n\x0b\x0c\r ]+?(?:-(?:(?<=[^\d\W]{2}-)|(?<=[^\d\W]-[^\d\W]-))(?=[^\d\W]-?[^\d\W])|(?=[\t\n\x0b\x0c\r ]|\z)|(?<=[\w!""'&.,?])(?=-{2,}\w)))",
            RegexOptions.Compiled);
        private static readonly Regex WordSepSimple = new Regex(@"([\t\n\x0b\x0c\r ]+)", RegexOptions.Compiled);
        private static readonly Regex SentenceEnd = new Regex(@"[a-z][\.\!\?][\""\']?\z", RegexOptions.Compiled);
        private static readonly Regex WhitespaceOnly = new Regex(@"^[ \t]+$", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex LeadingWhitespace = new Regex(@"(^[ \t]*)(?:[^ \t\n])", RegexOptions.Multiline | RegexOptions.Compiled);

        private sealed class Options
        {
            public int Width = 70;
            public string InitialIndent = "";
            public string SubsequentIndent = "";
            public bool ExpandTabs = true;
            public bool ReplaceWhitespace = true;
            public bool FixSentenceEndings;
            public bool BreakLongWords = true;
            public bool DropWhitespace = true;
            public bool BreakOnHyphens = true;
            public int TabSize = 8;
            public int MaxLines = -1;
            public string Placeholder = " [...]";
        }

        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["wrap"] = BuiltinFunctionValue.Make("wrap", (s, a, kw, c) => WrapFn(c, a, kw, "wrap", false)),
                ["fill"] = BuiltinFunctionValue.Make("fill", (s, a, kw, c) => WrapFn(c, a, kw, "fill", true)),
                ["shorten"] = BuiltinFunctionValue.Make("shorten", Shorten),
                ["dedent"] = BuiltinFunctionValue.Make("dedent", (s, a, kw, c) => c.Values.Str(Dedent(c, TextArg(c, a, kw, "dedent")))),
                ["indent"] = BuiltinFunctionValue.Make("indent", Indent),
            };
            return ctx.Values.Module("textwrap", m);
        }

        private static string TextArg(EvalContext ctx, ScriptValue[] a, KwArgs kw, string fn)
        {
            ScriptValue v = a.Length >= 1 ? a[0] : null;
            ScriptValue kv;
            if (kw.TryGet("text", out kv))
                v = kv;
            if (v == null)
                throw Raise.TypeError(ctx, fn + "() missing required argument 'text'");
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, fn + "() argument 'text' must be str, not " + v.PyTypeName);
            return s.Value;
        }

        private static Options ReadOptions(EvalContext ctx, ScriptValue[] a, KwArgs kw, string fn, int widthPos)
        {
            var o = new Options();
            if (a.Length > widthPos)
                o.Width = IntOf(ctx, a[widthPos], "width");
            Args.AtMost(ctx, a, fn, widthPos + 1);
            for (int j = 0; j < kw.Count; j++)
            {
                string k = kw.NameAt(j);
                ScriptValue v = kw.ValueAt(j);
                switch (k)
                {
                    case "text": break;
                    case "width": o.Width = IntOf(ctx, v, k); break;
                    case "initial_indent": o.InitialIndent = StrOf(ctx, v, k); break;
                    case "subsequent_indent": o.SubsequentIndent = StrOf(ctx, v, k); break;
                    case "expand_tabs": o.ExpandTabs = v.IsTruthy(ctx); break;
                    case "replace_whitespace": o.ReplaceWhitespace = v.IsTruthy(ctx); break;
                    case "fix_sentence_endings": o.FixSentenceEndings = v.IsTruthy(ctx); break;
                    case "break_long_words": o.BreakLongWords = v.IsTruthy(ctx); break;
                    case "drop_whitespace": o.DropWhitespace = v.IsTruthy(ctx); break;
                    case "break_on_hyphens": o.BreakOnHyphens = v.IsTruthy(ctx); break;
                    case "tabsize": o.TabSize = Coerce.ToCInt(ctx, v, k); break;
                    case "max_lines": o.MaxLines = v.Kind == ValueKind.None ? -1 : IntOf(ctx, v, k); break;
                    case "placeholder": o.Placeholder = StrOf(ctx, v, k); break;
                    default:
                        throw KwReader.UnexpectedError(ctx, fn, k);
                }
            }
            return o;
        }

        private static int IntOf(EvalContext ctx, ScriptValue v, string name)
        {
            if (v.Kind != ValueKind.Int && v.Kind != ValueKind.Bool)
                throw Raise.TypeError(ctx, "'" + name + "' must be int, not " + v.PyTypeName);
            System.Numerics.BigInteger b = NumericOps.AsBigInteger(v);   // pure Python takes any int: clamp
            return b > int.MaxValue ? int.MaxValue : b < int.MinValue ? int.MinValue : (int)b;
        }

        private static string StrOf(EvalContext ctx, ScriptValue v, string name)
        {
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, "'" + name + "' must be str, not " + v.PyTypeName);
            return s.Value;
        }

        private static ScriptValue WrapFn(EvalContext ctx, ScriptValue[] a, KwArgs kw, string fn, bool join)
        {
            string text = TextArg(ctx, a, kw, fn);
            Options o = ReadOptions(ctx, a, kw, fn, 1);
            List<string> lines = Wrap(ctx, text, o);
            if (join)
            {
                string s = string.Join("\n", lines);
                ctx.Values.EnsureStrLen(s.Length);
                return ctx.Values.Str(s);
            }
            ListValue list = ctx.Values.List(lines.Count);
            foreach (string l in lines)
                list.Add(ctx.Values.Str(l), ctx);
            return list;
        }

        private static ScriptValue Shorten(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            string text = TextArg(ctx, a, kw, "shorten");
            Options o = ReadOptions(ctx, a, kw, "shorten", 1);
            ScriptValue widthKw;
            if (a.Length < 2 && !kw.TryGet("width", out widthKw))
                throw Raise.TypeError(ctx, "shorten() missing required argument 'width'");
            o.MaxLines = 1;
            string collapsed = string.Join(" ", text.Trim(Whitespace.ToCharArray()).Split(Whitespace.ToCharArray(), StringSplitOptions.RemoveEmptyEntries));
            string s = string.Join("\n", Wrap(ctx, collapsed, o));
            return ctx.Values.Str(s);
        }

        // ---- TextWrapper._wrap_chunks and friends ----

        private static List<string> Wrap(EvalContext ctx, string text, Options o)
        {
            if (o.Width <= 0)
                throw Raise.ValueError(ctx, "invalid width " + o.Width + " (must be > 0)");
            if (o.MaxLines >= 0)
            {
                string indent = o.MaxLines > 1 ? o.SubsequentIndent : o.InitialIndent;
                if (indent.Length + o.Placeholder.TrimStart().Length > o.Width)
                    throw Raise.ValueError(ctx, "placeholder too large for max width");
            }
            if (o.ExpandTabs)
                text = ExpandTabs(text, o.TabSize);
            if (o.ReplaceWhitespace)
            {
                var sb = new StringBuilder(text.Length);
                foreach (char c in text)
                    sb.Append(Whitespace.IndexOf(c) >= 0 ? ' ' : c);
                text = sb.ToString();
            }
            // Matches() is lazy, so a step per chunk keeps a multi-megabyte text interruptible.
            var chunks = new List<string>();
            if (o.BreakOnHyphens)
            {
                foreach (Match m in WordSep.Matches(text))
                {
                    ctx.Budget.Step();
                    if (m.Value.Length > 0)
                        chunks.Add(m.Value);
                }
            }
            else
            {
                // wordsep_simple_re splits on the captured whitespace, keeping the non-whitespace between
                foreach (string p in WordSepSimple.Split(text))
                {
                    ctx.Budget.Step();
                    if (p.Length > 0)
                        chunks.Add(p);
                }
            }
            if (o.FixSentenceEndings)
                FixSentenceEndings(chunks);

            var lines = new List<string>();
            chunks.Reverse();   // pop from the end, as the Python does
            while (chunks.Count > 0)
            {
                ctx.Budget.Step();
                var cur = new List<string>();
                int curLen = 0;
                string indent = lines.Count > 0 ? o.SubsequentIndent : o.InitialIndent;
                int width = o.Width - indent.Length;
                if (o.DropWhitespace && IsBlank(chunks[chunks.Count - 1]) && lines.Count > 0)
                    chunks.RemoveAt(chunks.Count - 1);
                while (chunks.Count > 0)
                {
                    int l = chunks[chunks.Count - 1].Length;
                    if (curLen + l <= width)
                    {
                        cur.Add(chunks[chunks.Count - 1]);
                        chunks.RemoveAt(chunks.Count - 1);
                        curLen += l;
                    }
                    else
                        break;
                }
                if (chunks.Count > 0 && chunks[chunks.Count - 1].Length > width)
                    HandleLongWord(ctx, chunks, cur, ref curLen, width, o);
                if (o.DropWhitespace && cur.Count > 0 && IsBlank(cur[cur.Count - 1]))
                {
                    curLen -= cur[cur.Count - 1].Length;
                    cur.RemoveAt(cur.Count - 1);
                }
                if (cur.Count == 0)
                    continue;
                string line = string.Concat(cur);
                if (o.MaxLines < 0 || lines.Count + 1 < o.MaxLines
                    || ((chunks.Count == 0 || (o.DropWhitespace && chunks.Count == 1 && IsBlank(chunks[0])))
                        && curLen <= width))
                {
                    lines.Add(indent + line);
                    continue;
                }
                // the last permitted line: fit the placeholder
                bool placed = false;
                while (cur.Count > 0)
                {
                    if (!IsBlank(cur[cur.Count - 1]) && curLen + o.Placeholder.Length <= width)
                    {
                        cur.Add(o.Placeholder);
                        lines.Add(indent + string.Concat(cur));
                        placed = true;
                        break;
                    }
                    curLen -= cur[cur.Count - 1].Length;
                    cur.RemoveAt(cur.Count - 1);
                }
                if (!placed)
                {
                    if (lines.Count > 0)
                    {
                        string prev = lines[lines.Count - 1].TrimEnd();
                        if (prev.Length + o.Placeholder.Length <= o.Width)
                        {
                            lines[lines.Count - 1] = prev + o.Placeholder;
                            break;
                        }
                    }
                    lines.Add(indent + o.Placeholder.TrimStart());
                }
                break;
            }
            return lines;
        }

        private static void HandleLongWord(EvalContext ctx, List<string> chunks, List<string> cur, ref int curLen, int width, Options o)
        {
            // Re-slicing the tail per emitted piece is quadratic on a long word (CPython does the same),
            // so the deadline is checked here rather than every 1024 outer steps.
            ctx.Budget.CheckDeadlineNow();
            int spaceLeft = width < 1 ? 1 : width - curLen;
            string chunk = chunks[chunks.Count - 1];
            if (o.BreakLongWords)
            {
                int end = spaceLeft;
                if (o.BreakOnHyphens && chunk.Length > spaceLeft)
                {
                    // rfind('-', 0, space_left), taken only when something other than hyphens precedes it
                    int hyphen = spaceLeft > 0 ? chunk.LastIndexOf('-', spaceLeft - 1) : -1;
                    if (hyphen > 0 && chunk.Substring(0, hyphen).Trim('-').Length > 0)
                        end = hyphen + 1;
                }
                cur.Add(chunk.Substring(0, end));
                curLen += end;
                chunks[chunks.Count - 1] = chunk.Substring(end);
            }
            else if (cur.Count == 0)
            {
                cur.Add(chunk);
                curLen += chunk.Length;
                chunks.RemoveAt(chunks.Count - 1);
            }
        }

        private static void FixSentenceEndings(List<string> chunks)
        {
            int i = 0;
            while (i < chunks.Count - 1)
            {
                if (chunks[i + 1] == " " && SentenceEnd.IsMatch(chunks[i]))
                {
                    chunks[i + 1] = "  ";
                    i += 2;
                }
                else
                    i++;
            }
        }

        private static bool IsBlank(string s)
        {
            return s.Trim(Whitespace.ToCharArray()).Length == 0;
        }

        private static string ExpandTabs(string text, int tabSize)
        {
            if (text.IndexOf('\t') < 0)
                return text;
            var sb = new StringBuilder(text.Length + 16);
            int col = 0;
            foreach (char c in text)
            {
                if (c == '\t')
                {
                    if (tabSize > 0)
                    {
                        int n = tabSize - col % tabSize;
                        sb.Append(' ', n);
                        col += n;
                    }
                }
                else
                {
                    sb.Append(c);
                    col = c == '\n' || c == '\r' ? 0 : col + 1;
                }
            }
            return sb.ToString();
        }

        // ---- dedent / indent ----

        internal static string Dedent(EvalContext ctx, string text)
        {
            text = WhitespaceOnly.Replace(text, "");
            string margin = null;
            foreach (Match m in LeadingWhitespace.Matches(text))
            {
                ctx.Budget.Step();
                string indent = m.Groups[1].Value;
                if (margin == null)
                    margin = indent;
                else if (indent.StartsWith(margin, StringComparison.Ordinal))
                    continue;
                else if (margin.StartsWith(indent, StringComparison.Ordinal))
                    margin = indent;
                else
                {
                    int k = 0;
                    while (k < margin.Length && k < indent.Length && margin[k] == indent[k])
                        k++;
                    margin = margin.Substring(0, k);
                }
            }
            if (string.IsNullOrEmpty(margin))
                return text;
            var sb = new StringBuilder(text.Length);
            int start = 0;
            while (start <= text.Length)
            {
                ctx.Budget.Step();
                int nl = text.IndexOf('\n', start);
                string line = nl < 0 ? text.Substring(start) : text.Substring(start, nl - start);
                sb.Append(line.StartsWith(margin, StringComparison.Ordinal) ? line.Substring(margin.Length) : line);
                if (nl < 0)
                    break;
                sb.Append('\n');
                start = nl + 1;
            }
            return sb.ToString();
        }

        private static ScriptValue Indent(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            string text = TextArg(ctx, a, kw, "indent");
            ScriptValue pv = a.Length > 1 ? a[1] : null;
            ScriptValue predV = a.Length > 2 ? a[2] : null;
            ScriptValue v;
            if (kw.TryGet("prefix", out v))
                pv = v;
            if (kw.TryGet("predicate", out v))
                predV = v;
            if (pv == null)
                throw Raise.TypeError(ctx, "indent() missing required argument 'prefix'");
            string prefix = StrOf(ctx, pv, "prefix");
            var sb = new StringBuilder(text.Length + 16);
            int start = 0;
            while (start < text.Length)
            {
                ctx.Budget.Step();
                // one line of splitlines(keepends=True): any of str's line breaks, \r\n as one
                int i = start;
                while (i < text.Length && !PyUnicode.IsLineBreak(text[i]))
                    i++;
                if (i < text.Length)
                    i += text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                string line = text.Substring(start, i - start);
                bool take;
                if (predV == null || predV.Kind == ValueKind.None)
                    take = !IsAllSpace(line);   // the default predicate is line.strip()
                else
                    take = ctx.CallHook(predV, new ScriptValue[] { ctx.Values.Str(line) }, KwArgs.Empty).IsTruthy(ctx);
                if (take)
                    sb.Append(prefix);
                sb.Append(line);
                start = i;
            }
            ctx.Values.EnsureStrLen(sb.Length);
            return ctx.Values.Str(sb.ToString());
        }

        private static bool IsAllSpace(string s)
        {
            foreach (char c in s)
            {
                if (!PyUnicode.IsPythonSpace(c))
                    return false;
            }
            return true;
        }
    }
}

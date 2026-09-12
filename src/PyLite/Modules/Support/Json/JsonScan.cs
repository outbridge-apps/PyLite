using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // A strict RFC 8259 pass over the document text: the verdict behind JsonGuard. Newtonsoft is
    // deliberately lenient and has no strict mode (JamesNK/Newtonsoft.Json#646 — the maintainer declined
    // a flag), and most of what it accepts — a trailing comma, a leading zero, a raw control character —
    // is already normalised away by the time a token comes back, so it cannot be caught downstream.
    //
    // The control flow mirrors Lib/json/decoder.py, so a refusal carries the message and the position
    // CPython reports. It builds nothing and allocates nothing: one forward pass over the characters.
    // It runs only when the guard has a doubt or the reader gives up, never on the ordinary path — a
    // walk over every character costs 2-6% of the parse on records and half again on a document that
    // is one 8 MB string.
    internal static class JsonScan
    {
        internal static void Validate(EvalContext ctx, string s, StrValue doc, bool strict, int maxDepth)
        {
            ctx.Budget.Step(1 + s.Length / 512);
            int i = ScanValue(ctx, s, doc, SkipWs(s, 0), strict, maxDepth);
            i = SkipWs(s, i);
            if (i != s.Length)
                throw Error(ctx, s, doc, "Extra data", i);
        }

        // One value and everything nested inside it, iteratively: an explicit stack of container kinds
        // instead of a recursive descent, so nesting costs no C# frames.
        private static int ScanValue(EvalContext ctx, string s, StrValue doc, int i, bool strict, int maxDepth)
        {
            var isObject = new bool[maxDepth + 1];
            int depth = 0;
            while (true)
            {
                char c = At(s, i);
                if (c == '{' || c == '[')
                {
                    if (depth >= maxDepth)
                        throw Error(ctx, s, doc, "Too deeply nested", i);
                    bool obj = c == '{';
                    isObject[depth++] = obj;
                    i = SkipWs(s, i + 1);
                    if (At(s, i) == (obj ? '}' : ']'))
                    {
                        depth--;              // an empty container is itself a finished value
                        i++;
                    }
                    else
                    {
                        if (obj)
                            i = ScanMember(ctx, s, doc, i, strict);
                        continue;             // ... otherwise its first value comes next
                    }
                }
                else
                {
                    i = ScanScalar(ctx, s, doc, i, strict);
                }

                // A value is finished at i: close the containers it ended, or step to its sibling.
                while (depth > 0)
                {
                    i = SkipWs(s, i);
                    char d = At(s, i);
                    bool obj = isObject[depth - 1];
                    if (d == (obj ? '}' : ']'))
                    {
                        depth--;
                        i++;
                        continue;
                    }
                    if (d != ',')
                        throw Error(ctx, s, doc, "Expecting ',' delimiter", i);
                    i = SkipWs(s, i + 1);
                    if (obj)
                        i = ScanMember(ctx, s, doc, i, strict);
                    break;
                }
                if (depth == 0)
                    return i;
            }
        }

        // A member's key and colon, leaving i on its value. CPython names the whole phrase in the error,
        // which is why a trailing comma inside an object reports a missing property name.
        private static int ScanMember(EvalContext ctx, string s, StrValue doc, int i, bool strict)
        {
            if (At(s, i) != '"')
                throw Error(ctx, s, doc, "Expecting property name enclosed in double quotes", i);
            i = SkipWs(s, ScanString(ctx, s, doc, i + 1, strict));
            if (At(s, i) != ':')
                throw Error(ctx, s, doc, "Expecting ':' delimiter", i);
            return SkipWs(s, i + 1);
        }

        // Dispatched on the first character: a number never pays for the keyword comparisons, which is
        // most of the cost on a number-heavy document.
        private static int ScanScalar(EvalContext ctx, string s, StrValue doc, int i, bool strict)
        {
            char c = At(s, i);
            if (c == '"')
                return ScanString(ctx, s, doc, i + 1, strict);
            if (c == '-' || (c >= '0' && c <= '9'))
            {
                int end = Number(s, i);
                if (end > i)
                    return end;
                if (c == '-' && Is(s, i, "-Infinity"))
                    return i + 9;
                throw Error(ctx, s, doc, "Expecting value", i);
            }
            // CPython's decoder takes NaN and the two infinities by default, so they are not a leniency.
            switch (c)
            {
                case 'n': if (Is(s, i, "null")) return i + 4; break;
                case 't': if (Is(s, i, "true")) return i + 4; break;
                case 'f': if (Is(s, i, "false")) return i + 5; break;
                case 'N': if (Is(s, i, "NaN")) return i + 3; break;
                case 'I': if (Is(s, i, "Infinity")) return i + 8; break;
            }
            throw Error(ctx, s, doc, "Expecting value", i);
        }

        // -?(0|[1-9]\d*)(\.\d+)?([eE][-+]?\d+)? — CPython's NUMBER_RE, including the way an optional
        // group that does not match simply ends the number: '01' is the number 0 followed by junk.
        internal static int Number(string s, int i)
        {
            int n = s.Length;
            int p = i;
            if (p < n && s[p] == '-')
                p++;
            if (p < n && s[p] == '0')
            {
                p++;
            }
            else if (p < n && s[p] >= '1' && s[p] <= '9')
            {
                p++;
                while (p < n && s[p] >= '0' && s[p] <= '9')
                    p++;
            }
            else
            {
                return i;
            }
            if (p + 1 < n && s[p] == '.' && s[p + 1] >= '0' && s[p + 1] <= '9')
            {
                p += 2;
                while (p < n && s[p] >= '0' && s[p] <= '9')
                    p++;
            }
            if (p < n && (s[p] == 'e' || s[p] == 'E'))
            {
                int q = p + 1;
                if (q < n && (s[q] == '+' || s[q] == '-'))
                    q++;
                if (q < n && s[q] >= '0' && s[q] <= '9')
                {
                    q++;
                    while (q < n && s[q] >= '0' && s[q] <= '9')
                        q++;
                    p = q;
                }
            }
            return p;
        }

        // i is just past the opening quote. A lone surrogate is legal here, as it is in CPython.
        private static int ScanString(EvalContext ctx, string s, StrValue doc, int i, bool strict)
        {
            int begin = i - 1;
            while (true)
            {
                if (i >= s.Length)
                    throw Error(ctx, s, doc, "Unterminated string starting at", begin);
                char c = s[i];
                if (c == '"')
                    return i + 1;
                if (c == '\\')
                {
                    if (i + 1 >= s.Length)
                        throw Error(ctx, s, doc, "Unterminated string starting at", begin);
                    char e = s[i + 1];
                    if (e == 'u')
                    {
                        for (int k = i + 2; k < i + 6; k++)
                        {
                            if (k >= s.Length || !IsHex(s[k]))
                                throw Error(ctx, s, doc, "Invalid \\uXXXX escape", i + 1);
                        }
                        i += 6;
                        continue;
                    }
                    if (e != '"' && e != '\\' && e != '/' && e != 'b' && e != 'f'
                        && e != 'n' && e != 'r' && e != 't')
                        throw Error(ctx, s, doc, "Invalid \\escape: " + Repr(e), i + 1);
                    i += 2;
                    continue;
                }
                if (c < 0x20 && strict)
                    throw Error(ctx, s, doc, "Invalid control character " + Repr(c) + " at", i + 1);
                i++;
            }
        }

        // ---- text helpers ----

        private static int SkipWs(string s, int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
                    break;
                i++;
            }
            return i;
        }

        private static char At(string s, int i)
        {
            return i >= 0 && i < s.Length ? s[i] : '\0';
        }

        private static bool Is(string s, int i, string word)
        {
            if (i + word.Length > s.Length)
                return false;
            for (int k = 0; k < word.Length; k++)
            {
                if (s[i + k] != word[k])
                    return false;
            }
            return true;
        }

        private static bool IsDigit(char c) { return c >= '0' && c <= '9'; }

        private static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        // Python's repr of a single character, which is what the two messages above interpolate.
        private static string Repr(char c)
        {
            switch (c)
            {
                case '\t': return "'\\t'";
                case '\n': return "'\\n'";
                case '\r': return "'\\r'";
                case '\'': return "\"'\"";
                case '\\': return "'\\\\'";
            }
            if (c < 0x20 || c == 0x7F)
                return "'\\x" + ((int)c).ToString("x2") + "'";
            return "'" + c + "'";
        }

        // CPython's JSONDecodeError(msg, doc, pos): args holds the one formatted message, and msg, doc,
        // pos, lineno and colno are attributes, with the line arithmetic of Lib/json/__init__.py (only
        // '\n' counts). doc is the script's own str when the caller has it, so nothing is copied.
        internal static ScriptException Error(EvalContext ctx, string s, StrValue doc, string msg, int pos)
        {
            int lineno = 1;
            int lineStart = -1;
            int limit = pos < s.Length ? pos : s.Length;
            for (int k = 0; k < limit; k++)
            {
                if (s[k] == '\n')
                {
                    lineno++;
                    lineStart = k;
                }
            }
            int colno = pos - lineStart;
            ScriptException ex = Raise.Make(ctx, PyExceptionTypes.JSONDecodeError,
                msg + ": line " + lineno + " column " + colno + " (char " + pos + ")");
            ex.Value.Data = new System.Collections.Generic.Dictionary<string, ScriptValue>(5, System.StringComparer.Ordinal)
            {
                ["msg"] = ctx.Values.Str(msg),
                ["doc"] = doc ?? ctx.Values.Str(s),
                ["pos"] = ctx.Values.Int(pos),
                ["lineno"] = ctx.Values.Int(lineno),
                ["colno"] = ctx.Values.Int(colno),
            };
            return ex;
        }
    }
}

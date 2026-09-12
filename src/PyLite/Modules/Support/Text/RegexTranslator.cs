using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Modules.Support
{
    // Python group N <-> .NET group. Every capturing group (plain or (?P<name>)) is emitted as a synthetic
    // .NET named group "p{N}" keyed by its Python 1-based index, so .NET's "unnamed-before-named" renumbering
    // never applies and group(N)/group(name) resolve correctly. User names are a lookup layer over the index.
    internal sealed class GroupMap
    {
        public readonly int Count;
        public readonly Dictionary<string, int> NameIndex;   // user group name -> 1-based index

        public GroupMap(int count, Dictionary<string, int> nameIndex)
        {
            Count = count;
            NameIndex = nameIndex;
        }

        public static string Net(int index)
        {
            return "p" + index.ToString(CultureInfo.InvariantCulture);
        }
    }

    // Single-pass Python-pattern -> .NET-pattern translator. Handles group
    // renaming, (?P<>)/(?P=), \Z->\z, global inline-flag hoisting, and [ escaping in classes. Constructs
    // outside the v1 subset -> re.error. Scoped inline flags and atomic groups map straight onto .NET.
    internal static class RegexTranslator
    {
        private const int MaxPatternChars = 4096;

        internal sealed class Result
        {
            public string NetPattern;
            public RegexOptions Options;
            public GroupMap Groups;
            public int EffectiveFlags;
        }

        // Letter escapes valid in both Python and .NET, passed through verbatim (with the backslash).
        private const string PassLetters = "AbBdDsSwWnrtfva0xuU";

        // ASCII counterparts of the class escapes (re.A). .NET's \w/\d/\s/\b are Unicode-aware and
        // RegexOptions.ECMAScript cannot be used (it forbids lookbehind), so the flag is applied by
        // rewriting the escapes. \x20 rather than a literal space: it survives VERBOSE mode intact.
        private const string AsciiWord = "a-zA-Z0-9_";
        private const string AsciiSpace = "\\x20\\t\\n\\r\\f\\v";

        public static Result Translate(EvalContext ctx, string pattern, int flags)
        {
            if (pattern.Length > MaxPatternChars)
                throw Err(ctx, "regular expression too long");

            flags = FixFlags(ctx, flags);
            Result r = TranslateOnce(ctx, pattern, flags, (flags & ReFlags.A) != 0);
            // An inline (?a) is only known after the scan; redo the pass with the ASCII classes on.
            if ((flags & ReFlags.A) == 0 && (r.EffectiveFlags & ReFlags.A) != 0)
                r = TranslateOnce(ctx, pattern, flags, true);
            return r;
        }

        // CPython's sre_parse.fix_flags for a str pattern, applied to the CALLER's flags before parsing:
        // UNICODE is implied unless ASCII was asked for, the two together are a contradiction, and LOCALE
        // is meaningless. Inline (?...) flags are OR'd in afterwards, so `(?a)` on a plain compile leaves
        // the implied UNICODE bit standing — exactly what CPython reports in Pattern.flags.
        // A contradictory flag COMBINATION is a plain ValueError in CPython, not re.error.
        private static int FixFlags(EvalContext ctx, int flags)
        {
            if ((flags & ReFlags.L) != 0)
                throw Raise.ValueError(ctx, "cannot use LOCALE flag with a str pattern");
            if ((flags & ReFlags.A) == 0)
                return flags | ReFlags.U;
            if ((flags & ReFlags.U) != 0)
                throw Raise.ValueError(ctx, "ASCII and UNICODE flags are incompatible");
            return flags;
        }

        private static Result TranslateOnce(EvalContext ctx, string pattern, int flags, bool ascii)
        {
            var sb = new StringBuilder(pattern.Length + 16);
            var nameIndex = new Dictionary<string, int>(System.StringComparer.Ordinal);
            int groupCount = 0;
            int flagsAcc = flags;
            int i = 0;
            int n = pattern.Length;
            // (?a:...) turns the ASCII rewrite on for its group only: the depth at which it opened is
            // remembered, and the ')' that closes it restores the state outside.
            int depth = 0;
            var asciiScopes = new Stack<KeyValuePair<int, bool>>();
            // Where in the buffer the atom a quantifier would apply to begins. Only the possessive
            // rewrite reads it, and only a quantifier that follows the atom immediately.
            int atomStart = sb.Length;
            var groupStarts = new Stack<int>();

            while (i < n)
            {
                char c = pattern[i];
                if (c == '\\')
                {
                    atomStart = sb.Length;
                    i = Escape(ctx, pattern, i, sb, groupCount, ascii);
                }
                else if (c == '[')
                {
                    atomStart = sb.Length;
                    i = CharClass(ctx, pattern, i, sb, ascii);
                }
                else if (c == '(')
                {
                    depth++;
                    groupStarts.Push(sb.Length);
                    bool scopedAscii;
                    i = Group(ctx, pattern, i, sb, ref groupCount, nameIndex, ref flagsAcc, out scopedAscii);
                    if (scopedAscii)
                    {
                        asciiScopes.Push(new KeyValuePair<int, bool>(depth, ascii));
                        ascii = true;
                    }
                }
                else if (c == ')')
                {
                    if (asciiScopes.Count > 0 && asciiScopes.Peek().Key == depth)
                        ascii = asciiScopes.Pop().Value;
                    depth--;
                    atomStart = groupStarts.Count > 0 ? groupStarts.Pop() : sb.Length;
                    sb.Append(c);
                    i++;
                }
                else if (c == '*' || c == '+' || c == '?')
                {
                    sb.Append(c);
                    i = Possessive(pattern, i + 1, sb, atomStart);
                }
                else
                {
                    int after = CountedRepeat(pattern, i, sb);
                    if (after > i)
                    {
                        i = Possessive(pattern, after, sb, atomStart);
                        continue;
                    }
                    // In VERBOSE mode whitespace is not an atom, so a quantifier written after it still
                    // belongs to the atom before it (`a *+`).
                    if (!((flagsAcc & ReFlags.X) != 0 && IsPatternSpace(c)))
                        atomStart = sb.Length;
                    sb.Append(c);
                    i++;
                }
            }

            bool localeError, asciiRequested;
            RegexOptions opts = ReFlags.ToRegexOptions(flagsAcc & ReFlags.KnownMask, out localeError, out asciiRequested);
            if (localeError)
                throw Raise.ValueError(ctx, "cannot use LOCALE flag with a str pattern");   // inline (?L)

            return new Result
            {
                NetPattern = sb.ToString(),
                Options = opts,
                Groups = new GroupMap(groupCount, nameIndex),
                EffectiveFlags = flagsAcc,
            };
        }

        private static bool IsPatternSpace(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v';
        }

        // A quantifier has just been written. Python 3.11's possessive form (a*+, a++, a?+, a{m,n}+) has
        // no .NET spelling, but an atomic group is exactly what it means: (?>a*) never gives back what it
        // matched. The atom is already in the buffer, so the open goes in at its start and the close at
        // the end. A lazy `?` is left alone — .NET spells that one the same way Python does.
        private static int Possessive(string p, int i, StringBuilder sb, int atomStart)
        {
            if (i >= p.Length || p[i] != '+')
                return i;
            sb.Insert(atomStart, "(?>");
            sb.Append(')');
            return i + 1;
        }

        // {m}, {m,}, {m,n} and Python's {,n} — which .NET reads as a literal brace, so the missing lower
        // bound is written out as 0. Returns the index after the repeat, or i when this '{' is a literal
        // (`{}`, `{a}`, an unterminated brace), which both engines treat the same way.
        private static int CountedRepeat(string p, int i, StringBuilder sb)
        {
            if (p[i] != '{')
                return i;
            int n = p.Length;
            int j = i + 1;
            int loStart = j;
            while (j < n && p[j] >= '0' && p[j] <= '9')
                j++;
            int loEnd = j;
            bool comma = j < n && p[j] == ',';
            if (comma)
                j++;
            int hiStart = j;
            while (j < n && p[j] >= '0' && p[j] <= '9')
                j++;
            int hiEnd = j;
            if (j >= n || p[j] != '}' || (loEnd == loStart && !comma))
                return i;
            sb.Append('{');
            if (loEnd > loStart)
                sb.Append(p, loStart, loEnd - loStart);
            else
                sb.Append('0');
            if (comma)
            {
                sb.Append(',');
                if (hiEnd > hiStart)
                    sb.Append(p, hiStart, hiEnd - hiStart);
            }
            sb.Append('}');
            return j + 1;
        }

        // \N{NAME}: the same table the lexer resolves string literals from (3.8+ in patterns). Emitted as
        // \uXXXX code units so no metacharacter and no VERBOSE whitespace can survive into the result;
        // a named sequence is several of them, which is what CPython produces too.
        private static int NamedChar(EvalContext ctx, string p, int i, StringBuilder sb)
        {
            int start = i + 3;                       // past \N{
            int j = p.IndexOf('}', start);
            if (j < 0)
                throw Err(ctx, "missing }, unterminated name");
            string name = p.Substring(start, j - start);
            string value;
            if (!Outbridge.PyLite.Syntax.PyUnicodeNames.TryLookup(name, out value))
                throw Err(ctx, "undefined character name '" + name + "'");
            foreach (char ch in value)
                sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
            return j + 1;
        }

        private static int Group(EvalContext ctx, string p, int i, StringBuilder sb,
            ref int groupCount, Dictionary<string, int> nameIndex, ref int flags, out bool scopedAscii)
        {
            scopedAscii = false;
            int n = p.Length;
            if (i + 1 < n && p[i + 1] == '?')
            {
                char c2 = i + 2 < n ? p[i + 2] : '\0';
                switch (c2)
                {
                    case ':':
                    case '=':
                    case '!':
                    case '#':
                        sb.Append('(').Append('?').Append(c2);   // non-capturing / lookahead / comment
                        return i + 3;
                    case '<':
                    {
                        char c3 = i + 3 < n ? p[i + 3] : '\0';
                        if (c3 == '=' || c3 == '!')
                        {
                            sb.Append("(?<").Append(c3);         // lookbehind
                            return i + 4;
                        }
                        throw Err(ctx, "unknown extension ?<" + c3);
                    }
                    case 'P':
                    {
                        char c3 = i + 3 < n ? p[i + 3] : '\0';
                        if (c3 == '<')
                            return NamedGroup(ctx, p, i + 4, sb, ref groupCount, nameIndex);
                        if (c3 == '=')
                            return NamedBackref(ctx, p, i + 4, sb, nameIndex);
                        throw Err(ctx, "unknown extension ?P" + c3);
                    }
                    case '>':
                        sb.Append("(?>");                        // atomic group: same syntax in .NET
                        return i + 3;
                    case '(':
                        throw Err(ctx, "conditional groups are not supported in this dialect (v1)");
                    default:
                        return InlineFlags(ctx, p, i, sb, ref flags, out scopedAscii);
                }
            }

            // A plain capturing group -> a synthetic named group.
            groupCount++;
            sb.Append("(?<").Append(GroupMap.Net(groupCount)).Append('>');
            return i + 1;
        }

        private static int NamedGroup(EvalContext ctx, string p, int start, StringBuilder sb,
            ref int groupCount, Dictionary<string, int> nameIndex)
        {
            int j = start;
            while (j < p.Length && p[j] != '>')
                j++;
            if (j >= p.Length)
                throw Err(ctx, "missing >, unterminated name");
            string name = p.Substring(start, j - start);
            if (!IsGroupName(name))
                throw Err(ctx, "bad character in group name '" + name + "'");
            if (nameIndex.ContainsKey(name))
                throw Err(ctx, "redefinition of group name '" + name + "'");
            groupCount++;
            nameIndex[name] = groupCount;
            sb.Append("(?<").Append(GroupMap.Net(groupCount)).Append('>');
            return j + 1;
        }

        private static int NamedBackref(EvalContext ctx, string p, int start, StringBuilder sb,
            Dictionary<string, int> nameIndex)
        {
            int j = start;
            while (j < p.Length && p[j] != ')')
                j++;
            if (j >= p.Length)
                throw Err(ctx, "missing ), unterminated name");
            string name = p.Substring(start, j - start);
            int idx;
            if (!nameIndex.TryGetValue(name, out idx))
                throw Err(ctx, "unknown group name '" + name + "'");
            sb.Append("\\k<").Append(GroupMap.Net(idx)).Append('>');
            return j + 1;
        }

        private static int InlineFlags(EvalContext ctx, string p, int i, StringBuilder sb, ref int flags, out bool scopedAscii)
        {
            scopedAscii = false;
            int n = p.Length;
            int j = i + 2;
            int acc = 0;
            while (j < n && IsFlagLetter(p[j]))
            {
                acc |= FlagOf(p[j]);
                j++;
            }
            if (j < n && p[j] == ')')
            {
                flags |= acc;         // global inline flags -> hoist, strip from the pattern
                return j + 1;
            }
            if (j < n && (p[j] == ':' || p[j] == '-'))
                return ScopedFlags(ctx, p, i + 2, j, sb, out scopedAscii);
            throw Err(ctx, "unknown extension ?" + (i + 2 < n ? p[i + 2].ToString() : ""));
        }

        // (?imsx-imsx:...) — .NET takes the same syntax, so the body is translated by the main loop and
        // the closing ) is copied as a literal, exactly as for (?:...). onEnd is the index after the
        // "on" letters that InlineFlags already scanned.
        private static int ScopedFlags(EvalContext ctx, string p, int onStart, int onEnd, StringBuilder sb, out bool asciiOn)
        {
            int n = p.Length;
            string on = MapScopedFlags(ctx, p, onStart, onEnd, false, out asciiOn);
            string off = "";
            int j = onEnd;
            if (j < n && p[j] == '-')
            {
                int k = j + 1;
                while (k < n && IsFlagLetter(p[k]))
                    k++;
                if (k == j + 1)
                    throw Err(ctx, "missing flag");
                bool unused;
                off = MapScopedFlags(ctx, p, j + 1, k, true, out unused);
                j = k;
            }
            if (j >= n || p[j] != ':')
                throw Err(ctx, "missing :, unterminated subpattern");
            sb.Append("(?").Append(on);
            if (off.Length > 0)
                sb.Append('-').Append(off);
            sb.Append(':');
            return j + 1;
        }

        // 'u' is .NET's default and drops out; 'a' is not a .NET flag at all - it is reported back so the
        // main loop applies the class rewrite to the group; 'L' has no meaning for a str pattern. Only
        // i/m/s/x may be turned off, as in CPython.
        private static string MapScopedFlags(EvalContext ctx, string p, int start, int end, bool off, out bool asciiOn)
        {
            asciiOn = false;
            var letters = new StringBuilder(end - start);
            for (int k = start; k < end; k++)
            {
                char c = p[k];
                if (c == 'i' || c == 'm' || c == 's' || c == 'x')
                {
                    letters.Append(c);
                    continue;
                }
                if (off)
                    throw Err(ctx, "bad inline flags: cannot turn off flags 'a', 'u' and 'L'");
                if (c == 'u')
                    continue;
                if (c == 'a')
                {
                    asciiOn = true;
                    continue;
                }
                throw Err(ctx, "bad inline flags: cannot use 'L' flag with a str pattern");
            }
            return letters.ToString();
        }

        private static int Escape(EvalContext ctx, string p, int i, StringBuilder sb, int groupCount, bool ascii)
        {
            if (i + 1 >= p.Length)
                throw Err(ctx, "bad escape (end of pattern)");
            char c = p[i + 1];
            if (ascii && AppendAsciiClass(sb, c))
                return i + 2;
            if (c == 'N' && i + 2 < p.Length && p[i + 2] == '{')
                return NamedChar(ctx, p, i, sb);
            if (c >= '1' && c <= '9')
            {
                int num = c - '0';
                if (num <= groupCount)
                    sb.Append("\\k<").Append(GroupMap.Net(num)).Append('>');
                else
                    sb.Append('\\').Append(c);   // not a valid backref yet; leave for .NET (octal/error)
                return i + 2;
            }
            if (c == 'Z')
            {
                sb.Append("\\z");                // Python \Z (end of string) -> .NET \z
                return i + 2;
            }
            if (PassLetters.IndexOf(c) >= 0)
            {
                sb.Append('\\').Append(c);
                return i + 2;
            }
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                throw Err(ctx, "bad escape \\" + c);   // unknown letter escape (modern 3.6+)
            sb.Append('\\').Append(c);                 // \ + non-letter -> escaped literal
            return i + 2;
        }

        // The re.A rewrite of one escape letter outside a character class; false = not a class escape.
        private static bool AppendAsciiClass(StringBuilder sb, char c)
        {
            switch (c)
            {
                case 'w': sb.Append('[').Append(AsciiWord).Append(']'); return true;
                case 'W': sb.Append("[^").Append(AsciiWord).Append(']'); return true;
                case 'd': sb.Append("[0-9]"); return true;
                case 'D': sb.Append("[^0-9]"); return true;
                case 's': sb.Append('[').Append(AsciiSpace).Append(']'); return true;
                case 'S': sb.Append("[^").Append(AsciiSpace).Append(']'); return true;
                case 'b':
                    sb.Append("(?:(?<=[").Append(AsciiWord).Append("])(?![").Append(AsciiWord)
                      .Append("])|(?<![").Append(AsciiWord).Append("])(?=[").Append(AsciiWord).Append("]))");
                    return true;
                case 'B':
                    sb.Append("(?:(?<=[").Append(AsciiWord).Append("])(?=[").Append(AsciiWord)
                      .Append("])|(?<![").Append(AsciiWord).Append("])(?![").Append(AsciiWord).Append("]))");
                    return true;
                default: return false;
            }
        }

        // The re.A rewrite INSIDE [...]: a positive escape contributes its members; a NEGATED one
        // (\D \W \S) contributes the set it excludes, which the class rebuild below turns into a
        // complement of its own.
        private static bool AsciiClassMember(char c, out string members, out bool negated)
        {
            negated = c == 'D' || c == 'W' || c == 'S';
            switch (c)
            {
                case 'w': case 'W': members = AsciiWord; return true;
                case 'd': case 'D': members = "0-9"; return true;
                case 's': case 'S': members = AsciiSpace; return true;
                default: members = null; return false;
            }
        }

        // The class is collected first, because under re.A a negated escape has no place inside a .NET
        // class (subtraction only works as the last element). With none present the class goes out as
        // written. With some, it is rebuilt exactly:
        //   [\D x]   = (not digit) or x       -> (?:[^0-9]|[x])
        //   [^\D x]  = digit and not x        -> (?:(?![x])(?=[0-9])[\s\S])
        private static int CharClass(EvalContext ctx, string p, int i, StringBuilder sb, bool ascii)
        {
            int n = p.Length;
            var pos = new StringBuilder();
            var negs = new List<string>();
            int j = i + 1;
            bool complement = false;
            if (j < n && p[j] == '^')
            {
                complement = true;
                j++;
            }
            if (j < n && p[j] == ']')
            {
                pos.Append("\\]");   // a ] right after [ or [^ is a literal (Python); escape for .NET
                j++;
            }
            while (j < n && p[j] != ']')
            {
                char c = p[j];
                if (c == '\\')
                {
                    if (j + 1 < n)
                    {
                        char e = p[j + 1];
                        string members;
                        bool negated;
                        if (e == 'N' && j + 2 < n && p[j + 2] == '{')
                        {
                            j = NamedChar(ctx, p, j, pos);   // [\N{DEGREE SIGN}] is a member like any other
                            continue;
                        }
                        if (ascii && AsciiClassMember(e, out members, out negated))
                        {
                            if (negated)
                                negs.Add(members);
                            else
                                pos.Append(members);
                        }
                        else
                            pos.Append('\\').Append(e);
                        j += 2;
                        continue;
                    }
                    pos.Append("\\\\");
                    j++;
                    continue;
                }
                if (c == '[')
                {
                    pos.Append("\\[");   // escape against .NET class subtraction/nesting
                    j++;
                    continue;
                }
                pos.Append(c);
                j++;
            }
            if (j >= n)
                throw Err(ctx, "unterminated character set");

            if (negs.Count == 0)
            {
                sb.Append('[');
                if (complement)
                    sb.Append('^');
                sb.Append(pos).Append(']');
                return j + 1;
            }
            sb.Append("(?:");
            if (!complement)
            {
                for (int k = 0; k < negs.Count; k++)
                {
                    if (k > 0)
                        sb.Append('|');
                    sb.Append("[^").Append(negs[k]).Append(']');
                }
                if (pos.Length > 0)
                    sb.Append("|[").Append(pos).Append(']');
            }
            else
            {
                if (pos.Length > 0)
                    sb.Append("(?![").Append(pos).Append("])");
                foreach (string neg in negs)
                    sb.Append("(?=[").Append(neg).Append("])");
                sb.Append("[\\s\\S]");
            }
            sb.Append(')');
            return j + 1;
        }

        private static bool IsFlagLetter(char c)
        {
            return c == 'i' || c == 'm' || c == 's' || c == 'x' || c == 'a' || c == 'L' || c == 'u';
        }

        private static int FlagOf(char c)
        {
            switch (c)
            {
                case 'i': return ReFlags.I;
                case 'm': return ReFlags.M;
                case 's': return ReFlags.S;
                case 'x': return ReFlags.X;
                case 'a': return ReFlags.A;
                case 'L': return ReFlags.L;
                default: return ReFlags.U;   // 'u'
            }
        }

        private static bool IsGroupName(string s)
        {
            if (s.Length == 0)
                return false;
            char c0 = s[0];
            if (!(c0 == '_' || (c0 >= 'A' && c0 <= 'Z') || (c0 >= 'a' && c0 <= 'z')))
                return false;
            for (int k = 1; k < s.Length; k++)
            {
                char c = s[k];
                if (!(c == '_' || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')))
                    return false;
            }
            return true;
        }

        private static ScriptException Err(EvalContext ctx, string message)
        {
            return Raise.Make(ctx, PyExceptionTypes.ReError, message);
        }
    }
}

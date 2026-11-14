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
        internal bool Repeated;   // some group sits under a quantifier: a .NET Match keeps all its captures
        internal bool LongLoop;   // some group sits under `*`, `+` or a large count: the runner's stack grows with the subject
        internal long LoopPasses = long.MaxValue;   // with LongLoop: the most passes such a loop makes (MaxValue: unbounded)

        public GroupMap(int count, Dictionary<string, int> nameIndex)
        {
            Count = count;
            NameIndex = nameIndex;
        }

        public static string Net(int index)
        {
            return "p" + index.ToString(CultureInfo.InvariantCulture);
        }

        // .NET's number for each group (0 = the whole match), fixed once the pattern is compiled, so a group
        // read is an array index and not a string build plus a name lookup per access. The anchored twins
        // wrap the same pattern in (?:...), which adds no group, so the numbers hold for them too.
        private int[] _netNumbers;

        internal void Bind(Regex re)
        {
            var numbers = new int[Count + 1];
            for (int i = 1; i <= Count; i++)
                numbers[i] = re.GroupNumberFromName(Net(i));
            _netNumbers = numbers;
        }

        // The exact translation of a pattern bound to its native one: the same groups in the same order, so the same numbers.
        internal void CheckSameNumbers(Regex re)
        {
            for (int i = 1; i <= Count; i++)
            {
                if (_netNumbers != null && re.GroupNumberFromName(Net(i)) != _netNumbers[i])
                    throw new System.InvalidOperationException("regex translations number group " + i + " differently");
            }
        }

        internal Group Of(Match m, int index)
        {
            if (_netNumbers != null)
                return m.Groups[_netNumbers[index]];
            return index == 0 ? m.Groups[0] : m.Groups[Net(index)];
        }
    }

    // Single-pass Python-pattern -> .NET-pattern translator. Handles group
    // renaming, (?P<>)/(?P=), \Z->\z, global inline-flag hoisting, and [ escaping in classes. Constructs
    // outside the v1 subset -> re.error. Scoped inline flags and atomic groups map straight onto .NET.
    internal static class RegexTranslator
    {
        internal const int MaxPatternChars = 4096;

        internal sealed class Result
        {
            public string NetPattern;
            public RegexOptions Options;
            public GroupMap Groups;
            public int EffectiveFlags;
            public string NativePattern;   // .NET's own \w and \b and IGNORECASE, or null: the same answers on a subject free
                                           // of WordRisk and of CaseRisk
            public char[] CaseRisk;        // sorted: the characters the IGNORECASE extras add, or null
        }

        // Letter escapes valid in both Python and .NET, passed through verbatim (with the backslash).
        private const string PassLetters = "AbBdDsSwWnrtfva0xuUz";   // \z: 3.14 spells \Z so too

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
            Result r = TranslateOnce(ctx, pattern, flags, (flags & ReFlags.A) != 0, false);
            // An inline (?a) is only known after the scan; redo the pass with the ASCII classes on.
            if ((flags & ReFlags.A) == 0 && (r.EffectiveFlags & ReFlags.A) != 0)
                r = TranslateOnce(ctx, pattern, flags, true, false);
            string native = TranslateOnce(ctx, pattern, flags, (r.EffectiveFlags & ReFlags.A) != 0, true).NetPattern;
            if (!string.Equals(native, r.NetPattern, System.StringComparison.Ordinal))
                r.NativePattern = native;
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

        // native: .NET's own \w, \b and IGNORECASE (no sre extras); otherwise the exact translation, whose extras are noted.
        private static Result TranslateOnce(EvalContext ctx, string pattern, int flags, bool ascii, bool native)
        {
            var sb = new StringBuilder(pattern.Length + 16);
            HashSet<char> risk = native ? null : new HashSet<char>();
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
            bool longRepeat = false;
            bool repeatedGroup = false;   // a group under a quantifier: its every capture is kept
            bool longLoop = false;
            // Per open group: the most passes any quantified group inside it makes; nested counts multiply
            // ((?:(?:(?:ab){1,64}){1,64}){1,64} loops 64^3 times, as far as (?:ab)* does).
            var passes = new Stack<long>();
            long mostPasses = 0;   // the largest count product past 64, ManyPasses when unbounded
            // VERBOSE comments are dropped here: read as pattern, their text opened groups and classes of its own.
            bool verbose = (flagsAcc & ReFlags.X) != 0;
            var verboseScopes = new Stack<KeyValuePair<int, bool>>();
            // IGNORECASE as sre has it (RegexIgnoreCase): for the whole pattern, or inside (?i:...), off in (?-i:...).
            bool ignore = (flagsAcc & ReFlags.I) != 0;
            var ignoreScopes = new Stack<KeyValuePair<int, bool>>();

            bool lastWordLiteral = false;   // the previous atom is a plain ASCII word character, unquantified
            while (i < n)
            {
                char c = pattern[i];
                bool wordBefore = lastWordLiteral;
                lastWordLiteral = false;
                if (verbose && c == '#')
                {
                    i = AfterComment(pattern, i);
                }
                else if (c == '\\')
                {
                    atomStart = sb.Length;
                    string bound = native || ascii ? null : ShortBoundary(pattern, i, wordBefore, verbose);
                    if (bound != null)
                    {
                        sb.Append(bound);
                        i += 2;
                    }
                    else
                    {
                        i = Escape(ctx, pattern, i, sb, groupCount, ascii, ignore, native, risk);
                    }
                }
                else if (c == '[')
                {
                    atomStart = sb.Length;
                    i = CharClass(ctx, pattern, i, sb, ascii, ignore, native, risk);
                }
                else if (c == '(')
                {
                    int open = i;
                    depth++;
                    groupStarts.Push(sb.Length);
                    passes.Push(1);
                    bool scopedAscii;
                    i = Group(ctx, pattern, i, sb, ref groupCount, nameIndex, ref flagsAcc, out scopedAscii);
                    if (pattern[i - 1] == ')')
                    {
                        // Closed already: a comment, global flags or a named backreference has no ')' to come.
                        depth--;
                        int emittedAt = groupStarts.Pop();
                        passes.Pop();
                        if (sb.Length > emittedAt)
                            atomStart = emittedAt;
                        if (verboseScopes.Count == 0)
                            verbose |= (flagsAcc & ReFlags.X) != 0;
                        if (ignoreScopes.Count == 0)
                            ignore |= (flagsAcc & ReFlags.I) != 0;
                        continue;
                    }
                    bool inner = ScopedFlag(pattern, open, verbose, 'x');
                    if (inner != verbose)
                    {
                        verboseScopes.Push(new KeyValuePair<int, bool>(depth, verbose));
                        verbose = inner;
                    }
                    bool innerIgnore = ScopedFlag(pattern, open, ignore, 'i');
                    if (innerIgnore != ignore)
                    {
                        ignoreScopes.Push(new KeyValuePair<int, bool>(depth, ignore));
                        ignore = innerIgnore;
                    }
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
                    if (verboseScopes.Count > 0 && verboseScopes.Peek().Key == depth)
                        verbose = verboseScopes.Pop().Value;
                    if (ignoreScopes.Count > 0 && ignoreScopes.Peek().Key == depth)
                        ignore = ignoreScopes.Pop().Value;
                    depth--;
                    atomStart = groupStarts.Count > 0 ? groupStarts.Pop() : sb.Length;
                    long made = passes.Count > 0 ? passes.Pop() : 1;
                    sb.Append(c);
                    i++;
                    int next = QuantifierAt(pattern, i, verbose);   // past VERBOSE gaps and (?#...) comments, as sre reads it
                    if (next >= 0 && next < pattern.Length && "*+?{".IndexOf(pattern[next]) >= 0)
                    {
                        repeatedGroup = true;
                        made = System.Math.Min(made * LoopPasses(pattern, next), ManyPasses);
                        NoteLoop(made, ref longLoop, ref mostPasses);
                    }
                    if (passes.Count > 0 && made > passes.Peek())
                    {
                        passes.Pop();
                        passes.Push(made);
                    }
                }
                else if (c == '*' || c == '+' || c == '?')
                {
                    BackrefLoop(sb, atomStart, pattern, i, passes, ref longLoop, ref mostPasses);
                    sb.Append(c);
                    i = Possessive(pattern, i + 1, sb, atomStart);
                }
                else
                {
                    if (c == '{')
                        BackrefLoop(sb, atomStart, pattern, i, passes, ref longLoop, ref mostPasses);
                    int after = CountedRepeat(pattern, i, sb, ref longRepeat);
                    if (after > i)
                    {
                        i = Possessive(pattern, after, sb, atomStart);
                        continue;
                    }
                    // In VERBOSE mode whitespace is not an atom, so a quantifier written after it still
                    // belongs to the atom before it (`a *+`).
                    if (!((flagsAcc & ReFlags.X) != 0 && IsPatternSpace(c)))
                        atomStart = sb.Length;
                    if (!(ignore && IgnoreCaseLiteral(sb, c, ascii, native, risk)))
                        sb.Append(c);
                    i++;
                    lastWordLiteral = IsAsciiWordChar(c) && !Quantified(pattern, i, verbose);
                }
            }

            bool localeError, asciiRequested;
            RegexOptions opts = ReFlags.ToRegexOptions(flagsAcc & ReFlags.KnownMask, out localeError, out asciiRequested);
            if (localeError)
                throw Raise.ValueError(ctx, "cannot use LOCALE flag with a str pattern");   // inline (?L)

            // .NET merges nested loops of one character into one ((?:(?:a{60,}){60,}){20} is a{72000,}, its table one
            // uninterruptible 6 s): a prefix that may be that long is stopped as a long lower bound is, where the
            // prefix search meets it.
            string net = sb.ToString();
            int stop = longRepeat || RegexFootprint.RepeatBound(net) > LongRepeat
                ? RegexFootprint.PrefixStopAt(net, (opts & RegexOptions.IgnorePatternWhitespace) != 0) : -1;
            if (stop >= 0)
                net = net.Insert(stop, PrefixStop);
            return new Result
            {
                NetPattern = net,
                Options = opts,
                Groups = new GroupMap(groupCount, nameIndex)
                {
                    Repeated = repeatedGroup,
                    LongLoop = longLoop,
                    LoopPasses = mostPasses >= ManyPasses ? long.MaxValue : mostPasses,
                },
                EffectiveFlags = flagsAcc,
                CaseRisk = SortedRisk(risk),
            };
        }

        private static char[] SortedRisk(HashSet<char> risk)
        {
            if (risk == null || risk.Count == 0)
                return null;
            var a = new char[risk.Count];
            risk.CopyTo(a);
            System.Array.Sort(a);
            return a;
        }

        // Whether the subject holds a character the pattern's IGNORECASE extras match: without one, .NET's own folding
        // (the native pattern) gives sre's answers, since an extra changes a comparison only against itself.
        internal static bool HasCaseRisk(string s, char[] risk)
        {
            if (risk == null)
                return false;
            char min = risk[0];
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] >= min && System.Array.BinarySearch(risk, s[i]) >= 0)
                    return true;
            }
            return false;
        }

        // \b or \B next to a plain ASCII word character (a word character under IGNORECASE too) tests one side only:
        // the other side is that character. A leading lookbehind keeps .NET's literal-prefix scan, which the full
        // conditional defeats (300 searches for \bname\b over 2 KB: 40 ms instead of 2). Null: the full form is needed.
        private static string ShortBoundary(string p, int i, bool wordBefore, bool verbose)
        {
            if (i + 1 >= p.Length || (p[i + 1] != 'b' && p[i + 1] != 'B'))
                return null;
            bool wordAfter = i + 2 < p.Length && IsAsciiWordChar(p[i + 2]) && !Quantified(p, i + 3, verbose);
            bool b = p[i + 1] == 'b';
            if (wordAfter)
                return (b ? "(?<!" : "(?<=") + WordAtom + ")";
            if (wordBefore)
                return (b ? "(?!" : "(?=") + WordAtom + ")";
            return null;
        }

        private static bool IsAsciiWordChar(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
        }

        // A quantifier (any, `+` included: kept simple) at p[i] makes the atom before it optional or repeated; sre reads
        // it past `(?#...)` comments (and VERBOSE gaps) as well: `\ba(?#c)*` repeats the `a`.
        private static bool Quantified(string p, int i, bool verbose)
        {
            int j = QuantifierAt(p, i, verbose);
            return j < 0 || (j < p.Length && (p[j] == '*' || p[j] == '+' || p[j] == '?' || p[j] == '{'));
        }

        // Where the next quantifier for the atom before p[i] would be: past VERBOSE gaps and (?#...) comments. -1 for an
        // unterminated comment (an error later; callers keep their safe form).
        private static int QuantifierAt(string p, int i, bool verbose)
        {
            int j = i;
            while (true)
            {
                if (verbose)
                    j = SkipVerboseGap(p, j);
                if (j + 2 < p.Length && p[j] == '(' && p[j + 1] == '?' && p[j + 2] == '#')
                {
                    int close = p.IndexOf(')', j + 3);
                    if (close < 0)
                        return -1;
                    j = close + 1;
                    continue;
                }
                return j;
            }
        }

        private static void NoteLoop(long made, ref bool longLoop, ref long mostPasses)
        {
            if (made <= 64)
                return;
            longLoop = true;
            if (made > mostPasses)
                mostPasses = made;
        }

        // A backreference under a quantifier loops as a group does, a pass on the backtracking stack each: (a)\1* over
        // 1M chars kept 505 MB for 1 MB charged. It multiplies with the groups around it.
        private static void BackrefLoop(StringBuilder sb, int atomStart, string p, int i, Stack<long> passes, ref bool longLoop, ref long mostPasses)
        {
            if (atomStart + 3 > sb.Length || sb[atomStart] != '\\' || sb[atomStart + 1] != 'k' || sb[atomStart + 2] != '<')
                return;
            long made = LoopPasses(p, i);
            NoteLoop(made, ref longLoop, ref mostPasses);
            if (passes.Count > 0 && made > passes.Peek())
            {
                passes.Pop();
                passes.Push(made);
            }
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

        // .NET builds a Boyer-Moore table for the literal a pattern starts with, in time quadratic in its
        // length, and a leading a{m} is a literal of m chars: re.compile('a{100000}') spent ~11 s per Regex
        // (three per pattern) in one uninterruptible call. An empty atomic group where the prefix search meets the
        // loop matches nothing and ends that search (RegexFCD.Prefix stops inside a Greedy node). Only a pattern with
        // a lower bound past the longest literal a pattern can spell gets it, so every other one keeps its prefix scan.
        private const string PrefixStop = "(?>)";
        private const int LongRepeat = MaxPatternChars;

        // The most passes the quantifier at p[i] makes over its group, each pass on the backtracking stack: `*`, `+`
        // and `{n,}` as many as the subject allows (ManyPasses), `?` and a literal brace one. Past 64 passes, nested
        // counts multiplied, the stack follows the subject (GroupMap.LongLoop).
        private const long ManyPasses = 1L << 20;

        private static long LoopPasses(string p, int i)
        {
            if (p[i] == '*' || p[i] == '+')
                return ManyPasses;
            if (p[i] != '{')
                return 1;
            int n = p.Length;
            int j = i + 1;
            int lo = 0, hi = 0;
            while (j < n && p[j] >= '0' && p[j] <= '9')
            {
                lo = (int)System.Math.Min(lo * 10L + (p[j] - '0'), ManyPasses);
                j++;
            }
            bool comma = j < n && p[j] == ',';
            if (!comma)
            {
                hi = lo;
            }
            else
            {
                j++;
                int hiStart = j;
                while (j < n && p[j] >= '0' && p[j] <= '9')
                {
                    hi = (int)System.Math.Min(hi * 10L + (p[j] - '0'), ManyPasses);
                    j++;
                }
                if (j == hiStart)
                    hi = int.MaxValue;   // {n,}
            }
            if (j >= n || p[j] != '}')
                return 1;
            return hi == int.MaxValue ? ManyPasses : System.Math.Max(hi, 1);
        }

        // {m}, {m,}, {m,n} and Python's {,n} — which .NET reads as a literal brace, so the missing lower
        // bound is written out as 0. Returns the index after the repeat, or i when this '{' is a literal
        // (`{}`, `{a}`, an unterminated brace), which both engines treat the same way.
        private static int CountedRepeat(string p, int i, StringBuilder sb, ref bool longRepeat)
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
            int lo = 0;
            for (int k = loStart; k < loEnd && lo <= LongRepeat; k++)
                lo = lo * 10 + (p[k] - '0');
            if (lo > LongRepeat)
                longRepeat = true;
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
                        sb.Append('(').Append('?').Append(c2);   // non-capturing / lookahead
                        return i + 3;
                    case '#':
                    {
                        int close = p.IndexOf(')', i + 3);   // a comment ends at the first ')' and is dropped
                        if (close < 0)
                            throw Err(ctx, "missing ), unterminated comment");
                        return close + 1;
                    }
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

        private static int AfterComment(string p, int hash)
        {
            int end = p.IndexOf('\n', hash);
            return end < 0 ? p.Length : end + 1;
        }

        private static int SkipVerboseGap(string p, int i)
        {
            while (i < p.Length)
            {
                if (p[i] == '#')
                    i = AfterComment(p, i);
                else if (IsPatternSpace(p[i]))
                    i++;
                else
                    break;
            }
            return i;
        }

        // A literal under IGNORECASE: with what sre matches beyond .NET (Unicode), or exact where .NET would fold a
        // non-ASCII character (re.A). False: the character goes out as it is.
        private static bool IgnoreCaseLiteral(StringBuilder sb, char c, bool ascii, bool native, HashSet<char> risk)
        {
            if (ascii)
            {
                if (!RegexIgnoreCase.AsciiModeNeedsExact(c))
                    return false;
                sb.Append("(?-i:[").Append(RegexIgnoreCase.AsciiBothCases(c)).Append("])");
                return true;
            }
            if (native)
                return false;
            string extras = RegexIgnoreCase.Extras(c);
            if (extras == null)
                return false;
            sb.Append('[').Append(RegexIgnoreCase.Member(c)).Append(extras).Append(']');
            RegexIgnoreCase.NoteRisk(risk, c);
            return true;
        }

        // Whether the body of the group opened at `open` has the flag: (?x:...) turns it on, (?-x:...) off.
        private static bool ScopedFlag(string p, int open, bool current, char flag)
        {
            if (open + 1 >= p.Length || p[open + 1] != '?')
                return current;
            bool off = false;
            bool result = current;
            int j = open + 2;
            for (; j < p.Length && (IsFlagLetter(p[j]) || p[j] == '-'); j++)
            {
                if (p[j] == '-')
                    off = true;
                else if (p[j] == flag)
                    result = !off;
            }
            return j < p.Length && p[j] == ':' ? result : current;
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
            if (!Runtime.Values.StrMethods.IsIdentifier(name))
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

        private static int Escape(EvalContext ctx, string p, int i, StringBuilder sb, int groupCount, bool ascii, bool ignore, bool native, HashSet<char> risk)
        {
            if (i + 1 >= p.Length)
                throw Err(ctx, "bad escape (end of pattern)");
            char c = p[i + 1];
            if (ascii && ignore && (c == 'w' || c == 'W' || c == 'b' || c == 'B'))
            {
                // re.A under IGNORECASE: the set already holds both cases, and .NET's folding would take the Kelvin sign
                // as k and U+0130 as i
                sb.Append("(?-i:");
                AppendAsciiClass(sb, c);
                sb.Append(')');
                return i + 2;
            }
            if (ascii && AppendAsciiClass(sb, c))
                return i + 2;
            if (!ascii && AppendUnicodeClass(sb, c, native))
                return i + 2;
            char escaped;
            int after;
            if (ignore && TryHexEscape(p, i, out escaped, out after) && IgnoreCaseLiteral(sb, escaped, ascii, native, risk))
                return after;   // \x6b, K fold as the letter written plainly
            if (c == 'N' && i + 2 < p.Length && p[i + 2] == '{')
                return NamedChar(ctx, p, i, sb);
            if (c >= '1' && c <= '9')
                return NumberEscape(p, i, sb, groupCount);
            if (c == 'U' && AppendWide(sb, p, i, true))
                return i + 10;
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

        // sre_parse's order: two digits, and a third octal one after two octal ones makes an octal escape; what
        // is left is a group of one or two digits. RegexValidator has refused the out-of-range ones.
        private static int NumberEscape(string p, int i, StringBuilder sb, int groupCount)
        {
            int num = p[i + 1] - '0', end = i + 2;
            if (end < p.Length && p[end] >= '0' && p[end] <= '9')
            {
                int second = p[end] - '0';
                if (num < 8 && second < 8 && end + 1 < p.Length && p[end + 1] >= '0' && p[end + 1] <= '7')
                {
                    int value = num * 64 + second * 8 + (p[end + 1] - '0');
                    sb.Append("\\u").Append(value.ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                    return end + 2;
                }
                num = num * 10 + second;
                end++;
            }
            if (num <= groupCount)
                sb.Append("\\k<").Append(GroupMap.Net(num)).Append('>');
            else
                sb.Append('\\').Append(p, i + 1, end - i - 1);   // not a valid backref yet; leave for .NET (octal/error)
            return end;
        }

        // Python's Unicode \s and \w, which .NET's miss: \s also holds \x1c-\x1f, \w is str.isalnum() or '_'
        // (letters and every number, no combining marks) by the engine's own tables. .NET Framework's \p{L}\p{N}
        // (Unicode 8) lacked 267 BMP letters and holds U+1885 and U+1886, marks since Unicode 9: the first are added,
        // the second taken out where .NET allows it (not inside a class the script writes: deviations.md).
        private static readonly string[] WordDiff = BuildWordDiff();   // [0] letters to add, [1] marks to take out
        private static readonly string UnicodeWord = @"\p{L}\p{N}_" + WordDiff[0];
        private static readonly string WordAtom = WordDiff[1].Length == 0
            ? "[" + UnicodeWord + "]" : "[" + UnicodeWord + "-[" + WordDiff[1] + "]]";
        private static readonly string NonWordAtom = WordDiff[1].Length == 0
            ? "[^" + UnicodeWord + "]" : "(?:[^" + UnicodeWord + "]|[" + WordDiff[1] + "])";
        // The word set as a class body with the subtraction last, for a class the script writes (CharClass rebuilds it).
        private static readonly string WordSet = UnicodeWord + "-[" + WordDiff[1] + "]";
        private const string UnicodeSpace = @"\s\x1c-\x1f";

        // A subject with none of these characters gets the same answers from .NET's own \w, \W, \b, \B (Result.NativePattern)
        // as from the exact word set; built on first use.
        internal static bool HasWordRisk(string s)
        {
            bool[] risky = WordRiskTable.Risky;
            for (int i = 0; i < s.Length; i++)
            {
                if (risky[s[i]])
                    return true;
            }
            return false;
        }

        // Measured on the regexes themselves rather than read from the categories, with and without IGNORECASE (which folds
        // the subject's character before the test): a character is risky where .NET's \w or its \b word test and the exact
        // word set disagree on it. No timeout: a host's process-wide REGEX_DEFAULT_MATCH_TIMEOUT must not fail the type for good.
        private static class WordRiskTable
        {
            internal static readonly bool[] Risky = Build();

            private static bool[] Build()
            {
                var all = new char[0x10000];
                for (int c = 0; c < all.Length; c++)
                    all[c] = (char)c;
                string text = new string(all);
                var risky = new bool[0x10000];
                foreach (RegexOptions o in new[] { RegexOptions.CultureInvariant, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase })
                {
                    bool[] exact = Runs(text, "(?:" + WordAtom + ")+", o);
                    bool[] native = Runs(text, @"\w+", o);
                    bool[] boundary = BoundaryWord(text, o);
                    for (int c = 0; c < risky.Length; c++)
                        risky[c] |= exact[c] != native[c] || exact[c] != boundary[c];
                }
                return risky;
            }

            private static bool[] Runs(string text, string pattern, RegexOptions o)
            {
                var member = new bool[text.Length];
                for (Match m = new Regex(pattern, o, Regex.InfiniteMatchTimeout).Match(text); m.Success; m = m.NextMatch())
                {
                    for (int k = m.Index; k < m.Index + m.Length; k++)
                        member[k] = true;
                }
                return member;
            }

            // .NET's \b word test (not quite its \w): a boundary toggles it, and the text starts after a non-word edge
            private static bool[] BoundaryWord(string text, RegexOptions o)
            {
                var at = new bool[text.Length + 1];
                for (Match m = new Regex(@"\b", o, Regex.InfiniteMatchTimeout).Match(text); m.Success; m = m.NextMatch())
                    at[m.Index] = true;
                var word = new bool[text.Length];
                bool inWord = false;
                for (int k = 0; k < text.Length; k++)
                {
                    if (at[k])
                        inWord = !inWord;
                    word[k] = inWord;
                }
                return word;
            }
        }

        private static string[] BuildWordDiff()
        {
            var add = new StringBuilder();
            var take = new StringBuilder();
            int addStart = -1, takeStart = -1;
            for (int c = 0; c <= 0x10000; c++)
            {
                bool inAdd = false, inTake = false;
                if (c < 0x10000 && (c < 0xD800 || c > 0xDFFF))
                {
                    bool python = c == '_' || Runtime.Values.PyUnicodeClass.IsAlpha(c) || Runtime.Values.PyUnicodeClass.IsNumeric(c);
                    bool net = c == '_' || char.IsLetter((char)c) || char.IsNumber((char)c);
                    inAdd = python && !net;
                    inTake = net && !python;
                }
                CloseRange(add, ref addStart, c, inAdd);
                CloseRange(take, ref takeStart, c, inTake);
            }
            return new[] { add.ToString(), take.ToString() };
        }

        private static void CloseRange(StringBuilder sb, ref int start, int c, bool inside)
        {
            if (inside)
            {
                if (start < 0)
                    start = c;
                return;
            }
            if (start < 0)
                return;
            AppendClassChar(sb, start);
            if (c - 1 > start)
            {
                sb.Append('-');
                AppendClassChar(sb, c - 1);
            }
            start = -1;
        }

        // The character itself where a class reads it as itself (.NET parses it faster than an escape, and the word
        // class is copied into every \w and \b), else the escape.
        private static void AppendClassChar(StringBuilder sb, int c)
        {
            char ch = (char)c;
            if (c < 0x80 || char.IsWhiteSpace(ch) || char.IsControl(ch) || char.IsSurrogate(ch))
                sb.Append("\\u").Append(c.ToString("x4", CultureInfo.InvariantCulture));
            else
                sb.Append(ch);
        }

        private static bool UnicodeClassMember(char c, bool native, out string members, out bool negated)
        {
            negated = c == 'W' || c == 'S';
            switch (c)
            {
                case 'w': case 'W': members = UnicodeWord; return !native;   // native: kept as written
                case 's': case 'S': members = UnicodeSpace; return true;
                default: members = null; return false;
            }
        }

        private static bool AppendUnicodeClass(StringBuilder sb, char c, bool native)
        {
            if (native && (c == 'w' || c == 'W' || c == 'b' || c == 'B'))
                return false;   // passed through as .NET's own
            switch (c)
            {
                case 'w': sb.Append(WordAtom); return true;
                case 'W': sb.Append(NonWordAtom); return true;
                case 's': sb.Append('[').Append(UnicodeSpace).Append(']'); return true;
                case 'S': sb.Append("[^").Append(UnicodeSpace).Append(']'); return true;
                // .NET's own \b counts combining marks and connectors as word characters, and not every number. The
                // conditional copies the class three times, not four: .NET parses every copy anew.
                case 'b':
                    sb.Append("(?(?<=").Append(WordAtom).Append(")(?!").Append(WordAtom).Append(")|(?=").Append(WordAtom).Append("))");
                    return true;
                case 'B':
                    sb.Append("(?(?<=").Append(WordAtom).Append(")(?=").Append(WordAtom).Append(")|(?!").Append(WordAtom).Append("))");
                    return true;
                default: return false;
            }
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
        private static int CharClass(EvalContext ctx, string p, int i, StringBuilder sb, bool ascii, bool ignore, bool native, HashSet<char> risk)
        {
            int n = p.Length;
            var pos = new StringBuilder();
            var negs = new List<string>();
            var cased = ignore ? new List<KeyValuePair<char, char>>() : null;   // the plain members and ranges, for IGNORECASE
            bool escaped = false;   // a member written as an escape: not in `cased`, so the class keeps .NET's folding
            bool wordMember = false;   // re.A \w or \W among the members
            var atoms = new List<string>();   // whole atoms that cannot join the class body (the Unicode word set)
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
                            escaped = true;
                            j = NamedChar(ctx, p, j, pos);   // [\N{DEGREE SIGN}] is a member like any other
                            continue;
                        }
                        if (!ascii && !native && (e == 'w' || e == 'W') && WordDiff[1].Length > 0)
                        {
                            // the subtraction cannot sit among other members: \w joins as an alternative, \W as a
                            // negated set the rebuild below tests by lookahead
                            if (e == 'w')
                                atoms.Add(WordAtom);
                            else
                                negs.Add(WordSet);
                            j += 2;
                            continue;
                        }
                        if (ascii && (e == 'w' || e == 'W'))
                            wordMember = true;
                        if (ascii ? AsciiClassMember(e, out members, out negated) : UnicodeClassMember(e, native, out members, out negated))
                        {
                            if (negated)
                                negs.Add(members);
                            else
                                pos.Append(members);
                        }
                        else
                        {
                            int k = j + 2;
                            if (e == 'U' && AppendWide(pos, p, j, false))   // a BMP one: an astral member stays refused
                            {
                                k = j + 10;
                            }
                            else
                            {
                                pos.Append('\\').Append(e);
                                // its digits go with it: read as plain members, \x7a-~ was a range 'a'-'~'
                                int more = e == 'x' ? 2 : e == 'u' ? 4 : (e >= '0' && e <= '7') ? 2 : 0;
                                for (; more > 0 && k < n && IsEscapeDigit(p[k], e); more--, k++)
                                    pos.Append(p[k]);
                            }
                            // a \xHH or \uHHHH member is the letter it names (not one end of a range, which stays .NET's)
                            char hex;
                            int hexEnd;
                            bool rangeEnd = k + 1 < n && p[k] == '-' && p[k + 1] != ']';
                            if (cased != null && !rangeEnd && TryHexEscape(p, j, out hex, out hexEnd) && hexEnd == k)
                                cased.Add(new KeyValuePair<char, char>(hex, hex));
                            else
                                escaped = true;
                            j = k;
                            continue;
                        }
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
                if (cased != null && j + 2 < n && p[j + 1] == '-' && p[j + 2] != ']' && p[j + 2] != '\\' && p[j + 2] != '[')
                {
                    cased.Add(new KeyValuePair<char, char>(c, p[j + 2]));   // a range goes out as it was written
                    pos.Append(c).Append('-').Append(p[j + 2]);
                    j += 3;
                    continue;
                }
                if (cased != null && j + 2 < n && p[j + 1] == '-' && p[j + 2] == '\\')
                    escaped = true;   // a range to an escape is not tracked: the class keeps .NET's folding
                if (cased != null && c == '-' && j + 1 < n && p[j + 1] == ']')
                {
                    pos.Append("\\-");   // a last '-' stays a literal when extra members follow it
                    j++;
                    continue;
                }
                if (cased != null)
                    cased.Add(new KeyValuePair<char, char>(c, c));
                pos.Append(c);
                j++;
            }
            if (j >= n)
                throw Err(ctx, "unterminated character set");

            bool exact = cased != null && (cased.Count > 0 || wordMember) && IgnoreCaseMembers(pos, cased, ascii, escaped, wordMember, native, risk);
            int start = sb.Length;
            if (negs.Count == 0 && atoms.Count == 0)
            {
                sb.Append('[');
                if (complement)
                    sb.Append('^');
                sb.Append(pos).Append(']');
                return Exact(sb, start, exact, j + 1);
            }
            sb.Append("(?:");
            if (!complement)
            {
                bool first = true;
                foreach (string neg in negs)
                {
                    if (!first)
                        sb.Append('|');
                    first = false;
                    // a set with a subtraction is negated by lookahead: [^a-[b]] would be (not a) without b
                    if (neg.Contains("-["))
                        sb.Append("(?![").Append(neg).Append("])[\\s\\S]");
                    else
                        sb.Append("[^").Append(neg).Append(']');
                }
                foreach (string atom in atoms)
                {
                    if (!first)
                        sb.Append('|');
                    first = false;
                    sb.Append(atom);
                }
                if (pos.Length > 0)
                    sb.Append(first ? "[" : "|[").Append(pos).Append(']');
            }
            else
            {
                if (pos.Length > 0)
                    sb.Append("(?![").Append(pos).Append("])");
                foreach (string atom in atoms)
                    sb.Append("(?!").Append(atom).Append(')');
                foreach (string neg in negs)
                    sb.Append("(?=[").Append(neg).Append("])");
                sb.Append("[\\s\\S]");
            }
            sb.Append(')');
            return Exact(sb, start, exact, j + 1);
        }

        // \xHH, \uHHHH or a BMP \UHHHHHHHH at i: the character and the index after it.
        private static bool TryHexEscape(string p, int i, out char value, out int after)
        {
            value = '\0';
            after = i;
            if (i + 1 >= p.Length)
                return false;
            char e = p[i + 1];
            int digits = e == 'x' ? 2 : e == 'u' ? 4 : e == 'U' ? 8 : 0;
            if (digits == 0 || i + 2 + digits > p.Length)
                return false;
            int v = 0;
            for (int k = i + 2; k < i + 2 + digits; k++)
            {
                if (!IsEscapeDigit(p[k], 'x'))
                    return false;
                v = v * 16 + System.Convert.ToInt32(p[k].ToString(), 16);
                if (v > 0xFFFF)
                    return false;   // astral: .NET sees two halves, left as written
            }
            value = (char)v;
            after = i + 2 + digits;
            return true;
        }

        // \UHHHHHHHH at i, which .NET does not read: as \uHHHH, or as the surrogate pair .NET's strings hold for an
        // astral one, grouped so a quantifier takes both. False when it is not eight hex digits up to U+10FFFF (RegexValidator
        // has refused those already), and for an astral one in a class (outside: group false): two members there would make
        // [\x80-\U0010ffff] a range to the high half, so it is left to .NET's refusal as before.
        private static bool AppendWide(StringBuilder sb, string p, int i, bool group)
        {
            if (i + 10 > p.Length)
                return false;
            int v = 0;
            for (int k = i + 2; k < i + 10; k++)
            {
                if (!IsEscapeDigit(p[k], 'x'))
                    return false;
                v = v * 16 + System.Convert.ToInt32(p[k].ToString(), 16);
                if (v > 0x10FFFF)
                    return false;
            }
            if (v <= 0xFFFF)
            {
                AppendUnit(sb, v);
            }
            else
            {
                if (!group)
                    return false;
                v -= 0x10000;
                sb.Append("(?:");
                AppendUnit(sb, 0xD800 + (v >> 10));
                AppendUnit(sb, 0xDC00 + (v & 0x3FF));
                sb.Append(')');
            }
            return true;
        }

        private static void AppendUnit(StringBuilder sb, int unit)
        {
            sb.Append("\\u").Append(unit.ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
        }

        private static bool IsEscapeDigit(char c, char escape)
        {
            if (escape >= '0' && escape <= '7')
                return c >= '0' && c <= '7';
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        // re.A under IGNORECASE: the class written from `start` is compared case-sensitively.
        private static int Exact(StringBuilder sb, int start, bool exact, int next)
        {
            if (exact)
            {
                sb.Insert(start, "(?-i:");
                sb.Append(')');
            }
            return next;
        }

        // The members of a class under IGNORECASE (RegexIgnoreCase). Unicode: what sre matches beyond .NET for each.
        // re.A: true when .NET would fold one of them with a non-ASCII character; then the class goes out
        // case-sensitive, with both cases of its ASCII letters, unless an escaped member (not tracked) would lose its
        // folding. Each character's extras go in once, however many ranges hold it.
        private static bool IgnoreCaseMembers(StringBuilder pos, List<KeyValuePair<char, char>> cased, bool ascii, bool escaped, bool wordMember,
            bool native, HashSet<char> risk)
        {
            if (!ascii)
            {
                if (native)
                    return false;   // .NET's own folding: the subject decides (HasCaseRisk)
                var added = new HashSet<char>();
                foreach (KeyValuePair<char, char> r in cased)
                {
                    if (r.Key == r.Value)
                    {
                        string extras = RegexIgnoreCase.Extras(r.Key);
                        if (extras != null && added.Add(r.Key))
                        {
                            pos.Append(extras);
                            RegexIgnoreCase.NoteRisk(risk, r.Key);
                        }
                        continue;
                    }
                    RegexIgnoreCase.AppendRangeExtras(pos, r.Key, r.Value, added, risk);
                }
                return false;
            }
            if (escaped)
                return false;
            bool needs = wordMember;   // an ASCII word set in the class: .NET would fold the Kelvin sign into it
            foreach (KeyValuePair<char, char> r in cased)
            {
                for (int c = r.Key; c <= r.Value && !needs; c++)
                    needs = RegexIgnoreCase.AsciiModeNeedsExact((char)c);
            }
            if (!needs)
                return false;
            foreach (KeyValuePair<char, char> r in cased)
            {
                AddOtherCase(pos, r.Key, r.Value, 'a', 'z', -32);
                AddOtherCase(pos, r.Key, r.Value, 'A', 'Z', 32);
            }
            return true;
        }

        private static void AddOtherCase(StringBuilder pos, char lo, char hi, char from, char to, int shift)
        {
            char a = lo > from ? lo : from, b = hi < to ? hi : to;
            if (a > b)
                return;
            pos.Append(RegexIgnoreCase.Member((char)(a + shift))).Append('-').Append(RegexIgnoreCase.Member((char)(b + shift)));
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

        private static ScriptException Err(EvalContext ctx, string message)
        {
            return Raise.Make(ctx, PyExceptionTypes.ReError, message);
        }
    }
}

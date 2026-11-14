using System;
using System.Collections.Generic;

namespace Outbridge.PyLite.Modules.Support
{
    // What a .NET Framework 4.8 Regex keeps, read off the pattern it was given: measured over kept regexes with
    // GC.GetTotalMemory and by reflection on the runner's arrays, each token at or above what was measured.
    internal static class RegexFootprint
    {
        // The Regex, its translation and the PatternValue, and 2 B a character for the text .NET keeps.
        private const long PatternBytes = 1536;
        private const int TextBytes = 2;

        // Its code: 6 B a literal character (long literals 4.5-5.6), 16 a token that is no literal (an anchor, a class
        // escape, a group, a quantifier), 48 a branch (500 four-character alternatives: 74 B each), 8 a member of a
        // class, a long class kept once however often it is written (2,000 isolated members: 8.2 B each; a range ~0).
        private const int LiteralBytes = 6, TokenBytes = 16, BranchBytes = 48, MemberBytes = 8, SharedClassMembers = 32;

        // The literal run a match starts with gets a Boyer-Moore table: 1 KB for each 256-character page past ASCII it
        // touches and 2 KB for the page index (215 pages: 229 KB a regex, charged 5 KB before review 14).
        private const long PageIndexBytes = 2048, PageBytes = 1024;

        // A pattern that starts with c{m} has m c's for that literal, and an int each in the table: 6 B a repeat
        // (a{1500}: 10.2 KB a regex, charged 1.6 KB before review 15). Building the table takes time quadratic in m:
        // 22 ms for a{4000}, about m * m / 300 steps of the loop's pace, charged at m * m / 512.
        private const long RepeatBytes = 6, RepeatStepsDivisor = 512;

        internal static long Held(string p)
        {
            return Held(p, LeadingRepeat(p, false));
        }

        // What the text p holds once built, the text .NET shares with a string already held.
        internal static long Text(string p)
        {
            return (long)TextBytes * p.Length;
        }

        internal static int RepeatSteps(int repeat)
        {
            return (int)Math.Min(int.MaxValue, (long)repeat * repeat / RepeatStepsDivisor);
        }

        internal static long Held(string p, int repeat)
        {
            return Held(p, repeat, false);
        }

        // repeat: LeadingRepeat(p) for the options p is built with; ignoreCase: built with IgnoreCase.
        internal static long Held(string p, int repeat, bool ignoreCase)
        {
            return Held(p, repeat, ignoreCase, false);
        }

        // verbose: built with IgnorePatternWhitespace.
        internal static long Held(string p, int repeat, bool ignoreCase, bool verbose)
        {
            long bytes = PatternBytes + Text(p) + PrefixPageBytes(p, ignoreCase, verbose, repeat > 0) + RepeatBytes * repeat;
            HashSet<string> classes = null;
            for (int i = 0; i < p.Length; i++)
            {
                char c = p[i];
                if (c == '\\')
                {
                    bytes += IsLiteralEscape(p, i) ? LiteralBytes : TokenBytes;
                    i += EscapeLength(p, i) - 1;
                }
                else if (c == '[')
                {
                    int end = ClassEnd(p, i);
                    long members = end - i - 1;
                    if (members > SharedClassMembers)
                    {
                        if (classes == null)
                            classes = new HashSet<string>(StringComparer.Ordinal);
                        if (!classes.Add(p.Substring(i, end - i)))
                            members = 0;
                    }
                    bytes += TokenBytes + MemberBytes * members;
                    i = end;
                }
                else if (c == '|')
                {
                    bytes += BranchBytes;
                }
                else
                {
                    bytes += IsMeta(c) ? TokenBytes : LiteralBytes;
                }
            }
            return bytes;
        }

        // The runner a regex caches once it has run: its stacks are sized by the pattern's backtracking nodes, not by
        // the subject. Measured: ~600 B with none, 64 B a quantifier (128 on a group), 128 B a capturing or atomic group
        // or a branch, 256 B a lookaround, ~384 B a conditional; (?:, inline options and comments add nothing.
        private const long RunnerBaseBytes = 512, QuantifierBytes = 64, GroupBytes = 128, LookaroundBytes = 256,
            ConditionalBytes = 384;

        internal static long Runner(string p)
        {
            long bytes = RunnerBaseBytes;
            bool closed = false;   // the token before is a group's ')'
            for (int i = 0; i < p.Length; i++)
            {
                char c = p[i];
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '[')
                {
                    i = ClassEnd(p, i);
                }
                else if (c == '(' && At(p, i + 1) != '?')
                {
                    bytes += GroupBytes;
                }
                else if (c == '(')
                {
                    i++;   // the '?' is no quantifier
                    char k = At(p, i + 1);
                    if (k == '#')
                    {
                        int end = p.IndexOf(')', i);
                        i = end < 0 ? p.Length : end;
                    }
                    else if (k == '=' || k == '!' || (k == '<' && (At(p, i + 2) == '=' || At(p, i + 2) == '!')))
                    {
                        bytes += LookaroundBytes;
                    }
                    else if (k == '<' || k == '\'' || k == '>')
                    {
                        bytes += GroupBytes;
                    }
                    else if (k == '(')
                    {
                        bytes += ConditionalBytes;
                    }
                }
                else if (c == '|')
                {
                    bytes += GroupBytes;
                }
                else if (c == '?' || c == '*' || c == '+' || c == '{')
                {
                    bytes += closed ? GroupBytes : QuantifierBytes;
                }
                closed = c == ')';
            }
            return bytes;
        }

        // Where the literal .NET's prefix search reaches starts (RegexFCD.Prefix), in one pass: past zero-width items,
        // into the groups it enters (open: a flag each, Reducible and the white-space mode before it), and out of a
        // non-capturing group that held only zero-width items, which .NET reduces away ((?:^), (?:(?=x)); a capturing
        // one is kept and ends the search). verbose: whether white space is no literal there, as the pattern's options
        // and the inline ones on the way set it ((?x:, (?-x)). NodeBefore: the group holds a node before what follows
        // (an anchor, a lookaround; options and comments are none), so .NET cannot reduce it to that. Each flag holds the
        // group's '(' above PositionShift. A zero-width item under a quantifier is a loop, where the walk stops before it;
        // {1} is none: .NET keeps the item as it is ((?:){1}^ hid the anchor behind a stop, (?:){1}a{1500} its prefix).
        private const int Reducible = 1, SpacedBefore = 2, NodeBefore = 4, CaselessBefore = 8, PositionShift = 4;

        private static int PrefixStart(string p, GroupEnds closes, ref bool verbose, out List<int> open)
        {
            bool caseless = false;
            return PrefixStart(p, closes, ref verbose, ref caseless, out open);
        }

        // caseless: whether IGNORECASE holds there, as the options and the option groups on the way set it.
        private static int PrefixStart(string p, GroupEnds closes, ref bool verbose, ref bool caseless, out List<int> open)
        {
            int i = 0;
            open = null;
            bool walking = true;
            while (walking)
            {
                char c = At(p, i);
                int nothing = ZeroRepeatEnd(p, i, verbose, closes), item = ZeroWidthItemEnd(p, i, closes), opened = OpeningEnd(p, i);
                int top = open == null || open.Count == 0 ? 0 : open[open.Count - 1];
                Quantifier after = item > i ? QuantifierAt(p, SpacesEnd(p, item, verbose)) : default(Quantifier);
                if (nothing > i)
                {
                    i = nothing;
                }
                else if (item > i && (OptionsEnd(p, i) == item || NoLoop(after)))
                {
                    bool options = OptionsEnd(p, i) == item;
                    verbose = options ? Verbose(p, i, verbose) : verbose;
                    caseless = options ? Flag(p, i, 'i', caseless) : caseless;
                    if (!options && open != null && open.Count > 0)
                        open[open.Count - 1] = top | NodeBefore;
                    i = options || !after.Found ? item : after.End;
                }
                else if (opened > i)
                {
                    if (open == null)
                        open = new List<int>();
                    open.Add((At(p, i + 1) == '?' && p[opened - 1] == ':' ? Reducible : 0) | (verbose ? SpacedBefore : 0) | (caseless ? CaselessBefore : 0)
                        | (i << PositionShift));
                    verbose = Verbose(p, i, verbose);
                    caseless = Flag(p, i, 'i', caseless);
                    i = opened;
                }
                else if (c == ')' && (top & Reducible) != 0 && NoLoop(QuantifierAt(p, SpacesEnd(p, i + 1, (top & SpacedBefore) != 0))))
                {
                    // the group's options end at its ')': its quantifier is read in the mode around it, as .NET does
                    Quantifier closing = QuantifierAt(p, SpacesEnd(p, i + 1, (top & SpacedBefore) != 0));
                    verbose = (top & SpacedBefore) != 0;
                    caseless = (top & CaselessBefore) != 0;
                    open.RemoveAt(open.Count - 1);
                    if ((top & NodeBefore) != 0 && open.Count > 0)
                        open[open.Count - 1] |= NodeBefore;   // (?:^) leaves its ^ in the group around it
                    i = closing.Found ? closing.End : i + 1;
                }
                else if (verbose && IsVerboseSpace(c))
                {
                    i = SpacesEnd(p, i, true);
                }
                else
                {
                    walking = false;
                }
            }
            return i;
        }

        // The index past an anchor the prefix search passes, a lookaround, inline options or a comment at p[i], or i
        // for none (\B is no such anchor: \Ba{1500} keeps no prefix).
        private static int ZeroWidthItemEnd(string p, int i, GroupEnds closes)
        {
            char c = At(p, i), k = At(p, i + 1);
            int end = OptionsEnd(p, i);   // inline options or a comment
            if (end == i && (c == '^' || c == '$'))
            {
                end = i + 1;
            }
            else if (end == i && c == '\\' && "bGAzZ".IndexOf(k) >= 0)
            {
                end = i + 2;
            }
            else if (end == i && c == '(' && k == '?' && IsLookaround(p, i + 2) && closes != null)
            {
                end = closes.After(i);
            }
            return end;
        }

        // Whether white space is no literal after the inline options or option group at p[i], verbose before them.
        private static bool Verbose(string p, int i, bool verbose)
        {
            return Flag(p, i, 'x', verbose);
        }

        // Whether the option letter holds after the inline options or option group at p[i], current before them.
        private static bool Flag(string p, int i, char letter, bool current)
        {
            bool on = true, x = current;
            for (int j = i + 2; j < p.Length && "imnsx-".IndexOf(p[j]) >= 0; j++)
            {
                if (p[j] == '-')
                    on = false;
                else if (p[j] == letter)
                    x = on;
            }
            return At(p, i) == '(' && At(p, i + 1) == '?' ? x : current;
        }

        private static int SpacesEnd(string p, int i, bool verbose)
        {
            int j = i;
            while (verbose && IsVerboseSpace(At(p, j)))
            {
                j++;
            }
            return j;
        }

        // The index past what adds no node at p[i]: inline options, comments, empty non-capturing groups, what is
        // quantified {0} and white space in VERBOSE.
        private static int EmptiesEnd(string p, int i, bool verbose, GroupEnds closes)
        {
            int j = SpacesEnd(p, i, verbose);
            bool empty = true;
            while (empty)
            {
                int options = OptionsEnd(p, j);
                int nothing = ZeroRepeatEnd(p, j, verbose, closes);
                int group = EmptyGroupsEnd(p, j, verbose, closes);
                // (?x) holds to the group's ')', where the caller takes the mode around it back
                verbose = options > j ? Verbose(p, j, verbose) : verbose;
                if (options > j)
                    j = SpacesEnd(p, options, verbose);
                else if (nothing > j)
                    j = SpacesEnd(p, nothing, verbose);
                else if (group > j)
                    j = SpacesEnd(p, group, verbose);
                else
                    empty = false;
            }
            return j;
        }

        // The index past an empty non-capturing group at p[i], nested or quantified {1} ((?:), (?i:), (?:(?:)){1},
        // (?:b{0}), (?x: )), which .NET reduces away, or i for none. Each level reads white space in its own mode and
        // its quantifier in the mode around it: (?x: ) without VERBOSE is empty, (?-x: ) under it is not. A loop over
        // the nest, not a recursion.
        private static int EmptyGroupsEnd(string p, int i, bool verbose, GroupEnds closes)
        {
            int j = i, end = i;
            bool spaced = verbose;
            List<bool> around = null;   // the mode outside each open level
            bool going = At(p, i) == '(';
            while (going)
            {
                int level = around == null ? 0 : around.Count;
                j = SpacesEnd(p, j, spaced);
                int opened = OpeningEnd(p, j), options = OptionsEnd(p, j);
                int nothing = level > 0 ? ZeroRepeatEnd(p, j, spaced, closes) : j;
                if (options > j)
                {
                    spaced = Verbose(p, j, spaced);
                    j = options;
                }
                else if (nothing > j)
                {
                    j = nothing;
                }
                else if (opened > j && At(p, j + 1) == '?' && p[opened - 1] == ':')
                {
                    if (around == null)
                        around = new List<bool>();
                    around.Add(spaced);
                    spaced = Verbose(p, j, spaced);
                    j = opened;
                }
                else if (At(p, j) == ')' && level > 0)
                {
                    spaced = around[level - 1];
                    around.RemoveAt(level - 1);
                    Quantifier q = QuantifierAt(p, SpacesEnd(p, j + 1, spaced));
                    j = q.Found && q.Min == 1 && q.Max == 1 ? q.End : j + 1;
                    end = level == 1 ? j : end;
                    going = level > 1;
                }
                else
                {
                    going = false;
                }
            }
            return end;
        }

        // An upper bound on any prefix a loop of one character can give .NET, for the translator's guard against a
        // table built for seconds in one call: over every atom wherever it is, its least times the least of every group
        // around it (0 counted as 1), white space before a quantifier passed. Nothing .NET drops or reduces before an
        // atom ({0}, (?:), anchors) can hide it, and a bound past the truth only stops a prefix .NET would not build.
        internal static long RepeatBound(string p)
        {
            // only a counted quantifier repeats past one: without a brace the walk (and what it allocates) is spared
            Stack<long> around = p.IndexOf('{') >= 0 ? new Stack<long>() : null;
            long most = around == null ? 1 : 0;
            int i = around == null ? p.Length : 0;
            while (i < p.Length)
            {
                char c = p[i];
                if (c == '(' && OptionsEnd(p, i) > i)
                {
                    i = OptionsEnd(p, i);
                }
                else if (c == '(')
                {
                    around.Push(most);
                    most = 0;
                    i = GroupContentStart(p, i);
                }
                else if (c == ')' && around.Count > 0)
                {
                    Quantifier q = QuantifierAt(p, SpacesEnd(p, i + 1, true));
                    long inner = Math.Min(MostRepeat, Math.Max(1, most) * (q.Found ? Math.Max(1, q.Min) : 1));
                    most = Math.Max(around.Pop(), inner);
                    i = q.Found ? q.End : i + 1;
                }
                else if (IsVerboseSpace(c))
                {
                    // a run of white space read once (a space a time it was read to its end: quadratic, uncharged)
                    int run = SpacesEnd(p, i, true);
                    Quantifier q = QuantifierAt(p, run);
                    most = Math.Max(most, q.Found ? Math.Max(1, q.Min) : 1);
                    i = q.Found ? q.End : run;
                }
                else if (c != '\\' && c != '[' && c != '{' && c != '}' && !IsMeta(c) && !QuantifiedNext(At(p, i + 1)))
                {
                    // a literal nothing quantifies: the common case, read without the atom's tests
                    most = Math.Max(most, 1);
                    i++;
                }
                else
                {
                    int atom = c == '.' ? i + 1 : AtomEnd(p, i);
                    Quantifier q = atom < p.Length ? QuantifierAt(p, SpacesEnd(p, atom, true)) : default(Quantifier);
                    most = atom < p.Length ? Math.Max(most, q.Found ? Math.Max(1, q.Min) : 1) : most;
                    i = q.Found ? q.End : atom < p.Length ? atom : i + 1;   // past |, an anchor, a stray quantifier
                }
            }
            while (around != null && around.Count > 0)
            {
                most = Math.Max(around.Pop(), most);
            }
            return most;
        }

        // Where an empty atomic group ends .NET's prefix search and nothing else: where that search meets the first
        // item it does not pass, after the anchors (a stop in front hid ^ from .NET's start anchor, and an anchored
        // search ran from every position: ^(?:.{80}\n){60}END on 1.6 MB ran past its timeout, 88 ms before). -1 when
        // that item is a literal nothing quantifies: the prefix is then that literal, never a long loop. Should the walk
        // stop early, the stop is only earlier; nothing .NET passes is passed here that it does not.
        // Never inside a group under a quantifier: there the stop kept .NET from merging the loops ((?:(?>)a{10,}){500}b
        // backtracked past its timeout, 189 ms without it), so it goes before the outermost such group the walk entered.
        internal static int PrefixStopAt(string p, bool verbose)
        {
            bool spaced = verbose;
            List<int> open;
            GroupEnds ends = GroupEnds.Of(p, true);
            int at = PrefixStart(p, ends, ref spaced, out open);
            int stop = at;
            bool looped = false;
            for (int k = 0; open != null && k < open.Count && !looped; k++)
            {
                int opening = open[k] >> PositionShift;
                // read in the mode around the group: without VERBOSE (?:key...) + repeats a space, and a stop in front
                // of the group cost .NET its literal prefix (1.7x slower searches)
                Quantifier q = QuantifierAt(p, SpacesEnd(p, ends.After(opening), (open[k] & SpacedBefore) != 0));
                looped = q.Found && !(q.Min == 1 && q.Max == 1);
                stop = looped ? opening : stop;
            }
            int end = OneCharEnd(p, at);
            // a space is that literal only without VERBOSE (' total...' lost its prefix, 4x slower searches); the
            // quantifier after it is still looked for past white space, so a misread mode only stops earlier
            bool literal = !looped && end < p.Length && !(spaced && IsVerboseSpace(p[at])) && !QuantifierAt(p, SpacesEnd(p, end, true)).Found;
            return literal || SetAt(p, at) ? -1 : stop;
        }

        // Whether p[i] starts a set .NET never makes one character, so its prefix search ends there with none: '.', a
        // class escape, a class holding one (RegexCharClass.IsSingleton needs no category). A stop in front of it only
        // cost .NET its first-character scan (the exact \w: 3-4x slower searches).
        private static bool SetAt(string p, int i)
        {
            char c = At(p, i);
            bool set = c == '.' || (c == '\\' && ClassEscapes.IndexOf(At(p, i + 1)) >= 0);
            int close = c == '[' ? ClassEnd(p, i) : i;
            for (int j = i + 1; j < close && !set; j += p[j] == '\\' ? 2 : 1)
            {
                set = p[j] == '\\' && ClassEscapes.IndexOf(At(p, j + 1)) >= 0;
            }
            return set;
        }

        private const string ClassEscapes = "wWdDsSpP";

        // No quantifier, or {1}: .NET keeps the item as it is (RegexNode.MakeQuantifier).
        private static bool NoLoop(Quantifier q)
        {
            return !q.Found || (q.Min == 1 && q.Max == 1);
        }

        // Whether c may begin a quantifier, or white space before one.
        private static bool QuantifiedNext(char c)
        {
            return c == '*' || c == '+' || c == '?' || c == '{' || IsVerboseSpace(c);
        }

        // Where the content of the group opened at p[i] starts: past (, (?:, (?>, a name or options, past the opener of
        // a lookaround; at the condition's own ( of a conditional.
        private static int GroupContentStart(string p, int i)
        {
            int opened = OpeningEnd(p, i);
            return opened > i ? opened : At(p, i + 1) != '?' ? i + 1 : At(p, i + 2) == '(' ? i + 2
                : At(p, i + 2) == '<' ? i + 4 : i + 3;
        }

        // The index past the atom at p[i]: one character, an escape (a backreference \k<name> whole: \1{0} is
        // translated \k<p1>{0}, which .NET drops), a class; the pattern's length for any other.
        private static int AtomEnd(string p, int i)
        {
            int end = OneCharEnd(p, i);
            int named = At(p, i) == '\\' && At(p, i + 1) == 'k' && At(p, i + 2) == '<' ? p.IndexOf('>', i + 3) : -1;
            if (end == p.Length && named > 0)
                end = named + 1;
            else if (end == p.Length && At(p, i) == '\\')
                end = i + EscapeLength(p, i);
            else if (end == p.Length && At(p, i) == '[')
                end = Math.Min(p.Length, ClassEnd(p, i) + 1);
            return end;
        }

        // Where each group of a pattern ends, read in one pass and kept by its '(' alone (a table the length of the
        // pattern, built for every regex, grew a loop of 200-word alternations 40 MB): null without a group.
        private sealed class GroupEnds
        {
            private readonly int[] _opens, _ends;

            private GroupEnds(int[] opens, int[] ends)
            {
                _opens = opens;
                _ends = ends;
            }

            internal static GroupEnds Of(string p)
            {
                return Of(p, false);
            }

            // always: every group's end is wanted; else only to pass a group quantified {0} or a lookaround, without
            // either none is built
            internal static GroupEnds Of(string p, bool always)
            {
                bool needed = always || p.IndexOf("{0", StringComparison.Ordinal) >= 0 || p.IndexOf("(?=", StringComparison.Ordinal) >= 0
                    || p.IndexOf("(?!", StringComparison.Ordinal) >= 0 || p.IndexOf("(?<", StringComparison.Ordinal) >= 0;
                int count = 0;
                for (int j = 0; needed && j < p.Length; j = Next(p, j))
                {
                    if (p[j] == '(')
                        count++;
                }
                GroupEnds ends = null;
                if (count > 0)
                {
                    var opens = new int[count];
                    var after = new int[count];
                    var open = new Stack<int>();
                    int n = 0;
                    for (int j = 0; j < p.Length; j = Next(p, j))
                    {
                        if (p[j] == '(')
                        {
                            opens[n] = j;
                            after[n] = p.Length;
                            open.Push(n++);
                        }
                        else if (p[j] == ')' && open.Count > 0)
                        {
                            after[open.Pop()] = j + 1;
                        }
                    }
                    ends = new GroupEnds(opens, after);
                }
                return ends;
            }

            // The index past the token at p[j]: an escape and a class are one.
            private static int Next(string p, int j)
            {
                int next = j + 1;
                if (p[j] == '\\')
                    next = j + 2;
                else if (p[j] == '[')
                    next = ClassEnd(p, j) + 1;
                return next;
            }

            // The index past the ')' that closes the group opened at p[open].
            internal int After(int open)
            {
                int k = Array.BinarySearch(_opens, open);
                return k >= 0 ? _ends[k] : open + 1;
            }
        }

        // The index past an atom or group at p[i] quantified {0} or {0,0}, which .NET drops (x{0}a{1500} keeps the
        // prefix of a{1500}), or i for none.
        private static int ZeroRepeatEnd(string p, int i, bool verbose, GroupEnds closes)
        {
            // white space in VERBOSE is no atom: the walk passes its run once
            int end = verbose && IsVerboseSpace(At(p, i)) ? p.Length
                : At(p, i) == '(' && closes != null ? closes.After(i)
                : At(p, i) == '.' ? i + 1 : AtomEnd(p, i);
            Quantifier q = end < p.Length ? QuantifierAt(p, SpacesEnd(p, end, verbose)) : default(Quantifier);
            return end < p.Length && q.Found && q.Max == 0 ? q.End : i;
        }

        // The m of a pattern that starts with c{m}, c{m,} or c{m,n} (or c+), and 0 for any other: .NET's prefix is then
        // m c's. A loop of one character is merged into the loops of the non-capturing groups around it as .NET's
        // ReduceLoops does, each inner one bounded at no less than twice its least: (?:a{10,}){150} is a{1500,}, and
        // one it cannot merge is a loop of a group, which ends the prefix. A non-capturing group of the loop alone
        // (beside options, comments and (?:)) is reduced to it, quantified or not. verbose: built with
        // IgnorePatternWhitespace.
        internal static int LeadingRepeat(string p, bool verbose)
        {
            bool spaced = verbose;
            List<int> open;
            GroupEnds closes = GroupEnds.Of(p);
            int start = PrefixStart(p, closes, ref spaced, out open);
            int depth = open == null ? 0 : open.Count;
            Quantifier q = QuantifierAt(p, SpacesEnd(p, OneCharEnd(p, start), spaced));
            // a lazy loop of a class of one stays a set loop, which no prefix is made of ([a]{4,14}? keeps none)
            long least = q.Found && !(q.Lazy && At(p, start) == '[') ? q.Min : 0, most = q.Max;
            int end = q.End;
            bool merging = least > 0;
            while (merging && depth > 0 && At(p, EmptiesEnd(p, end, spaced, closes)) == ')')
            {
                int close = EmptiesEnd(p, end, spaced, closes);
                // the group's quantifier is read in the mode around it ((?x:a{2000}) {2} is no loop without VERBOSE)
                bool around = (open[depth - 1] & SpacedBefore) != 0;
                Quantifier outer = QuantifierAt(p, SpacesEnd(p, close + 1, around));
                bool once = outer.Found && outer.Min == 1 && outer.Max == 1;
                bool alone = (open[depth - 1] & (Reducible | NodeBefore)) == Reducible;
                if ((!outer.Found || once) && alone)
                {
                    depth--;
                    spaced = around;
                    end = outer.Found ? outer.End : close + 1;
                }
                else if (!outer.Found || once)
                {
                    merging = false;   // a group kept as it is: AnotherStart reads on from its ')'
                }
                else if (!alone || outer.Lazy != q.Lazy || most < 2 * least)
                {
                    least = 0;
                    merging = false;
                }
                else
                {
                    least = Math.Min(MostRepeat, least * outer.Min);
                    most = Math.Min(Unbounded, most * outer.Max);
                    depth--;
                    spaced = around;
                    end = outer.End;
                }
            }
            return least > 0 && !AnotherStart(p, end, depth, open) ? (int)least : 0;
        }

        // A quantifier read at p[i]: *, +, ?, {m}, {m,} or {m,n}, lazy or not. Max is Unbounded for none.
        private struct Quantifier
        {
            internal bool Found, Lazy;
            internal long Min, Max;
            internal int End;
        }

        private const long Unbounded = int.MaxValue, MostRepeat = 1000000;

        private static Quantifier QuantifierAt(string p, int i)
        {
            var q = new Quantifier { End = i };
            char c = At(p, i);
            if (c == '*' || c == '+' || c == '?')
            {
                q.Found = true;
                q.Min = c == '+' ? 1 : 0;
                q.Max = c == '?' ? 1 : Unbounded;
                q.End = i + 1;
            }
            else if (c == '{')
            {
                int lowEnd = DigitsEnd(p, i + 1), highEnd = lowEnd;
                bool comma = At(p, lowEnd) == ',';
                if (comma)
                    highEnd = DigitsEnd(p, lowEnd + 1);
                q.Found = lowEnd > i + 1 && At(p, highEnd) == '}';
                q.Min = Number(p, i + 1, lowEnd);
                q.Max = !comma ? q.Min : highEnd > lowEnd + 1 ? Number(p, lowEnd + 1, highEnd) : Unbounded;
                q.End = q.Found ? highEnd + 1 : i;
            }
            q.Lazy = q.Found && At(p, q.End) == '?';
            q.End += q.Lazy ? 1 : 0;
            return q;
        }

        // The index past the one character p[i] stands for (itself, a literal escape, a brace that opens no quantifier,
        // or a class of one, which .NET makes that character: [a]{1500} keeps what a{1500} does), or the pattern's
        // length for anything else.
        private static int OneCharEnd(string p, int i)
        {
            int end = p.Length;
            char c = At(p, i);
            if (i >= p.Length)
            {
                end = p.Length;
            }
            else if (c == '\\')
            {
                end = IsLiteralEscape(p, i) ? i + EscapeLength(p, i) : p.Length;
            }
            else if (c == '[')
            {
                int close = ClassEnd(p, i);
                end = close < p.Length && OnlyMember(p, i, close) ? close + 1 : p.Length;
            }
            else if (c == '}' || (c == '{' && !QuantifierAt(p, i).Found) || (!IsMeta(c) && c != '|'))
            {
                end = i + 1;
            }
            return end;
        }

        // Whether the class p[i..close] holds one character however it is written ([a], [aa], [a-a], [\b]), which .NET
        // makes that character.
        private static bool OnlyMember(string p, int i, int close)
        {
            int only = -1, j = i + 1;
            bool single = At(p, j) != '^';
            while (single && j < close)
            {
                int past;
                int low = MemberAt(p, j, out past), high = low;
                if (At(p, past) == '-' && past + 1 < close)
                    high = MemberAt(p, past + 1, out past);
                single = low >= 0 && low == high && (only < 0 || only == low);
                only = low;
                j = past;
            }
            return single && only >= 0;
        }

        // What \t \n \r \f \v \a \e and (in a class) \b stand for.
        private static readonly string ControlLetters = "tnrfvaeb";
        private static readonly string Controls = "\t\n\r\f\v\a" + (char)27 + "\b";

        // The character the class member at p[i] stands for, or -1 for a class escape (\d, \w); past: the index past it.
        private static int MemberAt(string p, int i, out int past)
        {
            int value = p[i];
            past = i + 1;
            if (p[i] == '\\')
            {
                char k = At(p, i + 1);
                int length = EscapeLength(p, i);
                int control = ControlLetters.IndexOf(k);
                value = control >= 0 ? Controls[control]
                    : k == '0' ? Convert.ToInt32("0" + p.Substring(i + 2, length - 2), 8)
                    : k == 'u' || k == 'x' ? EscapedChar(p, i)
                    : IsLiteralEscape(p, i) ? k : -1;
                past = i + length;
            }
            return value;
        }

        private static int DigitsEnd(string p, int i)
        {
            int j = i;
            while (j < p.Length && p[j] >= '0' && p[j] <= '9')
            {
                j++;
            }
            return j;
        }

        // The number p[i..end) spells, capped past what a pattern may repeat (the translator stops longer prefixes).
        private static int Number(string p, int i, int end)
        {
            int n = 0;
            for (int j = i; j < end; j++)
            {
                n = Math.Min(1000000, n * 10 + (p[j] - '0'));
            }
            return n;
        }

        // The pages past ASCII of the literal run a match starts with (after zero-width tokens and group openings),
        // none when a branch of a group around it or of the whole pattern offers another start, or that group is
        // repeated: .NET builds its table for that run only. The run is read as .NET merges it: a class of one, an
        // item quantified {1}, and a non-capturing or option group on the way are no end ([X], (?:X), X{1}: 215
        // pages spelled so kept 231 KB a regex charged 8-20 KB), and what it drops is passed ({0}, comments). A
        // character under other IGNORECASE than the first ends it, and under IGNORECASE counts as its lower case; a
        // group it opened is left out again when a branch in it or a loop on it keeps .NET from merging it.
        // White space is passed in VERBOSE, as (?x) and (?x: set it (an indent before a group hid its run).
        // repeated: a leading repeat .NET merged ((?:X+){2} is X{2,}), whose prefix is its first character m times.
        private static long PrefixPageBytes(string p, bool ignoreCase, bool verbose, bool repeated)
        {
            bool spaced = verbose, caseless = ignoreCase;
            List<int> open;
            GroupEnds closes = GroupEnds.Of(p);
            int i = PrefixStart(p, closes, ref spaced, ref caseless, out open);
            int depth = open == null ? 0 : open.Count, runCase = -1, first = -1;
            bool[] seen = null;
            // the pages in the order read; for each group opened on the way: touched.Count then, IGNORECASE and VERBOSE
            // before it
            List<int> touched = null, opened = null;
            bool reading = true;
            while (i < p.Length && reading)
            {
                // a literal nothing quantifies: the common case, read without the atom's tests
                char c = p[i];
                bool gap = spaced && IsVerboseSpace(c);
                bool plain = !gap && !IsMeta(c) && c != '\\' && c != '[' && c != '|' && !QuantifiedNext(At(p, i + 1));
                int cp = plain ? c : OneChar(p, i), nothing = plain ? i : ZeroRepeatEnd(p, i, spaced, closes);
                int options = plain ? i : OptionsEnd(p, i), group = plain ? i : OptionGroupEnd(p, i);
                if (gap)
                {
                    i = SpacesEnd(p, i, true);
                }
                else if (nothing > i)
                {
                    i = nothing;
                }
                else if (options > i)
                {
                    caseless = Flag(p, i, 'i', caseless);
                    spaced = Flag(p, i, 'x', spaced);
                    i = options;
                }
                else if (group > i)
                {
                    if (opened == null)
                        opened = new List<int>();
                    opened.Add(((touched == null ? 0 : touched.Count) << 2) | (caseless ? 1 : 0) | (spaced ? 2 : 0));
                    caseless = Flag(p, i, 'i', caseless);
                    spaced = Flag(p, i, 'x', spaced);
                    i = group;
                }
                else if (p[i] == ')' && opened != null && opened.Count > 0)
                {
                    int frame = opened[opened.Count - 1];
                    Quantifier q = QuantifierAt(p, SpacesEnd(p, i + 1, (frame & 2) != 0));   // in the mode around it
                    if (!NoLoop(q))
                        Forget(touched, seen, frame >> 2);   // a loop of the group: the run ends before it
                    reading = NoLoop(q);
                    caseless = (frame & 1) != 0;
                    spaced = (frame & 2) != 0;
                    opened.RemoveAt(opened.Count - 1);
                    i = q.Found ? q.End : i + 1;
                }
                else if (p[i] == ')' && depth > 0 && (open[depth - 1] & Reducible) != 0
                    && NoLoop(QuantifierAt(p, SpacesEnd(p, i + 1, (open[depth - 1] & SpacedBefore) != 0))))
                {
                    // a group the run started in is merged too ((?:XX)YY is one run); a loop on it is read below
                    Quantifier q = QuantifierAt(p, SpacesEnd(p, i + 1, (open[depth - 1] & SpacedBefore) != 0));
                    depth--;
                    caseless = (open[depth] & CaselessBefore) != 0;
                    spaced = (open[depth] & SpacedBefore) != 0;
                    i = q.Found ? q.End : i + 1;
                }
                else if (cp >= 0 && (runCase < 0 || runCase == (caseless ? 1 : 0)))
                {
                    runCase = caseless ? 1 : 0;
                    cp = caseless ? char.ToLowerInvariant((char)cp) : cp;
                    first = first < 0 ? cp : first;
                    if (cp >= 128 && seen == null)
                    {
                        seen = new bool[256];
                        touched = new List<int>();
                    }
                    if (cp >= 128 && !seen[cp >> 8])
                        touched.Add(cp >> 8);
                    if (cp >= 128)
                        seen[cp >> 8] = true;
                    // a loop of the character ends the run after it (taken as held: c{m} is m c's, one page)
                    int end = plain ? i + 1 : OneCharEnd(p, i);
                    Quantifier q = plain ? default(Quantifier) : QuantifierAt(p, SpacesEnd(p, end, spaced));
                    reading = NoLoop(q);
                    i = q.Found ? q.End : end;
                }
                else
                {
                    reading = false;
                }
            }
            int inner = opened == null ? 0 : opened.Count;
            int level = touched == null || touched.Count == 0 ? -1 : OtherStartLevel(p, i, depth + inner, open, depth, opened);
            if (level > depth)
                Forget(touched, seen, opened[level - depth - 1] >> 2);
            int pages = touched == null || (level >= 0 && level <= depth) ? 0 : touched.Count;
            pages = repeated && first >= 128 ? Math.Max(pages, 1) : pages;
            return pages == 0 ? 0 : PageIndexBytes + PageBytes * pages;
        }

        // The index past the opening of a non-capturing or option group at p[i] ((?:, (?i:, (?-i:), whose items .NET
        // merges into the run around it, or i for none.
        private static int OptionGroupEnd(string p, int i)
        {
            int opened = At(p, i) == '(' && At(p, i + 1) == '?' ? OpeningEnd(p, i) : i;
            return opened > i && p[opened - 1] == ':' ? opened : i;
        }

        // Drops the pages read after the first count.
        private static void Forget(List<int> touched, bool[] seen, int count)
        {
            for (int k = count; touched != null && k < touched.Count; k++)
            {
                seen[touched[k]] = false;
            }
            if (touched != null && count < touched.Count)
                touched.RemoveRange(count, touched.Count - count);
        }

        // The character the one-character atom at p[i] stands for, as OneCharEnd reads it, or -1 for none.
        private static int OneChar(string p, int i)
        {
            char c = At(p, i);
            int value = -1;
            if (i >= p.Length)
            {
                value = -1;
            }
            else if (c == '\\')
            {
                value = IsLiteralEscape(p, i) ? EscapedChar(p, i) : -1;
            }
            else if (c == '[')
            {
                int close = ClassEnd(p, i), past;
                value = close < p.Length && OnlyMember(p, i, close) ? MemberAt(p, i + 1, out past) : -1;
            }
            else if (c == '}' || (c == '{' && !QuantifierAt(p, i).Found) || (!IsMeta(c) && c != '|'))
            {
                value = c;
            }
            return value;
        }

        // The outermost of the levels around a run that ends at i inside depth groups (0 the whole pattern, depth the
        // innermost group) whose branch lets a match start without the run, or whose group a quantifier makes a loop;
        // -1 for none. A group's quantifier is read in the mode around it: VERBOSE before the group, as the flags in
        // open (the first pre levels, PrefixStart's) and opened (the rest, PrefixPageBytes's) hold it.
        private static int OtherStartLevel(string p, int i, int depth, List<int> open, int pre, List<int> opened)
        {
            int at = depth, around = depth, level = -1;
            for (; i < p.Length; i++)
            {
                char c = p[i];
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '[')
                {
                    i = ClassEnd(p, i);
                }
                else if (c == '(')
                {
                    at++;
                }
                else if (c == ')')
                {
                    at--;
                    bool leaving = at < around && at >= 0;
                    bool spaced = leaving && (at < pre ? (open[at] & SpacedBefore) != 0 : (opened[at - pre] & 2) != 0);
                    Quantifier q = leaving ? QuantifierAt(p, SpacesEnd(p, i + 1, spaced)) : default(Quantifier);
                    level = leaving && !NoLoop(q) ? at + 1 : level;
                    around = Math.Min(around, at);
                }
                else if (c == '|' && at <= around)
                {
                    level = at;
                }
            }
            return level;
        }

        // Whether, after a run that ends at i inside depth groups, a branch of a group around it (or of the whole
        // pattern) lets a match start without it, or a quantifier on such a group makes it a loop, where .NET's prefix
        // search stops ((?:a{1500})+ keeps 0.7 KB, a{1500} 10.2 KB).
        private static bool AnotherStart(string p, int i, int depth, List<int> open)
        {
            return OtherStartLevel(p, i, depth, open, depth, null) >= 0;
        }

        // (?=, (?!, (?<= or (?<! with p[i] the character after the '?'.
        private static bool IsLookaround(string p, int i)
        {
            char k = At(p, i);
            return k == '=' || k == '!' || (k == '<' && (At(p, i + 1) == '=' || At(p, i + 1) == '!'));
        }

        // The index past the opening of a group at p[i] whose content the prefix search enters ((, (?:, (?>, a named
        // group, (?i:), or i for none.
        private static int OpeningEnd(string p, int i)
        {
            int end = i;
            char k = At(p, i + 2);
            if (At(p, i) != '(')
            {
                end = i;
            }
            else if (At(p, i + 1) != '?')
            {
                end = i + 1;
            }
            else if (k == ':' || k == '>')
            {
                end = i + 3;
            }
            else if ((k == '<' || k == '\'') && !IsLookaround(p, i + 2))
            {
                int close = p.IndexOf(k == '<' ? '>' : '\'', i + 3);
                end = close < 0 ? i : close + 1;
            }
            else
            {
                int j = FlagsEnd(p, i + 2);
                end = j > i + 2 && At(p, j) == ':' ? j + 1 : i;
            }
            return end;
        }

        // The index past inline options ((?i), (?-x)) or a comment at p[i], which add no node, or i for none.
        private static int OptionsEnd(string p, int i)
        {
            bool open = At(p, i) == '(' && At(p, i + 1) == '?';
            int j = open && At(p, i + 2) == '#' ? p.IndexOf(')', i + 3) : FlagsEnd(p, i + 2);
            return open && j > i + 2 && At(p, j) == ')' ? j + 1 : i;
        }

        private static int FlagsEnd(string p, int i)
        {
            int j = i;
            while ("imnsx-".IndexOf(At(p, j)) >= 0)
            {
                j++;
            }
            return j;
        }

        // What IgnorePatternWhitespace ignores (RegexParser.IsSpace): not \v, which .NET keeps as a literal, so
        // (?:a{10,}\v){400} under re.X merges nothing there and was charged a 4,000-repeat merge
        private static bool IsVerboseSpace(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\f' || c == '\r';
        }

        // A table, not a search of the meta string: every character of every pattern compiled is tested.
        private static readonly bool[] Meta = MetaTable();

        private static bool[] MetaTable()
        {
            var meta = new bool[128];
            foreach (char c in ".^$()*+?{}")
            {
                meta[c] = true;
            }
            return meta;
        }

        private static bool IsMeta(char c)
        {
            return c < 128 && Meta[c];
        }

        // An escape that stands for one character: escaped punctuation, a control letter, \uXXXX, \xHH or an octal \0.
        private static bool IsLiteralEscape(string p, int i)
        {
            char k = At(p, i + 1);
            return k != '\0' && (!char.IsLetterOrDigit(k) || "tnrfvaeux0".IndexOf(k) >= 0);
        }

        private static int EscapeLength(string p, int i)
        {
            char k = At(p, i + 1);
            int length = k == 'u' && Hex(p, i + 2, 4) ? 6 : k == 'x' && Hex(p, i + 2, 2) ? 4 : k == '0' ? 2 + Octal(p, i + 2) : 2;
            return Math.Min(length, p.Length - i);
        }

        // How many of the (at most two) characters at p[i] are octal digits, the rest of a \0 escape.
        private static int Octal(string p, int i)
        {
            int n = 0;
            while (n < 2 && At(p, i + n) >= '0' && At(p, i + n) <= '7')
            {
                n++;
            }
            return n;
        }

        private static int EscapedChar(string p, int i)
        {
            char k = At(p, i + 1);
            int length = EscapeLength(p, i);
            return (k == 'u' || k == 'x') && length > 2 ? Convert.ToInt32(p.Substring(i + 2, length - 2), 16) : k;
        }

        private static bool Hex(string p, int start, int count)
        {
            bool all = start + count <= p.Length;
            for (int j = start; all && j < start + count; j++)
            {
                all = Uri.IsHexDigit(p[j]);
            }
            return all;
        }

        // The index of the ']' that closes the class opened at i, or the pattern's length: a ']' first in it (after a
        // '^') is a member, an escape takes the character after it, and a subtraction (-[...], last in a class: the
        // translator's exact \w is one) is read to its own ']' first (the first ']' hid \w{0} from the walk).
        private static int ClassEnd(string p, int i)
        {
            int j = i + 1, levels = 1;
            bool opening = true;
            while (j < p.Length && levels > 0)
            {
                if (opening)
                {
                    j += At(p, j) == '^' ? 1 : 0;
                    j += At(p, j) == ']' ? 1 : 0;
                    opening = false;
                }
                else if (p[j] == '\\')
                {
                    j += 2;
                }
                else if (p[j] == '-' && At(p, j + 1) == '[')
                {
                    levels++;
                    opening = true;
                    j += 2;
                }
                else if (p[j] == ']')
                {
                    levels--;
                    j += levels > 0 ? 1 : 0;
                }
                else
                {
                    j++;
                }
            }
            return Math.Min(j, p.Length);
        }

        private static char At(string s, int i)
        {
            return i < s.Length ? s[i] : '\0';
        }
    }
}

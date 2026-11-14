using System;
using System.Collections.Generic;
using System.Globalization;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // CPython's sre_parse, for its errors only: the pattern is walked as sre_parse walks it and the first error
    // raised there is raised here with its text and position (" at position N", plus the line and column when
    // the pattern has several lines). Widths are kept for sre_compile's look-behind check. The translation to
    // .NET runs after this on a pattern known to be well formed.
    internal sealed class RegexValidator
    {
        private const long Unbounded = uint.MaxValue;   // sre's MAXREPEAT
        private const string Flags = "iLmsxau";

        private readonly EvalContext _ctx;
        private readonly string _p;
        private int _i;
        private int _flags;
        private readonly List<long[]> _widths = new List<long[]> { new long[] { 0, 0 } };   // per group, null while open
        private readonly Dictionary<string, int> _names = new Dictionary<string, int>(StringComparer.Ordinal);
        private int _lookbehindGroups = -1;   // the first group number inside the look-behind being parsed
        private bool _variableLookbehind;

        private RegexValidator(EvalContext ctx, string pattern, int flags)
        {
            _ctx = ctx;
            _p = pattern;
            _flags = flags;
        }

        public static void Check(EvalContext ctx, string pattern, int flags)
        {
            var v = new RegexValidator(ctx, pattern, flags);
            v.ParseSub((flags & ReFlags.X) != 0, 0);
            if (v._i < pattern.Length)
                throw v.Error("unbalanced parenthesis", v._i);   // the ')' that closes nothing
            if (v._variableLookbehind)
                throw Raise.Make(ctx, PyExceptionTypes.ReError, "look-behind requires fixed-width pattern");
        }

        private int Groups { get { return _widths.Count; } }

        // sre_constants.error: the text, and msg, pattern, pos, lineno and colno as attributes
        private ScriptException Error(string message, int pos)
        {
            string text = message + " at position " + Num(pos);
            int line = 1;
            for (int k = 0; k < pos && k < _p.Length; k++)
            {
                if (_p[k] == '\n')
                    line++;
            }
            int col = pos - (pos > 0 ? _p.LastIndexOf('\n', Math.Min(pos, _p.Length) - 1) : -1);
            if (_p.IndexOf('\n') >= 0)
                text += " (line " + Num(line) + ", column " + Num(col) + ")";
            ScriptException ex = Raise.Make(_ctx, PyExceptionTypes.ReError, text);
            ex.Value.Data = new Dictionary<string, ScriptValue>(5, StringComparer.Ordinal)
            {
                ["msg"] = _ctx.Values.Str(message),
                ["pattern"] = _ctx.Values.Str(_p),
                ["pos"] = _ctx.Values.Int(pos),
                ["lineno"] = _ctx.Values.Int(line),
                ["colno"] = _ctx.Values.Int(col),
            };
            return ex;
        }

        private static string Num(long n) { return n.ToString(CultureInfo.InvariantCulture); }

        private bool AtEnd { get { return _i >= _p.Length; } }

        private char Next { get { return _i < _p.Length ? _p[_i] : '\0'; } }

        private bool Match(char c)
        {
            if (_i < _p.Length && _p[_i] == c)
            {
                _i++;
                return true;
            }
            return false;
        }

        // alternatives: the widths of the shortest and the longest
        private long[] ParseSub(bool verbose, int nested)
        {
            long lo = long.MaxValue, hi = 0;
            while (true)
            {
                if (nested == 0)
                    verbose |= (_flags & ReFlags.X) != 0;
                long[] w = Parse(verbose, nested + 1, nested == 0 && lo == long.MaxValue);
                lo = Math.Min(lo, w[0]);
                hi = Math.Max(hi, w[1]);
                if (!Match('|'))
                    break;
            }
            return new[] { lo, hi };
        }

        private enum Item { None, Atom, Anchor, Repeat }

        private long[] Parse(bool verbose, int nested, bool first)
        {
            long lo = 0, hi = 0;
            long itemLo = 0, itemHi = 0;
            Item last = Item.None;
            while (!AtEnd)
            {
                char c = _p[_i];
                if (c == '|' || c == ')')
                    break;
                int at = _i++;
                if (verbose && IsWhite(c))
                    continue;
                if (verbose && c == '#')
                {
                    while (!AtEnd && _p[_i++] != '\n')
                    {
                    }
                    continue;
                }
                long wLo = 1, wHi = 1;
                Item kind = Item.Atom;
                if (c == '\\')
                {
                    if (AtEnd)
                        throw Error("bad escape (end of pattern)", at);
                    if (Escape(at, out wLo, out wHi))
                        kind = Item.Anchor;
                }
                else if (c == '[')
                    CharClass(at);
                else if (c == '*' || c == '+' || c == '?' || c == '{')
                {
                    long min, max;
                    if (c == '{')
                    {
                        if (!Counted(out min, out max))
                        {
                            Add(ref lo, ref hi, ref itemLo, ref itemHi, 1, 1, ref last, Item.Atom);   // a literal '{'
                            continue;
                        }
                    }
                    else
                    {
                        min = c == '+' ? 1 : 0;
                        max = c == '?' ? 1 : Unbounded;
                    }
                    if (last == Item.None || last == Item.Anchor)
                        throw Error("nothing to repeat", at);
                    if (last == Item.Repeat)
                        throw Error("multiple repeat", at);
                    if (!Match('?'))
                        Match('+');
                    lo -= itemLo;
                    hi -= itemHi;
                    itemLo = Mul(itemLo, min);
                    itemHi = Mul(itemHi, max);
                    lo = Sum(lo, itemLo);
                    hi = Sum(hi, itemHi);
                    last = Item.Repeat;
                    continue;
                }
                else if (c == '(')
                {
                    long[] g = Group(at, verbose, nested, first && last == Item.None);
                    if (g == null)
                    {
                        verbose |= (_flags & ReFlags.X) != 0;   // a comment, or global flags: (?x) holds from here
                        continue;
                    }
                    wLo = g[0];
                    wHi = g[1];
                }
                else if (c == '^' || c == '$')
                {
                    wLo = wHi = 0;
                    kind = Item.Anchor;
                }
                else if (char.IsHighSurrogate(c) && !AtEnd && char.IsLowSurrogate(_p[_i]))
                    _i++;   // one code point
                Add(ref lo, ref hi, ref itemLo, ref itemHi, wLo, wHi, ref last, kind);
            }
            return new[] { lo, hi };
        }

        private static void Add(ref long lo, ref long hi, ref long itemLo, ref long itemHi, long wLo, long wHi, ref Item last, Item kind)
        {
            itemLo = wLo;
            itemHi = wHi;
            lo = Sum(lo, wLo);
            hi = Sum(hi, wHi);
            last = kind;
        }

        private static long Sum(long a, long b) { return Math.Min(a + b, Unbounded); }

        private static long Mul(long a, long b) { return a == 0 || b == 0 ? 0 : a >= Unbounded / b ? Unbounded : a * b; }

        private static bool IsWhite(char c) { return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v'; }

        // {m}, {m,}, {,n}, {m,n}; false (nothing consumed) when the brace is a literal
        private bool Counted(out long min, out long max)
        {
            int here = _i;   // past '{'
            min = 0;
            max = Unbounded;
            int j = _i;
            while (j < _p.Length && IsDigit(_p[j]))
                j++;
            string lo = _p.Substring(_i, j - _i);
            string hi = lo;
            if (j < _p.Length && _p[j] == ',')
            {
                int k = ++j;
                while (j < _p.Length && IsDigit(_p[j]))
                    j++;
                hi = _p.Substring(k, j - k);
            }
            if (j >= _p.Length || _p[j] != '}' || (lo.Length == 0 && hi.Length == 0 && _p[j - 1] == '{'))
                return false;
            _i = j + 1;
            if (lo.Length > 0)
                min = Parse(lo);
            if (hi.Length > 0)
            {
                max = Parse(hi);
                if (max < min)
                    throw Error("min repeat greater than max repeat", here);
            }
            if (min >= Unbounded || (hi.Length > 0 && max >= Unbounded))
                throw Raise.Overflow(_ctx, "the repetition number is too large");
            return true;
        }

        private static long Parse(string digits)
        {
            long v = 0;
            foreach (char d in digits)
            {
                v = v * 10 + (d - '0');
                if (v >= Unbounded)
                    return Unbounded;
            }
            return v;
        }

        private static bool IsDigit(char c) { return c >= '0' && c <= '9'; }

        private static bool IsOct(char c) { return c >= '0' && c <= '7'; }

        private static bool IsHex(char c) { return IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'); }

        private static bool IsAsciiLetter(char c) { return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'); }

        // _escape outside a class; true for an anchor (\A \b \B \Z \z), whose width is 0. at is the backslash.
        private bool Escape(int at, out long lo, out long hi)
        {
            char c = _p[_i++];
            lo = hi = 1;
            switch (c)
            {
                case 'A': case 'b': case 'B': case 'Z': case 'z':
                    lo = hi = 0;
                    return true;
                case 'd': case 'D': case 's': case 'S': case 'w': case 'W':
                case 'a': case 'f': case 'n': case 'r': case 't': case 'v': case '\\':
                    return false;
            }
            if (CodeEscape(at, c))
                return false;
            if (c == '0')
            {
                Take(2, IsOct);
                return false;
            }
            if (IsDigit(c))
            {
                if (IsDigit(Next))
                {
                    char d2 = _p[_i++];
                    if (IsOct(c) && IsOct(d2) && IsOct(Next))
                    {
                        _i++;
                        int v = (c - '0') * 64 + (d2 - '0') * 8 + (_p[_i - 1] - '0');
                        if (v > 255)
                            throw Error("octal escape value " + _p.Substring(at, 4) + " outside of range 0-0o377", at);
                        return false;
                    }
                }
                int group = int.Parse(_p.Substring(at + 1, _i - at - 1), CultureInfo.InvariantCulture);
                if (group < Groups)
                {
                    if (_widths[group] == null)
                        throw Error("cannot refer to an open group", at);
                    CheckLookbehindGroup(group, at);
                    lo = _widths[group][0];
                    hi = _widths[group][1];
                    return false;
                }
                throw Error("invalid group reference " + Num(group), at + 1);
            }
            if (IsAsciiLetter(c))
                throw Error("bad escape \\" + c, at);
            return false;
        }

        // \x \u \U \N, the same inside and outside a class; false for any other escape
        private bool CodeEscape(int at, char c)
        {
            int n = c == 'x' ? 2 : c == 'u' ? 4 : c == 'U' ? 8 : 0;
            if (n > 0)
            {
                int got = Take(n, IsHex);
                if (got != n)
                    throw Error("incomplete escape " + _p.Substring(at, _i - at), at);
                if (c == 'U' && long.Parse(_p.Substring(at + 2, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture) > 0x10FFFF)
                    throw Error("bad escape " + _p.Substring(at, _i - at), at);
                return true;
            }
            if (c != 'N')
                return false;
            if (!Match('{'))
                throw Error("missing {", _i);
            int start = _i;
            string name = Until('}', "character name");
            string value;
            if (!Syntax.PyUnicodeNames.TryLookup(name, out value))
                throw Error("undefined character name '" + name + "'", at);
            return true;
        }

        private int Take(int max, Func<char, bool> ok)
        {
            int got = 0;
            while (got < max && !AtEnd && ok(_p[_i]))
            {
                _i++;
                got++;
            }
            return got;
        }

        // Tokenizer.getuntil
        private string Until(char terminator, string what)
        {
            int start = _i;
            while (true)
            {
                if (AtEnd)
                {
                    if (_i == start)
                        throw Error("missing " + what, _i);
                    throw Error("missing " + terminator + ", unterminated name", start);
                }
                char c = _p[_i++];
                if (c == terminator)
                {
                    if (_i - 1 == start)
                        throw Error("missing " + what, _i - 1);
                    return _p.Substring(start, _i - 1 - start);
                }
            }
        }

        private void CheckGroupName(string name, int at)
        {
            if (!Runtime.Values.StrMethods.IsIdentifier(name))
                throw Error("bad character in group name '" + name + "'", at);
        }

        private void CheckLookbehindGroup(int group, int at)
        {
            if (_lookbehindGroups >= 0 && group >= _lookbehindGroups)
                throw Error("cannot refer to group defined in the same lookbehind subpattern", at);
        }

        // [...]; at is the '['
        private void CharClass(int at)
        {
            Match('^');
            bool any = false;
            while (true)
            {
                if (AtEnd)
                    throw Error("unterminated character set", at);
                int start = _i;
                char c = _p[_i++];
                if (c == ']' && any)
                    return;
                SkipLowSurrogate(c);
                any = true;
                bool literal1 = c != '\\' || ClassEscape(start);
                if (!Match('-'))
                    continue;
                if (AtEnd)
                    throw Error("unterminated character set", at);
                if (_p[_i] == ']')
                {
                    _i++;
                    return;
                }
                int start2 = _i;
                char c2 = _p[_i++];
                SkipLowSurrogate(c2);
                bool literal2 = c2 != '\\' || ClassEscape(start2);
                if (!literal1 || !literal2 || ClassCode(start2) < ClassCode(start))
                    throw Error("bad character range " + _p.Substring(start, _i - start), start);
            }
        }

        private void SkipLowSurrogate(char c)
        {
            if (char.IsHighSurrogate(c) && !AtEnd && char.IsLowSurrogate(_p[_i]))
                _i++;
        }

        // _class_escape; true for a single character, false for a category (\d \s \w and their negations)
        private bool ClassEscape(int at)
        {
            if (AtEnd)
                throw Error("bad escape (end of pattern)", at);
            char c = _p[_i++];
            switch (c)
            {
                case 'd': case 'D': case 's': case 'S': case 'w': case 'W':
                    return false;
                case 'a': case 'b': case 'f': case 'n': case 'r': case 't': case 'v': case '\\':
                    return true;
            }
            if (CodeEscape(at, c))
                return true;
            if (IsOct(c))
            {
                Take(2, IsOct);
                int v = Convert.ToInt32(_p.Substring(at + 1, _i - at - 1), 8);
                if (v > 255)
                    throw Error("octal escape value " + _p.Substring(at, _i - at) + " outside of range 0-0o377", at);
                return true;
            }
            if (IsDigit(c) || IsAsciiLetter(c))
                throw Error("bad escape \\" + c, at);
            return true;
        }

        // the code point a class member at index start stands for (literal or escape)
        private int ClassCode(int start)
        {
            if (_p[start] != '\\')
                return start + 1 < _p.Length && char.IsSurrogatePair(_p[start], _p[start + 1]) ? char.ConvertToUtf32(_p[start], _p[start + 1]) : _p[start];   // a lone half is its own code
            char c = _p[start + 1];
            switch (c)
            {
                case 'a': return 7;
                case 'b': return 8;
                case 'f': return 12;
                case 'n': return 10;
                case 'r': return 13;
                case 't': return 9;
                case 'v': return 11;
                case 'x': case 'u': case 'U':
                    int n = c == 'x' ? 2 : c == 'u' ? 4 : 8;
                    return (int)long.Parse(_p.Substring(start + 2, n), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                case 'N':
                    int close = _p.IndexOf('}', start);
                    string value;
                    Syntax.PyUnicodeNames.TryLookup(_p.Substring(start + 3, close - start - 3), out value);
                    return char.ConvertToUtf32(value, 0);
            }
            if (IsOct(c))
            {
                int k = start + 1;
                int v = 0;
                while (k < start + 4 && k < _p.Length && IsOct(_p[k]))
                    v = v * 8 + (_p[k++] - '0');
                return v;
            }
            return c;
        }

        // (...): null for a comment or global flags (nothing to repeat), else the widths; a look-around
        // reports three items. at is the '('.
        private long[] Group(int at, bool verbose, int nested, bool first)
        {
            bool capture = true;
            string name = null;
            int nameAt = 0;
            int addFlags = 0, delFlags = 0;
            bool behind = false;
            if (Match('?'))
            {
                if (AtEnd)
                    throw Error("unexpected end of pattern", _i);
                char c = _p[_i++];
                if (c == 'P')
                {
                    if (Match('<'))
                    {
                        nameAt = _i;
                        name = Until('>', "group name");
                        CheckGroupName(name, nameAt);
                    }
                    else if (Match('='))
                    {
                        nameAt = _i;
                        string refName = Until(')', "group name");
                        CheckGroupName(refName, nameAt);
                        int gid;
                        if (!_names.TryGetValue(refName, out gid))
                            throw Error("unknown group name '" + refName + "'", nameAt);
                        if (_widths[gid] == null)
                            throw Error("cannot refer to an open group", nameAt);
                        CheckLookbehindGroup(gid, nameAt);
                        return new[] { _widths[gid][0], _widths[gid][1] };
                    }
                    else
                    {
                        if (AtEnd)
                            throw Error("unexpected end of pattern", _i);
                        throw Error("unknown extension ?P" + _p[_i], at + 1);
                    }
                }
                else if (c == ':')
                    capture = false;
                else if (c == '#')
                {
                    while (true)
                    {
                        if (AtEnd)
                            throw Error("missing ), unterminated comment", at);
                        if (_p[_i++] == ')')
                            return null;
                    }
                }
                else if (c == '=' || c == '!' || c == '<')
                {
                    if (c == '<')
                    {
                        if (AtEnd)
                            throw Error("unexpected end of pattern", _i);
                        char d = _p[_i++];
                        if (d != '=' && d != '!')
                            throw Error("unknown extension ?<" + d, at + 1);
                        behind = true;
                    }
                    int saved = _lookbehindGroups;
                    if (behind && _lookbehindGroups < 0)
                        _lookbehindGroups = Groups;
                    long[] w = ParseSub(verbose, nested + 1);
                    if (behind)
                    {
                        _lookbehindGroups = saved;
                        if (w[0] != w[1])
                            _variableLookbehind = true;
                    }
                    if (!Match(')'))
                        throw Error("missing ), unterminated subpattern", at);
                    return new long[] { 0, 0 };   // a look-around: no width, but sre repeats it
                }
                else if (c == '(')
                    throw Raise.Make(_ctx, PyExceptionTypes.ReError, "conditional groups are not supported in this dialect (v1)");
                else if (c == '>')
                    capture = false;
                else if (Flags.IndexOf(c) >= 0 || c == '-')
                {
                    if (!InlineFlags(c, out addFlags, out delFlags))
                    {
                        if (!first)
                            throw Error("global flags not at the start of the expression", at);
                        return null;
                    }
                    capture = false;
                }
                else
                    throw Error("unknown extension ?" + c, at + 1);
            }

            int group = -1;
            if (capture)
            {
                group = Groups;
                if (name != null)
                {
                    int was;
                    if (_names.TryGetValue(name, out was))
                        throw Error("redefinition of group name '" + name + "' as group " + Num(group) + "; was group " + Num(was), nameAt);
                    _names[name] = group;
                }
                _widths.Add(null);
            }
            bool subVerbose = (verbose || (addFlags & ReFlags.X) != 0) && (delFlags & ReFlags.X) == 0;
            long[] width = ParseSub(subVerbose, nested + 1);
            if (!Match(')'))
                throw Error("missing ), unterminated subpattern", at);
            if (group >= 0)
                _widths[group] = width;
            return width;
        }

        // _parse_flags; false for global flags `(?x)`. c is the first flag letter or '-'.
        private bool InlineFlags(char c, out int add, out int del)
        {
            add = 0;
            del = 0;
            if (c != '-')
            {
                while (true)
                {
                    if (c == 'L')
                        throw Error("bad inline flags: cannot use 'L' flag with a str pattern", _i);
                    int flag = FlagOf(c);
                    add |= flag;
                    int types = add & (ReFlags.A | ReFlags.U);
                    if ((flag & (ReFlags.A | ReFlags.U)) != 0 && types != flag)
                        throw Error("bad inline flags: flags 'a', 'u' and 'L' are incompatible", _i);
                    if (AtEnd)
                        throw Error("missing -, : or )", _i);
                    c = _p[_i++];
                    if (c == ')' || c == '-' || c == ':')
                        break;
                    if (Flags.IndexOf(c) < 0)
                        throw Error(char.IsLetter(c) ? "unknown flag" : "missing -, : or )", _i - 1);
                }
            }
            if (c == ')')
            {
                _flags |= add;
                return false;
            }
            if (c == '-')
            {
                if (AtEnd)
                    throw Error("missing flag", _i);
                c = _p[_i++];
                if (Flags.IndexOf(c) < 0)
                    throw Error(char.IsLetter(c) ? "unknown flag" : "missing flag", _i - 1);
                while (true)
                {
                    int flag = FlagOf(c);
                    if ((flag & (ReFlags.A | ReFlags.U | ReFlags.L)) != 0)
                        throw Error("bad inline flags: cannot turn off flags 'a', 'u' and 'L'", _i);
                    del |= flag;
                    if (AtEnd)
                        throw Error("missing :", _i);
                    c = _p[_i++];
                    if (c == ':')
                        break;
                    if (Flags.IndexOf(c) < 0)
                        throw Error(char.IsLetter(c) ? "unknown flag" : "missing :", _i - 1);
                }
            }
            if ((add & del) != 0)
                throw Error("bad inline flags: flag turned on and off", _i - 1);
            return true;
        }

        private static int FlagOf(char c)
        {
            switch (c)
            {
                case 'i': return ReFlags.I;
                case 'L': return ReFlags.L;
                case 'm': return ReFlags.M;
                case 's': return ReFlags.S;
                case 'x': return ReFlags.X;
                case 'a': return ReFlags.A;
                default: return ReFlags.U;
            }
        }
    }
}

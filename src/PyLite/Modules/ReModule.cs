using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // The re module. HYBRID over .NET Regex: RegexTranslator maps the Python pattern
    // RegexCache compiles (per-EvalContext), every operation is fenced by MatchTimeout + ChargeRegexTime +
    // CheckDeadlineNow: compile, search/match/fullmatch, findall/finditer, sub/subn, split, escape and the
    // Pattern/Match API.
    internal static class ReModule
    {
        // the keyword names of the functions, built once (a params array would be built on every call)
        private static readonly string[] CompileNames = { "pattern", "flags" };
        private static readonly string[] SubjectNames = { "pattern", "string", "flags" };
        private static readonly string[] SubNames = { "pattern", "repl", "string", "count", "flags" };
        private static readonly string[] SplitNames = { "pattern", "string", "maxsplit", "flags" };

        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["compile"] = BuiltinFunctionValue.Make("compile", (s, a, kw, c) => ReCompile(c, Kw(c, a, kw, "compile", CompileNames))),
                                ["sub"] = BuiltinFunctionValue.Make("sub", (s, a, kw, c) => ReSub(c, Kw(c, a, kw, "sub", SubNames), false)),
                ["subn"] = BuiltinFunctionValue.Make("subn", (s, a, kw, c) => ReSub(c, Kw(c, a, kw, "subn", SubNames), true)),
                ["split"] = BuiltinFunctionValue.Make("split", (s, a, kw, c) => ReSplit(c, Kw(c, a, kw, "split", SplitNames))),
                ["escape"] = BuiltinFunctionValue.Make("escape", Args.Positional("escape", 1, 1, (s, a, kw, c) => ReEscape(c, a))),
                ["purge"] = BuiltinFunctionValue.Make("purge", Args.Positional("purge", 0, 0, (s, a, kw, c) => Purge(c))),
                ["error"] = ModuleKit.ExceptionClass(PyExceptionTypes.ReError),
                ["PatternError"] = ModuleKit.ExceptionClass(PyExceptionTypes.ReError),
                ["I"] = ctx.Values.Int(ReFlags.I), ["IGNORECASE"] = ctx.Values.Int(ReFlags.I),
                ["M"] = ctx.Values.Int(ReFlags.M), ["MULTILINE"] = ctx.Values.Int(ReFlags.M),
                ["S"] = ctx.Values.Int(ReFlags.S), ["DOTALL"] = ctx.Values.Int(ReFlags.S),
                ["X"] = ctx.Values.Int(ReFlags.X), ["VERBOSE"] = ctx.Values.Int(ReFlags.X),
                ["U"] = ctx.Values.Int(ReFlags.U), ["UNICODE"] = ctx.Values.Int(ReFlags.U),
                ["A"] = ctx.Values.Int(ReFlags.A), ["ASCII"] = ctx.Values.Int(ReFlags.A),
                ["L"] = ctx.Values.Int(ReFlags.L), ["LOCALE"] = ctx.Values.Int(ReFlags.L),
            };
            foreach (KeyValuePair<string, ReOp> op in SubjectOps)
            {
                KeyValuePair<string, ReOp> o = op;
                m[o.Key] = BuiltinFunctionValue.Make(o.Key, (s, a, kw, c) => ModuleOp(c, o.Value, o.Key, Kw(c, a, kw, o.Key, SubjectNames)));
            }
            return ctx.Values.Module("re", m);

        }

        // ---- cache (per-EvalContext, in ModuleState) ----

        private static RegexCache Cache(EvalContext ctx)
        {
            object o;
            if (ctx.ModuleState.TryGetValue("re", out o))
                return (RegexCache)o;
            var cache = new RegexCache();
            ctx.ModuleState["re"] = cache;
            return cache;
        }

        public static PatternValue Compile(EvalContext ctx, ScriptValue patternArg, int flags)
        {
            PatternValue existing = patternArg as PatternValue;
            if (existing != null)
            {
                if (flags != 0)
                    throw Raise.ValueError(ctx, "cannot process flags argument with a compiled pattern");
                return existing;
            }
            StrValue s = patternArg as StrValue;
            if (patternArg is BytesValue)
                throw Raise.TypeError(ctx, "bytes patterns are not supported in this dialect");   // CPython's text named another error
            if (s == null)
                throw Raise.TypeError(ctx, "first argument must be string or compiled pattern");
            return Cache(ctx).GetOrCompile(ctx, s.Value, flags);
        }

        // ---- the subject operations, shared by the module functions and the Pattern methods ----

        internal enum ReOp { Search, Match, FullMatch, Findall, Finditer }

        private static readonly KeyValuePair<string, ReOp>[] SubjectOps =
        {
            new KeyValuePair<string, ReOp>("search", ReOp.Search),
            new KeyValuePair<string, ReOp>("match", ReOp.Match),
            new KeyValuePair<string, ReOp>("fullmatch", ReOp.FullMatch),
            new KeyValuePair<string, ReOp>("findall", ReOp.Findall),
            new KeyValuePair<string, ReOp>("finditer", ReOp.Finditer),
        };

        private static ScriptValue Run(EvalContext c, ReOp op, PatternValue pat, StrValue subj, int pos, int endpos)
        {
            switch (op)
            {
                case ReOp.Search: return Search(c, pat, subj, pos, endpos);
                case ReOp.Match: return MatchAt(c, pat, subj, pos, endpos);
                case ReOp.FullMatch: return FullMatch(c, pat, subj, pos, endpos);
                case ReOp.Findall: return Findall(c, pat, subj, pos, endpos);
                default: return c.Values.EngineIterator(new ReFindIterator(pat, subj, pos, endpos), "callable_iterator");
            }
        }

        // re.<op>(pattern, string, flags=0)
        private static ScriptValue ModuleOp(EvalContext c, ReOp op, string fname, ScriptValue[] a)
        {
            PatternValue pat = Compile(c, ArgAt(c, a, 0, fname), a.Length >= 3 ? FlagsArg(c, a[2]) : 0);
            StrValue subj = AsSubject(c, a, 1);
            return Run(c, op, pat, subj, 0, subj.Value.Length);
        }

        // Pattern.<op>(string, pos=0, endpos=len(string))
        internal static ScriptValue PatternOp(EvalContext c, ReOp op, PatternValue pat, ScriptValue[] a)
        {
            StrValue subj = AsSubject(c, a, 0);
            int pos = ClampPos(a, 1, 0, subj.Value.Length, c);
            int endpos = ClampPos(a, 2, subj.Value.Length, subj.Value.Length, c);
            if (pos > endpos)
            {
                // an empty window past its own end: nothing matches, as in CPython (.NET threw: an engine fault)
                if (op == ReOp.Findall)
                    return c.Values.List(0);
                if (op == ReOp.Finditer)
                    return c.Values.EngineIterator(PyOps.GetIterator(c.Values.List(0), c), "callable_iterator");
                return c.Values.None;
            }
            return Run(c, op, pat, subj, pos, endpos);
        }

        // Keyword spelling for the module functions and the Pattern methods; a numeric slot skipped on the
        // way to a later keyword takes its 0 default (re.sub(..., flags=re.I) leaves count unset), a
        // trailing endpos is absent.
        internal static ScriptValue[] Kw(EvalContext c, ScriptValue[] a, KwArgs kw, string fname, params string[] names)
        {
            Args.AtMost(c, a, fname, names.Length);   // re.match(p, s, 0, 1) dropped the extra argument
            ScriptValue[] r = StrMethods.WithKw(c, a, kw, fname, names);
            if (kw.Count == 0)
                return r;
            for (int i = a.Length; i < r.Length; i++)
            {
                string nm = names[i];
                if (r[i].Kind == ValueKind.None && (nm == "pos" || nm == "count" || nm == "maxsplit" || nm == "flags"))
                    r[i] = c.Values.Int(0);
            }
            if (r.Length > 0 && r[r.Length - 1].Kind == ValueKind.None && names[r.Length - 1] == "endpos")
                Array.Resize(ref r, r.Length - 1);
            return r;
        }

        // ---- module-level functions ----

        private static ScriptValue ReCompile(EvalContext c, ScriptValue[] a)
        {
            if (a.Length < 1)
                throw Raise.TypeError(c, "compile() missing required argument: 'pattern'");
            return Compile(c, a[0], a.Length >= 2 ? FlagsArg(c, a[1]) : 0);
        }







        // re.escape — modern (3.7+) rules: escape only the regex-special characters.
        private static ScriptValue ReEscape(EvalContext c, ScriptValue[] a)
        {
            StrValue s = AsSubject(c, a, 0);
            string v = s.Value;
            c.Budget.ChargeLinear(v.Length);
            int capacity = v.Length + 8;
            if (2L * v.Length > c.Limits.MaxStrChars)
            {
                // the worst case could pass the cap, so the exact length comes first (16M dots became 32M
                // chars before the cap refused them); the cap only, as Str() below charges the result
                c.Budget.ChargeLinear(v.Length);
                long len = v.Length;
                for (int i = 0; i < v.Length; i++)
                {
                    if (v[i] < 128 && EscapeSpecial[v[i]])
                        len++;
                }
                c.Values.CheckStrLen(len);
                capacity = (int)len;
            }
            var sb = new System.Text.StringBuilder(capacity);
            for (int i = 0; i < v.Length; i++)
            {
                char ch = v[i];
                if (ch < 128 && EscapeSpecial[ch])
                    sb.Append('\\');
                sb.Append(ch);
            }
            return c.Values.Str(sb.ToString());
        }

        private static readonly bool[] EscapeSpecial = EscapeTable("()[]{}?*+-|^$\\.&~# \t\n\r\v\f");

        private static bool[] EscapeTable(string special)
        {
            var t = new bool[128];
            foreach (char ch in special)
                t[ch] = true;
            return t;
        }

        private static ScriptValue Purge(EvalContext c)
        {
            object o;
            if (c.ModuleState.TryGetValue("re", out o))
                ((RegexCache)o).Purge();
            return c.Values.None;
        }

        // ---- module-level sub / subn / split ----
        // Signatures: re.sub(pattern, repl, string, count=0, flags=0); re.split(pattern, string, maxsplit=0, flags=0).

        private static ScriptValue ReSub(EvalContext c, ScriptValue[] a, bool subn)
        {
            if (a.Length < 3)
                throw Raise.TypeError(c, (subn ? "subn" : "sub") + "() missing required arguments");
            return SubImpl(c, Compile(c, a[0], a.Length >= 5 ? FlagsArg(c, a[4]) : 0), a, 1, subn);
        }

        private static ScriptValue ReSplit(EvalContext c, ScriptValue[] a)
        {
            return SplitImpl(c, Compile(c, ArgAt(c, a, 0, "split"), a.Length >= 4 ? FlagsArg(c, a[3]) : 0), a, 1);
        }

        internal static ScriptValue PatSub(PatternValue pat, ScriptValue[] a, EvalContext c, bool subn)
        {
            if (a.Length < 2)
                throw Raise.TypeError(c, (subn ? "subn" : "sub") + "() missing required arguments");
            return SubImpl(c, pat, a, 0, subn);
        }

        internal static ScriptValue PatSplit(PatternValue pat, ScriptValue[] a, EvalContext c)
        {
            return SplitImpl(c, pat, a, 0);
        }

        // sub/subn once the pattern is known: repl at a[r], string after it, then count.
        private static ScriptValue SubImpl(EvalContext c, PatternValue pat, ScriptValue[] a, int r, bool subn)
        {
            StrValue subj = AsSubject(c, a, r + 1);
            int count = a.Length > r + 2 ? IntArg(c, a[r + 2]) : 0;
            int n;
            string outStr = SubCore(c, pat, a[r], subj, count, out n);
            return subn ? (ScriptValue)c.Values.Tuple(new ScriptValue[] { c.Values.Str(outStr), c.Values.Int(n) }) : c.Values.Str(outStr);
        }

        // split once the pattern is known: string at a[i], then maxsplit.
        private static ScriptValue SplitImpl(EvalContext c, PatternValue pat, ScriptValue[] a, int i)
        {
            StrValue subj = AsSubject(c, a, i);
            return Split(c, pat, subj, a.Length > i + 1 ? IntArg(c, a[i + 1]) : 0);
        }






        // Modern (3.7+) sub scan: every match counts; empty match advances by 1. repl is a template
        // string OR a callable taking the Match. count<=0 => unlimited (count<0 => 0 replacements).
        private static string SubCore(EvalContext c, PatternValue pat, ScriptValue repl, StrValue subj, int count, out int replacements)
        {
            string input = subj.Value;
            int end = input.Length;
            StrValue replStr = repl as StrValue;
            // One-entry template cache on the pattern: a pattern reused with the same repl string
            // (the dominant sub shape) parses the template once, not per call.
            List<object> template = null;
            if (replStr != null)
            {
                if (string.Equals(pat.CachedReplText, replStr.Value, System.StringComparison.Ordinal))
                {
                    template = pat.CachedReplTemplate;
                }
                else
                {
                    template = ParseTemplate(c, replStr.Value, pat);
                    c.Values.PreCharge(32L * template.Count);   // kept on the pattern: a reference and a boxed group number per piece
                    pat.CachedReplText = replStr.Value;
                    pat.CachedReplTemplate = template;
                }
            }

            var sb = new System.Text.StringBuilder(end + 16);
            int last = 0, p = 0, n = 0;
            Regex re = pat.Plain(c, input);   // once: a callable repl may run the pattern on other subjects
            while (p <= end)
            {
                if (count != 0 && n >= count)
                    break;
                Match m = RunMatch(c, pat, re, input, p, end - p);
                if (!m.Success)
                    break;
                int b = m.Index, e = m.Index + m.Length;
                if (last < b)
                    sb.Append(input, last, b - last);
                if (template != null)
                    ApplyTemplate(c, template, m, pat.Groups, sb);
                else
                    AppendCapped(c, sb, CallRepl(c, repl, MakeMatch(c, m, pat, subj, 0, end)));   // Match wrapper only for a callable repl
                last = e;
                n++;
                p = m.Length == 0 ? e + 1 : e;
            }
            sb.Append(input, last, end - last);
            replacements = n;
            return sb.ToString();
        }

        private static string CallRepl(EvalContext c, ScriptValue repl, MatchValue mv)
        {
            ScriptValue res = c.CallHook1(repl, mv);
            StrValue s = res as StrValue;
            if (s == null)
                throw Raise.TypeError(c, "expected str instance, " + res.PyTypeName + " found");
            return s.Value;
        }

        // Modern (3.7+) split: empty matches split too. Capturing groups are inserted into the result;
        // a non-participating group -> None.
        private static ScriptValue Split(EvalContext c, PatternValue pat, StrValue subj, int maxsplit)
        {
            string input = subj.Value;
            int end = input.Length;
            ListValue result = c.Values.List(8);
            int last = 0, p = 0, n = 0;
            Regex re = pat.Plain(c, input);
            while (p <= end)
            {
                if (maxsplit != 0 && n >= maxsplit)
                    break;
                Match m = RunMatch(c, pat, re, input, p, end - p);
                if (!m.Success)
                    break;
                int b = m.Index, e = m.Index + m.Length;
                result.Add(c.Values.Str(input.Substring(last, b - last)), c);
                for (int i = 1; i <= pat.Groups.Count; i++)
                {
                    Group g = pat.Groups.Of(m, i);
                    result.Add(g.Success ? (ScriptValue)c.Values.Str(g.Value) : c.Values.None, c);
                }
                last = e;
                n++;
                p = m.Length == 0 ? e + 1 : e;
            }
            result.Add(c.Values.Str(input.Substring(last, end - last)), c);
            return result;
        }

        // --- repl-template parser: \1..\99, \g<name>, \g<0>; $ literal; modern escapes ----

        private static List<object> ParseTemplate(EvalContext c, string repl, PatternValue pat)
        {
            c.Budget.ChargeLinear(repl.Length);
            var segs = new List<object>();
            var lit = new System.Text.StringBuilder();
            int i = 0, n = repl.Length;
            while (i < n)
            {
                if ((i & 0xFFFE) == 0xFFFE)   // i steps by 1 or 2: one of the two is hit in every 64K, never index 0
                    c.Budget.CheckDeadlineAfterCall();
                char ch = repl[i];
                if (ch != '\\')
                {
                    lit.Append(ch);
                    i++;
                    continue;
                }
                if (i + 1 >= n)
                    throw Raise.Make(c, PyExceptionTypes.ReError, "bad escape (end of pattern)");
                char c2 = repl[i + 1];
                if (c2 == '0')
                {
                    // CPython's parse_template: \0 and up to two more octal digits are a character, never group 0
                    int j = i + 2, code = 0;
                    while (j < n && j < i + 4 && IsOctal(repl[j]))
                        code = code * 8 + (repl[j++] - '0');
                    lit.Append((char)code);
                    i = j;
                    continue;
                }
                if (c2 >= '1' && c2 <= '9')
                {
                    // three octal digits are a character; otherwise one or two digits are a group
                    if (i + 3 < n && IsOctal(c2) && IsOctal(repl[i + 2]) && IsOctal(repl[i + 3]))
                    {
                        int code = (c2 - '0') * 64 + (repl[i + 2] - '0') * 8 + (repl[i + 3] - '0');
                        if (code > 0xFF)
                            throw Raise.Make(c, PyExceptionTypes.ReError,
                                "octal escape value \\" + repl.Substring(i + 1, 3) + " outside of range 0-0o377");
                        lit.Append((char)code);
                        i += 4;
                        continue;
                    }
                    int num = c2 - '0', j2 = i + 2;
                    if (j2 < n && repl[j2] >= '0' && repl[j2] <= '9')
                        num = num * 10 + (repl[j2++] - '0');
                    FlushLit(segs, lit);
                    segs.Add(GroupRef(c, num, pat));
                    i = j2;
                    continue;
                }
                if (c2 == 'g')
                {
                    i = GroupNameRef(c, repl, i, pat, segs, lit);
                    continue;
                }
                switch (c2)
                {
                    case 'n': lit.Append('\n'); break;
                    case 't': lit.Append('\t'); break;
                    case 'r': lit.Append('\r'); break;
                    case 'f': lit.Append('\f'); break;
                    case 'v': lit.Append('\v'); break;
                    case 'a': lit.Append('\a'); break;
                    case 'b': lit.Append('\b'); break;
                    case '\\': lit.Append('\\'); break;
                    default:
                        if ((c2 >= 'a' && c2 <= 'z') || (c2 >= 'A' && c2 <= 'Z'))
                            throw Raise.Make(c, PyExceptionTypes.ReError, "bad escape \\" + c2);
                        lit.Append(c2);   // \ + non-letter -> the literal char
                        break;
                }
                i += 2;
            }
            FlushLit(segs, lit);
            return segs;
        }

        private static int GroupNameRef(EvalContext c, string repl, int i, PatternValue pat, List<object> segs, System.Text.StringBuilder lit)
        {
            int n = repl.Length;
            if (i + 2 >= n || repl[i + 2] != '<')
                throw Raise.Make(c, PyExceptionTypes.ReError, "missing <, unterminated name");
            int j = i + 3;
            while (j < n && repl[j] != '>')
                j++;
            if (j >= n)
                throw Raise.Make(c, PyExceptionTypes.ReError, "missing >, unterminated name");
            string name = repl.Substring(i + 3, j - (i + 3));
            int gi;
            if (int.TryParse(name, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out gi))
            {
                FlushLit(segs, lit);
                segs.Add(GroupRef(c, gi, pat));
            }
            else
            {
                if (!pat.Groups.NameIndex.TryGetValue(name, out gi))
                {
                    // CPython: a well-formed name the pattern lacks is an IndexError, not a pattern error
                    throw Raise.Make(c, StrMethods.IsIdentifier(name) ? PyExceptionTypes.IndexError : PyExceptionTypes.ReError,
                        "unknown group name '" + name + "'");
                }
                FlushLit(segs, lit);
                segs.Add(gi);
            }
            return j + 1;
        }

        private static bool IsOctal(char ch)
        {
            return ch >= '0' && ch <= '7';
        }

        private static int GroupRef(EvalContext c, int num, PatternValue pat)
        {
            if (num > pat.Groups.Count)
                throw Raise.Make(c, PyExceptionTypes.ReError, "invalid group reference " + num);
            return num;   // 0 = whole match
        }

        private static void FlushLit(List<object> segs, System.Text.StringBuilder lit)
        {
            if (lit.Length > 0)
            {
                segs.Add(lit.ToString());
                lit.Clear();
            }
        }

        // Match.expand(template): the same template machinery re.sub uses, applied to one match.
        internal static ScriptValue MatchExpand(MatchValue mv, ScriptValue[] a, EvalContext c)
        {
            Args.Exactly(c, a, "expand", 1);
            StrValue repl = a[0] as StrValue;
            if (repl == null)
                throw Raise.TypeError(c, "expected str instance, " + a[0].PyTypeName + " found");
            List<object> template = ParseTemplate(c, repl.Value, mv.Pattern);
            var sb = new System.Text.StringBuilder();
            ApplyTemplate(c, template, mv.Net, mv.Groups, sb);
            return c.Values.Str(sb.ToString());
        }

        // lastindex/lastgroup: the group that closed LAST, which is the successful group ending furthest
        // right, and the outer one when two end together. .NET does not track the engine's own mark, so
        // this is read off the group spans instead.
        internal static int LastGroupIndex(MatchValue mv)
        {
            if (mv.LastIndexCache != MatchValue.NotComputed)
                return mv.LastIndexCache;
            int best = -1, bestEnd = -1;
            for (int gi = 1; gi <= mv.Groups.Count; gi++)
            {
                Group g = mv.Groups.Of(mv.Net, gi);
                if (!g.Success)
                    continue;
                int end = g.Index + g.Length;
                if (end > bestEnd)
                {
                    bestEnd = end;
                    best = gi;
                }
            }
            mv.LastIndexCache = best;
            return best;
        }

        // One expansion of one match, appended to sb. A template repeating a group (br'\1' * 100000)
        // multiplies the subject by the template length, so the result is checked as it grows: without that the
        // StringBuilder raises OutOfMemory and the run ends as an engine fault instead of a budget stop.
        private static void ApplyTemplate(EvalContext c, List<object> segs, Match m, GroupMap groups, System.Text.StringBuilder sb)
        {
            for (int k = 0; k < segs.Count; k++)
            {
                c.Budget.Step();   // a template of 1M group references is 1M pieces
                object seg = segs[k];
                string s = seg as string;
                if (s == null)
                {
                    int gi = (int)seg;
                    if (gi == 0)
                    {
                        s = m.Value;
                    }
                    else
                    {
                        Group g = groups.Of(m, gi);
                        s = g.Success ? g.Value : "";   // a non-participating group -> "" (modern parity)
                    }
                }
                AppendCapped(c, sb, s);
            }
        }

        // The whole result so far is capped, only the piece is charged (a template or a callable may repeat
        // a large group or return the same large string many times).
        private static void AppendCapped(EvalContext c, System.Text.StringBuilder sb, string s)
        {
            long total = (long)sb.Length + s.Length;
            if (total > c.Limits.MaxStrChars)
                c.Values.EnsureStrLen(total);
            c.Budget.ChargeAllocation(2L * s.Length);
            sb.Append(s);
        }

        // ---- findall / finditer core ----

        internal static ScriptValue Findall(EvalContext c, PatternValue pat, StrValue subj, int pos, int endpos)
        {
            ListValue result = c.Values.List(8);
            string input = Truncated(c, subj.Value, endpos);
            int p = pos;
            Regex re = pat.Plain(c, input);
            while (p <= endpos)
            {
                Match m = RunMatch(c, pat, re, input, p, endpos - p);
                if (!m.Success)
                    break;
                result.Add(FindallElement(c, pat, m), c);
                int mEnd = m.Index + m.Length;
                p = m.Length == 0 ? mEnd + 1 : mEnd;   // modern advance
            }
            return result;
        }

        // findall element: 0 groups -> the whole match; 1 group -> that group; N -> a tuple. A non-participating
        // group is the empty string (Python findall parity), NOT None.
        private static ScriptValue FindallElement(EvalContext c, PatternValue pat, Match m)
        {
            int n = pat.Groups.Count;
            if (n == 0)
                return c.Values.Str(m.Value);
            if (n == 1)
            {
                Group g = pat.Groups.Of(m, 1);
                return c.Values.Str(g.Success ? g.Value : "");
            }
            var items = new ScriptValue[n];
            for (int i = 1; i <= n; i++)
            {
                Group g = pat.Groups.Of(m, i);
                items[i - 1] = c.Values.Str(g.Success ? g.Value : "");
            }
            return c.Values.Tuple(items);
        }

        // ---- Pattern.groupindex ----






        public static ScriptValue GroupIndexDict(PatternValue pat, EvalContext c)
        {
            DictValue d = c.Values.Dict(pat.Groups.NameIndex.Count);
            foreach (KeyValuePair<string, int> kv in ByIndex(pat.Groups.NameIndex))
                d.SetItem(c.Values.Str(kv.Key), c.Values.Int(kv.Value), c);
            return d;
        }

        // ---- run + build ----

        private static ScriptValue Search(EvalContext c, PatternValue pat, StrValue subj, int pos, int endpos)
        {
            Match m = RunMatch(c, pat, pat.Plain(c, subj.Value), subj.Value, pos, endpos - pos);
            return m.Success ? (ScriptValue)MakeMatch(c, m, pat, subj, pos, endpos) : c.Values.None;
        }

        private static ScriptValue MatchAt(EvalContext c, PatternValue pat, StrValue subj, int pos, int endpos)
        {
            Match m = RunMatch(c, pat, pat.Anchored(c, subj.Value), subj.Value, pos, endpos - pos);   // CPython's match is anchored at pos
            return m.Success && m.Index == pos ? (ScriptValue)MakeMatch(c, m, pat, subj, pos, endpos) : c.Values.None;
        }

        private static ScriptValue FullMatch(EvalContext c, PatternValue pat, StrValue subj, int pos, int endpos)
        {
            Match m = RunMatch(c, pat, pat.Full(c, subj.Value), subj.Value, pos, endpos - pos);
            return m.Success && m.Index == pos && m.Index + m.Length == endpos
                ? (ScriptValue)MakeMatch(c, m, pat, subj, pos, endpos)
                : c.Values.None;
        }

        // The single fenced .NET Match entry: deadline before/after, per-op MatchTimeout, ChargeRegexTime.
        // Stopwatch timestamp ticks -> TimeSpan (100 ns) ticks, so we can measure a match without allocating
        // a Stopwatch object on every call (this runs once per match, and N times per sub/findall).
        private static readonly double SwToTimeSpanTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;

        // CPython's endpos ends the subject, but pos is no string boundary: \b, lookbehind and ^ still see the text
        // before it. Regex.Run, the callee of Match(s, startat), scans the text [0, end) from startat: that window with
        // no copy of s[:endpos], which a lexer's match(buf, pos, line_end) made per line (2.4 GB over a 100 KB buffer).
        private static readonly Func<Regex, bool, int, string, int, int, int, Match> WindowRun = FindWindowRun();

        private static Func<Regex, bool, int, string, int, int, int, Match> FindWindowRun()
        {
            try
            {
                System.Reflection.MethodInfo run = typeof(Regex).GetMethod("Run",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null,
                    new[] { typeof(bool), typeof(int), typeof(string), typeof(int), typeof(int), typeof(int) }, null);
                if (run == null || run.ReturnType != typeof(Match))
                    return null;
                return (Func<Regex, bool, int, string, int, int, int, Match>)Delegate.CreateDelegate(
                    typeof(Func<Regex, bool, int, string, int, int, int, Match>), run, false);
            }
            catch (Exception)
            {
                return null;   // a runtime that hides it: the charged copy below stands in, the type must not fail
            }
        }

        // The subject findall and finditer scan for endpos: the whole one when RunMatch can window it, else the copy,
        // once per operation and charged (a finditer keeps it: sixty kept iterators held 487 MB).
        internal static string Truncated(EvalContext ctx, string input, int endpos)
        {
            if (endpos == input.Length || WindowRun != null)
                return input;
            ctx.Values.PreCharge(24 + 2L * endpos);
            return input.Substring(0, endpos);
        }

        // A pattern that loops a group without a small bound keeps a backtracking stack as long as the longest
        // subject it ran on, inside the .NET Regex the run holds (40 cached ones over 500K chars kept 446 MB):
        // charged once per new longest subject, for each regex the pattern runs. A bounded loop ({1,10} in {1,10}:
        // 100 passes) stops at its passes (charged per subject char, 12 such rules over a 3.6 MB document were
        // refused). `(?:INFO|WARN)?` and other patterns keep a small stack.
        internal static Match RunMatch(EvalContext ctx, PatternValue pat, Regex re, string input, int beginning, int length)
        {
            int reach = (int)System.Math.Min(beginning + (long)length, pat.Groups.LoopPasses);
            int slot = pat.RunSlot(ctx, re);
            if (pat.Groups.LongLoop && reach > pat.StackReach(slot))
            {
                ctx.Budget.ChargeAllocation(16L * (reach - pat.StackReach(slot)));
                pat.SetStackReach(slot, reach);
            }
            return RunMatch(ctx, re, input, beginning, length);
        }

        internal static Match RunMatch(EvalContext ctx, Regex re, string input, int beginning, int length)
        {
            // Matches pay steps: re.sub('', '', s) over 4M chars made 4M matches on 94 steps.
            ctx.Budget.StepMatch();
            ctx.Budget.CheckDeadlineNow();
            long t0 = Stopwatch.GetTimestamp();
            Match m;
            try
            {
                // .NET's (beginning, length) overload treats both window edges as string edges: see WindowRun
                int end = beginning + length;
                if (end == input.Length)
                {
                    m = re.Match(input, beginning);
                }
                else if (WindowRun != null)
                {
                    m = WindowRun(re, false, -1, input, 0, end, beginning);
                }
                else
                {
                    ctx.Budget.ChargeAllocation(2L * end);   // the copy lives as long as the match: 400 kept cost 800 MB
                    m = re.Match(input.Substring(0, end), beginning);
                }
            }
            catch (RegexMatchTimeoutException)
            {
                throw ctx.Budget.CreateAbort(EngineAbortKind.RegexTimeout, "RegexPerOpTimeout",
                    ctx.Limits.RegexPerOpTimeoutMs, ctx.Limits.RegexPerOpTimeoutMs);
            }
            catch (OutOfMemoryException)
            {
                // A pathological bound (e.g. a{999999999}) makes the .NET backtracking engine try to allocate
                // a tracking array past the max array size. It is a controlled resource guard, and the run
                // ends here regardless — surface it as a memory-budget abort, not an EngineFault.
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "RegexEngine",
                    ctx.Limits.MaxAllocBytes, ctx.Limits.MaxAllocBytes);
            }
            long swTicks = Stopwatch.GetTimestamp() - t0;
            ctx.Budget.ChargeRegexTime(TimeSpan.FromTicks((long)(swTicks * SwToTimeSpanTicks)));
            ctx.Budget.CheckDeadlineNow();
            return m;
        }

        // A .NET Match keeps an array per group and, for a group under a quantifier, every capture it made:
        // 4000 matches of 2000 groups, or 100 of (a)* over 500K chars, took 360-550 MB charged as nothing.
        internal static MatchValue MakeMatch(EvalContext c, Match m, PatternValue pat, StrValue subj, int pos, int endpos)
        {
            long bytes = 64 + 32L * pat.Groups.Count;
            if (pat.Groups.Repeated)
            {
                for (int g = 1; g <= pat.Groups.Count; g++)
                    bytes += 8L * pat.Groups.Of(m, g).Captures.Count;
            }
            c.Values.PreCharge(bytes);
            return new MatchValue(m, pat.Groups, pat, subj, pos, endpos);
        }

        // ---- Match methods (called by MatchValue slots) ----

        public static ScriptValue MatchGroup(MatchValue m, ScriptValue[] a, EvalContext c)
        {
            if (a.Length == 0)
                return GroupObj(c, m, 0);
            if (a.Length == 1)
                return GroupObj(c, m, ResolveIndex(c, m, a[0]));
            var items = new ScriptValue[a.Length];
            for (int i = 0; i < a.Length; i++)
                items[i] = GroupObj(c, m, ResolveIndex(c, m, a[i]));
            return c.Values.Tuple(items);
        }

        public static ScriptValue MatchGroups(MatchValue m, ScriptValue[] a, EvalContext c)
        {
            ScriptValue dflt = a.Length >= 1 ? a[0] : c.Values.None;
            var items = new ScriptValue[m.Groups.Count];
            for (int i = 1; i <= m.Groups.Count; i++)
            {
                Group g = m.Groups.Of(m.Net, i);
                items[i - 1] = g.Success ? (ScriptValue)c.Values.Str(g.Value) : dflt;
            }
            return c.Values.Tuple(items);
        }

        public static ScriptValue MatchGroupDict(MatchValue m, ScriptValue[] a, EvalContext c)
        {
            ScriptValue dflt = a.Length >= 1 ? a[0] : c.Values.None;
            DictValue d = c.Values.Dict(m.Groups.NameIndex.Count);
            foreach (KeyValuePair<string, int> kv in ByIndex(m.Groups.NameIndex))
            {
                Group g = m.Groups.Of(m.Net, kv.Value);
                d.SetItem(c.Values.Str(kv.Key), g.Success ? (ScriptValue)c.Values.Str(g.Value) : dflt, c);
            }
            return d;
        }

        public static ScriptValue MatchStart(MatchValue m, ScriptValue[] a, EvalContext c)
        {
            int s, e;
            SpanOf(m, a.Length >= 1 ? ResolveIndex(c, m, a[0]) : 0, out s, out e);
            return c.Values.Int(s);
        }

        public static ScriptValue MatchEnd(MatchValue m, ScriptValue[] a, EvalContext c)
        {
            int s, e;
            SpanOf(m, a.Length >= 1 ? ResolveIndex(c, m, a[0]) : 0, out s, out e);
            return c.Values.Int(e);
        }

        public static ScriptValue MatchSpan(MatchValue m, ScriptValue[] a, EvalContext c)
        {
            int s, e;
            SpanOf(m, a.Length >= 1 ? ResolveIndex(c, m, a[0]) : 0, out s, out e);
            return c.Values.Tuple(new ScriptValue[] { c.Values.Int(s), c.Values.Int(e) });
        }

        private static ScriptValue GroupObj(EvalContext c, MatchValue m, int index)
        {
            if (index == 0)
                return c.Values.Str(m.Net.Value);
            Group g = m.Groups.Of(m.Net, index);
            return g.Success ? (ScriptValue)c.Values.Str(g.Value) : c.Values.None;
        }

        private static void SpanOf(MatchValue m, int index, out int start, out int end)
        {
            if (index == 0)
            {
                start = m.Net.Index;
                end = m.Net.Index + m.Net.Length;
                return;
            }
            Group g = m.Groups.Of(m.Net, index);
            if (g.Success)
            {
                start = g.Index;
                end = g.Index + g.Length;
            }
            else
            {
                start = -1;
                end = -1;
            }
        }

        private static int ResolveIndex(EvalContext c, MatchValue m, ScriptValue key)
        {
            IntValue iv = key as IntValue;
            if (iv != null)
            {
                System.Numerics.BigInteger gi = iv.Value;
                if (gi < 0 || gi > m.Groups.Count)
                    throw Raise.Make(c, PyExceptionTypes.IndexError, "no such group");
                return (int)gi;
            }
            StrValue s = key as StrValue;
            if (s != null)
            {
                int idx;
                if (m.Groups.NameIndex.TryGetValue(s.Value, out idx))
                    return idx;
                throw Raise.Make(c, PyExceptionTypes.IndexError, "no such group");
            }
            throw Raise.TypeError(c, "group indices must be integers or strings, not " + key.PyTypeName);
        }

        // ---- argument helpers ----

        private static ScriptValue ArgAt(EvalContext c, ScriptValue[] a, int i, string fn)
        {
            if (a.Length <= i)
                throw Raise.TypeError(c, fn + "() missing required argument");
            return a[i];
        }

        private static StrValue AsSubject(EvalContext c, ScriptValue[] a, int i)
        {
            if (a.Length <= i)
                throw Raise.TypeError(c, "missing subject string");
            StrValue s = a[i] as StrValue;
            if (s == null)
                throw Raise.TypeError(c, "expected string or bytes-like object");
            return s;
        }

        private static int ClampPos(ScriptValue[] a, int i, int dflt, int len, EvalContext c)
        {
            if (a.Length <= i || a[i].Kind == ValueKind.None)
                return dflt;
            if (a[i].Kind != ValueKind.Int && a[i].Kind != ValueKind.Bool)
                throw Raise.TypeError(c, "pos/endpos must be integers");
            System.Numerics.BigInteger v = NumericOps.AsBigInteger(a[i]);
            if (v < 0) return 0;
            if (v > len) return len;
            return (int)v;
        }

        private static int FlagsArg(EvalContext c, ScriptValue v)
        {
            return Support.Coerce.ToInt32(c, v, "flags must be an integer");   // clamps: a huge int must not overflow the cast
        }

        private static int IntArg(EvalContext c, ScriptValue v)
        {
            return Support.Coerce.ToInt32(c, v, "expected an integer");
        }

        // named groups in ascending index order (definition order — deterministic, matches CPython groupdict)
        private static IEnumerable<KeyValuePair<string, int>> ByIndex(Dictionary<string, int> nameIndex)
        {
            var list = new List<KeyValuePair<string, int>>(nameIndex);
            list.Sort((x, y) => x.Value.CompareTo(y.Value));
            return list;
        }
    }
}

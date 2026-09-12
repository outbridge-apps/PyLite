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
    // CheckDeadlineNow. Phase 1: compile/search/match/fullmatch + Pattern/Match API. (findall/finditer/sub/
    // subn/split land next.)
    internal static class ReModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["compile"] = BuiltinFunctionValue.Make("compile", (s, a, kw, c) => ReCompile(c, Kw(c, a, kw, "compile", "pattern", "flags"))),
                ["search"] = BuiltinFunctionValue.Make("search", (s, a, kw, c) => ReSearch(c, Kw(c, a, kw, "search", "pattern", "string", "flags"))),
                ["match"] = BuiltinFunctionValue.Make("match", (s, a, kw, c) => ReMatch(c, Kw(c, a, kw, "match", "pattern", "string", "flags"))),
                ["fullmatch"] = BuiltinFunctionValue.Make("fullmatch", (s, a, kw, c) => ReFullMatch(c, Kw(c, a, kw, "fullmatch", "pattern", "string", "flags"))),
                ["findall"] = BuiltinFunctionValue.Make("findall", (s, a, kw, c) => ReFindall(c, Kw(c, a, kw, "findall", "pattern", "string", "flags"))),
                ["finditer"] = BuiltinFunctionValue.Make("finditer", (s, a, kw, c) => ReFinditer(c, Kw(c, a, kw, "finditer", "pattern", "string", "flags"))),
                ["sub"] = BuiltinFunctionValue.Make("sub", (s, a, kw, c) => ReSub(c, Kw(c, a, kw, "sub", "pattern", "repl", "string", "count", "flags"))),
                ["subn"] = BuiltinFunctionValue.Make("subn", (s, a, kw, c) => ReSubn(c, Kw(c, a, kw, "subn", "pattern", "repl", "string", "count", "flags"))),
                ["split"] = BuiltinFunctionValue.Make("split", (s, a, kw, c) => ReSplit(c, Kw(c, a, kw, "split", "pattern", "string", "maxsplit", "flags"))),
                ["escape"] = BuiltinFunctionValue.Make("escape", (s, a, kw, c) => ReEscape(c, a)),
                ["purge"] = BuiltinFunctionValue.Make("purge", (s, a, kw, c) => Purge(c)),
                ["error"] = ErrorType(),
                ["I"] = ctx.Values.Int(ReFlags.I), ["IGNORECASE"] = ctx.Values.Int(ReFlags.I),
                ["M"] = ctx.Values.Int(ReFlags.M), ["MULTILINE"] = ctx.Values.Int(ReFlags.M),
                ["S"] = ctx.Values.Int(ReFlags.S), ["DOTALL"] = ctx.Values.Int(ReFlags.S),
                ["X"] = ctx.Values.Int(ReFlags.X), ["VERBOSE"] = ctx.Values.Int(ReFlags.X),
                ["U"] = ctx.Values.Int(ReFlags.U), ["UNICODE"] = ctx.Values.Int(ReFlags.U),
                ["A"] = ctx.Values.Int(ReFlags.A), ["ASCII"] = ctx.Values.Int(ReFlags.A),
                ["L"] = ctx.Values.Int(ReFlags.L), ["LOCALE"] = ctx.Values.Int(ReFlags.L),
            };
            return ctx.Values.Module("re", m);
        }

        private static TypeValue ErrorType()
        {
            BuiltinDelegate ctor = (self, args, kw, c) => c.Values.Exception(PyExceptionTypes.ReError, args);
            return TypeValue.Make("error", ctor, null, null, PyExceptionTypes.ReError);
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
            if (s == null)
                throw Raise.TypeError(ctx, "first argument must be string or compiled pattern");
            return Cache(ctx).GetOrCompile(ctx, s.Value, flags);
        }

        // ---- module-level functions ----

        private static ScriptValue ReCompile(EvalContext c, ScriptValue[] a)
        {
            if (a.Length < 1)
                throw Raise.TypeError(c, "compile() missing required argument: 'pattern'");
            return Compile(c, a[0], a.Length >= 2 ? FlagsArg(c, a[1]) : 0);
        }

        private static ScriptValue ReSearch(EvalContext c, ScriptValue[] a)
        {
            PatternValue pat = Compile(c, ArgAt(c, a, 0, "search"), a.Length >= 3 ? FlagsArg(c, a[2]) : 0);
            StrValue subj = AsSubject(c, a, 1);
            return Search(c, pat, subj, 0, subj.Value.Length);
        }

        private static ScriptValue ReMatch(EvalContext c, ScriptValue[] a)
        {
            PatternValue pat = Compile(c, ArgAt(c, a, 0, "match"), a.Length >= 3 ? FlagsArg(c, a[2]) : 0);
            StrValue subj = AsSubject(c, a, 1);
            return MatchAt(c, pat, subj, 0, subj.Value.Length);
        }

        // Keyword spelling for the module-level functions; a numeric slot skipped on the way to a later
        // keyword (sub(..., flags=re.I) leaves count unset) takes its 0 default.
        private static ScriptValue[] Kw(EvalContext c, ScriptValue[] a, KwArgs kw, string fname, params string[] names)
        {
            ScriptValue[] r = StrMethods.WithKw(c, a, kw, fname, names);
            if (kw.Count == 0)
                return r;
            for (int i = a.Length; i < r.Length; i++)
            {
                string nm = names[i];
                if (r[i].Kind == ValueKind.None && (nm == "count" || nm == "maxsplit" || nm == "flags"))
                    r[i] = c.Values.Int(0);
            }
            return r;
        }

        private static ScriptValue ReFullMatch(EvalContext c, ScriptValue[] a)
        {
            PatternValue pat = Compile(c, ArgAt(c, a, 0, "fullmatch"), a.Length >= 3 ? FlagsArg(c, a[2]) : 0);
            StrValue subj = AsSubject(c, a, 1);
            return FullMatch(c, pat, subj, 0, subj.Value.Length);
        }

        private static ScriptValue ReFindall(EvalContext c, ScriptValue[] a)
        {
            PatternValue pat = Compile(c, ArgAt(c, a, 0, "findall"), a.Length >= 3 ? FlagsArg(c, a[2]) : 0);
            StrValue subj = AsSubject(c, a, 1);
            return Findall(c, pat, subj, 0, subj.Value.Length);
        }

        private static ScriptValue ReFinditer(EvalContext c, ScriptValue[] a)
        {
            PatternValue pat = Compile(c, ArgAt(c, a, 0, "finditer"), a.Length >= 3 ? FlagsArg(c, a[2]) : 0);
            StrValue subj = AsSubject(c, a, 1);
            return c.Values.Iterator(new ReFindIterator(pat, subj, 0, subj.Value.Length), "callable_iterator");
        }

        // re.escape — modern (3.7+) rules: escape only the regex-special characters.
        private static ScriptValue ReEscape(EvalContext c, ScriptValue[] a)
        {
            StrValue s = AsSubject(c, a, 0);
            const string special = "()[]{}?*+-|^$\\.&~# \t\n\r\v\f";
            string v = s.Value;
            var sb = new System.Text.StringBuilder(v.Length + 8);
            for (int i = 0; i < v.Length; i++)
            {
                char ch = v[i];
                if (special.IndexOf(ch) >= 0)
                    sb.Append('\\');
                sb.Append(ch);
            }
            return c.Values.Str(sb.ToString());
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

        private static ScriptValue ReSub(EvalContext c, ScriptValue[] a)
        {
            if (a.Length < 3)
                throw Raise.TypeError(c, "sub() missing required arguments");
            PatternValue pat = Compile(c, a[0], a.Length >= 5 ? FlagsArg(c, a[4]) : 0);
            StrValue subj = AsSubject(c, a, 2);
            int count = a.Length >= 4 ? IntArg(c, a[3]) : 0;
            int n;
            string outStr = SubCore(c, pat, a[1], subj, count, out n);
            return c.Values.Str(outStr);
        }

        private static ScriptValue ReSubn(EvalContext c, ScriptValue[] a)
        {
            if (a.Length < 3)
                throw Raise.TypeError(c, "subn() missing required arguments");
            PatternValue pat = Compile(c, a[0], a.Length >= 5 ? FlagsArg(c, a[4]) : 0);
            StrValue subj = AsSubject(c, a, 2);
            int count = a.Length >= 4 ? IntArg(c, a[3]) : 0;
            int n;
            string outStr = SubCore(c, pat, a[1], subj, count, out n);
            return c.Values.Tuple(new ScriptValue[] { c.Values.Str(outStr), c.Values.Int(n) });
        }

        private static ScriptValue ReSplit(EvalContext c, ScriptValue[] a)
        {
            PatternValue pat = Compile(c, ArgAt(c, a, 0, "split"), a.Length >= 4 ? FlagsArg(c, a[3]) : 0);
            StrValue subj = AsSubject(c, a, 1);
            int maxsplit = a.Length >= 3 ? IntArg(c, a[2]) : 0;
            return Split(c, pat, subj, maxsplit);
        }

        public static ScriptValue PatSub(PatternValue pat, ScriptValue[] a, EvalContext c)
        {
            if (a.Length < 2)
                throw Raise.TypeError(c, "sub() missing required arguments");
            StrValue subj = AsSubject(c, a, 1);
            int count = a.Length >= 3 ? IntArg(c, a[2]) : 0;
            int n;
            return c.Values.Str(SubCore(c, pat, a[0], subj, count, out n));
        }

        public static ScriptValue PatSubn(PatternValue pat, ScriptValue[] a, EvalContext c)
        {
            if (a.Length < 2)
                throw Raise.TypeError(c, "subn() missing required arguments");
            StrValue subj = AsSubject(c, a, 1);
            int count = a.Length >= 3 ? IntArg(c, a[2]) : 0;
            int n;
            string outStr = SubCore(c, pat, a[0], subj, count, out n);
            return c.Values.Tuple(new ScriptValue[] { c.Values.Str(outStr), c.Values.Int(n) });
        }

        public static ScriptValue PatSplit(PatternValue pat, ScriptValue[] a, EvalContext c)
        {
            StrValue subj = AsSubject(c, a, 0);
            int maxsplit = a.Length >= 2 ? IntArg(c, a[1]) : 0;
            return Split(c, pat, subj, maxsplit);
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
                    pat.CachedReplText = replStr.Value;
                    pat.CachedReplTemplate = template;
                }
            }

            var sb = new System.Text.StringBuilder(end + 16);
            int last = 0, p = 0, n = 0;
            while (p <= end)
            {
                if (count != 0 && n >= count)
                    break;
                Match m = RunMatch(c, pat.Net, input, p, end - p);
                if (!m.Success)
                    break;
                int b = m.Index, e = m.Index + m.Length;
                if (last < b)
                    sb.Append(input, last, b - last);
                if (template != null)
                    sb.Append(ApplyTemplate(c, template, m));
                else
                    sb.Append(CallRepl(c, repl, MakeMatch(c, m, pat, subj, 0, end)));   // Match wrapper only for a callable repl
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
            while (p <= end)
            {
                if (maxsplit != 0 && n >= maxsplit)
                    break;
                Match m = RunMatch(c, pat.Net, input, p, end - p);
                if (!m.Success)
                    break;
                int b = m.Index, e = m.Index + m.Length;
                result.Add(c.Values.Str(input.Substring(last, b - last)), c);
                for (int i = 1; i <= pat.Groups.Count; i++)
                {
                    Group g = m.Groups[GroupMap.Net(i)];
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
            var segs = new List<object>();
            var lit = new System.Text.StringBuilder();
            int i = 0, n = repl.Length;
            while (i < n)
            {
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
                if (c2 >= '0' && c2 <= '9')
                {
                    int j = i + 1, num = 0, digits = 0;
                    while (j < n && repl[j] >= '0' && repl[j] <= '9' && digits < 2)
                    {
                        num = num * 10 + (repl[j] - '0');
                        j++;
                        digits++;
                    }
                    FlushLit(segs, lit);
                    segs.Add(GroupRef(c, num, pat));
                    i = j;
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
                    throw Raise.Make(c, PyExceptionTypes.ReError, "unknown group name '" + name + "'");
                FlushLit(segs, lit);
                segs.Add(gi);
            }
            return j + 1;
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
            return c.Values.Str(ApplyTemplate(c, template, mv.Net));
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
                Group g = mv.Net.Groups[GroupMap.Net(gi)];
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

        // One expansion of one match. A template repeating a group (br'\1' * 100000) multiplies the
        // subject by the template length, so the result is checked as it grows: without that the
        // StringBuilder raises OutOfMemory and the run ends as an engine fault instead of a budget stop.
        private static string ApplyTemplate(EvalContext c, List<object> segs, Match m)
        {
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < segs.Count; k++)
            {
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
                        Group g = m.Groups[GroupMap.Net(gi)];
                        s = g.Success ? g.Value : "";   // a non-participating group -> "" (modern parity)
                    }
                }
                c.Values.EnsureStrLen((long)sb.Length + s.Length);
                sb.Append(s);
            }
            return sb.ToString();
        }

        // ---- findall / finditer core ----

        internal static ScriptValue Findall(EvalContext c, PatternValue pat, StrValue subj, int pos, int endpos)
        {
            ListValue result = c.Values.List(8);
            int p = pos;
            while (p <= endpos)
            {
                Match m = RunMatch(c, pat.Net, subj.Value, p, endpos - p);
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
                Group g = m.Groups[GroupMap.Net(1)];
                return c.Values.Str(g.Success ? g.Value : "");
            }
            var items = new ScriptValue[n];
            for (int i = 1; i <= n; i++)
            {
                Group g = m.Groups[GroupMap.Net(i)];
                items[i - 1] = c.Values.Str(g.Success ? g.Value : "");
            }
            return c.Values.Tuple(items);
        }

        // ---- Pattern methods (called by PatternValue slots) ----

        public static ScriptValue PatSearch(PatternValue pat, ScriptValue[] a, EvalContext c)
        {
            StrValue subj = AsSubject(c, a, 0);
            int pos = ClampPos(a, 1, 0, subj.Value.Length, c);
            int endpos = ClampPos(a, 2, subj.Value.Length, subj.Value.Length, c);
            return Search(c, pat, subj, pos, endpos);
        }

        public static ScriptValue PatMatch(PatternValue pat, ScriptValue[] a, EvalContext c)
        {
            StrValue subj = AsSubject(c, a, 0);
            int pos = ClampPos(a, 1, 0, subj.Value.Length, c);
            int endpos = ClampPos(a, 2, subj.Value.Length, subj.Value.Length, c);
            return MatchAt(c, pat, subj, pos, endpos);
        }

        public static ScriptValue PatFullMatch(PatternValue pat, ScriptValue[] a, EvalContext c)
        {
            StrValue subj = AsSubject(c, a, 0);
            int pos = ClampPos(a, 1, 0, subj.Value.Length, c);
            int endpos = ClampPos(a, 2, subj.Value.Length, subj.Value.Length, c);
            return FullMatch(c, pat, subj, pos, endpos);
        }

        public static ScriptValue PatFindall(PatternValue pat, ScriptValue[] a, EvalContext c)
        {
            StrValue subj = AsSubject(c, a, 0);
            int pos = ClampPos(a, 1, 0, subj.Value.Length, c);
            int endpos = ClampPos(a, 2, subj.Value.Length, subj.Value.Length, c);
            return Findall(c, pat, subj, pos, endpos);
        }

        public static ScriptValue PatFinditer(PatternValue pat, ScriptValue[] a, EvalContext c)
        {
            StrValue subj = AsSubject(c, a, 0);
            int pos = ClampPos(a, 1, 0, subj.Value.Length, c);
            int endpos = ClampPos(a, 2, subj.Value.Length, subj.Value.Length, c);
            return c.Values.Iterator(new ReFindIterator(pat, subj, pos, endpos), "callable_iterator");
        }

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
            Match m = RunMatch(c, pat.Net, subj.Value, pos, endpos - pos);
            return m.Success ? (ScriptValue)MakeMatch(c, m, pat, subj, pos, endpos) : c.Values.None;
        }

        private static ScriptValue MatchAt(EvalContext c, PatternValue pat, StrValue subj, int pos, int endpos)
        {
            Match m = RunMatch(c, pat.Net, subj.Value, pos, endpos - pos);
            return m.Success && m.Index == pos ? (ScriptValue)MakeMatch(c, m, pat, subj, pos, endpos) : c.Values.None;
        }

        private static ScriptValue FullMatch(EvalContext c, PatternValue pat, StrValue subj, int pos, int endpos)
        {
            Match m = RunMatch(c, pat.FullRegex, subj.Value, pos, endpos - pos);
            return m.Success && m.Index == pos && m.Index + m.Length == endpos
                ? (ScriptValue)MakeMatch(c, m, pat, subj, pos, endpos)
                : c.Values.None;
        }

        // The single fenced .NET Match entry: deadline before/after, per-op MatchTimeout, ChargeRegexTime.
        // Stopwatch timestamp ticks -> TimeSpan (100 ns) ticks, so we can measure a match without allocating
        // a Stopwatch object on every call (this runs once per match, and N times per sub/findall).
        private static readonly double SwToTimeSpanTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;

        internal static Match RunMatch(EvalContext ctx, Regex re, string input, int beginning, int length)
        {
            ctx.Budget.CheckDeadlineNow();
            long t0 = Stopwatch.GetTimestamp();
            Match m;
            try
            {
                // CPython semantics: endpos truncates the subject, but pos is NOT a string boundary — \b,
                // lookbehind and ^ still see the text before it. .NET's (beginning, length) overload treats
                // the window edges as the string edges, so scan with a start offset on a truncated string.
                int end = beginning + length;
                string subject = end == input.Length ? input : input.Substring(0, end);
                m = re.Match(subject, beginning);
            }
            catch (RegexMatchTimeoutException)
            {
                throw ctx.Budget.CreateAbort(EngineAbortKind.RegexTimeout, "RegexPerOpTimeout",
                    RegexCache.PerOpTimeoutMs, RegexCache.PerOpTimeoutMs);
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

        internal static MatchValue MakeMatch(EvalContext c, Match m, PatternValue pat, StrValue subj, int pos, int endpos)
        {
            c.Values.PreCharge(64);
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
                Group g = m.Net.Groups[GroupMap.Net(i)];
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
                Group g = m.Net.Groups[GroupMap.Net(kv.Value)];
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
            Group g = m.Net.Groups[GroupMap.Net(index)];
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
            Group g = m.Net.Groups[GroupMap.Net(index)];
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

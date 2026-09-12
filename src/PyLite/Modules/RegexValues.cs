using System.Collections.Generic;
using System.Text.RegularExpressions;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // A compiled pattern. Opaque; immutable; holds the .NET Regex + GroupMap. repr is
    // deterministic (no object address — a documented difference from the 3.4 repr).
    internal sealed class PatternValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = BuildType();

        public readonly Regex Net;
        public readonly GroupMap Groups;
        public readonly int Flags;
        public readonly string Source;

        // One-entry sub-template cache (ReModule.SubCore): patterns live per run (RegexCache in
        // ModuleState), so this is single-threaded state; reuse with the same repl string skips the
        // per-call template parse.
        internal string CachedReplText;
        internal System.Collections.Generic.List<object> CachedReplTemplate;

        // fullmatch needs the WHOLE span to match, which a plain match plus a length check cannot express
        // ('a|ab' against 'ab' picks 'a'); the anchored twin forces the alternation to backtrack.
        private Regex _full;
        internal Regex FullRegex
        {
            get
            {
                if (_full == null)
                    _full = new Regex(@"\G(?:" + Net.ToString() + @")\z", Net.Options, Net.MatchTimeout);
                return _full;
            }
        }

        // Keyword spelling for the Pattern methods; a numeric slot skipped on the way to a later keyword
        // takes its default (pos/count/maxsplit -> 0, a trailing endpos -> absent).
        private static ScriptValue[] Kw(EvalContext c, ScriptValue[] a, Outbridge.PyLite.Runtime.Evaluator.KwArgs kw, string fname, params string[] names)
        {
            ScriptValue[] r = StrMethods.WithKw(c, a, kw, fname, names);
            if (kw.Count == 0)
                return r;
            for (int i = a.Length; i < r.Length; i++)
            {
                string nm = names[i];
                if (r[i].Kind == ValueKind.None && (nm == "pos" || nm == "count" || nm == "maxsplit"))
                    r[i] = c.Values.Int(0);
            }
            if (r.Length > 0 && r[r.Length - 1].Kind == ValueKind.None && names[r.Length - 1] == "endpos")
                System.Array.Resize(ref r, r.Length - 1);
            return r;
        }

        public PatternValue(Regex net, GroupMap groups, int flags, string source)
        {
            Net = net;
            Groups = groups;
            Flags = flags;
            Source = source;
        }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("re.compile(");
            sb.Append(ctx.Values.Str(Source).Repr(ctx));
            sb.Append(")");
        }

        private static ScriptTypeInfo BuildType()
        {
            var d = new Dictionary<string, SlotDescriptor>(System.StringComparer.Ordinal)
            {
                ["search"] = SlotDescriptor.MakeMethod("search", (self, a, kw, c) => ReModule.PatSearch((PatternValue)self, Kw(c, a, kw, "search", "string", "pos", "endpos"), c)),
                ["match"] = SlotDescriptor.MakeMethod("match", (self, a, kw, c) => ReModule.PatMatch((PatternValue)self, Kw(c, a, kw, "match", "string", "pos", "endpos"), c)),
                ["fullmatch"] = SlotDescriptor.MakeMethod("fullmatch", (self, a, kw, c) => ReModule.PatFullMatch((PatternValue)self, Kw(c, a, kw, "fullmatch", "string", "pos", "endpos"), c)),
                ["findall"] = SlotDescriptor.MakeMethod("findall", (self, a, kw, c) => ReModule.PatFindall((PatternValue)self, Kw(c, a, kw, "findall", "string", "pos", "endpos"), c)),
                ["finditer"] = SlotDescriptor.MakeMethod("finditer", (self, a, kw, c) => ReModule.PatFinditer((PatternValue)self, Kw(c, a, kw, "finditer", "string", "pos", "endpos"), c)),
                ["sub"] = SlotDescriptor.MakeMethod("sub", (self, a, kw, c) => ReModule.PatSub((PatternValue)self, Kw(c, a, kw, "sub", "repl", "string", "count"), c)),
                ["subn"] = SlotDescriptor.MakeMethod("subn", (self, a, kw, c) => ReModule.PatSubn((PatternValue)self, Kw(c, a, kw, "subn", "repl", "string", "count"), c)),
                ["split"] = SlotDescriptor.MakeMethod("split", (self, a, kw, c) => ReModule.PatSplit((PatternValue)self, Kw(c, a, kw, "split", "string", "maxsplit"), c)),
                ["pattern"] = SlotDescriptor.MakeProperty("pattern", (self, c) => c.Values.Str(((PatternValue)self).Source)),
                ["flags"] = SlotDescriptor.MakeProperty("flags", (self, c) => c.Values.Int(((PatternValue)self).Flags)),
                ["groups"] = SlotDescriptor.MakeProperty("groups", (self, c) => c.Values.Int(((PatternValue)self).Groups.Count)),
                ["groupindex"] = SlotDescriptor.MakeProperty("groupindex", (self, c) => ReModule.GroupIndexDict((PatternValue)self, c)),
            };
            return new ScriptTypeInfo("Pattern", d);
        }
    }

    // A match result. Opaque; immutable; holds the .NET Match + GroupMap + subject.
    internal sealed class MatchValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = BuildType();

        // m[g] is m.group(g) (3.6+).
        protected internal override ScriptValue GetItemCore(ScriptValue index, EvalContext ctx)
        {
            return ReModule.MatchGroup(this, new[] { index }, ctx);
        }

        public readonly Match Net;
        public readonly GroupMap Groups;
        public readonly PatternValue Pattern;
        public readonly StrValue Subject;
        public readonly int Pos;
        public readonly int EndPos;
        internal int LastIndexCache = NotComputed;   // lastindex is a scan; a match answers it once
        internal const int NotComputed = -2;

        public MatchValue(Match net, GroupMap groups, PatternValue pattern, StrValue subject, int pos, int endPos)
        {
            Net = net;
            Groups = groups;
            Pattern = pattern;
            Subject = subject;
            Pos = pos;
            EndPos = endPos;
        }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            sb.Append("<re.Match object; span=(");
            sb.Append(Net.Index.ToString(ci));
            sb.Append(", ");
            sb.Append((Net.Index + Net.Length).ToString(ci));
            sb.Append("), match=");
            sb.Append(ctx.Values.Str(Net.Value).Repr(ctx));
            sb.Append(">");
        }

        private static ScriptTypeInfo BuildType()
        {
            var d = new Dictionary<string, SlotDescriptor>(System.StringComparer.Ordinal)
            {
                ["group"] = SlotDescriptor.MakeMethod("group", (self, a, kw, c) => ReModule.MatchGroup((MatchValue)self, a, c)),
                ["groups"] = SlotDescriptor.MakeMethod("groups", (self, a, kw, c) => ReModule.MatchGroups((MatchValue)self, a, c)),
                ["groupdict"] = SlotDescriptor.MakeMethod("groupdict", (self, a, kw, c) => ReModule.MatchGroupDict((MatchValue)self, a, c)),
                ["start"] = SlotDescriptor.MakeMethod("start", (self, a, kw, c) => ReModule.MatchStart((MatchValue)self, a, c)),
                ["end"] = SlotDescriptor.MakeMethod("end", (self, a, kw, c) => ReModule.MatchEnd((MatchValue)self, a, c)),
                ["span"] = SlotDescriptor.MakeMethod("span", (self, a, kw, c) => ReModule.MatchSpan((MatchValue)self, a, c)),
                ["re"] = SlotDescriptor.MakeProperty("re", (self, c) => ((MatchValue)self).Pattern),
                ["string"] = SlotDescriptor.MakeProperty("string", (self, c) => ((MatchValue)self).Subject),
                ["pos"] = SlotDescriptor.MakeProperty("pos", (self, c) => c.Values.Int(((MatchValue)self).Pos)),
                ["endpos"] = SlotDescriptor.MakeProperty("endpos", (self, c) => c.Values.Int(((MatchValue)self).EndPos)),
                ["expand"] = SlotDescriptor.MakeMethod("expand", (self, a, kw, c) => ReModule.MatchExpand((MatchValue)self, a, c)),
                ["lastindex"] = SlotDescriptor.MakeProperty("lastindex", (self, c) =>
                {
                    int i = ReModule.LastGroupIndex((MatchValue)self);
                    return i < 0 ? c.Values.None : (ScriptValue)c.Values.Int(i);
                }),
                ["lastgroup"] = SlotDescriptor.MakeProperty("lastgroup", (self, c) =>
                {
                    var mv = (MatchValue)self;
                    int i = ReModule.LastGroupIndex(mv);
                    if (i >= 0)
                    {
                        foreach (KeyValuePair<string, int> kv in mv.Groups.NameIndex)
                        {
                            if (kv.Value == i)
                                return c.Values.Str(kv.Key);
                        }
                    }
                    return c.Values.None;
                }),
            };
            return new ScriptTypeInfo("Match", d);
        }
    }

    // Lazy match iterator for re.finditer / Pattern.finditer. Every MoveNext runs one
    // fenced .NET match and advances with the modern (3.7+) empty-match rule; each MoveNext charges a Budget
    // step (the base) + the regex time (RunMatch).
    internal sealed class ReFindIterator : ScriptIteratorBase
    {
        private readonly PatternValue _pat;
        private readonly StrValue _subj;
        private readonly int _startPos;
        private readonly int _end;
        private int _pos;

        public ReFindIterator(PatternValue pat, StrValue subj, int pos, int endPos)
        {
            _pat = pat;
            _subj = subj;
            _startPos = pos;
            _end = endPos;
            _pos = pos;
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_pos <= _end)
            {
                Match m = ReModule.RunMatch(ctx, _pat.Net, _subj.Value, _pos, _end - _pos);
                if (m.Success)
                {
                    int mEnd = m.Index + m.Length;
                    _pos = m.Length == 0 ? mEnd + 1 : mEnd;   // modern advance
                    value = ReModule.MakeMatch(ctx, m, _pat, _subj, _startPos, _end);
                    return true;
                }
            }
            _pos = _end + 1;   // exhausted (never revives)
            value = null;
            return false;
        }
    }
}


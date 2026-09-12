using System.Collections.Generic;

namespace Outbridge.PyLite.Syntax.Ast
{
    // PEP 634 structural pattern matching. Class patterns are parsed nowhere yet — they
    // need record classes; the parser rejects them explicitly.
    public abstract class PatternNode : Node
    {
        protected PatternNode(SourceInfo src) : base(src) { }
    }

    // Bare NAME binds the subject; '_' is the wildcard (Target == null, binds nothing).
    public sealed class CapturePattern : PatternNode
    {
        public readonly NameNode Target;   // null => wildcard '_'
        public CapturePattern(SourceInfo src, NameNode target) : base(src) { Target = target; }
    }

    // Number / string / None / True / False literal. Value is a LiteralNode (possibly a UnaryOp minus).
    public sealed class LiteralPattern : PatternNode
    {
        public readonly ExprNode Value;
        public LiteralPattern(SourceInfo src, ExprNode value) : base(src) { Value = value; }
    }

    // Dotted name foo.bar — matched by equality against the looked-up value.
    public sealed class ValuePattern : PatternNode
    {
        public readonly ExprNode Value;   // AttributeNode
        public ValuePattern(SourceInfo src, ExprNode value) : base(src) { Value = value; }
    }

    // '*name' / '*_' element inside a sequence pattern (only valid there).
    public sealed class StarPattern : PatternNode
    {
        public readonly NameNode Target;   // null => '*_'
        public StarPattern(SourceInfo src, NameNode target) : base(src) { Target = target; }
    }

    // [p, ...] / (p, ...) — matches a list/tuple/range of the right shape (never str/dict/set).
    public sealed class SequencePattern : PatternNode
    {
        public readonly IReadOnlyList<PatternNode> Elements;
        public readonly int StarIndex;   // -1 => no star element
        public SequencePattern(SourceInfo src, IReadOnlyList<PatternNode> elements, int starIndex) : base(src)
        {
            Elements = elements; StarIndex = starIndex;
        }
    }

    // {key: pattern, ..., **rest} — matches a mapping containing the given keys.
    public sealed class MappingPattern : PatternNode
    {
        public readonly IReadOnlyList<ExprNode> Keys;         // literal/value key expressions
        public readonly IReadOnlyList<PatternNode> Values;    // Count == Keys.Count
        public readonly NameNode Rest;                        // '**rest' capture, null if none
        public MappingPattern(SourceInfo src, IReadOnlyList<ExprNode> keys, IReadOnlyList<PatternNode> values, NameNode rest) : base(src)
        {
            Keys = keys; Values = values; Rest = rest;
        }
    }

    public sealed class OrPattern : PatternNode
    {
        public readonly IReadOnlyList<PatternNode> Alternatives;   // Count >= 2
        public OrPattern(SourceInfo src, IReadOnlyList<PatternNode> alternatives) : base(src) { Alternatives = alternatives; }
    }

    // ClassName(pos..., kw=pat...) — isinstance check, then positional (via __match_args__) and keyword
    // sub-patterns read as attributes. Class patterns work against record classes/dataclasses.
    public sealed class ClassPattern : PatternNode
    {
        public readonly ExprNode Cls;                          // NameNode / dotted AttributeNode
        public readonly IReadOnlyList<PatternNode> Positional;
        public readonly IReadOnlyList<string> KwNames;
        public readonly IReadOnlyList<PatternNode> KwPatterns; // parallel to KwNames
        public ClassPattern(SourceInfo src, ExprNode cls, IReadOnlyList<PatternNode> positional,
            IReadOnlyList<string> kwNames, IReadOnlyList<PatternNode> kwPatterns) : base(src)
        {
            Cls = cls; Positional = positional; KwNames = kwNames; KwPatterns = kwPatterns;
        }
    }

    // pattern 'as' NAME — matches Inner, then binds the subject to Target.
    public sealed class AsPattern : PatternNode
    {
        public readonly PatternNode Inner;
        public readonly NameNode Target;
        public AsPattern(SourceInfo src, PatternNode inner, NameNode target) : base(src) { Inner = inner; Target = target; }
    }

    public sealed class MatchCase : Node
    {
        public readonly PatternNode Pattern;
        public readonly ExprNode Guard;   // null => no guard
        public readonly IReadOnlyList<StmtNode> Body;
        public MatchCase(SourceInfo src, PatternNode pattern, ExprNode guard, IReadOnlyList<StmtNode> body) : base(src)
        {
            Pattern = pattern; Guard = guard; Body = body;
        }
    }

    public sealed class MatchNode : StmtNode
    {
        public readonly ExprNode Subject;
        public readonly IReadOnlyList<MatchCase> Cases;   // Count >= 1
        public MatchNode(SourceInfo src, ExprNode subject, IReadOnlyList<MatchCase> cases) : base(src, NodeKind.Match)
        {
            Subject = subject; Cases = cases;
        }
    }
}

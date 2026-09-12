using System.Collections.Generic;

namespace Outbridge.PyLite.Syntax.Ast
{
    // A single parameter; Default is an already-parsed expression (evaluated at def-time by the Evaluator).
    public sealed class Param : Node
    {
        public readonly string Name;
        public readonly ExprNode Default;   // null => no default
        public readonly string AnnotationName;   // the annotation when it is a plain name, else null
        public Param(SourceInfo src, string name, ExprNode def, string annotationName = null) : base(src)
        {
            Name = name; Default = def; AnnotationName = annotationName;
        }
    }

    public sealed class ParamList : Node
    {
        public readonly IReadOnlyList<Param> Positional;
        public readonly Param StarArg;      // null => no *args
        public readonly IReadOnlyList<Param> KwOnly;
        public readonly Param KwArg;        // null => no **kwargs
        public readonly bool HasBareStar;   // bare '*' separator (no *args name)
        public readonly int PositionalOnlyCount;   // PEP 570: leading params before '/' (0 => none)

        public ParamList(SourceInfo src, IReadOnlyList<Param> positional, Param starArg,
            IReadOnlyList<Param> kwOnly, Param kwArg, bool hasBareStar, int positionalOnlyCount) : base(src)
        {
            Positional = positional;
            StarArg = starArg;
            KwOnly = kwOnly;
            KwArg = kwArg;
            HasBareStar = hasBareStar;
            PositionalOnlyCount = positionalOnlyCount;
        }
    }

    public sealed class Arg : Node
    {
        public readonly ArgKind Kind;
        public readonly string Keyword;     // Keyword args only, else null
        public readonly ExprNode Value;
        public Arg(SourceInfo src, ArgKind kind, string keyword, ExprNode value) : base(src)
        {
            Kind = kind;
            Keyword = keyword;
            Value = value;
        }
    }

    // One comprehension clause: a `for` (Target in Iter) or an `if` (Cond).
    public sealed class CompClause : Node
    {
        public readonly bool IsFor;
        public readonly ExprNode Target;    // for-clause
        public readonly ExprNode Iter;      // for-clause
        public readonly ExprNode Cond;      // if-clause

        // Closure-compiled Cond or its not-compilable sentinel (the benign-race cache pattern of
        // ComprehensionNode.CompiledElement). Per clause, so multi-if comprehensions never collide.
        // FusedCond is the superinstruction twin (pure-long condition, EvalExpr.Fused).
        public object CompiledCond;         // if-clause
        public object FusedCond;            // if-clause
        public CompClause(SourceInfo src, bool isFor, ExprNode target, ExprNode iter, ExprNode cond) : base(src)
        {
            IsFor = isFor;
            Target = target;
            Iter = iter;
            Cond = cond;
        }
    }

    public sealed class FStringPart : Node
    {
        public readonly bool IsLiteral;
        public readonly string Literal;                       // literal parts
        public readonly ExprNode Expr;                        // expression parts
        public readonly char Conversion;                      // '\0' | 's' | 'r' | 'a'
        public readonly IReadOnlyList<FStringPart> FormatSpec; // null => none
        public FStringPart(SourceInfo src, bool isLiteral, string literal, ExprNode expr,
            char conversion, IReadOnlyList<FStringPart> formatSpec) : base(src)
        {
            IsLiteral = isLiteral;
            Literal = literal;
            Expr = expr;
            Conversion = conversion;
            FormatSpec = formatSpec;
        }
    }

    public sealed class ExceptHandler : Node
    {
        public readonly ExprNode Type;      // null => bare except
        public readonly string Name;        // null => no 'as name'
        public readonly IReadOnlyList<StmtNode> Body;
        public ExceptHandler(SourceInfo src, ExprNode type, string name, IReadOnlyList<StmtNode> body) : base(src)
        {
            Type = type;
            Name = name;
            Body = body;
        }
    }

    public sealed class ImportAlias : Node
    {
        public readonly string DottedName;
        public readonly string AsName;      // null => none
        public ImportAlias(SourceInfo src, string dottedName, string asName) : base(src)
        {
            DottedName = dottedName;
            AsName = asName;
        }
    }
}

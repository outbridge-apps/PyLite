using System;
using System.Collections.Generic;

namespace Outbridge.PyLite.Syntax.Ast
{
    public sealed class LiteralNode : ExprNode
    {
        public readonly object Value;       // BigInteger | double | string | bool | NoneMarker.Instance
        public readonly LiteralKind Kind;
        internal object CachedSmall;        // shared zero-charge IntValue singleton (-5..256), set lazily
        public LiteralNode(SourceInfo src, object value, LiteralKind kind) : base(src, NodeKind.Literal)
        {
            Value = value;
            Kind = kind;
        }
    }

    public sealed class NameNode : ExprNode
    {
        public readonly string Id;
        internal NameBinding Binding;   // baked by the Resolver — dictionary-free hot-path resolution
        public NameNode(SourceInfo src, string id) : base(src, NodeKind.Name) { Id = id; }
    }

    public sealed class BinOpNode : ExprNode
    {
        public readonly BinOp Op;
        public readonly ExprNode Left;
        public readonly ExprNode Right;
        public BinOpNode(SourceInfo src, BinOp op, ExprNode left, ExprNode right)
            : base(src, NodeKind.BinOp, ChildDepth.Max(ChildDepth.Of(left), ChildDepth.Of(right)))
        {
            Op = op; Left = left; Right = right;
        }
    }

    public sealed class UnaryOpNode : ExprNode
    {
        public readonly UnaryOp Op;
        public readonly ExprNode Operand;
        public UnaryOpNode(SourceInfo src, UnaryOp op, ExprNode operand) : base(src, NodeKind.UnaryOp, ChildDepth.Of(operand)) { Op = op; Operand = operand; }
    }

    public sealed class BoolOpNode : ExprNode
    {
        public readonly BoolOp Op;
        public readonly IReadOnlyList<ExprNode> Values;   // >= 2
        public BoolOpNode(SourceInfo src, BoolOp op, IReadOnlyList<ExprNode> values) : base(src, NodeKind.BoolOp, ChildDepth.OfList(values))
        {
            Op = op; Values = values;
        }
    }

    public sealed class CompareChainNode : ExprNode
    {
        public readonly ExprNode Left;
        public readonly IReadOnlyList<CompareOp> Ops;         // Count >= 1
        public readonly IReadOnlyList<ExprNode> Comparators;  // Count == Ops.Count
        public CompareChainNode(SourceInfo src, ExprNode left, IReadOnlyList<CompareOp> ops, IReadOnlyList<ExprNode> comps)
            : base(src, NodeKind.CompareChain, ChildDepth.Max(ChildDepth.Of(left), ChildDepth.OfList(comps)))
        {
            Left = left; Ops = ops; Comparators = comps;
        }
    }

    public sealed class IfExpNode : ExprNode
    {
        public readonly ExprNode Body;
        public readonly ExprNode Test;
        public readonly ExprNode OrElse;
        public IfExpNode(SourceInfo src, ExprNode body, ExprNode test, ExprNode orElse)
            : base(src, NodeKind.IfExp, ChildDepth.Max(ChildDepth.Of(body), ChildDepth.Of(test), ChildDepth.Of(orElse)))
        {
            Body = body; Test = test; OrElse = orElse;
        }
    }

    public sealed class LambdaNode : ExprNode
    {
        public readonly ParamList Params;
        public readonly ExprNode Body;
        public LambdaNode(SourceInfo src, ParamList prms, ExprNode body)
            : base(src, NodeKind.Lambda, ChildDepth.Max(ChildDepth.Of(body), ChildDepth.OfParamDefaults(prms))) { Params = prms; Body = body; }
    }

    public sealed class CallNode : ExprNode
    {
        public readonly ExprNode Func;
        public readonly IReadOnlyList<Arg> Args;
        internal object CachedCallSlot;   // monomorphic (TypeInfo -> descriptor) inline cache, set lazily
        public CallNode(SourceInfo src, ExprNode func, IReadOnlyList<Arg> args)
            : base(src, NodeKind.Call, ChildDepth.Max(ChildDepth.Of(func), ChildDepth.OfArgs(args))) { Func = func; Args = args; }
    }

    public sealed class AttributeNode : ExprNode
    {
        public readonly ExprNode Value;
        public readonly string Attr;
        public AttributeNode(SourceInfo src, ExprNode value, string attr) : base(src, NodeKind.Attribute, ChildDepth.Of(value)) { Value = value; Attr = attr; }
    }

    public sealed class IndexNode : ExprNode
    {
        public readonly ExprNode Value;
        public readonly ExprNode Index;   // SliceNode | TupleNode | scalar
        public IndexNode(SourceInfo src, ExprNode value, ExprNode index)
            : base(src, NodeKind.Index, ChildDepth.Max(ChildDepth.Of(value), ChildDepth.Of(index))) { Value = value; Index = index; }
    }

    public sealed class SliceNode : ExprNode
    {
        public readonly ExprNode Lower;   // may be null
        public readonly ExprNode Upper;   // may be null
        public readonly ExprNode Step;    // may be null
        public SliceNode(SourceInfo src, ExprNode lower, ExprNode upper, ExprNode step)
            : base(src, NodeKind.Slice, ChildDepth.Max(ChildDepth.Of(lower), ChildDepth.Of(upper), ChildDepth.Of(step)))
        {
            Lower = lower; Upper = upper; Step = step;
        }
    }

    public sealed class ListNode : ExprNode
    {
        public readonly IReadOnlyList<ExprNode> Elts;
        public ListNode(SourceInfo src, IReadOnlyList<ExprNode> elts) : base(src, NodeKind.List, ChildDepth.OfList(elts)) { Elts = elts; }
    }

    public sealed class TupleNode : ExprNode
    {
        public readonly IReadOnlyList<ExprNode> Elts;   // empty tuple => Count == 0
        public TupleNode(SourceInfo src, IReadOnlyList<ExprNode> elts) : base(src, NodeKind.Tuple, ChildDepth.OfList(elts)) { Elts = elts; }
    }

    public sealed class SetNode : ExprNode
    {
        public readonly IReadOnlyList<ExprNode> Elts;   // Count >= 1
        public SetNode(SourceInfo src, IReadOnlyList<ExprNode> elts) : base(src, NodeKind.Set, ChildDepth.OfList(elts)) { Elts = elts; }
    }

    public sealed class DictNode : ExprNode
    {
        public readonly IReadOnlyList<ExprNode> Keys;
        public readonly IReadOnlyList<ExprNode> Values; // Count == Keys.Count
        public DictNode(SourceInfo src, IReadOnlyList<ExprNode> keys, IReadOnlyList<ExprNode> values)
            : base(src, NodeKind.Dict, ChildDepth.Max(ChildDepth.OfList(keys), ChildDepth.OfList(values)))
        {
            Keys = keys; Values = values;
        }
    }

    public sealed class StarredNode : ExprNode
    {
        public readonly ExprNode Value;
        public StarredNode(SourceInfo src, ExprNode value) : base(src, NodeKind.Starred, ChildDepth.Of(value)) { Value = value; }
    }

    // PEP 572 walrus: (target := Value). Target is always a bare name; binds in the enclosing scope.
    public sealed class NamedExprNode : ExprNode
    {
        public readonly NameNode Target;
        public readonly ExprNode Value;
        public NamedExprNode(SourceInfo src, NameNode target, ExprNode value)
            : base(src, NodeKind.NamedExpr, ChildDepth.Max(ChildDepth.Of(target), ChildDepth.Of(value))) { Target = target; Value = value; }
    }

    public sealed class ComprehensionNode : ExprNode
    {
        public readonly CompKind Kind;
        public readonly ExprNode Element;
        public readonly ExprNode ValueElement;   // null for non-dict
        public readonly IReadOnlyList<CompClause> Clauses;   // Clauses[0] is always a for

        // Runtime caches (the benign-race pattern of CallNode.CachedCallSlot / LiteralNode.CachedSmall):
        // the evaluator's closure-compiled element/value delegate, or its not-compilable sentinel;
        // the Fused* twins hold the superinstruction form (pure-long delegate, EvalExpr.Fused).
        // Shareable across runs: compiled closures capture only immutable AST/binding data.
        // (Conditions cache per clause — CompClause.CompiledCond/FusedCond.)
        public object CompiledElement;
        public object CompiledValueElement;
        public object FusedElement;
        public object FusedValueElement;

        public ComprehensionNode(SourceInfo src, CompKind kind, ExprNode element, ExprNode valueElement, IReadOnlyList<CompClause> clauses)
            : base(src, NodeKind.Comprehension,
                ChildDepth.Max(ChildDepth.Of(element), ChildDepth.Of(valueElement), ChildDepth.OfClauses(clauses)))
        {
            if ((kind == CompKind.Dict) != (valueElement != null))
                throw new ArgumentException("ValueElement must be non-null iff Kind == Dict");
            Kind = kind; Element = element; ValueElement = valueElement; Clauses = clauses;
        }
    }

    public sealed class FStringNode : ExprNode
    {
        public readonly IReadOnlyList<FStringPart> Parts;
        public FStringNode(SourceInfo src, IReadOnlyList<FStringPart> parts) : base(src, NodeKind.FString, ChildDepth.OfFStringParts(parts)) { Parts = parts; }
    }

    // Created ONLY by the Resolver (const folding); keeps the original for diagnostics.
    public sealed class ConstFoldedNode : ExprNode
    {
        public readonly ExprNode Original;
        public readonly LiteralNode Folded;
        public ConstFoldedNode(ExprNode original, LiteralNode folded) : base(original.Src, NodeKind.ConstFolded)
        {
            Original = original; Folded = folded;
        }
    }
}

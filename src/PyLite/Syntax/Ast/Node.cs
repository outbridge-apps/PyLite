namespace Outbridge.PyLite.Syntax.Ast
{
    // Shared singleton marker for a None literal value (so LiteralNode.Value is never null).
    public sealed class NoneMarker
    {
        public static readonly NoneMarker Instance = new NoneMarker();
        private NoneMarker() { }
    }

    // Shared singleton marker for an Ellipsis (`...`) literal value.
    public sealed class EllipsisMarker
    {
        public static readonly EllipsisMarker Instance = new EllipsisMarker();
        private EllipsisMarker() { }
    }

    // P2: dispatch tag — EvalExpr/ExecStmt switch on this int (a jump table) instead of a sequential
    // type-pattern chain. Every dispatched Expr/Stmt node passes its kind up; support nodes stay Other.
    public enum NodeKind
    {
        Other = 0,
        // expressions
        Literal, ConstFolded, Name, BinOp, UnaryOp, BoolOp, CompareChain, IfExp, Index, Attribute,
        List, Tuple, Set, Dict, Slice, Call, NamedExpr, Lambda, Comprehension, FString, Starred,
        // statements
        ExprStmt, Assign, AugAssign, Del, Pass, If, While, For, FuncDef, Return, Break, Continue,
        Assert, Try, Raise, Import, FromImport, Global, Nonlocal, Match, ClassDef,
        Module,
    }

    public abstract class Node
    {
        public readonly SourceInfo Src;
        public readonly NodeKind NodeKind;
        internal FunctionInfo ScopeInfo;   // baked by the Resolver for scope-owning nodes (else null)
        protected Node(SourceInfo src) { Src = src; }
        protected Node(SourceInfo src, NodeKind kind) { Src = src; NodeKind = kind; }
    }

    public abstract class ExprNode : Node
    {
        // Depth of this subtree (leaf = 1). Bounded at parse time (SyntaxLimits.MaxAstDepth) so a pathological
        // flat chain (1+1+…, a[0][0]…, a.b.b…) — whose AST depth equals its length — cannot drive the
        // downstream recursive tree-walks (resolver, const-folder, evaluator) into an uncatchable StackOverflow.
        public readonly int Depth;
        protected ExprNode(SourceInfo s) : base(s) { Depth = 1; }
        protected ExprNode(SourceInfo s, NodeKind k) : base(s, k) { Depth = 1; }
        protected ExprNode(SourceInfo s, NodeKind k, int childMaxDepth) : base(s, k) { Depth = childMaxDepth + 1; }
    }

    public abstract class StmtNode : Node
    {
        protected StmtNode(SourceInfo s) : base(s) { }
        protected StmtNode(SourceInfo s, NodeKind k) : base(s, k) { }
    }
}

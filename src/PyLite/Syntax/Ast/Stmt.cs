using System.Collections.Generic;

namespace Outbridge.PyLite.Syntax.Ast
{
    public sealed class ExprStmtNode : StmtNode
    {
        public readonly ExprNode Value;
        public ExprStmtNode(SourceInfo src, ExprNode value) : base(src, NodeKind.ExprStmt) { Value = value; }
    }

    public sealed class AssignNode : StmtNode
    {
        public readonly IReadOnlyList<ExprNode> Targets;
        public readonly ExprNode Value;
        public AssignNode(SourceInfo src, IReadOnlyList<ExprNode> targets, ExprNode value) : base(src, NodeKind.Assign)
        {
            Targets = targets; Value = value;
        }
    }

    public sealed class AugAssignNode : StmtNode
    {
        public readonly ExprNode Target;   // Name | Attribute | Index
        public readonly BinOp Op;
        public readonly ExprNode Value;
        public AugAssignNode(SourceInfo src, ExprNode target, BinOp op, ExprNode value) : base(src, NodeKind.AugAssign)
        {
            Target = target; Op = op; Value = value;
        }
    }

    public sealed class DelNode : StmtNode
    {
        public readonly IReadOnlyList<ExprNode> Targets;
        public DelNode(SourceInfo src, IReadOnlyList<ExprNode> targets) : base(src, NodeKind.Del) { Targets = targets; }
    }

    public sealed class PassNode : StmtNode { public PassNode(SourceInfo src) : base(src, NodeKind.Pass) { } }
    public sealed class BreakNode : StmtNode { public BreakNode(SourceInfo src) : base(src, NodeKind.Break) { } }
    public sealed class ContinueNode : StmtNode { public ContinueNode(SourceInfo src) : base(src, NodeKind.Continue) { } }

    public sealed class IfNode : StmtNode
    {
        public readonly ExprNode Test;
        public readonly IReadOnlyList<StmtNode> Body;
        public readonly IReadOnlyList<StmtNode> OrElse;   // elif unfolded here
        public IfNode(SourceInfo src, ExprNode test, IReadOnlyList<StmtNode> body, IReadOnlyList<StmtNode> orElse) : base(src, NodeKind.If)
        {
            Test = test; Body = body; OrElse = orElse;
        }
    }

    public sealed class WhileNode : StmtNode
    {
        public readonly ExprNode Test;
        public readonly IReadOnlyList<StmtNode> Body;
        public readonly IReadOnlyList<StmtNode> OrElse;
        public WhileNode(SourceInfo src, ExprNode test, IReadOnlyList<StmtNode> body, IReadOnlyList<StmtNode> orElse) : base(src, NodeKind.While)
        {
            Test = test; Body = body; OrElse = orElse;
        }
    }

    public sealed class ForNode : StmtNode
    {
        public readonly ExprNode Target;
        public readonly ExprNode Iter;
        public readonly IReadOnlyList<StmtNode> Body;
        public readonly IReadOnlyList<StmtNode> OrElse;
        public ForNode(SourceInfo src, ExprNode target, ExprNode iter, IReadOnlyList<StmtNode> body, IReadOnlyList<StmtNode> orElse) : base(src, NodeKind.For)
        {
            Target = target; Iter = iter; Body = body; OrElse = orElse;
        }
    }

    public sealed class FuncDefNode : StmtNode
    {
        public readonly string Name;
        public readonly ParamList Params;
        public readonly IReadOnlyList<StmtNode> Body;
        public readonly IReadOnlyList<ExprNode> Decorators;   // source order (outermost first); applied bottom-up
        public readonly string ReturnAnnotationName;          // '-> Name'; null when absent or not a plain name
        public FuncDefNode(SourceInfo src, string name, ParamList prms, IReadOnlyList<StmtNode> body,
            IReadOnlyList<ExprNode> decorators, string returnAnnotationName = null) : base(src, NodeKind.FuncDef)
        {
            Name = name; Params = prms; Body = body; Decorators = decorators; ReturnAnnotationName = returnAnnotationName;
        }
    }

    public sealed class ReturnNode : StmtNode
    {
        public readonly ExprNode Value;   // may be null
        public ReturnNode(SourceInfo src, ExprNode value) : base(src, NodeKind.Return) { Value = value; }
    }

    public sealed class ImportNode : StmtNode
    {
        public readonly IReadOnlyList<ImportAlias> Names;
        public ImportNode(SourceInfo src, IReadOnlyList<ImportAlias> names) : base(src, NodeKind.Import) { Names = names; }
    }

    public sealed class FromImportNode : StmtNode
    {
        public readonly string Module;
        public readonly IReadOnlyList<ImportAlias> Names;
        public readonly bool IsStar;   // always false after validation
        public FromImportNode(SourceInfo src, string module, IReadOnlyList<ImportAlias> names, bool isStar) : base(src, NodeKind.FromImport)
        {
            Module = module; Names = names; IsStar = isStar;
        }
    }

    public sealed class RaiseNode : StmtNode
    {
        public readonly ExprNode Exc;     // null => bare raise
        public readonly ExprNode Cause;   // Cause != null => Exc != null
        public RaiseNode(SourceInfo src, ExprNode exc, ExprNode cause) : base(src, NodeKind.Raise) { Exc = exc; Cause = cause; }
    }

    public sealed class TryNode : StmtNode
    {
        public readonly IReadOnlyList<StmtNode> Body;
        public readonly IReadOnlyList<ExceptHandler> Handlers;
        public readonly IReadOnlyList<StmtNode> OrElse;
        public readonly IReadOnlyList<StmtNode> Finally;
        public TryNode(SourceInfo src, IReadOnlyList<StmtNode> body, IReadOnlyList<ExceptHandler> handlers,
            IReadOnlyList<StmtNode> orElse, IReadOnlyList<StmtNode> finallyBody) : base(src, NodeKind.Try)
        {
            Body = body; Handlers = handlers; OrElse = orElse; Finally = finallyBody;
        }
    }

    public sealed class AssertNode : StmtNode
    {
        public readonly ExprNode Test;
        public readonly ExprNode Msg;   // may be null
        public AssertNode(SourceInfo src, ExprNode test, ExprNode msg) : base(src, NodeKind.Assert) { Test = test; Msg = msg; }
    }

    public sealed class GlobalNode : StmtNode
    {
        public readonly IReadOnlyList<string> Names;
        public GlobalNode(SourceInfo src, IReadOnlyList<string> names) : base(src, NodeKind.Global) { Names = names; }
    }

    public sealed class NonlocalNode : StmtNode
    {
        public readonly IReadOnlyList<string> Names;
        public NonlocalNode(SourceInfo src, IReadOnlyList<string> names) : base(src, NodeKind.Nonlocal) { Names = names; }
    }

    public sealed class ModuleNode : Node
    {
        public readonly IReadOnlyList<StmtNode> Body;
        public ModuleNode(SourceInfo src, IReadOnlyList<StmtNode> body) : base(src, NodeKind.Module) { Body = body; }
    }
}

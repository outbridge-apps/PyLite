using System.Collections.Generic;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class ResolverPass
    {
        private static PySyntaxErrorException Err(SourceInfo s, string msg) { return PySyntaxErrorException.Syntax(msg, s.Line, s.Col); }

        // --- return/break/continue context validation ----
        // continue inside finally is legal (3.8+); ExecTry already treats any finally signal
        // as overriding the pending one, so no finally tracking is needed here.
        private void ValidateControl(IReadOnlyList<StmtNode> stmts)
        {
            new ControlValidator().Stmts(stmts, default(ControlCtx));
        }

        private struct ControlCtx
        {
            public bool InFunction;
            public int LoopDepth;
        }

        private sealed class ControlValidator : AstWalker<ControlCtx>
        {
            protected override void Expr(ExprNode e, ControlCtx c) { }   // no statement lives in an expression

            protected override void Stmt(StmtNode st, ControlCtx c)
            {
                switch (st)
                {
                    case ReturnNode r:
                        if (!c.InFunction)
                            throw Err(r.Src, "'return' outside function");
                        break;
                    case BreakNode b:
                        if (c.LoopDepth == 0)
                            throw Err(b.Src, "'break' outside loop");
                        break;
                    case ContinueNode cn:
                        if (c.LoopDepth == 0)
                            throw Err(cn.Src, "'continue' not properly in loop");
                        break;
                    case WhileNode w:
                        Probe(w.Src);
                        Stmts(w.Body, InLoop(c));
                        Stmts(w.OrElse, c);
                        break;
                    case ForNode f:
                        Probe(f.Src);
                        Stmts(f.Body, InLoop(c));
                        Stmts(f.OrElse, c);
                        break;
                    default: base.Stmt(st, c); break;
                }
            }

            // A def (or a method) starts a function context and resets the loop one.
            protected override void FuncDef(FuncDefNode fd, ControlCtx c)
            {
                Stmts(fd.Body, new ControlCtx { InFunction = true });
            }

            private static ControlCtx InLoop(ControlCtx c)
            {
                c.LoopDepth++;
                return c;
            }
        }

        // --- nonlocal/global validation ----
        private void ValidateScope(ScopeInfo scope, IReadOnlyList<StmtNode> stmts)
        {
            new DeclValidator().Stmts(stmts, new DeclCtx(scope));
            foreach (var child in scope.Children)
            {
                FuncDefNode fd = child.Node as FuncDefNode;
                if (fd != null)
                    ValidateScope(child, fd.Body);
            }
        }

        private sealed class DeclCtx
        {
            public readonly ScopeInfo Scope;
            public readonly HashSet<string> Params;
            public readonly HashSet<string> Assigned = new HashSet<string>(System.StringComparer.Ordinal);
            public readonly HashSet<string> Used = new HashSet<string>(System.StringComparer.Ordinal);
            public readonly HashSet<string> Globals = new HashSet<string>(System.StringComparer.Ordinal);
            public readonly HashSet<string> Nonlocals = new HashSet<string>(System.StringComparer.Ordinal);

            public DeclCtx(ScopeInfo scope)
            {
                Scope = scope;
                Params = new HashSet<string>(scope.ParamNames, System.StringComparer.Ordinal);
            }
        }

        // A global/nonlocal declaration must precede every use and assignment of the name in its scope.
        // Nested scopes are not descended; what a nested def, lambda or comprehension evaluates in this
        // scope (decorators, defaults, the first iterable) counts here, as in CPython's symtable.
        private sealed class DeclValidator : AstWalker<DeclCtx>
        {
            protected override void Name(NameNode n, DeclCtx c) { c.Used.Add(n.Id); }

            protected override void Bind(string name, DeclCtx c) { c.Assigned.Add(name); }

            protected override void Method(FuncDefNode meth, DeclCtx c) { }   // evaluated in the class scope

            protected override void Stmt(StmtNode st, DeclCtx c)
            {
                switch (st)
                {
                    case GlobalNode g:
                        foreach (var name in g.Names)
                        {
                            if (c.Params.Contains(name))
                                throw Err(g.Src, "name '" + name + "' is parameter and global");
                            if (c.Nonlocals.Contains(name))
                                throw Err(g.Src, "name '" + name + "' is nonlocal and global");
                            if (c.Assigned.Contains(name))
                                throw Err(g.Src, "name '" + name + "' is assigned to before global declaration");
                            if (c.Used.Contains(name))
                                throw Err(g.Src, "name '" + name + "' is used prior to global declaration");
                            c.Globals.Add(name);
                        }
                        break;
                    case NonlocalNode nl:
                        if (c.Scope.Kind == ScopeKind.Module)
                            throw Err(nl.Src, "nonlocal declaration not allowed at module level");
                        foreach (var name in nl.Names)
                        {
                            if (c.Params.Contains(name))
                                throw Err(nl.Src, "name '" + name + "' is parameter and nonlocal");
                            if (c.Globals.Contains(name))
                                throw Err(nl.Src, "name '" + name + "' is nonlocal and global");
                            if (!EnclosingFunctionBinds(c.Scope.Parent, name))
                                throw Err(nl.Src, "no binding for nonlocal '" + name + "' found");
                            if (c.Assigned.Contains(name))
                                throw Err(nl.Src, "name '" + name + "' is assigned to before nonlocal declaration");
                            if (c.Used.Contains(name))
                                throw Err(nl.Src, "name '" + name + "' is used prior to nonlocal declaration");
                            c.Nonlocals.Add(name);
                        }
                        break;
                    default: base.Stmt(st, c); break;
                }
            }
        }
    }
}

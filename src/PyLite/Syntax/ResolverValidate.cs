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
                    case MatchNode m:
                        PatternValidator.Validate(m);
                        base.Stmt(st, c);
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
            public readonly HashSet<string> Annotated = new HashSet<string>(System.StringComparer.Ordinal);   // simple `x: T` targets
            // the first directive of each name: CPython reports a conflict there
            public readonly Dictionary<string, SourceInfo> Globals = new Dictionary<string, SourceInfo>(System.StringComparer.Ordinal);
            public readonly Dictionary<string, SourceInfo> Nonlocals = new Dictionary<string, SourceInfo>(System.StringComparer.Ordinal);
            public Dictionary<Node, ScopeInfo> Comprehensions;   // this scope's, on the first one met

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

            // PEP 572: a walrus in a comprehension, nested ones included, assigns in the enclosing function. At module
            // level it is a global of the comprehension, as in CPython's symtable: no assignment in the module scope.
            protected override void Comprehension(ComprehensionNode comp, DeclCtx c)
            {
                base.Comprehension(comp, c);
                if (c.Scope.Kind == ScopeKind.Module)
                    return;
                if (c.Comprehensions == null)
                {
                    c.Comprehensions = new Dictionary<Node, ScopeInfo>();
                    foreach (ScopeInfo child in c.Scope.Children)
                    {
                        if (child.Kind == ScopeKind.Comprehension)
                            c.Comprehensions[child.Node] = child;
                    }
                }
                ScopeInfo scope;
                if (c.Comprehensions.TryGetValue(comp, out scope))
                    AddWalrusTargets(scope, c.Assigned);
            }

            // PEP 526: a simple annotated target cannot be a global or nonlocal of a function; at module level a global
            // annotation is legal (CPython's symtable).
            private static void Annotate(string name, SourceInfo src, DeclCtx c)
            {
                if (c.Scope.Kind != ScopeKind.Module)
                {
                    if (c.Globals.ContainsKey(name))
                        throw Err(src, "annotated name '" + name + "' can't be global");
                    if (c.Nonlocals.ContainsKey(name))
                        throw Err(src, "annotated name '" + name + "' can't be nonlocal");
                }
                c.Annotated.Add(name);
            }

            private static void AddWalrusTargets(ScopeInfo comp, HashSet<string> assigned)
            {
                if (comp.WalrusTargets != null)
                    assigned.UnionWith(comp.WalrusTargets);
                foreach (ScopeInfo child in comp.Children)
                {
                    if (child.Kind == ScopeKind.Comprehension)
                        AddWalrusTargets(child, assigned);
                }
            }

            protected override void Stmt(StmtNode st, DeclCtx c)
            {
                switch (st)
                {
                    case AssignNode a when a.InClassBody:
                        break;   // a class body's value, hoisted: CPython reads its names in the class scope
                    case AssignNode a when a.AnnotatedName != null:
                        Annotate(a.AnnotatedName, a.Src, c);
                        base.Stmt(st, c);
                        break;
                    case PassNode p when p.AnnotatedName != null:
                        Annotate(p.AnnotatedName, p.Src, c);
                        break;

                    case GlobalNode g:
                        foreach (var name in g.Names)
                        {
                            if (c.Params.Contains(name))
                                throw Err(g.Src, "name '" + name + "' is parameter and global");
                            SourceInfo first;
                            if (c.Nonlocals.TryGetValue(name, out first))
                                throw Err(first, "name '" + name + "' is nonlocal and global");
                            // CPython's order: a use, then an annotation, then an assignment
                            if (c.Used.Contains(name))
                                throw Err(g.Src, "name '" + name + "' is used prior to global declaration");
                            if (c.Annotated.Contains(name))
                                throw Err(g.Src, "annotated name '" + name + "' can't be global");
                            if (c.Assigned.Contains(name))
                                throw Err(g.Src, "name '" + name + "' is assigned to before global declaration");
                            if (!c.Globals.ContainsKey(name))
                                c.Globals[name] = g.Src;
                        }
                        break;
                    case NonlocalNode nl:
                        if (c.Scope.Kind == ScopeKind.Module)
                            throw Err(nl.Src, "nonlocal declaration not allowed at module level");
                        foreach (var name in nl.Names)
                        {
                            if (c.Params.Contains(name))
                                throw Err(nl.Src, "name '" + name + "' is parameter and nonlocal");
                            SourceInfo first;
                            if (c.Globals.TryGetValue(name, out first))
                                throw Err(first, "name '" + name + "' is nonlocal and global");
                            if (c.Used.Contains(name))
                                throw Err(nl.Src, "name '" + name + "' is used prior to nonlocal declaration");
                            if (c.Annotated.Contains(name))
                                throw Err(nl.Src, "annotated name '" + name + "' can't be nonlocal");
                            if (c.Assigned.Contains(name))
                                throw Err(nl.Src, "name '" + name + "' is assigned to before nonlocal declaration");
                            // CPython finds a missing binding after these, in its later analysis pass
                            if (!EnclosingFunctionBinds(c.Scope.Parent, name))
                                throw Err(nl.Src, "no binding for nonlocal '" + name + "' found");
                            if (!c.Nonlocals.ContainsKey(name))
                                c.Nonlocals[name] = nl.Src;
                        }
                        break;
                    default: base.Stmt(st, c); break;
                }
            }
        }
    }
}

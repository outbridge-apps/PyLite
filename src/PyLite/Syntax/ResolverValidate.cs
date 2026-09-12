using System.Collections.Generic;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class ResolverPass
    {
        private static PySyntaxErrorException Err(SourceInfo s, string msg) { return PySyntaxErrorException.Syntax(msg, s.Line, s.Col); }

        // Same calling-thread probe as the resolver/folder walks. The validator recurses per statement
        // level too (an elif chain synthesizes IfNode nesting as deep as the chain is long), and its
        // frames are not the resolver's: a depth that PASSES the resolver's probe can still overflow
        // HERE — that exact window (5.5-6.4k elifs on a 1MB compile thread) killed a host process.
        private static void ProbeStack(StmtNode at)
        {
            try
            {
                System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
            }
            catch (System.InsufficientExecutionStackException)
            {
                throw new PySyntaxErrorException("SyntaxError", "input too complex to compile", at.Src.Line, at.Src.Col);
            }
        }

        // --- return/break/continue context validation ----
        // continue inside finally is legal (3.8+); ExecTry already treats any finally signal
        // as overriding the pending one, so no finally tracking is needed here.
        private void ValidateControl(IReadOnlyList<StmtNode> stmts, bool inFunction, int loopDepth)
        {
            foreach (var st in stmts)
            {
                ProbeStack(st);
                switch (st)
                {
                    case ReturnNode r:
                        if (!inFunction)
                            throw Err(r.Src, "'return' outside function");
                        break;
                    case BreakNode b:
                        if (loopDepth == 0)
                            throw Err(b.Src, "'break' outside loop");
                        break;
                    case ContinueNode c:
                        if (loopDepth == 0)
                            throw Err(c.Src, "'continue' not properly in loop");
                        break;
                    case IfNode i:
                        ValidateControl(i.Body, inFunction, loopDepth);
                        ValidateControl(i.OrElse, inFunction, loopDepth);
                        break;
                    case WhileNode w:
                        ValidateControl(w.Body, inFunction, loopDepth + 1);
                        ValidateControl(w.OrElse, inFunction, loopDepth);
                        break;
                    case ForNode f:
                        ValidateControl(f.Body, inFunction, loopDepth + 1);
                        ValidateControl(f.OrElse, inFunction, loopDepth);
                        break;
                    case FuncDefNode fd:
                        ValidateControl(fd.Body, true, 0); // a new function resets the loop context
                        break;
                    case ClassDefNode cd:
                        foreach (var meth in cd.Methods)
                            ValidateControl(meth.Body, true, 0);
                        break;
                    case TryNode t:
                        ValidateControl(t.Body, inFunction, loopDepth);
                        foreach (var h in t.Handlers)
                            ValidateControl(h.Body, inFunction, loopDepth);
                        ValidateControl(t.OrElse, inFunction, loopDepth);
                        ValidateControl(t.Finally, inFunction, loopDepth);
                        break;
                    case MatchNode mt:
                        foreach (var cs in mt.Cases)
                            ValidateControl(cs.Body, inFunction, loopDepth);
                        break;
                    default:
                        break;
                }
            }
        }

        // --- nonlocal/global validation ----
        private void ValidateScope(ScopeInfo scope, IReadOnlyList<StmtNode> stmts)
        {
            var ctx = new DeclCtx(scope.ParamNames);
            ValidateDecls(stmts, scope, ctx);
            foreach (var child in scope.Children)
            {
                FuncDefNode fd = child.Node as FuncDefNode;
                if (fd != null)
                    ValidateScope(child, fd.Body);
            }
        }

        private sealed class DeclCtx
        {
            public readonly HashSet<string> Params;
            public readonly HashSet<string> Assigned = new HashSet<string>(System.StringComparer.Ordinal);
            public readonly HashSet<string> Used = new HashSet<string>(System.StringComparer.Ordinal);
            public readonly HashSet<string> Globals = new HashSet<string>(System.StringComparer.Ordinal);
            public readonly HashSet<string> Nonlocals = new HashSet<string>(System.StringComparer.Ordinal);
            public DeclCtx(IReadOnlyList<string> paramNames) { Params = new HashSet<string>(paramNames, System.StringComparer.Ordinal); }
        }

        private void ValidateDecls(IReadOnlyList<StmtNode> stmts, ScopeInfo scope, DeclCtx c)
        {
            foreach (var st in stmts)
            {
                ProbeStack(st);
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
                        if (scope.Kind == ScopeKind.Module)
                            throw Err(nl.Src, "nonlocal declaration not allowed at module level");
                        foreach (var name in nl.Names)
                        {
                            if (c.Params.Contains(name))
                                throw Err(nl.Src, "name '" + name + "' is parameter and nonlocal");
                            if (c.Globals.Contains(name))
                                throw Err(nl.Src, "name '" + name + "' is nonlocal and global");
                            if (!EnclosingFunctionBinds(scope.Parent, name))
                                throw Err(nl.Src, "no binding for nonlocal '" + name + "' found");
                            if (c.Assigned.Contains(name))
                                throw Err(nl.Src, "name '" + name + "' is assigned to before nonlocal declaration");
                            if (c.Used.Contains(name))
                                throw Err(nl.Src, "name '" + name + "' is used prior to nonlocal declaration");
                            c.Nonlocals.Add(name);
                        }

                        break;
                    case AssignNode a:
                        CollectUses(a.Value, c);
                        foreach (var t in a.Targets)
                            CollectAssigned(t, c);
                        break;
                    case AugAssignNode aug:
                        CollectUses(aug.Value, c);
                        CollectAssigned(aug.Target, c);
                        break;
                    case ExprStmtNode e:
                        CollectUses(e.Value, c);
                        break;
                    case DelNode d:
                        foreach (var t in d.Targets)
                            CollectAssigned(t, c);
                        break;
                    case ReturnNode r:
                        if (r.Value != null)
                            CollectUses(r.Value, c);
                        break;
                    case RaiseNode ra:
                        if (ra.Exc != null)
                            CollectUses(ra.Exc, c);
                        if (ra.Cause != null)
                            CollectUses(ra.Cause, c);
                        break;
                    case AssertNode asrt:
                        CollectUses(asrt.Test, c);
                        if (asrt.Msg != null)
                            CollectUses(asrt.Msg, c);
                        break;
                    case IfNode i:
                        CollectUses(i.Test, c);
                        ValidateDecls(i.Body, scope, c);
                        ValidateDecls(i.OrElse, scope, c);
                        break;
                    case WhileNode w:
                        CollectUses(w.Test, c);
                        ValidateDecls(w.Body, scope, c);
                        ValidateDecls(w.OrElse, scope, c);
                        break;
                    case ForNode f:
                        CollectUses(f.Iter, c);
                        CollectAssigned(f.Target, c);
                        ValidateDecls(f.Body, scope, c);
                        ValidateDecls(f.OrElse, scope, c);
                        break;
                    case TryNode t:
                        ValidateDecls(t.Body, scope, c);
                        foreach (var h in t.Handlers)
                        {
                            if (h.Type != null)
                                CollectUses(h.Type, c);
                            if (h.Name != null)
                                c.Assigned.Add(h.Name);
                            ValidateDecls(h.Body, scope, c);
                        }

                        ValidateDecls(t.OrElse, scope, c);
                        ValidateDecls(t.Finally, scope, c);
                        break;
                    case FuncDefNode fd:
                        foreach (var d in fd.Decorators)
                            CollectUses(d, c);
                        c.Assigned.Add(fd.Name);
                        break; // nested scope not descended
                    case ClassDefNode cd:
                        foreach (var d in cd.Decorators)
                            CollectUses(d, c);
                        if (cd.Base != null)
                            CollectUses(cd.Base, c);
                        foreach (var f in cd.Fields)
                            if (f.Default != null)
                                CollectUses(f.Default, c);
                        c.Assigned.Add(cd.Name);
                        break; // method scopes not descended
                    case MatchNode m:
                        CollectUses(m.Subject, c);
                        foreach (var cs in m.Cases)
                        {
                            CollectPatternAssigned(cs.Pattern, c);
                            if (cs.Guard != null)
                                CollectUses(cs.Guard, c);
                            ValidateDecls(cs.Body, scope, c);
                        }
                        break;
                    case ImportNode im:
                        foreach (var al in im.Names)
                            c.Assigned.Add(ImportBoundName(al));
                        break;
                    case FromImportNode fi:
                        foreach (var al in fi.Names)
                            c.Assigned.Add(al.AsName ?? al.DottedName);
                        break;
                    default:
                        break;
                }
            }
        }

        // Collect used names (does not descend into nested scopes).
        private void CollectUses(ExprNode e, DeclCtx c)
        {
            ProbeStack(e);   // resolver's ExprNode overload (same partial class)
            switch (e)
            {
                case NameNode n: c.Used.Add(n.Id); break;
                case BinOpNode b: CollectUses(b.Left, c); CollectUses(b.Right, c); break;
                case UnaryOpNode u: CollectUses(u.Operand, c); break;
                case BoolOpNode bo: foreach (var v in bo.Values)
                    CollectUses(v, c); break;
                case CompareChainNode cc: CollectUses(cc.Left, c); foreach (var v in cc.Comparators)
                    CollectUses(v, c); break;
                case IfExpNode ie: CollectUses(ie.Body, c); CollectUses(ie.Test, c); CollectUses(ie.OrElse, c); break;
                case CallNode call: CollectUses(call.Func, c); foreach (var arg in call.Args)
                    CollectUses(arg.Value, c); break;
                case AttributeNode at: CollectUses(at.Value, c); break;
                case IndexNode idx: CollectUses(idx.Value, c); CollectUses(idx.Index, c); break;
                case SliceNode sl:
                    if (sl.Lower != null)
                        CollectUses(sl.Lower, c);
                    if (sl.Upper != null)
                        CollectUses(sl.Upper, c);
                    if (sl.Step != null)
                        CollectUses(sl.Step, c);
                    break;
                case TupleNode t: foreach (var v in t.Elts)
                    CollectUses(v, c); break;
                case ListNode l: foreach (var v in l.Elts)
                    CollectUses(v, c); break;
                case SetNode se: foreach (var v in se.Elts)
                    CollectUses(v, c); break;
                case DictNode d: for (int i = 0; i < d.Keys.Count; i++)
                {
                    if (d.Keys[i] != null)   // null key => '**' unpacking entry
                        CollectUses(d.Keys[i], c);
                    CollectUses(d.Values[i], c);
                } break;
                case StarredNode s: CollectUses(s.Value, c); break;
                case NamedExprNode ne: c.Assigned.Add(ne.Target.Id); CollectUses(ne.Value, c); break;
                default: break;   // Literal / nested Lambda / Comprehension / FString not descended for ordering
            }
        }

        private void CollectPatternAssigned(PatternNode p, DeclCtx c)
        {
            switch (p)
            {
                case CapturePattern cp: if (cp.Target != null)
                    c.Assigned.Add(cp.Target.Id); break;
                case StarPattern st: if (st.Target != null)
                    c.Assigned.Add(st.Target.Id); break;
                case LiteralPattern _: break;
                case ValuePattern v: CollectUses(v.Value, c); break;
                case SequencePattern seq: foreach (var e in seq.Elements)
                    CollectPatternAssigned(e, c); break;
                case MappingPattern m:
                    foreach (var k in m.Keys)
                        CollectUses(k, c);
                    foreach (var vp in m.Values)
                        CollectPatternAssigned(vp, c);
                    if (m.Rest != null)
                        c.Assigned.Add(m.Rest.Id);
                    break;
                case OrPattern o: foreach (var alt in o.Alternatives)
                    CollectPatternAssigned(alt, c); break;
                case AsPattern a: CollectPatternAssigned(a.Inner, c); c.Assigned.Add(a.Target.Id); break;
                case ClassPattern cp:
                    CollectUses(cp.Cls, c);
                    foreach (var p2 in cp.Positional)
                        CollectPatternAssigned(p2, c);
                    foreach (var p2 in cp.KwPatterns)
                        CollectPatternAssigned(p2, c);
                    break;
            }
        }

        private void CollectAssigned(ExprNode e, DeclCtx c)
        {
            ProbeStack(e);
            switch (e)
            {
                case NameNode n: c.Assigned.Add(n.Id); break;
                case TupleNode t: foreach (var elt in t.Elts)
                    CollectAssigned(elt, c); break;
                case ListNode l: foreach (var elt in l.Elts)
                    CollectAssigned(elt, c); break;
                case StarredNode s: CollectAssigned(s.Value, c); break;
                case AttributeNode a: CollectUses(a.Value, c); break;
                case IndexNode idx: CollectUses(idx.Value, c); CollectUses(idx.Index, c); break;
                default: break;
            }
        }
    }
}

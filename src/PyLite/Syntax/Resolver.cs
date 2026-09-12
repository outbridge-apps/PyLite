using System.Collections.Generic;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    // Static analysis pass over the finished AST: scope tables, name classification (LEGB + cell/free),
    // slot layout. Recursion is over scope/AST nesting only (bounded by the parser's depth limit).
    internal static class Resolver
    {
        public static ResolvedProgram Resolve(ModuleNode module, SyntaxLimits limits)
        {
            return new ResolverPass().Run(module);
        }
    }

    internal sealed partial class ResolverPass
    {
        private readonly List<KeyValuePair<NameNode, ScopeInfo>> _names = new List<KeyValuePair<NameNode, ScopeInfo>>();
        private ScopeInfo _module;

        public ResolvedProgram Run(ModuleNode module)
        {
            _module = new ScopeInfo { Kind = ScopeKind.Module, Name = "<module>", Node = null };
            WalkStmts(module.Body, _module);

            Analyze(_module);
            ValidateControl(module.Body, false, 0);
            ValidateScope(_module, module.Body);
            AssignSlots(_module);

            var scopeInfos = new Dictionary<Node, FunctionInfo>();
            BuildScopeInfos(_module, scopeInfos);
            FunctionInfo moduleInfo = MakeFunctionInfo(_module);

            var bindings = new Dictionary<NameNode, NameBinding>();
            var builtinSlots = new Dictionary<string, NameBinding>(System.StringComparer.Ordinal);
            var globalSlots = new Dictionary<string, NameBinding>(System.StringComparer.Ordinal);
            foreach (var kv in _names)
            {
                NameBinding b = Classify(kv.Value, kv.Key.Id);
                if (b != null && b.Kind == NameKind.Builtin)
                {
                    // One shared binding per builtin name; its Slot indexes the per-run resolved cache.
                    NameBinding shared;
                    if (!builtinSlots.TryGetValue(kv.Key.Id, out shared))
                    {
                        shared = new NameBinding(NameKind.Builtin, builtinSlots.Count);
                        builtinSlots[kv.Key.Id] = shared;
                    }
                    b = shared;
                }
                else if (b != null && b.Kind == NameKind.Global)
                {
                    // Same sharing for globals: the Slot indexes the per-run versioned dict-entry
                    // cache (all occurrences of one name, in any scope, share one slot).
                    NameBinding shared;
                    if (!globalSlots.TryGetValue(kv.Key.Id, out shared))
                    {
                        shared = new NameBinding(NameKind.Global, globalSlots.Count);
                        globalSlots[kv.Key.Id] = shared;
                    }
                    b = shared;
                }
                bindings[kv.Key] = b;
                kv.Key.Binding = b;   // baked onto the node — the runtime reads the field, not the dictionary
            }

            var folds = ConstFolder.FoldAll(module);
            return new ResolvedProgram(module, moduleInfo, scopeInfos, bindings, folds, builtinSlots.Count, globalSlots.Count);
        }

        private void Record(NameNode n, ScopeInfo s) { _names.Add(new KeyValuePair<NameNode, ScopeInfo>(n, s)); }

        // ---------------- collection walk ----------------

        private void WalkStmts(IReadOnlyList<StmtNode> stmts, ScopeInfo s)
        {
            foreach (var st in stmts)
                WalkStmt(st, s);
        }

        private void WalkStmt(StmtNode st, ScopeInfo s)
        {
            switch (st)
            {
                case ExprStmtNode e: WalkExpr(e.Value, s); break;
                case AssignNode a: WalkExpr(a.Value, s); foreach (var t in a.Targets)
                    WalkTarget(t, s); break;
                case AugAssignNode aug: WalkExpr(aug.Value, s); WalkTarget(aug.Target, s); break;
                case DelNode d: foreach (var t in d.Targets)
                    WalkTarget(t, s); break;
                case IfNode i: WalkExpr(i.Test, s); WalkStmts(i.Body, s); WalkStmts(i.OrElse, s); break;
                case WhileNode w: WalkExpr(w.Test, s); WalkStmts(w.Body, s); WalkStmts(w.OrElse, s); break;
                case ForNode f: WalkExpr(f.Iter, s); WalkTarget(f.Target, s); WalkStmts(f.Body, s); WalkStmts(f.OrElse, s); break;
                case FuncDefNode fd: s.Bind(fd.Name); WalkFuncDef(fd, s); break;
                case ClassDefNode cd:
                    s.Bind(cd.Name);
                    foreach (var d in cd.Decorators)
                        WalkExpr(d, s);
                    if (cd.Base != null)
                        WalkExpr(cd.Base, s);
                    foreach (var f in cd.Fields)
                        if (f.Default != null)
                            WalkExpr(f.Default, s);
                    foreach (var meth in cd.Methods)   // methods are function scopes parented at s (class scope is not a closure)
                        WalkFuncDef(meth, s);
                    break;
                case ReturnNode r: if (r.Value != null)
                    WalkExpr(r.Value, s); break;
                case RaiseNode ra: if (ra.Exc != null)
                    WalkExpr(ra.Exc, s); if (ra.Cause != null)
                    WalkExpr(ra.Cause, s); break;
                case AssertNode asrt: WalkExpr(asrt.Test, s); if (asrt.Msg != null)
                    WalkExpr(asrt.Msg, s); break;
                case TryNode t:
                    WalkStmts(t.Body, s);
                    foreach (var h in t.Handlers)
                    {
                        if (h.Type != null)
                            WalkExpr(h.Type, s);
                        if (h.Name != null)
                            s.Bind(h.Name);
                        WalkStmts(h.Body, s);
                    }
                    WalkStmts(t.OrElse, s);
                    WalkStmts(t.Finally, s);
                    break;
                case GlobalNode g: foreach (var n in g.Names)
                {
                    s.Globals.Add(n);
                    _module.Bound.Add(n);
                } break;
                case NonlocalNode nl: foreach (var n in nl.Names)
                    s.Nonlocals.Add(n); break;
                case ImportNode im: foreach (var al in im.Names)
                    s.Bind(ImportBoundName(al)); break;
                case FromImportNode fi: foreach (var al in fi.Names)
                    s.Bind(al.AsName ?? al.DottedName); break;
                case MatchNode m:
                    WalkExpr(m.Subject, s);
                    foreach (var cs in m.Cases)
                    {
                        WalkPattern(cs.Pattern, s);
                        if (cs.Guard != null)
                            WalkExpr(cs.Guard, s);
                        WalkStmts(cs.Body, s);
                    }
                    break;
                default: break;   // Pass/Break/Continue
            }
        }

        private static string ImportBoundName(ImportAlias al)
        {
            if (al.AsName != null)
                return al.AsName;
            int dot = al.DottedName.IndexOf('.');
            return dot < 0 ? al.DottedName : al.DottedName.Substring(0, dot);
        }

        // Pattern capture/star/rest/as targets bind in the enclosing scope; value/literal/key exprs are uses.
        private void WalkPattern(PatternNode p, ScopeInfo s)
        {
            switch (p)
            {
                case CapturePattern c: if (c.Target != null)
                    WalkTarget(c.Target, s); break;
                case StarPattern st: if (st.Target != null)
                    WalkTarget(st.Target, s); break;
                case LiteralPattern _: break;
                case ValuePattern v: WalkExpr(v.Value, s); break;
                case SequencePattern seq: foreach (var e in seq.Elements)
                    WalkPattern(e, s); break;
                case MappingPattern m:
                    foreach (var k in m.Keys)
                        WalkExpr(k, s);
                    foreach (var vp in m.Values)
                        WalkPattern(vp, s);
                    if (m.Rest != null)
                        WalkTarget(m.Rest, s);
                    break;
                case OrPattern o: foreach (var alt in o.Alternatives)
                    WalkPattern(alt, s); break;
                case AsPattern a: WalkPattern(a.Inner, s); WalkTarget(a.Target, s); break;
                case ClassPattern cp:
                    WalkExpr(cp.Cls, s);
                    foreach (var p2 in cp.Positional)
                        WalkPattern(p2, s);
                    foreach (var p2 in cp.KwPatterns)
                        WalkPattern(p2, s);
                    break;
            }
        }

        private void WalkTarget(ExprNode e, ScopeInfo s)
        {
            switch (e)
            {
                case NameNode n: s.Bind(n.Id); Record(n, s); break;
                case TupleNode t: foreach (var elt in t.Elts)
                    WalkTarget(elt, s); break;
                case ListNode l: foreach (var elt in l.Elts)
                    WalkTarget(elt, s); break;
                case StarredNode st: WalkTarget(st.Value, s); break;
                case AttributeNode a: WalkExpr(a.Value, s); break;
                case IndexNode idx: WalkExpr(idx.Value, s); WalkExpr(idx.Index, s); break;
                default: WalkExpr(e, s); break;
            }
        }

        // The parser bounds AST depth (MaxAstDepth), but this walk recurses on the CALLING thread whose
        // remaining stack is the host's business — probe every entry so a near-exhausted caller gets a
        // SyntaxError instead of an uncatchable StackOverflow (same conversion as the parser's probe).
        private static void ProbeStack(ExprNode at)
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

        private void WalkExpr(ExprNode e, ScopeInfo s)
        {
            ProbeStack(e);
            switch (e)
            {
                case NameNode n: s.Referenced.Add(n.Id); Record(n, s); break;
                case LiteralNode _: break;
                case BinOpNode b: WalkExpr(b.Left, s); WalkExpr(b.Right, s); break;
                case UnaryOpNode u: WalkExpr(u.Operand, s); break;
                case BoolOpNode bo: foreach (var v in bo.Values)
                    WalkExpr(v, s); break;
                case CompareChainNode c: WalkExpr(c.Left, s); foreach (var cc in c.Comparators)
                    WalkExpr(cc, s); break;
                case IfExpNode ie: WalkExpr(ie.Body, s); WalkExpr(ie.Test, s); WalkExpr(ie.OrElse, s); break;
                case CallNode call: WalkExpr(call.Func, s); foreach (var arg in call.Args)
                    WalkExpr(arg.Value, s); break;
                case AttributeNode at: WalkExpr(at.Value, s); break;
                case IndexNode idx: WalkExpr(idx.Value, s); WalkExpr(idx.Index, s); break;
                case SliceNode sl:
                    if (sl.Lower != null)
                        WalkExpr(sl.Lower, s);
                    if (sl.Upper != null)
                        WalkExpr(sl.Upper, s);
                    if (sl.Step != null)
                        WalkExpr(sl.Step, s);
                    break;
                case TupleNode t: foreach (var v in t.Elts)
                    WalkExpr(v, s); break;
                case ListNode l: foreach (var v in l.Elts)
                    WalkExpr(v, s); break;
                case SetNode set: foreach (var v in set.Elts)
                    WalkExpr(v, s); break;
                case DictNode d:
                    for (int i = 0; i < d.Keys.Count; i++)
                    {
                        if (d.Keys[i] != null)   // null key => '**' unpacking entry
                            WalkExpr(d.Keys[i], s);
                        WalkExpr(d.Values[i], s);
                    }
                    break;
                case StarredNode st: WalkExpr(st.Value, s); break;
                case NamedExprNode ne: WalkExpr(ne.Value, s); WalkTarget(ne.Target, s); break;
                case LambdaNode lam: WalkLambda(lam, s); break;
                case ComprehensionNode comp: WalkComprehension(comp, s); break;
                case FStringNode fs:
                    foreach (var p in fs.Parts)
                    {
                        if (!p.IsLiteral)
                            WalkExpr(p.Expr, s);
                        if (p.FormatSpec != null)
                            foreach (var fp in p.FormatSpec)
                                if (!fp.IsLiteral)
                                    WalkExpr(fp.Expr, s);
                    }
                    break;
                default: break;
            }
        }

        private void WalkFuncDef(FuncDefNode fd, ScopeInfo parent)
        {
            foreach (var d in fd.Decorators)   // decorators evaluate in the enclosing scope
                WalkExpr(d, parent);
            WalkDefaults(fd.Params, parent);
            var fs = new ScopeInfo { Kind = ScopeKind.Function, Parent = parent, Name = fd.Name, Node = fd };
            parent.Children.Add(fs);
            BindParams(fd.Params, fs);
            WalkStmts(fd.Body, fs);
        }

        private void WalkLambda(LambdaNode lam, ScopeInfo parent)
        {
            WalkDefaults(lam.Params, parent);
            var ls = new ScopeInfo { Kind = ScopeKind.Lambda, Parent = parent, Name = "<lambda>", Node = lam };
            parent.Children.Add(ls);
            BindParams(lam.Params, ls);
            WalkExpr(lam.Body, ls);
        }

        private void WalkComprehension(ComprehensionNode comp, ScopeInfo parent)
        {
            WalkExpr(comp.Clauses[0].Iter, parent);   // first iterable in the enclosing scope
            var cs = new ScopeInfo { Kind = ScopeKind.Comprehension, Parent = parent, Name = "<comp>", Node = comp };
            parent.Children.Add(cs);
            WalkTarget(comp.Clauses[0].Target, cs);
            for (int i = 1; i < comp.Clauses.Count; i++)
            {
                var cl = comp.Clauses[i];
                if (cl.IsFor)
                {
                    WalkExpr(cl.Iter, cs);
                    WalkTarget(cl.Target, cs);
                }
                else
                    WalkExpr(cl.Cond, cs);
            }
            WalkExpr(comp.Element, cs);
            if (comp.ValueElement != null)
                WalkExpr(comp.ValueElement, cs);
        }

        private void WalkDefaults(ParamList p, ScopeInfo parent)
        {
            foreach (var pp in p.Positional)
                if (pp.Default != null)
                    WalkExpr(pp.Default, parent);
            foreach (var pp in p.KwOnly)
                if (pp.Default != null)
                    WalkExpr(pp.Default, parent);
        }

        private static void BindParams(ParamList p, ScopeInfo s)
        {
            foreach (var pp in p.Positional)
            {
                s.Bind(pp.Name);
                s.ParamNames.Add(pp.Name);
            }
            s.PositionalCount = p.Positional.Count;
            s.PositionalOnlyCount = p.PositionalOnlyCount;
            if (p.StarArg != null)
            {
                s.Bind(p.StarArg.Name);
                s.ParamNames.Add(p.StarArg.Name);
                s.HasStarArg = true;
            }
            foreach (var pp in p.KwOnly)
            {
                s.Bind(pp.Name);
                s.ParamNames.Add(pp.Name);
            }
            s.KwOnlyCount = p.KwOnly.Count;
            if (p.KwArg != null)
            {
                s.Bind(p.KwArg.Name);
                s.ParamNames.Add(p.KwArg.Name);
                s.HasKwArg = true;
            }
        }

        // ---------------- cell/free analysis ----------------

        private HashSet<string> Analyze(ScopeInfo s)
        {
            var childRequests = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var child in s.Children)
                childRequests.UnionWith(Analyze(child));

            foreach (var name in childRequests)
            {
                if (s.IsFunctionLike && s.Bound.Contains(name) && !s.Globals.Contains(name) && !s.Nonlocals.Contains(name))
                    s.AddCell(name);
                else if (s.Kind != ScopeKind.Module)
                    s.AddFree(name);
            }

            foreach (var name in s.Referenced)
            {
                if (s.Bound.Contains(name) || s.Globals.Contains(name) || s.Nonlocals.Contains(name))
                    continue;
                if (EnclosingFunctionBinds(s.Parent, name))
                    s.AddFree(name);
            }

            foreach (var name in s.Nonlocals)
                s.AddFree(name);

            return s.Kind == ScopeKind.Module ? new HashSet<string>() : new HashSet<string>(s.FreeVars);
        }

        private static bool EnclosingFunctionBinds(ScopeInfo s, string name)
        {
            while (s != null)
            {
                if (s.IsFunctionLike && s.Bound.Contains(name) && !s.Globals.Contains(name))
                    return true;
                if (s.Kind == ScopeKind.Module)
                    return false;
                s = s.Parent;
            }
            return false;
        }

        // ---------------- slot assignment ----------------

        private void AssignSlots(ScopeInfo s)
        {
            int cellIdx = 0;
            foreach (var n in s.CellOrder)
                s.CellSlot[n] = cellIdx++;
            int freeIdx = 0;
            foreach (var n in s.FreeOrder)
                s.FreeSlot[n] = freeIdx++;

            if (s.Kind != ScopeKind.Module)
            {
                var paramSet = new HashSet<string>(s.ParamNames, System.StringComparer.Ordinal);
                int localIdx = 0;
                foreach (var pname in s.ParamNames)
                {
                    if (s.CellVars.Contains(pname))
                        continue; // cell params live in cellVars
                    s.LocalSlot[pname] = localIdx++;
                    s.LocalNames.Add(pname);
                }

                foreach (var n in s.BoundOrder)
                {
                    if (paramSet.Contains(n) || s.CellVars.Contains(n) || s.Globals.Contains(n) || s.Nonlocals.Contains(n))
                        continue;
                    s.LocalSlot[n] = localIdx++;
                    s.LocalNames.Add(n);
                }
            }

            foreach (var child in s.Children)
                AssignSlots(child);
        }

        // ---------------- FunctionInfo + classification ----------------

        private void BuildScopeInfos(ScopeInfo s, Dictionary<Node, FunctionInfo> map)
        {
            if (s.Node != null)
            {
                FunctionInfo fi = MakeFunctionInfo(s);
                map[s.Node] = fi;
                s.Node.ScopeInfo = fi;   // baked onto the node (see NameNode.Binding)
            }
            foreach (var child in s.Children)
                BuildScopeInfos(child, map);
        }

        private FunctionInfo MakeFunctionInfo(ScopeInfo s)
        {
            var paramSlots = new List<int>();
            var paramIsCell = new List<bool>();
            foreach (var pname in s.ParamNames)
            {
                if (s.CellVars.Contains(pname))
                {
                    paramSlots.Add(s.CellSlot[pname]);
                    paramIsCell.Add(true);
                }
                else
                {
                    paramSlots.Add(s.LocalSlot[pname]);
                    paramIsCell.Add(false);
                }
            }
            return new FunctionInfo(
                s.Name, s.Kind, s.PositionalCount + s.KwOnlyCount, s.PositionalCount, s.KwOnlyCount,
                s.HasStarArg, s.HasKwArg,
                AstList.FreezeArray(s.ParamNames.ToArray()), AstList.FreezeArray(paramSlots.ToArray()),
                AstList.FreezeArray(paramIsCell.ToArray()), AstList.FreezeArray(s.LocalNames.ToArray()),
                s.LocalNames.Count, AstList.FreezeArray(s.CellOrder.ToArray()), AstList.FreezeArray(s.FreeOrder.ToArray()),
                s.PositionalOnlyCount);
        }

        private NameBinding Classify(ScopeInfo s, string name)
        {
            if (s.Globals.Contains(name))
                return new NameBinding(NameKind.Global, -1);
            if (s.Nonlocals.Contains(name))
                return new NameBinding(NameKind.Free, SlotOrMinus(s.FreeSlot, name));
            if (s.Bound.Contains(name))
            {
                if (s.CellVars.Contains(name))
                    return new NameBinding(NameKind.Cell, s.CellSlot[name]);
                if (s.Kind == ScopeKind.Module)
                    return new NameBinding(NameKind.Global, -1);
                return new NameBinding(NameKind.Local, s.LocalSlot[name]);
            }
            if (s.FreeVars.Contains(name))
                return new NameBinding(NameKind.Free, s.FreeSlot[name]);
            if (_module.Bound.Contains(name))
                return new NameBinding(NameKind.Global, -1);
            return new NameBinding(NameKind.Builtin, -1);
        }

        private static int SlotOrMinus(Dictionary<string, int> map, string name)
        {
            int v;
            return map.TryGetValue(name, out v) ? v : -1;
        }
    }
}

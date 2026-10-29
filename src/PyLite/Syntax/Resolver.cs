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
        private int _literalCount;

        public ResolvedProgram Run(ModuleNode module)
        {
            _module = new ScopeInfo { Kind = ScopeKind.Module, Name = "<module>", Node = null };
            new ScopeCollector(this).Stmts(module.Body, _module);

            Analyze(_module);
            ValidateControl(module.Body);
            ValidateScope(_module, module.Body);
            AssignSlots(_module);

            BuildScopeInfos(_module);
            FunctionInfo moduleInfo = MakeFunctionInfo(_module);

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
                kv.Key.Binding = b;   // baked onto the node — the runtime reads the field, not the dictionary
            }

            return new ResolvedProgram(module, moduleInfo, builtinSlots.Count, globalSlots.Count, _literalCount);
        }

        private void Record(NameNode n, ScopeInfo s) { _names.Add(new KeyValuePair<NameNode, ScopeInfo>(n, s)); }

        // ---------------- collection walk ----------------

        // Builds the scope tree: what each scope binds, reads and declares. Every NameNode is recorded with
        // its scope for the classification pass.
        private sealed class ScopeCollector : AstWalker<ScopeInfo>
        {
            private readonly ResolverPass _pass;

            internal ScopeCollector(ResolverPass pass) { _pass = pass; }

            protected override void Name(NameNode n, ScopeInfo s)
            {
                s.Referenced.Add(n.Id);
                _pass.Record(n, s);
            }

            protected override void Bind(string name, ScopeInfo s) { s.Bind(name); }

            // str/float/bytes literals get a per-run cache slot: one value per literal per run (P4).
            protected override void Expr(ExprNode e, ScopeInfo s)
            {
                if (e.NodeKind == NodeKind.Literal)
                {
                    LiteralNode lit = (LiteralNode)e;
                    if (lit.CacheSlot < 0 && (lit.Kind == LiteralKind.Str || lit.Kind == LiteralKind.Float || lit.Kind == LiteralKind.Bytes))
                        lit.CacheSlot = _pass._literalCount++;
                    return;
                }
                base.Expr(e, s);
            }

            protected override void BindName(NameNode n, ScopeInfo s)
            {
                s.Bind(n.Id);
                _pass.Record(n, s);
            }

            protected override void Stmt(StmtNode st, ScopeInfo s)
            {
                switch (st)
                {
                    case GlobalNode g:
                        foreach (var n in g.Names)
                        {
                            s.Globals.Add(n);
                            _pass._module.Bound.Add(n);
                        }
                        break;
                    case NonlocalNode nl:
                        foreach (var n in nl.Names)
                            s.Nonlocals.Add(n);
                        break;
                    default: base.Stmt(st, s); break;
                }
            }

            protected override void FuncDef(FuncDefNode fd, ScopeInfo parent)
            {
                base.FuncDef(fd, parent);
                Stmts(fd.Body, ChildScope(parent, ScopeKind.Function, fd.Name, fd, fd.Params));
            }

            protected override void Lambda(LambdaNode lam, ScopeInfo parent)
            {
                base.Lambda(lam, parent);
                Expr(lam.Body, ChildScope(parent, ScopeKind.Lambda, "<lambda>", lam, lam.Params));
            }

            protected override void Comprehension(ComprehensionNode comp, ScopeInfo parent)
            {
                base.Comprehension(comp, parent);
                ScopeInfo cs = ChildScope(parent, ScopeKind.Comprehension, "<comp>", comp, null);
                Target(comp.Clauses[0].Target, cs);
                for (int i = 1; i < comp.Clauses.Count; i++)
                {
                    var cl = comp.Clauses[i];
                    if (cl.IsFor)
                    {
                        Expr(cl.Iter, cs);
                        Target(cl.Target, cs);
                    }
                    else
                    {
                        Expr(cl.Cond, cs);
                    }
                }
                Expr(comp.Element, cs);
                if (comp.ValueElement != null)
                    Expr(comp.ValueElement, cs);
            }

            private static ScopeInfo ChildScope(ScopeInfo parent, ScopeKind kind, string name, Node node, ParamList ps)
            {
                var s = new ScopeInfo { Kind = kind, Parent = parent, Name = name, Node = node };
                parent.Children.Add(s);
                if (ps != null)
                    BindParams(ps, s);
                return s;
            }
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

        private void BuildScopeInfos(ScopeInfo s)
        {
            if (s.Node != null)
                s.Node.ScopeInfo = MakeFunctionInfo(s);   // baked onto the node (see NameNode.Binding)
            foreach (var child in s.Children)
                BuildScopeInfos(child);
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

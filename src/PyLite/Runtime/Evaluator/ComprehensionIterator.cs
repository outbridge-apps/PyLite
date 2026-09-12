using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Runtime.Evaluator
{
    // The comprehension / genexp odometer. The clause nesting is walked by a
    // recursive C# iterator over CLAUSES (depth = clause count, static <= parser limit — NOT recursion
    // over data), which gives natural laziness for genexp. Runs in its OWN scope (Py3): clause targets
    // do not leak. The first for-iterable was evaluated in the outer scope (eager) at construction.
    internal sealed class ComprehensionIterator : ScriptIteratorBase
    {
        private readonly ComprehensionNode _node;
        private readonly Environment _scope;
        private readonly IScriptIterator _firstIter;
        private readonly EvalContext _ctx;
        private readonly Evaluator _ev;
        private readonly bool _fused;   // T2.3c gate: MaxIntBits >= 128, IntOpSmall's own threshold
        private IEnumerator<ScriptValue> _gen;

        internal ComprehensionIterator(ComprehensionNode node, Environment scope, IScriptIterator firstIter,
            EvalContext ctx, Evaluator ev)
        {
            _node = node;
            _scope = scope;
            _firstIter = firstIter;
            _ctx = ctx;
            _ev = ev;
            _fused = ctx.Limits.MaxIntBits >= 128;
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_gen == null)
            {
                _gen = Walk(0).GetEnumerator();
            }
            bool moved;
            try
            {
                moved = _gen.MoveNext();
            }
            catch (ScriptException se) when (se.Value.ExcType.IsSubtypeOf(PyExceptionTypes.StopIteration))
            {
                // PEP 479: a StopIteration escaping the comprehension/genexp body (element
                // a condition, or a non-first iterable — everything but the eager first for-iterable) is a
                // RuntimeError, not a silent end-of-iteration. The bool MoveNext protocol never surfaces
                // StopIteration itself, so anything caught here came from user code in the body.
                throw Raise.RuntimeError(ctx, "generator raised StopIteration");
            }
            if (moved)
            {
                value = _gen.Current;
                return true;
            }
            value = null;
            return false;
        }

        private IEnumerable<ScriptValue> Walk(int clauseIndex)
        {
            if (clauseIndex >= _node.Clauses.Count)
            {
                yield return EvalElement();
                yield break;
            }
            CompClause c = _node.Clauses[clauseIndex];
            if (c.IsFor)
            {
                IScriptIterator it = clauseIndex == 0
                    ? _firstIter
                    : PyOps.GetIterator(_ev.EvalExpr(c.Iter, _scope, _ctx), _ctx);
                ScriptValue item;
                while (it.MoveNext(_ctx, out item))
                {
                    _ev.AssignTo(c.Target, item, _scope, _ctx);
                    foreach (ScriptValue v in Walk(clauseIndex + 1))
                    {
                        yield return v;
                    }
                }
            }
            else
            {
                // T2.3c: a pure-int condition over ONE loop variable reads its slot and decides in
                // C# longs; anything else (or a non-int/unbound slot) keeps the compiled/tree walk.
                // Both caches are per clause (multi-if safe).
                bool keep;
                FusedScopeCond fusedCond = _fused ? Evaluator.GetFusedScopeCond(ref c.FusedCond, c.Cond) : null;
                if (!(fusedCond != null && fusedCond(_scope, out keep)))
                {
                    CompiledEval condFn = Evaluator.GetCompiledExpr(ref c.CompiledCond, c.Cond);
                    keep = PyOps.Truth(condFn != null ? condFn(_ev, _scope, _ctx) : _ev.EvalExpr(c.Cond, _scope, _ctx), _ctx);
                }
                if (keep)
                {
                    foreach (ScriptValue v in Walk(clauseIndex + 1))
                    {
                        yield return v;
                    }
                }
            }
        }

        private ScriptValue EvalElement()
        {
            // T2.3c/T2.1: a pure-int element over one loop variable computes in C# longs off its
            // slot; otherwise the closure-compiled form; otherwise the tree walk. Iterables and
            // targets stay interpreted; a fused false (non-int/overflow/unbound) re-runs generally.
            long f;
            FusedScopeLong fusedElem = _fused ? Evaluator.GetFusedScopeLong(ref _node.FusedElement, _node.Element) : null;
            if (_node.Kind == CompKind.Dict)
            {
                ScriptValue k;
                if (fusedElem != null && fusedElem(_scope, out f))
                {
                    k = _ctx.Values.Int(f);
                }
                else
                {
                    CompiledEval elemFn = Evaluator.GetCompiledExpr(ref _node.CompiledElement, _node.Element);
                    k = elemFn != null ? elemFn(_ev, _scope, _ctx) : _ev.EvalExpr(_node.Element, _scope, _ctx);
                }
                ScriptValue v;
                FusedScopeLong fusedVal = _fused
                    ? Evaluator.GetFusedScopeLong(ref _node.FusedValueElement, _node.ValueElement) : null;
                if (fusedVal != null && fusedVal(_scope, out f))
                {
                    v = _ctx.Values.Int(f);
                }
                else
                {
                    CompiledEval valFn = Evaluator.GetCompiledExpr(ref _node.CompiledValueElement, _node.ValueElement);
                    v = valFn != null ? valFn(_ev, _scope, _ctx) : _ev.EvalExpr(_node.ValueElement, _scope, _ctx);
                }
                return _ctx.Values.Tuple(new[] { k, v });
            }
            if (fusedElem != null && fusedElem(_scope, out f))
            {
                return _ctx.Values.Int(f);
            }
            CompiledEval elemFn2 = Evaluator.GetCompiledExpr(ref _node.CompiledElement, _node.Element);
            return elemFn2 != null ? elemFn2(_ev, _scope, _ctx) : _ev.EvalExpr(_node.Element, _scope, _ctx);
        }
    }
}

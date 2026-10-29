using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Runtime.Evaluator
{
    // A comprehension body is re-evaluated once per element; the closure compiler turns its
    // expression tree into linked delegates ONCE (T2.1), so each element skips the per-node
    // dispatch switch and counters. Every leaf calls the SAME helper the tree-walk path calls
    // (EvalLiteral/EvalName/TrySmallIntBinOp/BinaryOp/CompareOne/Truth), so results, errors and
    // charges are identical by construction. Closures capture only immutable AST/binding data plus
    // the per-call (ev, env, ctx) parameters — safe to cache on the shared AST node (benign race,
    // same pattern as CallNode.CachedCallSlot). Anything outside the whitelist compiles to null and
    // the caller keeps the tree-walk for the whole expression.
    internal delegate ScriptValue CompiledEval(Evaluator ev, Environment env, EvalContext ctx);

    internal sealed partial class Evaluator
    {
        private static readonly object NotCompilable = new object();

        internal static CompiledEval GetCompiledExpr(ref object cache, ExprNode e, EvalContext ctx)
        {
            if (ctx.InterpretOnly)
                return null;
            object c = cache;
            return c != null ? c as CompiledEval : Store(ref cache, TryCompileExpr(e));
        }

        // Every tier caches on the shared AST node: the built delegate, or NotCompilable when there is none.
        private static T Store<T>(ref object cache, T built) where T : class
        {
            cache = (object)built ?? NotCompilable;
            return built;
        }

        // The compiled form when there is one, else the tree walk: the two always agree.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal ScriptValue EvalCompiledOr(CompiledEval fn, ExprNode e, Environment env, EvalContext ctx)
        {
            return fn != null ? fn(this, env, ctx) : EvalExpr(e, env, ctx);
        }

        // Fused longs only when IntOpSmall's own threshold holds (the big path's estimate aborts stay
        // reachable) and the run is not interpret-only.
        internal static bool FusionAllowed(EvalContext ctx)
        {
            return ctx.Limits.MaxIntBits >= 128 && !ctx.InterpretOnly;
        }

        private static CompiledEval TryCompileExpr(ExprNode e)
        {
            switch (e.NodeKind)
            {
                case NodeKind.Literal:
                {
                    LiteralNode lit = (LiteralNode)e;
                    return (ev, env, ctx) => ev.EvalLiteral(lit, ctx);
                }
                case NodeKind.Name:
                {
                    NameNode nm = (NameNode)e;
                    return (ev, env, ctx) => ev.EvalName(nm, env, ctx);
                }
                case NodeKind.BinOp:
                {
                    BinOpNode b = (BinOpNode)e;
                    CompiledEval fa = TryCompileExpr(b.Left);
                    CompiledEval fb = TryCompileExpr(b.Right);
                    if (fa == null || fb == null)
                        return null;
                    PyBinOp op = MapBin(b.Op);
                    return (ev, env, ctx) =>
                    {
                        ScriptValue x = fa(ev, env, ctx);
                        return ApplyBinary(op, x, fb(ev, env, ctx), ctx);
                    };
                }
                case NodeKind.UnaryOp:
                {
                    UnaryOpNode u = (UnaryOpNode)e;
                    CompiledEval fo = TryCompileExpr(u.Operand);
                    if (fo == null)
                        return null;
                    UnaryOp uop = u.Op;
                    return (ev, env, ctx) => ApplyUnary(uop, fo(ev, env, ctx), ctx);
                }
                case NodeKind.CompareChain:
                {
                    CompareChainNode c = (CompareChainNode)e;
                    CompiledEval fl = TryCompileExpr(c.Left);
                    if (fl == null)
                        return null;
                    var fcs = new CompiledEval[c.Comparators.Count];
                    for (int i = 0; i < fcs.Length; i++)
                    {
                        fcs[i] = TryCompileExpr(c.Comparators[i]);
                        if (fcs[i] == null)
                            return null;
                    }
                    var chain = c;
                    return (ev, env, ctx) =>
                    {
                        ScriptValue left = fl(ev, env, ctx);
                        for (int i = 0; i < fcs.Length; i++)
                        {
                            ScriptValue right = fcs[i](ev, env, ctx);
                            if (!ev.CompareOne(chain.Ops[i], left, right, ctx))
                            {
                                return ctx.Values.Bool(false);   // short-circuit: the rest not evaluated
                            }
                            left = right;
                        }
                        return ctx.Values.Bool(true);
                    };
                }
                case NodeKind.BoolOp:
                {
                    BoolOpNode bo = (BoolOpNode)e;
                    var fs = new CompiledEval[bo.Values.Count];
                    for (int i = 0; i < fs.Length; i++)
                    {
                        fs[i] = TryCompileExpr(bo.Values[i]);
                        if (fs[i] == null)
                            return null;
                    }
                    bool isAnd = bo.Op == BoolOp.And;
                    return (ev, env, ctx) =>
                    {
                        ScriptValue v = null;
                        for (int i = 0; i < fs.Length; i++)
                        {
                            v = fs[i](ev, env, ctx);
                            if (PyOps.Truth(v, ctx) != isAnd)
                                return v;   // and: the first falsy; or: the first truthy
                        }
                        return v;
                    };
                }
                case NodeKind.IfExp:
                {
                    IfExpNode ie = (IfExpNode)e;
                    CompiledEval ft = TryCompileExpr(ie.Test);
                    CompiledEval fb2 = TryCompileExpr(ie.Body);
                    CompiledEval fe = TryCompileExpr(ie.OrElse);
                    if (ft == null || fb2 == null || fe == null)
                        return null;
                    return (ev, env, ctx) => PyOps.Truth(ft(ev, env, ctx), ctx)
                        ? fb2(ev, env, ctx)
                        : fe(ev, env, ctx);
                }
                default:
                    // Calls, subscripts, attributes, displays, lambdas, f-strings, walrus, nested
                    // comprehensions: the tree-walk owns these (probes, call machinery, assignment).
                    return null;
            }
        }
    }
}

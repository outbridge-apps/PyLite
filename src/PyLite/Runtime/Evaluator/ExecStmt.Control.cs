using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Runtime.Evaluator
{
    // Control constructs. while/for charge a BackEdge step before every condition
    // check; their else branch runs only when the loop was not broken out of. Return propagates outward.
    internal sealed partial class Evaluator
    {
        private ExecSignal ExecIf(IfNode n, Environment env, EvalContext ctx)
        {
            if (PyOps.Truth(EvalExpr(n.Test, env, ctx), ctx))
            {
                return ExecBlock(n.Body, env, ctx);
            }
            return ExecBlock(n.OrElse, env, ctx);   // empty when no else; elif desugars into OrElse
        }

        private ExecSignal ExecWhile(WhileNode n, Environment env, EvalContext ctx)
        {
            bool brokeOut = false;
            while (true)
            {
                ctx.Budget.Step(BudgetCost.BackEdge);
                if (!PyOps.Truth(EvalExpr(n.Test, env, ctx), ctx))
                {
                    break;
                }
                ExecSignal sig = ExecBlock(n.Body, env, ctx);
                if (sig.Kind == ExecSignalKind.Break)
                {
                    brokeOut = true;
                    break;
                }
                if (sig.Kind == ExecSignalKind.Continue)
                {
                    continue;
                }
                if (!sig.IsNormal)
                {
                    return sig;   // Return or Raised
                }
            }
            if (!brokeOut)
            {
                ExecSignal orelse = ExecBlock(n.OrElse, env, ctx);
                if (!orelse.IsNormal)
                {
                    return orelse;
                }
            }
            return ExecSignal.Normal;
        }

        private ExecSignal ExecFor(ForNode n, Environment env, EvalContext ctx)
        {
            ScriptValue iterable = EvalExpr(n.Iter, env, ctx);
            // P5: `for <name> in range(...)` with long-fitting bounds walks a long counter directly —
            // no iterator allocation, no virtual MoveNext per step. Quarter-range keeps += overflow-free.
            if (iterable.Kind == ValueKind.Range && n.Target.NodeKind == NodeKind.Name)
            {
                RangeValue r = (RangeValue)iterable;
                const long Lo = long.MinValue / 4, Hi = long.MaxValue / 4;
                if (r.Start >= Lo && r.Start <= Hi && r.Stop >= Lo && r.Stop <= Hi && r.Step >= Lo && r.Step <= Hi)
                    return ExecForRange(n, (NameNode)n.Target, (long)r.Start, (long)r.Stop, (long)r.Step, env, ctx);
            }
            IScriptIterator it = PyOps.GetIterator(iterable, ctx);
            bool brokeOut = false;
            ScriptValue item;
            while (true)
            {
                ctx.Budget.Step(BudgetCost.BackEdge);
                if (!it.MoveNext(ctx, out item))
                {
                    break;
                }
                AssignTo(n.Target, item, env, ctx);
                ExecSignal sig = ExecBlock(n.Body, env, ctx);
                if (sig.Kind == ExecSignalKind.Break)
                {
                    brokeOut = true;
                    break;
                }
                if (sig.Kind == ExecSignalKind.Continue)
                {
                    continue;
                }
                if (!sig.IsNormal)
                {
                    return sig;   // Return or Raised
                }
            }
            if (!brokeOut)
            {
                ExecSignal orelse = ExecBlock(n.OrElse, env, ctx);
                if (!orelse.IsNormal)
                {
                    return orelse;
                }
            }
            return ExecSignal.Normal;
        }

        // Long twin of the general ExecFor loop. Budget cadence is identical: one BackEdge step per
        // iteration INCLUDING the final failed test (the general loop charges before MoveNext returns false).
        private ExecSignal ExecForRange(ForNode n, NameNode target, long cur, long stop, long step,
            Environment env, EvalContext ctx)
        {
            bool up = step > 0;
            bool brokeOut = false;
            while (true)
            {
                ctx.Budget.Step(BudgetCost.BackEdge);
                if (up ? cur >= stop : cur <= stop)
                {
                    break;
                }
                SetName(target, ctx.Values.Int(cur), env);
                ExecSignal sig = ExecBlock(n.Body, env, ctx);
                if (sig.Kind == ExecSignalKind.Break)
                {
                    brokeOut = true;
                    break;
                }
                if (sig.Kind == ExecSignalKind.Return || sig.Kind == ExecSignalKind.Raised)
                {
                    return sig;
                }
                cur += step;   // Continue and Normal both advance
            }
            if (!brokeOut)
            {
                ExecSignal orelse = ExecBlock(n.OrElse, env, ctx);
                if (!orelse.IsNormal)
                {
                    return orelse;
                }
            }
            return ExecSignal.Normal;
        }

        private ExecSignal ExecReturn(ReturnNode n, Environment env, EvalContext ctx)
        {
            ScriptValue v = n.Value == null ? ctx.Values.None : EvalExpr(n.Value, env, ctx);
            return ExecSignal.Return(v);
        }

        private ExecSignal ExecAssert(AssertNode n, Environment env, EvalContext ctx)
        {
            if (PyOps.Truth(EvalExpr(n.Test, env, ctx), ctx))
            {
                return ExecSignal.Normal;
            }
            if (n.Msg == null)
            {
                throw Raise.Make(ctx, PyExceptionTypes.AssertionError, System.Array.Empty<ScriptValue>());
            }
            ScriptValue msg = EvalExpr(n.Msg, env, ctx);
            throw Raise.Make(ctx, PyExceptionTypes.AssertionError, new[] { msg });
        }
    }
}

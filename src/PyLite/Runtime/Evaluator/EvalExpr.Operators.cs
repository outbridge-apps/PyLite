using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Runtime.Evaluator
{
    // Operators and comparison chains. Strict left-to-right evaluation
    // and/or/if-exp short-circuit; comparison chains evaluate each operand exactly once.
    internal sealed partial class Evaluator
    {
        // The operator semantics every tier shares; a tier only changes how the operands are produced.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static ScriptValue ApplyBinary(PyBinOp op, ScriptValue a, ScriptValue b, EvalContext ctx)
        {
            // Small-int shortcut: same IntOpSmall the full path lands in, minus the dispatch layers.
            return PyOps.TrySmallIntBinOp(op, a, b, ctx) ?? PyOps.BinaryOp(op, a, b, ctx);
        }

        internal static ScriptValue ApplyUnary(UnaryOp op, ScriptValue a, EvalContext ctx)
        {
            switch (op)
            {
                case UnaryOp.Not: return ctx.Values.Bool(!PyOps.Truth(a, ctx));
                case UnaryOp.USub: return PyOps.UnaryOp(PyUnaryOp.Neg, a, ctx);
                case UnaryOp.UAdd: return PyOps.UnaryOp(PyUnaryOp.Pos, a, ctx);
                case UnaryOp.Invert: return PyOps.UnaryOp(PyUnaryOp.Invert, a, ctx);
                default: throw new System.InvalidOperationException("unhandled unary op " + op);   // parser can't produce this
            }
        }

        private ScriptValue EvalBinOp(BinOpNode n, Environment env, EvalContext ctx)
        {
            ScriptValue a = EvalExpr(n.Left, env, ctx);
            return ApplyBinary(MapBin(n.Op), a, EvalExpr(n.Right, env, ctx), ctx);
        }

        private ScriptValue EvalUnaryOp(UnaryOpNode n, Environment env, EvalContext ctx)
        {
            return ApplyUnary(n.Op, EvalExpr(n.Operand, env, ctx), ctx);
        }

        private ScriptValue EvalBoolOp(BoolOpNode n, Environment env, EvalContext ctx)
        {
            ScriptValue v = null;
            bool isAnd = n.Op == BoolOp.And;
            for (int i = 0; i < n.Values.Count; i++)
            {
                v = EvalExpr(n.Values[i], env, ctx);
                if (PyOps.Truth(v, ctx) != isAnd)
                    return v;   // and: the first falsy; or: the first truthy
            }
            return v;   // the last operand
        }

        private ScriptValue EvalIfExp(IfExpNode n, Environment env, EvalContext ctx)
        {
            if (PyOps.Truth(EvalExpr(n.Test, env, ctx), ctx))
            {
                return EvalExpr(n.Body, env, ctx);
            }
            return EvalExpr(n.OrElse, env, ctx);
        }

        private ScriptValue EvalCompare(CompareChainNode n, Environment env, EvalContext ctx)
        {
            ScriptValue left = EvalExpr(n.Left, env, ctx);
            for (int i = 0; i < n.Ops.Count; i++)
            {
                ScriptValue right = EvalExpr(n.Comparators[i], env, ctx);
                if (!CompareOne(n.Ops[i], left, right, ctx))
                {
                    return ctx.Values.Bool(false);   // short-circuit: the rest is not evaluated
                }
                left = right;
            }
            return ctx.Values.Bool(true);
        }

        private bool CompareOne(CompareOp op, ScriptValue a, ScriptValue b, EvalContext ctx)
        {
            // Small-int shortcut for the ordering/equality ops (identity and membership keep their own
            // semantics below). RichCompare on Int x Int reduces to exactly these long comparisons.
            if (a.Kind == ValueKind.Int && b.Kind == ValueKind.Int && op <= CompareOp.Ne)
            {
                IntValue ia = (IntValue)a, ib = (IntValue)b;
                if (ia.IsSmall && ib.IsSmall)
                    return LongCompare(op, ia.Small, ib.Small);
            }
            switch (op)
            {
                // Eq/Ne route through RichCompare for the operator-level NaN guard (nan == nan is False
                // even for the same instance; the Equals engine keeps identity shortcuts for containers).
                case CompareOp.Eq: return PyOps.Truth(PyOps.RichCompare(PyCmpOp.Eq, a, b, ctx), ctx);
                case CompareOp.Ne: return PyOps.Truth(PyOps.RichCompare(PyCmpOp.Ne, a, b, ctx), ctx);
                case CompareOp.Lt: return PyOps.Truth(PyOps.RichCompare(PyCmpOp.Lt, a, b, ctx), ctx);
                case CompareOp.Le: return PyOps.Truth(PyOps.RichCompare(PyCmpOp.Le, a, b, ctx), ctx);
                case CompareOp.Gt: return PyOps.Truth(PyOps.RichCompare(PyCmpOp.Gt, a, b, ctx), ctx);
                case CompareOp.Ge: return PyOps.Truth(PyOps.RichCompare(PyCmpOp.Ge, a, b, ctx), ctx);
                case CompareOp.In: return PyOps.Contains(a, b, ctx);
                case CompareOp.NotIn: return !PyOps.Contains(a, b, ctx);
                case CompareOp.Is: return ReferenceEquals(a, b);
                default: return !ReferenceEquals(a, b);   // CompareOp.IsNot
            }
        }
    }
}

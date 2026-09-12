using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Runtime.Evaluator
{
    // Operators and comparison chains. Strict left-to-right evaluation
    // and/or/if-exp short-circuit; comparison chains evaluate each operand exactly once.
    internal sealed partial class Evaluator
    {
        private ScriptValue EvalBinOp(BinOpNode n, Environment env, EvalContext ctx)
        {
            ScriptValue a = EvalExpr(n.Left, env, ctx);
            ScriptValue b = EvalExpr(n.Right, env, ctx);
            PyBinOp op = MapBin(n.Op);
            // Small-int shortcut: same IntOpSmall the full path lands in, minus the dispatch layers.
            ScriptValue fast = PyOps.TrySmallIntBinOp(op, a, b, ctx);
            if (fast != null)
            {
                return fast;
            }
            return PyOps.BinaryOp(op, a, b, ctx);
        }

        private ScriptValue EvalUnaryOp(UnaryOpNode n, Environment env, EvalContext ctx)
        {
            ScriptValue a = EvalExpr(n.Operand, env, ctx);
            switch (n.Op)
            {
                case UnaryOp.Not: return ctx.Values.Bool(!PyOps.Truth(a, ctx));
                case UnaryOp.USub: return PyOps.UnaryOp(PyUnaryOp.Neg, a, ctx);
                case UnaryOp.UAdd: return PyOps.UnaryOp(PyUnaryOp.Pos, a, ctx);
                case UnaryOp.Invert: return PyOps.UnaryOp(PyUnaryOp.Invert, a, ctx);
                default: throw new System.InvalidOperationException("unhandled unary op " + n.Op);   // parser can't produce this
            }
        }

        private ScriptValue EvalBoolOp(BoolOpNode n, Environment env, EvalContext ctx)
        {
            ScriptValue v = null;
            bool isAnd = n.Op == BoolOp.And;
            for (int i = 0; i < n.Values.Count; i++)
            {
                v = EvalExpr(n.Values[i], env, ctx);
                bool t = PyOps.Truth(v, ctx);
                if (isAnd && !t)
                {
                    return v;   // and: first falsy short-circuits
                }
                if (!isAnd && t)
                {
                    return v;   // or: first truthy short-circuits
                }
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
            if (a.Kind == ValueKind.Int && b.Kind == ValueKind.Int)
            {
                IntValue ia = (IntValue)a, ib = (IntValue)b;
                if (ia.IsSmall && ib.IsSmall)
                {
                    long x = ia.Small, y = ib.Small;
                    switch (op)
                    {
                        case CompareOp.Eq: return x == y;
                        case CompareOp.Ne: return x != y;
                        case CompareOp.Lt: return x < y;
                        case CompareOp.Le: return x <= y;
                        case CompareOp.Gt: return x > y;
                        case CompareOp.Ge: return x >= y;
                    }
                }
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

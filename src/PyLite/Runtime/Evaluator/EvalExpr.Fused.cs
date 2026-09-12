using System.Numerics;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Runtime.Evaluator
{
    // Superinstruction fusion (T2.3). A comprehension body/condition that is pure machine-int
    // arithmetic over the loop variable and int literals fuses into ONE delegate computing in C#
    // longs — no intermediate ScriptValue boxing, no slot reads, no per-node dispatch. Semantics
    // hold by construction: every operator defers to NumericOps.TryLongBinOp (the exact long core
    // the interpreted path runs first anyway) and ANY undecidable case (overflow, zero divisor,
    // bad shift) returns false — the drain then re-runs THAT element through the general path,
    // whose results, errors and charges are canonical. Fused elements skip the interpreted path's
    // intermediate-value allocation charges (the Tier 2 precedent: the drain's per-element
    // Budget.Step and the final-result charges keep every bomb bounded — ResourceBound battery).
    // Delegates capture only operator codes, literal constants and child delegates, so the
    // benign-race AST cache pattern applies. Callers gate on ctx.Limits.MaxIntBits >= 128
    // (IntOpSmall's own threshold) to keep the big path's estimate-based aborts reachable.
    internal delegate bool FusedLong(long x, out long r);   // false => redo this element generally
    internal delegate bool FusedCond(long x, out bool r);

    // Odometer (genexp / multi-clause) twins: the clause walk has already written every loop
    // variable into its slot, so the wrapper reads the expression's SOLE referenced local from the
    // scope (a machine int drives the fused core; bool/float/str/big/unbound fall back — an unbound
    // read then raises the canonical UnboundLocalError on the general path). Expressions touching
    // two or more locals do not fuse — the compiled path owns them.
    internal delegate bool FusedScopeLong(Environment scope, out long r);
    internal delegate bool FusedScopeCond(Environment scope, out bool r);

    internal sealed partial class Evaluator
    {
        internal static FusedLong GetFusedLong(ref object cache, ExprNode e, int xSlot)
        {
            object c = cache;
            if (c == null)
            {
                FusedLong f = TryFuseLong(e, xSlot);
                cache = c = f != null ? (object)f : NotCompilable;
            }
            return c as FusedLong;
        }

        internal static FusedCond GetFusedCond(ref object cache, ExprNode e, int xSlot)
        {
            object c = cache;
            if (c == null)
            {
                FusedCond f = TryFuseCond(e, xSlot);
                cache = c = f != null ? (object)f : NotCompilable;
            }
            return c as FusedCond;
        }

        // Odometer accessors. They share the AST cache fields with the flat-drain accessors — safe
        // because a comprehension node is drained by exactly ONE path (genexp/multi-clause = the
        // odometer, single-for list/set/dict = flat), so a field only ever holds that path's shape.
        internal static FusedScopeLong GetFusedScopeLong(ref object cache, ExprNode e)
        {
            object c = cache;
            if (c == null)
            {
                FusedScopeLong f = TryFuseScopeLong(e);
                cache = c = f != null ? (object)f : NotCompilable;
            }
            return c as FusedScopeLong;
        }

        internal static FusedScopeCond GetFusedScopeCond(ref object cache, ExprNode e)
        {
            object c = cache;
            if (c == null)
            {
                FusedScopeCond f = TryFuseScopeCond(e);
                cache = c = f != null ? (object)f : NotCompilable;
            }
            return c as FusedScopeCond;
        }

        private static FusedScopeLong TryFuseScopeLong(ExprNode e)
        {
            // A bare loop variable gains nothing here: the compiled path reuses the boxed slot
            // value directly, while the wrapper would unbox and RE-box it (measured net loss).
            if (e.NodeKind == NodeKind.Name)
                return null;
            int slot = FindSoleLocalSlot(e);
            if (slot == Conflict)
                return null;
            FusedLong f = TryFuseLong(e, slot >= 0 ? slot : 0);
            if (f == null)
                return null;
            if (slot < 0)   // constant expression: nothing to read
                return (Environment scope, out long r) => f(0, out r);
            return (Environment scope, out long r) =>
            {
                ScriptValue v = scope.PeekLocal(slot);
                if (v != null && v.Kind == ValueKind.Int)
                {
                    IntValue iv = (IntValue)v;
                    if (iv.IsSmall)
                        return f(iv.Small, out r);
                }
                r = 0;
                return false;   // bool/float/str/big/unbound: the general path owns this element
            };
        }

        private static FusedScopeCond TryFuseScopeCond(ExprNode e)
        {
            int slot = FindSoleLocalSlot(e);
            if (slot == Conflict)
                return null;
            FusedCond f = TryFuseCond(e, slot >= 0 ? slot : 0);
            if (f == null)
                return null;
            if (slot < 0)
                return (Environment scope, out bool r) => f(0, out r);
            return (Environment scope, out bool r) =>
            {
                ScriptValue v = scope.PeekLocal(slot);
                if (v != null && v.Kind == ValueKind.Int)
                {
                    IntValue iv = (IntValue)v;
                    if (iv.IsSmall)
                        return f(iv.Small, out r);
                }
                r = false;
                return false;
            };
        }

        // The single Local slot an expression reads: NoName when it references no name at all
        // (a constant shape), Conflict when it touches two different locals or any non-Local /
        // non-fusable node — those never fuse against one variable.
        private const int NoName = -2;
        private const int Conflict = -3;

        private static int FindSoleLocalSlot(ExprNode e)
        {
            switch (e.NodeKind)
            {
                case NodeKind.Literal:
                case NodeKind.ConstFolded:
                    return NoName;
                case NodeKind.Name:
                {
                    NameBinding b = ((NameNode)e).Binding;
                    return b != null && b.Kind == NameKind.Local ? b.Slot : Conflict;
                }
                case NodeKind.BinOp:
                {
                    BinOpNode b = (BinOpNode)e;
                    return MergeSlots(FindSoleLocalSlot(b.Left), FindSoleLocalSlot(b.Right));
                }
                case NodeKind.UnaryOp:
                    return FindSoleLocalSlot(((UnaryOpNode)e).Operand);
                case NodeKind.IfExp:
                {
                    IfExpNode ie = (IfExpNode)e;
                    return MergeSlots(FindSoleLocalSlot(ie.Test),
                        MergeSlots(FindSoleLocalSlot(ie.Body), FindSoleLocalSlot(ie.OrElse)));
                }
                case NodeKind.CompareChain:
                {
                    CompareChainNode c = (CompareChainNode)e;
                    int s = FindSoleLocalSlot(c.Left);
                    for (int i = 0; i < c.Comparators.Count && s != Conflict; i++)
                        s = MergeSlots(s, FindSoleLocalSlot(c.Comparators[i]));
                    return s;
                }
                case NodeKind.BoolOp:
                {
                    BoolOpNode bo = (BoolOpNode)e;
                    int s = NoName;
                    for (int i = 0; i < bo.Values.Count && s != Conflict; i++)
                        s = MergeSlots(s, FindSoleLocalSlot(bo.Values[i]));
                    return s;
                }
                default:
                    return Conflict;   // outside the fuse whitelist anyway
            }
        }

        private static int MergeSlots(int a, int b)
        {
            if (a == Conflict || b == Conflict)
                return Conflict;
            if (a == NoName)
                return b;
            if (b == NoName || a == b)
                return a;
            return Conflict;
        }

        // Int-valued fusion. Only shapes that the interpreted path evaluates to ints for EVERY
        // small-int x are admitted (bools/floats/strings change result TYPES — excluded), so a
        // successful fused element is bit-identical to the tree-walk.
        private static FusedLong TryFuseLong(ExprNode e, int xSlot)
        {
            switch (e.NodeKind)
            {
                case NodeKind.Literal:
                    return FuseIntLiteral((LiteralNode)e);
                case NodeKind.ConstFolded:
                    return FuseIntLiteral(((ConstFoldedNode)e).Folded);
                case NodeKind.Name:
                {
                    NameBinding b = ((NameNode)e).Binding;
                    if (b == null || b.Kind != NameKind.Local || b.Slot != xSlot)
                        return null;   // only the loop variable: anything else can hold any value
                    return (long x, out long r) => { r = x; return true; };
                }
                case NodeKind.BinOp:
                {
                    BinOpNode b = (BinOpNode)e;
                    PyBinOp op = MapBin(b.Op);
                    switch (op)
                    {
                        case PyBinOp.Add:
                        case PyBinOp.Sub:
                        case PyBinOp.Mul:
                        case PyBinOp.FloorDiv:
                        case PyBinOp.Mod:
                        case PyBinOp.LShift:
                        case PyBinOp.RShift:
                        case PyBinOp.BitAnd:
                        case PyBinOp.BitOr:
                        case PyBinOp.BitXor:
                        case PyBinOp.Pow:
                            break;
                        default:
                            return null;   // TrueDiv yields floats; anything else is exotic
                    }
                    FusedLong fa = TryFuseLong(b.Left, xSlot);
                    FusedLong fb = TryFuseLong(b.Right, xSlot);
                    if (fa == null || fb == null)
                        return null;
                    return (long x, out long r) =>
                    {
                        long va, vb;
                        if (fa(x, out va) && fb(x, out vb))
                            return NumericOps.TryLongBinOp(op, va, vb, out r);
                        r = 0;
                        return false;
                    };
                }
                case NodeKind.UnaryOp:
                {
                    UnaryOpNode u = (UnaryOpNode)e;
                    FusedLong fo = TryFuseLong(u.Operand, xSlot);
                    if (fo == null)
                        return null;
                    switch (u.Op)
                    {
                        case UnaryOp.USub:
                            return (long x, out long r) =>
                            {
                                long v;
                                if (fo(x, out v) && v != long.MinValue)   // -MinValue promotes to big
                                {
                                    r = -v;
                                    return true;
                                }
                                r = 0;
                                return false;
                            };
                        case UnaryOp.UAdd:
                            return fo;   // +int is the identity
                        case UnaryOp.Invert:
                            return (long x, out long r) =>
                            {
                                long v;
                                if (fo(x, out v))
                                {
                                    r = ~v;   // two's complement ~a == -a-1, never overflows
                                    return true;
                                }
                                r = 0;
                                return false;
                            };
                        default:
                            return null;   // `not` yields bool
                    }
                }
                case NodeKind.IfExp:
                {
                    IfExpNode ie = (IfExpNode)e;
                    FusedCond ft = TryFuseCond(ie.Test, xSlot);
                    FusedLong fb2 = TryFuseLong(ie.Body, xSlot);
                    FusedLong fe = TryFuseLong(ie.OrElse, xSlot);
                    if (ft == null || fb2 == null || fe == null)
                        return null;
                    return (long x, out long r) =>
                    {
                        bool t;
                        if (!ft(x, out t))
                        {
                            r = 0;
                            return false;
                        }
                        return t ? fb2(x, out r) : fe(x, out r);
                    };
                }
                default:
                    return null;
            }
        }

        private static FusedLong FuseIntLiteral(LiteralNode lit)
        {
            if (lit.Kind != LiteralKind.Int)
                return null;   // bool literals stay bools (True is not the int 1 for repr/type)
            BigInteger v = (BigInteger)lit.Value;
            if (v < long.MinValue || v > long.MaxValue)
                return null;
            long c = (long)v;
            return (long x, out long r) => { r = c; return true; };
        }

        // Truth-valued fusion for conditions: comparisons/and/or/not over fused-long operands, or
        // the nonzero-truth of a fused-long expression. Result is Python's Truth of the interpreted
        // value (identical short-circuit order), never a ScriptValue.
        private static FusedCond TryFuseCond(ExprNode e, int xSlot)
        {
            switch (e.NodeKind)
            {
                case NodeKind.CompareChain:
                {
                    CompareChainNode c = (CompareChainNode)e;
                    FusedLong fl = TryFuseLong(c.Left, xSlot);
                    if (fl == null)
                        return null;
                    var fcs = new FusedLong[c.Comparators.Count];
                    for (int i = 0; i < fcs.Length; i++)
                    {
                        switch (c.Ops[i])
                        {
                            case CompareOp.Eq:
                            case CompareOp.Ne:
                            case CompareOp.Lt:
                            case CompareOp.Le:
                            case CompareOp.Gt:
                            case CompareOp.Ge:
                                break;
                            default:
                                return null;   // is/in keep identity/membership semantics
                        }
                        fcs[i] = TryFuseLong(c.Comparators[i], xSlot);
                        if (fcs[i] == null)
                            return null;
                    }
                    CompareChainNode chain = c;
                    return (long x, out bool r) =>
                    {
                        r = false;
                        long left, right;
                        if (!fl(x, out left))
                            return false;
                        for (int i = 0; i < fcs.Length; i++)
                        {
                            if (!fcs[i](x, out right))
                                return false;
                            if (!LongCompare(chain.Ops[i], left, right))
                            {
                                return true;   // short-circuit: the rest not evaluated (as interpreted)
                            }
                            left = right;
                        }
                        r = true;
                        return true;
                    };
                }
                case NodeKind.BoolOp:
                {
                    BoolOpNode bo = (BoolOpNode)e;
                    var fs = new FusedCond[bo.Values.Count];
                    for (int i = 0; i < fs.Length; i++)
                    {
                        fs[i] = TryFuseCond(bo.Values[i], xSlot);
                        if (fs[i] == null)
                            return null;
                    }
                    bool isAnd = bo.Op == BoolOp.And;
                    // Truth(a and b) == Truth(a) && Truth(b) with the same short-circuit order.
                    return (long x, out bool r) =>
                    {
                        r = isAnd;
                        for (int i = 0; i < fs.Length; i++)
                        {
                            bool t;
                            if (!fs[i](x, out t))
                                return false;
                            if (isAnd && !t)
                            {
                                r = false;
                                return true;
                            }
                            if (!isAnd && t)
                            {
                                r = true;
                                return true;
                            }
                        }
                        return true;
                    };
                }
                case NodeKind.UnaryOp:
                {
                    UnaryOpNode u = (UnaryOpNode)e;
                    if (u.Op != UnaryOp.Not)
                        break;   // -x/~x as a condition: nonzero truth below
                    FusedCond fo = TryFuseCond(u.Operand, xSlot);
                    if (fo == null)
                        return null;
                    return (long x, out bool r) =>
                    {
                        bool t;
                        if (fo(x, out t))
                        {
                            r = !t;
                            return true;
                        }
                        r = false;
                        return false;
                    };
                }
            }
            // Truth of a pure-int expression (`if x % 3:`): nonzero.
            FusedLong fv = TryFuseLong(e, xSlot);
            if (fv == null)
                return null;
            return (long x, out bool r) =>
            {
                long v;
                if (fv(x, out v))
                {
                    r = v != 0;
                    return true;
                }
                r = false;
                return false;
            };
        }

        // Exactly CompareOne's small-int shortcut — Int x Int RichCompare reduces to these.
        private static bool LongCompare(CompareOp op, long a, long b)
        {
            switch (op)
            {
                case CompareOp.Eq: return a == b;
                case CompareOp.Ne: return a != b;
                case CompareOp.Lt: return a < b;
                case CompareOp.Le: return a <= b;
                case CompareOp.Gt: return a > b;
                default: return a >= b;   // Ge — the only op left through the fuse whitelist
            }
        }
    }
}

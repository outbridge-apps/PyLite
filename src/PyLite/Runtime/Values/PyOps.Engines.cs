using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // Iterative hashing / equality / ordering engines. Explicit stacks only
    // no C# recursion over user data except the reentrant key-Equals channel (R3).
    public static partial class PyOps
    {
        // -------- Hash (tuple engine, CPython tuplehash) --------

        public static long Hash(ScriptValue v, EvalContext ctx, int depth)
        {
            StackProbe(ctx);
            if (depth > ctx.Limits.MaxDataDepth)
                throw Raise.RecursionError(ctx, "maximum recursion depth exceeded in comparison");
            if (v.Kind != ValueKind.Tuple)
                return v.HashLeafCore(ctx, depth);

            var stack = new Stack<HashFrame>();
            stack.Push(new HashFrame((TupleValue)v));
            while (true)
            {
                HashFrame f = stack.Peek();
                ctx.Budget.Step();
                if (f.I == f.T.Items.Length)
                {
                    long r = unchecked((long)(f.X + 97531UL));
                    if (r == -1L)
                        r = -2L;
                    stack.Pop();
                    if (stack.Count == 0)
                        return r;
                    HashFrame p = stack.Peek();
                    p.Combine(r);
                    p.I++;
                }
                else
                {
                    ScriptValue child = f.T.Items[f.I];
                    if (child.Kind == ValueKind.Tuple)
                    {
                        if (stack.Count + depth >= ctx.Limits.MaxDataDepth)
                            throw Raise.RecursionError(ctx, "maximum recursion depth exceeded in comparison");
                        stack.Push(new HashFrame((TupleValue)child));
                    }
                    else
                    {
                        f.Combine(child.HashLeafCore(ctx, depth + stack.Count));
                        f.I++;
                    }
                }
            }
        }

        private sealed class HashFrame
        {
            public readonly TupleValue T;
            public int I;
            public ulong X;
            public ulong Mult;

            public HashFrame(TupleValue t) { T = t; X = 0x345678UL; Mult = 1000003UL; }

            public void Combine(long y)
            {
                unchecked
                {
                    X = (X ^ (ulong)y) * Mult;
                    Mult = Mult + (ulong)(82520 + 2 * (T.Items.Length - I - 1));
                }
            }
        }

        // -------- Equals (iterative, list/tuple descend via explicit stack) --------

        public static bool Equals(ScriptValue a, ScriptValue b, EvalContext ctx, int depth)
        {
            StackProbe(ctx);
            if (depth > ctx.Limits.MaxDataDepth)
                throw Raise.RecursionError(ctx, "maximum recursion depth exceeded in comparison");

            Stack<EqFrame> stack = null;
            ScriptValue x = a, y = b;
            while (true)
            {
                ctx.Budget.Step();
                bool result;
                EqFrame newFrame = null;
                if (ReferenceEquals(x, y))
                    result = true;
                else if (NumericOps.IsNumber(x) && NumericOps.IsNumber(y))
                    result = NumericOps.NumericEquals(x, y);
                else if (x.Kind == ValueKind.Str && y.Kind == ValueKind.Str)
                {
                    string sx = ((StrValue)x).Value, sy = ((StrValue)y).Value;
                    result = sx.Length == sy.Length && string.Equals(sx, sy, StringComparison.Ordinal);
                }
                else if (BothSeq(x, y))
                {
                    int cx = SeqCount(x), cy = SeqCount(y);
                    if (cx != cy)
                        result = false;
                    else if (cx == 0)
                        result = true;
                    else
                    {
                        newFrame = new SeqFrame(x, y, cx);
                        result = false;
                    }
                }
                else if (x is OrderedDictValue && y is OrderedDictValue)
                {
                    // OrderedDict == OrderedDict is order-sensitive; OD vs plain dict falls to the
                    // order-insensitive dict path below.
                    result = OrderedDictValue.OrderedEquals((OrderedDictValue)x, (OrderedDictValue)y, ctx, depth);
                }
                else if (x.Kind == ValueKind.Dict && y.Kind == ValueKind.Dict)
                {
                    if (((DictValue)x).Count != ((DictValue)y).Count)
                        result = false;
                    else if (((DictValue)x).Count == 0)
                        result = true;
                    else
                    {
                        newFrame = new DictFrame((DictValue)x, (DictValue)y);
                        result = false;
                    }
                }
                else if (IsSetFamily(x) && IsSetFamily(y))
                {
                    SetValue sx = (SetValue)x, sy = (SetValue)y;
                    result = sx.Count == sy.Count && SetOps.AllContained(sx, sy, ctx, depth);
                }
                else if (x.Kind != y.Kind)
                {
                    // A leaf hook gets the nesting it is called at, so one that re-enters the engine
                    // (a record's fields, a deque's items) continues the count instead of restarting it.
                    int d = depth + (stack == null ? 0 : stack.Count);
                    bool crossEq;
                    if (x.TryEqualsAcrossKindCore(y, ctx, d, out crossEq) || y.TryEqualsAcrossKindCore(x, ctx, d, out crossEq))
                        result = crossEq;
                    else
                        result = false;
                }
                else
                    result = x.EqualsLeafCore(y, ctx, depth + (stack == null ? 0 : stack.Count));
                if (newFrame != null)
                {
                    if (stack == null)
                        stack = new Stack<EqFrame>();
                    if (stack.Count + depth >= ctx.Limits.MaxDataDepth)
                        throw Raise.RecursionError(ctx, "maximum recursion depth exceeded in comparison");
                    ctx.Budget.ChargeAllocation(32);
                    bool mismatch = false;
                    if (newFrame.TryNext(ctx, depth, out x, out y, ref mismatch))
                    {
                        stack.Push(newFrame);
                        continue;
                    }

                    if (mismatch)
                        return false;
                    result = true; // container with no comparable children -> equal
                }

                // fold `result` up through the parent frames
                while (true)
                {
                    if (stack == null || stack.Count == 0)
                        return result;
                    if (!result)
                        return false;
                    EqFrame f = stack.Peek();
                    bool mismatch = false;
                    if (f.TryNext(ctx, depth, out x, out y, ref mismatch))
                        break; // next child pair
                    if (mismatch)
                        return false;
                    stack.Pop();
                    result = true; // frame exhausted -> matched
                }
            }
        }

        // A frame yields successive child pairs; TryNext returns false when exhausted, and sets
        // mismatch=true for a structural miss (dict key absent in the other operand).
        private abstract class EqFrame
        {
            public abstract bool TryNext(EvalContext ctx, int depth, out ScriptValue cx, out ScriptValue cy, ref bool mismatch);
        }

        private sealed class SeqFrame : EqFrame
        {
            private readonly ScriptValue _a, _b;
            private readonly int _count;
            private int _i;
            public SeqFrame(ScriptValue a, ScriptValue b, int count) { _a = a; _b = b; _count = count; }

            public override bool TryNext(EvalContext ctx, int depth, out ScriptValue cx, out ScriptValue cy, ref bool mismatch)
            {
                if (_i < _count)
                {
                    // A user __eq__ running below this frame can mutate the lists mid-compare; a
                    // shrink past the captured count is "unequal", never an out-of-range fault.
                    if (_i >= SeqCount(_a) || _i >= SeqCount(_b))
                    {
                        mismatch = true;
                        cx = null;
                        cy = null;
                        return false;
                    }
                    cx = SeqItem(_a, _i);
                    cy = SeqItem(_b, _i);
                    _i++;
                    return true;
                }
                cx = null; cy = null; return false;
            }
        }

        private sealed class DictFrame : EqFrame
        {
            private readonly DictValue _x, _y;
            private int _pos;
            public DictFrame(DictValue x, DictValue y) { _x = x; _y = y; }

            public override bool TryNext(EvalContext ctx, int depth, out ScriptValue cx, out ScriptValue cy, ref bool mismatch)
            {
                while (_pos < _x.Table.EntriesUsed)
                {
                    long h;
                    ScriptValue k, vx;
                    bool live = _x.Table.TryGetEntryAt(_pos, out h, out k, out vx);
                    _pos++;
                    if (!live)
                        continue;
                    ScriptValue vy;
                    if (!_y.Table.TryGetValue(k, h, ctx, depth + 1, out vy))
                    {
                        mismatch = true;
                        cx = null;
                        cy = null;
                        return false;
                    }

                    cx = vx;
                    cy = vy;
                    return true;
                }
                cx = null; cy = null; return false;
            }
        }

        // -------- RichCompare / ordering --------

        public static ScriptValue RichCompare(PyCmpOp op, ScriptValue a, ScriptValue b, EvalContext ctx)
        {
            return RichCompare(op, a, b, ctx, 0);
        }

        // depth: the nesting a leaf hook re-enters at (a deque or record ordering its items).
        public static ScriptValue RichCompare(PyCmpOp op, ScriptValue a, ScriptValue b, EvalContext ctx, int depth)
        {
            if (op == PyCmpOp.Eq || op == PyCmpOp.Ne)
            {
                // IEEE NaN: nan == anything is False at the OPERATOR level, even for the same object
                // (exposed by the shared math.nan instance). The identity-or-equal shortcut stays in the
                // Equals engine for container/membership paths — nan in [nan] is True (CPython parity).
                if (IsNaNFloat(a) || IsNaNFloat(b))
                    return ctx.Values.Bool(op == PyCmpOp.Ne);
                // An explicit __ne__ answers != on its own, as in CPython; without one, != is the
                // inversion of Equals, which is also CPython's default __ne__ (the Equals engine
                // consults __eq__ on either operand). Only the OPERATOR consults __ne__: membership
                // and container comparison go through __eq__, there as here.
                bool ne;
                if (op == PyCmpOp.Ne && TryRecordDunder(PyCmpOp.Ne, a, b, ctx, out ne))
                    return ctx.Values.Bool(ne);
                return ctx.Values.Bool(op == PyCmpOp.Eq ? Equals(a, b, ctx, depth) : !Equals(a, b, ctx, depth));
            }
            return ctx.Values.Bool(Order(op, a, b, ctx, depth));
        }

        private static bool IsNaNFloat(ScriptValue v)
        {
            FloatValue f = v as FloatValue;
            return f != null && double.IsNaN(f.Value);
        }

        private static bool Order(PyCmpOp op, ScriptValue a, ScriptValue b, EvalContext ctx, int depth)
        {
            while (true) // tail iteration into the first unequal element pair (no C# recursion)
            {
                ctx.Budget.Step();
                if (NumericOps.IsNumber(a) && NumericOps.IsNumber(b))
                    return NumericOps.NumericCompareOp(op, a, b);
                if (a.Kind == ValueKind.Str && b.Kind == ValueKind.Str)
                    return NumericOps.ApplyCmp(op, string.CompareOrdinal(((StrValue)a).Value, ((StrValue)b).Value));
                if (BothSeq(a, b))
                {
                    int la = SeqCount(a), lb = SeqCount(b), n = Math.Min(la, lb);
                    int firstUnequal = -1;
                    for (int i = 0; i < n; i++)
                    {
                        ctx.Budget.Step();
                        if (!Equals(SeqItem(a, i), SeqItem(b, i), ctx, depth))
                        {
                            firstUnequal = i;
                            break;
                        }
                    }

                    if (firstUnequal >= 0)
                    {
                        a = SeqItem(a, firstUnequal);
                        b = SeqItem(b, firstUnequal);
                        depth++;
                        continue;
                    }

                    return NumericOps.ApplyCmp(op, la.CompareTo(lb));
                }

                CounterValue ca = a as CounterValue, cb = b as CounterValue;
                if (ca != null && cb != null)
                    return CounterValue.Inclusion(op, ca, cb, ctx);

                if (SetOps.IsSetLike(a) && SetOps.IsSetLike(b))
                    return SetOps.OrderLike(op, a, b, ctx);   // subset ordering: set, frozenset, dict keys/items

                // Opaque values (e.g. functools.cmp_to_key wrappers, Fraction/Decimal) order via their leaf hook;
                // fall back to the reflected hook (negated) so `2 < Fraction(5,2)` works with int on the left.
                // A NaN operand orders as False against every numeric (Fraction/Decimal included), never as an error.
                if (IsNaNFloat(a) || IsNaNFloat(b))
                    return false;

                // A record's own rich comparison, before the generated @dataclass(order=True) ordering.
                bool rich;
                if (TryRecordDunder(op, a, b, ctx, out rich))
                    return rich;

                int leaf;
                if (a.TryCompareLeafCore(b, ctx, depth, out leaf))
                    return NumericOps.ApplyCmp(op, leaf);
                if (b.TryCompareLeafCore(a, ctx, depth, out leaf))
                    return NumericOps.ApplyCmp(op, -leaf);

                throw Raise.TypeError(ctx, "'" + Sym(op) + "' not supported between instances of '"
                    + a.PyTypeName + "' and '" + b.PyTypeName + "'");
            }
        }

        // The left operand's comparison method, else the right operand's reflected one. CPython reaches
        // the reflected direction when the left returns NotImplemented; the dialect has no such value,
        // so a missing method plays that part. Falling through leaves the existing paths untouched.
        private static bool TryRecordDunder(PyCmpOp op, ScriptValue a, ScriptValue b, EvalContext ctx, out bool result)
        {
            result = false;
            RecordInstanceValue ra = a as RecordInstanceValue;
            FunctionValue f = ra == null ? null : ra.Class.FindMethod(CmpDunder(op));
            if (f != null)
            {
                result = Truth(ctx.CallHook(f, new ScriptValue[] { a, b }, Evaluator.KwArgs.Empty), ctx);
                return true;
            }
            RecordInstanceValue rb = b as RecordInstanceValue;
            f = rb == null ? null : rb.Class.FindMethod(CmpDunder(Reflected(op)));
            if (f != null)
            {
                result = Truth(ctx.CallHook(f, new ScriptValue[] { b, a }, Evaluator.KwArgs.Empty), ctx);
                return true;
            }
            return false;
        }

        private static string CmpDunder(PyCmpOp op)
        {
            switch (op)
            {
                case PyCmpOp.Lt: return "__lt__";
                case PyCmpOp.Le: return "__le__";
                case PyCmpOp.Gt: return "__gt__";
                case PyCmpOp.Ge: return "__ge__";
                default: return "__ne__";
            }
        }

        private static PyCmpOp Reflected(PyCmpOp op)
        {
            switch (op)
            {
                case PyCmpOp.Lt: return PyCmpOp.Gt;
                case PyCmpOp.Le: return PyCmpOp.Ge;
                case PyCmpOp.Gt: return PyCmpOp.Lt;
                case PyCmpOp.Ge: return PyCmpOp.Le;
                default: return PyCmpOp.Ne;   // __ne__ is its own reflection
            }
        }

        // -------- sequence helpers (list/tuple only; same-Kind guaranteed by callers) --------

        private static bool IsSetFamily(ScriptValue v) { return v.Kind == ValueKind.Set || v.Kind == ValueKind.FrozenSet; }

        // struct_time compares/orders as a plain tuple (CPython structseq is a tuple subclass).
        private static bool TupleLike(ScriptValue v) { return v.Kind == ValueKind.Tuple || v is StructTimeValue; }

        private static bool BothSeq(ScriptValue x, ScriptValue y)
        {
            return (x.Kind == ValueKind.List && y.Kind == ValueKind.List)
                || (TupleLike(x) && TupleLike(y));
        }

        private static int SeqCount(ScriptValue v)
        {
            if (v.Kind == ValueKind.List)
                return ((ListValue)v).Items.Count;
            StructTimeValue st = v as StructTimeValue;
            return st != null ? st.Items.Length : ((TupleValue)v).Items.Length;
        }

        private static ScriptValue SeqItem(ScriptValue v, int i)
        {
            if (v.Kind == ValueKind.List)
                return ((ListValue)v).Items[i];
            StructTimeValue st = v as StructTimeValue;
            return st != null ? st.Items[i] : ((TupleValue)v).Items[i];
        }
    }
}

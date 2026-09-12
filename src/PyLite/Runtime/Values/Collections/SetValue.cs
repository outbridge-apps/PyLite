using System;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    public class SetValue : ScriptValue
    {
        private static readonly ScriptTypeInfo SetType = new ScriptTypeInfo("set", SetMethods.BuildSetSlots());
        internal static ScriptTypeInfo SetTypeInfo { get { return SetType; } }

        internal readonly OrderedTable Table;   // each entry Value == null
        internal SetValue(OrderedTable table) { Table = table; }

        public override ScriptTypeInfo TypeInfo { get { return SetType; } }
        internal override ValueKind Kind { get { return ValueKind.Set; } }
        internal virtual bool IsFrozen { get { return false; } }

        public int Count { get { return Table.Count; } }

        protected internal override bool IsTruthyCore(EvalContext ctx) { return Table.Count > 0; }
        protected internal override bool TryLengthCore(out long length) { length = Table.Count; return true; }
        protected internal override IScriptIterator GetIteratorCore(EvalContext ctx) { return new SetIterator(Table); }

        protected internal override bool TryContainsCore(ScriptValue item, EvalContext ctx, int depth, out bool found)
        {
            long h = PyOps.Hash(item, ctx, 0);   // unhashable element -> TypeError (parity)
            found = Table.ContainsKey(item, h, ctx, depth);
            return true;
        }

        protected internal override ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        {
            if (other.Kind != ValueKind.Set && other.Kind != ValueKind.FrozenSet)
                return null;
            ScriptValue left = reflected ? other : this;
            ScriptValue right = reflected ? this : other;
            return SetOps.Apply(op, (SetValue)left, right, ctx, left.Kind == ValueKind.FrozenSet);
        }

        internal bool AddItem(ScriptValue v, EvalContext ctx)
        {
            if (IsFrozen)
                throw new InvalidOperationException("frozenset is immutable");
            long h = PyOps.Hash(v, ctx, 0);
            return Table.InsertOrUpdate(v, h, null, ctx, 0);
        }

        internal bool RemoveItem(ScriptValue v, EvalContext ctx)
        {
            if (IsFrozen)
                throw new InvalidOperationException("frozenset is immutable");
            long h = PyOps.Hash(v, ctx, 0);
            return Table.Delete(v, h, ctx, 0);
        }

        // "arbitrary" element = the first live one in insertion order.
        internal ScriptValue PopFirst(EvalContext ctx)
        {
            for (int p = 0; p < Table.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, v;
                if (Table.TryGetEntryAt(p, out h, out k, out v))
                {
                    Table.Delete(k, h, ctx, 0);
                    return k;
                }
            }
            return null;
        }
    }

    public sealed class FrozenSetValue : SetValue
    {
        private static readonly ScriptTypeInfo FrozenType = new ScriptTypeInfo("frozenset", SetMethods.BuildFrozenSlots());
        internal static ScriptTypeInfo FrozenTypeInfo { get { return FrozenType; } }

        private long _hashCache;
        private bool _hashComputed;

        internal FrozenSetValue(OrderedTable table) : base(table) { }

        public override ScriptTypeInfo TypeInfo { get { return FrozenType; } }
        internal override ValueKind Kind { get { return ValueKind.FrozenSet; } }
        internal override bool IsFrozen { get { return true; } }

        // Port of Objects/setobject.c::frozenset_hash over the stored entry hashes (order-independent).
        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            if (_hashComputed)
                return _hashCache;
            unchecked
            {
                ulong hash = 1927868237UL * (ulong)(Table.Count + 1);
                for (int p = 0; p < Table.EntriesUsed; p++)
                {
                    long eh;
                    ScriptValue k, v;
                    if (!Table.TryGetEntryAt(p, out eh, out k, out v))
                        continue;
                    ulong h = (ulong)eh;
                    hash ^= (h ^ (h << 16) ^ 89869747UL) * 3644798167UL;
                }
                hash = hash * 69069UL + 907133923UL;
                long r = (long)hash;
                if (r == -1L)
                    r = 590923713L;
                _hashCache = r;
                _hashComputed = true;
                return r;
            }
        }
    }

    internal sealed class SetIterator : TableViewIterator
    {
        internal SetIterator(OrderedTable t) : base(t) { }

        // CPython's set iterator checks the size and nothing else, so a same-size remove+add goes on.
        protected override bool Invalidated() { return SizeChanged(); }
        protected override string MutationText(int at, int now) { return "Set changed size during iteration"; }
        protected override ScriptValue Project(EvalContext ctx, long hash, ScriptValue key, ScriptValue value) { return key; }
    }

    // Shared primitives for the set operators (here) and the set methods (builtins package).
    internal static class SetOps
    {
        internal static SetValue Union(SetValue a, ScriptValue b, EvalContext ctx, bool frozen)
        {
            OrderedTable t = a.Table.CloneShallow(ctx);   // a's elements in order
            SetValue sb = b as SetValue;
            if (sb != null)
            {
                // set|set: b's table already stores every element's hash — no re-hash, no iterator,
                // and a direct entry walk (no per-element delegate).
                OrderedTable bt = sb.Table;
                for (int p = 0; p < bt.EntriesUsed; p++)
                {
                    long h;
                    ScriptValue k, unused;
                    if (bt.TryGetEntryAt(p, out h, out k, out unused))
                        t.InsertOrUpdate(k, h, null, ctx, 0);
                }
                return Wrap(t, frozen, ctx);
            }
            IScriptIterator it = PyOps.GetIterator(b, ctx);
            ScriptValue cur;
            while (it.MoveNext(ctx, out cur))
                t.InsertOrUpdate(cur, PyOps.Hash(cur, ctx, 0), null, ctx, 0);
            return Wrap(t, frozen, ctx);
        }

        internal static SetValue Intersection(SetValue a, ScriptValue b, EvalContext ctx, bool frozen)
        {
            OrderedTable m = Membership(b, ctx);
            var t = new OrderedTable(ctx, a.Count);
            ForEach(a.Table, (h, k) => { if (m.ContainsKey(k, h, ctx, 0))
                t.InsertOrUpdate(k, h, null, ctx, 0); });
            return Wrap(t, frozen, ctx);
        }

        internal static SetValue Difference(SetValue a, ScriptValue b, EvalContext ctx, bool frozen)
        {
            OrderedTable m = Membership(b, ctx);
            var t = new OrderedTable(ctx, a.Count);
            ForEach(a.Table, (h, k) => { if (!m.ContainsKey(k, h, ctx, 0))
                t.InsertOrUpdate(k, h, null, ctx, 0); });
            return Wrap(t, frozen, ctx);
        }

        internal static SetValue SymmetricDifference(SetValue a, ScriptValue b, EvalContext ctx, bool frozen)
        {
            OrderedTable m = Membership(b, ctx);
            var t = new OrderedTable(ctx, a.Count);
            ForEach(a.Table, (h, k) => { if (!m.ContainsKey(k, h, ctx, 0))
                t.InsertOrUpdate(k, h, null, ctx, 0); });
            ForEach(m, (h, k) => { if (!a.Table.ContainsKey(k, h, ctx, 0))
                t.InsertOrUpdate(k, h, null, ctx, 0); });
            return Wrap(t, frozen, ctx);
        }

        internal static bool AllContained(SetValue a, SetValue b, EvalContext ctx, int depth)
        {
            for (int p = 0; p < a.Table.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, v;
                if (a.Table.TryGetEntryAt(p, out h, out k, out v) && !b.Table.ContainsKey(k, h, ctx, depth + 1))
                    return false;
            }
            return true;
        }

        internal static bool IsSubset(SetValue a, SetValue b, EvalContext ctx) { return AllContained(a, b, ctx, 0); }

        // The four set operators; null when op is not one of them. `b` may be any iterable (dict views use this).
        internal static SetValue Apply(PyBinOp op, SetValue a, ScriptValue b, EvalContext ctx, bool frozen)
        {
            switch (op)
            {
                case PyBinOp.BitOr: return Union(a, b, ctx, frozen);
                case PyBinOp.BitAnd: return Intersection(a, b, ctx, frozen);
                case PyBinOp.Sub: return Difference(a, b, ctx, frozen);
                case PyBinOp.BitXor: return SymmetricDifference(a, b, ctx, frozen);
                default: return null;
            }
        }

        // set, frozenset and the set-like dict views (keys/items) take part in set comparisons.
        internal static bool IsSetLike(ScriptValue v)
        {
            if (v.Kind == ValueKind.Set || v.Kind == ValueKind.FrozenSet)
                return true;
            DictViewValue view = v as DictViewValue;
            return view != null && view.IsSetLike;
        }

        // a <= b by membership, never hashing a's elements: an items view holding an unhashable value still
        // compares (CPython's Set mixin does the same); two sets keep the direct table walk.
        internal static bool SubsetLike(ScriptValue a, ScriptValue b, EvalContext ctx)
        {
            SetValue sa = a as SetValue, sb = b as SetValue;
            if (sa != null && sb != null)
                return AllContained(sa, sb, ctx, 0);
            IScriptIterator it = PyOps.GetIterator(a, ctx);
            ScriptValue cur;
            while (it.MoveNext(ctx, out cur))
                if (!PyOps.Contains(cur, b, ctx))
                    return false;
            return true;
        }

        internal static bool EqualLike(ScriptValue a, ScriptValue b, EvalContext ctx)
        {
            return PyOps.Length(a, ctx) == PyOps.Length(b, ctx) && SubsetLike(a, b, ctx);
        }

        internal static bool OrderLike(PyCmpOp op, ScriptValue a, ScriptValue b, EvalContext ctx)
        {
            long la = PyOps.Length(a, ctx), lb = PyOps.Length(b, ctx);
            switch (op)
            {
                case PyCmpOp.Lt: return la < lb && SubsetLike(a, b, ctx);
                case PyCmpOp.Le: return SubsetLike(a, b, ctx);
                case PyCmpOp.Gt: return lb < la && SubsetLike(b, a, ctx);
                default: return SubsetLike(b, a, ctx); // Ge
            }
        }

        private static OrderedTable Membership(ScriptValue b, EvalContext ctx)
        {
            if (b.Kind == ValueKind.Set || b.Kind == ValueKind.FrozenSet)
                return ((SetValue)b).Table;
            var t = new OrderedTable(ctx, 8);
            IScriptIterator it = PyOps.GetIterator(b, ctx);
            ScriptValue cur;
            while (it.MoveNext(ctx, out cur))
                t.InsertOrUpdate(cur, PyOps.Hash(cur, ctx, 0), null, ctx, 0);
            return t;
        }

        private static void ForEach(OrderedTable t, Action<long, ScriptValue> action)
        {
            for (int p = 0; p < t.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, v;
                if (t.TryGetEntryAt(p, out h, out k, out v))
                    action(h, k);
            }
        }

        private static SetValue Wrap(OrderedTable t, bool frozen, EvalContext ctx)
        {
            ctx.Budget.ChargeAllocation(32);
            return frozen ? (SetValue)new FrozenSetValue(t) : new SetValue(t);
        }
    }
}

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
            int p = Table.FindFirstLive();
            if (p < 0)
                return null;
            long h;
            ScriptValue k, v;
            Table.TryGetEntryAt(p, out h, out k, out v);
            Table.Delete(k, h, ctx, 0);
            return k;
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
}

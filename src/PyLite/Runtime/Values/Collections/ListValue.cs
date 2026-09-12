using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // Mutable sequence. Unhashable (base leaf throws). ListIterator is by index, no version guard
    // (CPython parity). The read-only protocol is SequenceOps; this file owns the mutation.
    public sealed class ListValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("list", ListMethods.BuildListSlots());
        internal static ScriptTypeInfo ListType { get { return Type; } }

        internal readonly List<ScriptValue> Items;
        internal ListValue(List<ScriptValue> items) { Items = items; }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.List; } }

        protected internal override bool IsTruthyCore(EvalContext ctx) { return Items.Count > 0; }
        protected internal override bool TryLengthCore(out long length) { length = Items.Count; return true; }
        protected internal override IScriptIterator GetIteratorCore(EvalContext ctx) { return new ListIterator(Items); }

        protected internal override bool TryContainsCore(ScriptValue item, EvalContext ctx, int depth, out bool found)
        {
            found = SequenceOps.Contains(new ListSeq(Items), item, ctx);
            return true;
        }

        protected internal override ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        {
            switch (op)
            {
                case PyBinOp.Add:
                    if (reflected)
                        return null;
                    if (other.Kind == ValueKind.List)
                        return ConcatList((ListValue)other, ctx);
                    throw SequenceOps.ConcatTypeError(other, "list", ctx);
                case PyBinOp.Mul:
                    int n = SequenceOps.RepeatCount(Items.Count, other, ctx);
                    return n < 0 ? null : RepeatList(n, ctx);
                default:
                    return null;
            }
        }

        internal ScriptValue GetItem(ScriptValue index, EvalContext ctx)
        {
            if (index.Kind == ValueKind.Slice)
                return ctx.Values.ListFrom(new List<ScriptValue>(SequenceOps.Slice(new ListSeq(Items), (SliceValue)index, ctx)));
            return Items[NormIndex(index, ctx)];
        }

        internal void SetItem(ScriptValue index, ScriptValue value, EvalContext ctx)
        {
            if (index.Kind == ValueKind.Slice)
            {
                SetSlice((SliceValue)index, value, ctx);
                return;
            }
            Items[NormIndex(index, ctx)] = value;
        }

        internal void DelItem(ScriptValue index, EvalContext ctx)
        {
            if (index.Kind == ValueKind.Slice)
            {
                DelSlice((SliceValue)index, ctx);
                return;
            }
            Items.RemoveAt(NormIndex(index, ctx));
        }

        private int NormIndex(ScriptValue index, EvalContext ctx)
        {
            if (!SequenceOps.IsIndex(index))
                throw SequenceOps.IndexTypeError(index, "list", ctx);
            return SequenceOps.NormIndex(index, Items.Count, "list", ctx);
        }

        internal void Add(ScriptValue v, EvalContext ctx)
        {
            if (Items.Count >= ctx.Limits.MaxCollectionItems)
                SequenceOps.CheckCap((long)Items.Count + 1, ctx);
            ctx.Budget.ChargeAllocation(8);
            Items.Add(v);
        }

        internal void InPlaceConcat(ScriptValue iterable, EvalContext ctx)
        {
            if (ReferenceEquals(iterable, this))
            {
                var copy = new List<ScriptValue>(Items);
                foreach (ScriptValue v in copy)
                    Add(v, ctx);
                return;
            }
            IScriptIterator it = PyOps.GetIterator(iterable, ctx);
            ScriptValue cur;
            while (it.MoveNext(ctx, out cur))
                Add(cur, ctx);
        }

        internal void InPlaceRepeat(BigInteger n, EvalContext ctx)
        {
            if (n <= 0)
            {
                Items.Clear();
                return;
            }
            if (n == 1 || Items.Count == 0)
                return;   // an empty list stays empty; its count would not fit an int
            BigInteger total = (BigInteger)Items.Count * n;
            SequenceOps.CheckCap(total, ctx);
            var orig = new List<ScriptValue>(Items);
            ctx.Budget.ChargeAllocation(8L * (int)(n - 1) * orig.Count);
            for (int r = 0; r < (int)n - 1; r++)
                Items.AddRange(orig);
        }

        private ListValue ConcatList(ListValue o, EvalContext ctx)
        {
            long total = (long)Items.Count + o.Items.Count;
            SequenceOps.CheckCap(total, ctx);
            var lst = new List<ScriptValue>((int)total);
            lst.AddRange(Items);
            lst.AddRange(o.Items);
            return ctx.Values.ListFrom(lst);
        }

        private ListValue RepeatList(int count, EvalContext ctx)
        {
            var lst = new List<ScriptValue>(Items.Count * count);
            for (int r = 0; r < count; r++)
                lst.AddRange(Items);
            return ctx.Values.ListFrom(lst);
        }

        private void SetSlice(SliceValue sl, ScriptValue rhs, EvalContext ctx)
        {
            SliceIndices ind = sl.Indices(Items.Count, ctx);
            int start = (int)ind.Start, step = ind.StepInt, len = (int)ind.Length;
            List<ScriptValue> repl = Materialize(rhs, ctx);
            if (step == 1)
            {
                Items.RemoveRange(start, len);
                ctx.Budget.ChargeAllocation(8L * repl.Count);
                Items.InsertRange(start, repl);
            }
            else
            {
                if (repl.Count != len)
                    throw Raise.ValueError(ctx, "attempt to assign sequence of size " + repl.Count + " to extended slice of size " + len);
                int idx = start;
                for (int k = 0; k < len; k++)
                {
                    Items[idx] = repl[k];
                    idx += step;
                }
            }
        }

        private void DelSlice(SliceValue sl, EvalContext ctx)
        {
            SliceIndices ind = sl.Indices(Items.Count, ctx);
            int start = (int)ind.Start, step = ind.StepInt, len = (int)ind.Length;
            if (len == 0)
                return;
            if (step == 1)
            {
                Items.RemoveRange(start, len);
                return;
            }
            // remove in descending index order
            if (step > 0)
                for (int k = len - 1; k >= 0; k--)
                    Items.RemoveAt(start + k * step);
            else
                for (int k = 0; k < len; k++)
                    Items.RemoveAt(start + k * step);
        }

        private static List<ScriptValue> Materialize(ScriptValue rhs, EvalContext ctx)
        {
            var tmp = new List<ScriptValue>();
            IScriptIterator it = PyOps.GetIterator(rhs, ctx);
            ScriptValue cur;
            while (it.MoveNext(ctx, out cur))
            {
                SequenceOps.CheckCap((long)tmp.Count + 1, ctx);
                ctx.Budget.ChargeAllocation(8);
                tmp.Add(cur);
            }
            return tmp;
        }
    }

    internal sealed class ListIterator : ScriptIteratorBase
    {
        private readonly List<ScriptValue> _items;
        private int _pos;
        internal ListIterator(List<ScriptValue> items) { _items = items; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_pos < _items.Count)
            {
                value = _items[_pos++];
                return true;
            }
            value = null;
            return false;
        }
    }
}

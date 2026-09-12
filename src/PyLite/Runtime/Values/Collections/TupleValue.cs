using System;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // Immutable sequence. Hashable via the PyOps.Hash tuple engine (not a leaf hook).
    // Not sealed: collections.namedtuple instances subclass it, keeping Kind==Tuple.
    public class TupleValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("tuple", ListMethods.BuildTupleSlots());
        internal static ScriptTypeInfo TupleType { get { return Type; } }

        internal readonly ScriptValue[] Items;
        internal TupleValue(ScriptValue[] items) { Items = items; }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Tuple; } }

        protected internal override bool IsTruthyCore(EvalContext ctx) { return Items.Length > 0; }
        protected internal override bool TryLengthCore(out long length) { length = Items.Length; return true; }
        protected internal override IScriptIterator GetIteratorCore(EvalContext ctx) { return new TupleIterator(Items); }

        protected internal override bool TryContainsCore(ScriptValue item, EvalContext ctx, int depth, out bool found)
        {
            found = SequenceOps.Contains(new ArraySeq(Items), item, ctx);
            return true;
        }

        protected internal override ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        {
            switch (op)
            {
                case PyBinOp.Add:
                    if (reflected)
                        return null;
                    if (other.Kind == ValueKind.Tuple)
                        return ConcatTuple((TupleValue)other, ctx);
                    throw SequenceOps.ConcatTypeError(other, "tuple", ctx);
                case PyBinOp.Mul:
                    int n = SequenceOps.RepeatCount(Items.Length, other, ctx);
                    return n < 0 ? null : RepeatTuple(n, ctx);
                default:
                    return null;
            }
        }

        internal ScriptValue GetItem(ScriptValue index, EvalContext ctx)
        {
            return GetItem(Items, index, ctx);
        }

        // tuple[i] over any tuple-shaped array (struct_time reads its items the same way).
        internal static ScriptValue GetItem(ScriptValue[] items, ScriptValue index, EvalContext ctx)
        {
            if (index.Kind == ValueKind.Slice)
                return ctx.Values.Tuple(SequenceOps.Slice(new ArraySeq(items), (SliceValue)index, ctx));
            if (!SequenceOps.IsIndex(index))
                throw SequenceOps.IndexTypeError(index, "tuple", ctx);
            return items[SequenceOps.NormIndex(index, items.Length, "tuple", ctx)];
        }

        private TupleValue ConcatTuple(TupleValue o, EvalContext ctx)
        {
            long total = (long)Items.Length + o.Items.Length;
            SequenceOps.CheckCap(total, ctx);
            if (Items.Length == 0)
                return o;
            if (o.Items.Length == 0)
                return this;
            var arr = new ScriptValue[total];
            Array.Copy(Items, arr, Items.Length);
            Array.Copy(o.Items, 0, arr, Items.Length, o.Items.Length);
            return ctx.Values.Tuple(arr);
        }

        private TupleValue RepeatTuple(int count, EvalContext ctx)
        {
            var arr = new ScriptValue[Items.Length * count];
            int w = 0;
            for (int r = 0; r < count; r++)
                for (int i = 0; i < Items.Length; i++)
                    arr[w++] = Items[i];
            return ctx.Values.Tuple(arr);
        }
    }

    internal sealed class TupleIterator : ScriptIteratorBase
    {
        private readonly ScriptValue[] _items;
        private int _pos;
        internal TupleIterator(ScriptValue[] items) { _items = items; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_pos < _items.Length)
            {
                value = _items[_pos++];
                return true;
            }
            value = null;
            return false;
        }
    }
}

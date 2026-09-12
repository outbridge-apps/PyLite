using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // Not sealed: collections OrderedDict/defaultdict/Counter subclass it, keeping Kind==Dict so the
    // evaluator treats them as dicts for subscript/assign/del/in/iter.
    public class DictValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("dict", DictMethods.BuildSlots());
        internal static ScriptTypeInfo DictType { get { return Type; } }

        internal readonly OrderedTable Table;
        internal DictValue(OrderedTable table) { Table = table; }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Dict; } }

        public int Count { get { return Table.Count; } }

        // Repr wrapper for dict subtypes: rendered around the {k: v} body. null => plain dict.
        internal virtual string ReprPrefix(EvalContext ctx) { return null; }
        internal virtual string ReprSuffix(EvalContext ctx) { return null; }
        internal virtual string ReprEmpty(EvalContext ctx) { return null; }   // non-null: the whole repr when empty

        protected internal override bool IsTruthyCore(EvalContext ctx) { return Table.Count > 0; }
        protected internal override bool TryLengthCore(out long length) { length = Table.Count; return true; }
        protected internal override IScriptIterator GetIteratorCore(EvalContext ctx) { return new DictKeyIterator(Table); }
        protected internal override IScriptIterator GetReverseIteratorCore(EvalContext ctx) { return new ReverseDictIterator(Table, DictViewValue.ViewKind.Keys); }

        protected internal override bool TryContainsCore(ScriptValue item, EvalContext ctx, int depth, out bool found)
        {
            long h = PyOps.Hash(item, ctx, 0);
            found = Table.ContainsKey(item, h, ctx, depth);
            return true;
        }

        // PEP 584: d1 | d2 -> merged copy, right wins, insertion order preserved. Both
        // operands must be dict; anything else falls through to the generic TypeError.
        protected internal override ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        {
            if (op != PyBinOp.BitOr || other.Kind != ValueKind.Dict)
                return null;
            DictValue left = reflected ? (DictValue)other : this;
            DictValue right = reflected ? this : (DictValue)other;
            DictValue result = ctx.Values.Dict(left.Count + right.Count);
            DictMethods.Merge(ctx, result, left);
            DictMethods.Merge(ctx, result, right);
            return result;
        }

        public bool TryGet(ScriptValue key, EvalContext ctx, out ScriptValue value)
        {
            long h = PyOps.Hash(key, ctx, 0);
            return Table.TryGetValue(key, h, ctx, 0, out value);
        }

        // d[key] with the type's __missing__ and without the KeyError: defaultdict answers its factory's
        // value, Counter a 0. A ChainMap asks each map this way instead of catching.
        public virtual bool TryGetItem(ScriptValue key, EvalContext ctx, out ScriptValue value)
        {
            return TryGet(key, ctx, out value);
        }

        public ScriptValue GetItemOrThrow(ScriptValue key, EvalContext ctx)
        {
            ScriptValue v;
            if (TryGetItem(key, ctx, out v))
                return v;
            throw Raise.KeyError(ctx, key);
        }

        public void SetItem(ScriptValue key, ScriptValue value, EvalContext ctx)
        {
            long h = PyOps.Hash(key, ctx, 0);   // unhashable key -> TypeError
            Table.InsertOrUpdate(key, h, value, ctx, 0);
        }

        public void DelItemOrThrow(ScriptValue key, EvalContext ctx)
        {
            long h = PyOps.Hash(key, ctx, 0);
            if (!Table.Delete(key, h, ctx, 0))
                throw Raise.KeyError(ctx, key);
        }

        public DictViewValue KeysView(EvalContext ctx) { return MakeView(ctx, DictViewValue.ViewKind.Keys); }
        public DictViewValue ValuesView(EvalContext ctx) { return MakeView(ctx, DictViewValue.ViewKind.Values); }
        public DictViewValue ItemsView(EvalContext ctx) { return MakeView(ctx, DictViewValue.ViewKind.Items); }

        private DictViewValue MakeView(EvalContext ctx, DictViewValue.ViewKind kind)
        {
            ctx.Budget.ChargeAllocation(32);
            return new DictViewValue(this, kind);
        }
    }

    public sealed class DictViewValue : ScriptValue
    {
        internal enum ViewKind { Keys, Values, Items }

        private static readonly ScriptTypeInfo KeysType = new ScriptTypeInfo("dict_keys", DictMethods.BuildViewSlots());
        private static readonly ScriptTypeInfo ValuesType = new ScriptTypeInfo("dict_values", null);
        private static readonly ScriptTypeInfo ItemsType = new ScriptTypeInfo("dict_items", DictMethods.BuildViewSlots());

        internal readonly DictValue Owner;
        internal readonly ViewKind View;
        internal DictViewValue(DictValue owner, ViewKind view) { Owner = owner; View = view; }

        public override ScriptTypeInfo TypeInfo
        {
            get { return View == ViewKind.Keys ? KeysType : View == ViewKind.Values ? ValuesType : ItemsType; }
        }
        internal override ValueKind Kind { get { return ValueKind.DictView; } }

        // keys()/items() are set-like (collections.abc.Set): operators, subset ordering, equality with sets
        // and with each other. values() is a plain iterable container and compares by identity.
        internal bool IsSetLike { get { return View != ViewKind.Values; } }

        protected internal override bool IsTruthyCore(EvalContext ctx) { return Owner.Count > 0; }
        protected internal override bool TryLengthCore(out long length) { length = Owner.Count; return true; }
        protected internal override IScriptIterator GetReverseIteratorCore(EvalContext ctx) { return new ReverseDictIterator(Owner.Table, View); }

        // view <op> x and x <op> view for any iterable x; the result is always a plain set, as in CPython
        // (an items pair whose value is unhashable raises TypeError here, parity).
        protected internal override ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        {
            if (!IsSetLike || PyOps.TryGetIterator(other, ctx) == null)
                return null;
            if (!reflected)
                return SetOps.Apply(op, FromIterable(this, ctx), other, ctx, false);
            SetValue left = other as SetValue ?? FromIterable(other, ctx);
            return SetOps.Apply(op, left, this, ctx, false);
        }

        private static SetValue FromIterable(ScriptValue src, EvalContext ctx)
        {
            var t = new OrderedTable(ctx, 8);
            IScriptIterator it = PyOps.GetIterator(src, ctx);
            ScriptValue cur;
            while (it.MoveNext(ctx, out cur))
                t.InsertOrUpdate(cur, PyOps.Hash(cur, ctx, 0), null, ctx, 0);
            ctx.Budget.ChargeAllocation(32);
            return new SetValue(t);
        }

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            DictViewValue o = other as DictViewValue;
            if (o == null || !IsSetLike || !o.IsSetLike)
                return ReferenceEquals(this, other);
            return SetOps.EqualLike(this, o, ctx);
        }

        protected internal override bool TryEqualsAcrossKindCore(ScriptValue other, EvalContext ctx, int depth, out bool equal)
        {
            if (IsSetLike && (other.Kind == ValueKind.Set || other.Kind == ValueKind.FrozenSet))
            {
                equal = SetOps.EqualLike(this, other, ctx);
                return true;
            }
            equal = false;
            return false;
        }

        protected internal override IScriptIterator GetIteratorCore(EvalContext ctx)
        {
            switch (View)
            {
                case ViewKind.Keys: return new DictKeyIterator(Owner.Table);
                case ViewKind.Values: return new DictValuesIterator(Owner.Table);
                default: return new DictItemsIterator(Owner.Table);
            }
        }

        protected internal override bool TryContainsCore(ScriptValue item, EvalContext ctx, int depth, out bool found)
        {
            if (View == ViewKind.Keys)
            {
                long h = PyOps.Hash(item, ctx, 0);
                found = Owner.Table.ContainsKey(item, h, ctx, depth);
                return true;
            }
            if (View == ViewKind.Items)
            {
                if (item.Kind != ValueKind.Tuple || ((TupleValue)item).Items.Length != 2)
                {
                    found = false;
                    return true;
                }

                ScriptValue k = ((TupleValue)item).Items[0], v = ((TupleValue)item).Items[1];
                ScriptValue got;
                found = Owner.TryGet(k, ctx, out got) && PyOps.Equals(got, v, ctx, 0);
                return true;
            }
            // Values: linear scan
            IScriptIterator it = new DictValuesIterator(Owner.Table);
            ScriptValue cur;
            while (it.MoveNext(ctx, out cur))
                if (PyOps.Equals(item, cur, ctx, 0))
                {
                    found = true;
                    return true;
                }
            found = false;
            return true;
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            string name = View == ViewKind.Keys ? "dict_keys" : View == ViewKind.Values ? "dict_values" : "dict_items";
            ListValue tmp = ctx.Values.List(Owner.Count);
            IScriptIterator it = GetIteratorCore(ctx);
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
                tmp.Add(v, ctx);
            sb.Append(name);
            sb.Append('(');
            sb.Append(tmp.Repr(ctx, depth + 1));
            sb.Append(')');
        }
    }

    // Iterates live entries in insertion order; a structural change (version bump) aborts.
    internal abstract class TableViewIterator : ScriptIteratorBase
    {
        protected readonly OrderedTable Table;
        private readonly ulong _version;
        private readonly int _count;
        private int _pos;

        protected TableViewIterator(OrderedTable table)
        {
            Table = table;
            _version = table.Version;
            _count = table.Count;
        }

        // The version bumps for a size change and for a same-size key change alike; CPython words the
        // two differently, so the text is chosen from the count at the point the guard fires.
        protected abstract string MutationText(int countAtStart, int countNow);
        protected abstract ScriptValue Project(EvalContext ctx, long hash, ScriptValue key, ScriptValue value);

        // Dicts abort on any structural change (CPython 3.8+); sets only on a size change, because that is
        // all CPython's set iterator checks. The walk below is bounds-checked against the live table at
        // every step, so continuing after a same-size rebuild is safe - it merely yields the unspecified
        // order CPython yields too.
        protected virtual bool Invalidated() { return Table.Version != _version; }
        protected bool SizeChanged() { return Table.Count != _count; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (Invalidated())
                throw Raise.RuntimeError(ctx, MutationText(_count, Table.Count));
            while (_pos < Table.EntriesUsed)
            {
                long h;
                ScriptValue k, v;
                bool live = Table.TryGetEntryAt(_pos, out h, out k, out v);
                _pos++;
                if (live)
                {
                    value = Project(ctx, h, k, v);
                    return true;
                }
            }
            value = null;
            return false;
        }
    }

    // "changed size" when the count moved, "keys changed" when a delete+insert kept it — CPython 3.8+
    // reports both, with these two texts.
    internal static class DictMutation
    {
        internal static string Text(int countAtStart, int countNow)
        {
            return countNow != countAtStart
                ? "dictionary changed size during iteration"
                : "dictionary keys changed during iteration";
        }
    }

    internal sealed class DictKeyIterator : TableViewIterator
    {
        internal DictKeyIterator(OrderedTable t) : base(t) { }
        protected override string MutationText(int at, int now) { return DictMutation.Text(at, now); }
        protected override ScriptValue Project(EvalContext ctx, long hash, ScriptValue key, ScriptValue value) { return key; }
    }

    internal sealed class DictValuesIterator : TableViewIterator
    {
        internal DictValuesIterator(OrderedTable t) : base(t) { }
        protected override string MutationText(int at, int now) { return DictMutation.Text(at, now); }
        protected override ScriptValue Project(EvalContext ctx, long hash, ScriptValue key, ScriptValue value) { return value; }
    }

    internal sealed class DictItemsIterator : TableViewIterator
    {
        internal DictItemsIterator(OrderedTable t) : base(t) { }
        protected override string MutationText(int at, int now) { return DictMutation.Text(at, now); }
        protected override ScriptValue Project(EvalContext ctx, long hash, ScriptValue key, ScriptValue value)
        {
            return ctx.Values.Tuple(new[] { key, value });
        }
    }

    // reversed(dict) / reversed(view): entries in reverse insertion order; version-guarded.
    internal sealed class ReverseDictIterator : ScriptIteratorBase
    {
        private readonly OrderedTable _table;
        private readonly DictViewValue.ViewKind _view;
        private readonly ulong _version;
        private readonly int _count;
        private int _pos;

        internal ReverseDictIterator(OrderedTable table, DictViewValue.ViewKind view)
        {
            _table = table;
            _view = view;
            _version = table.Version;
            _count = table.Count;
            _pos = table.EntriesUsed - 1;
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_table.Version != _version)
                throw Raise.RuntimeError(ctx, DictMutation.Text(_count, _table.Count));
            while (_pos >= 0)
            {
                long h;
                ScriptValue k, v;
                bool live = _table.TryGetEntryAt(_pos, out h, out k, out v);
                _pos--;
                if (live)
                {
                    value = _view == DictViewValue.ViewKind.Keys ? k
                        : _view == DictViewValue.ViewKind.Values ? v
                        : ctx.Values.Tuple(new[] { k, v });
                    return true;
                }
            }
            value = null;
            return false;
        }
    }
}

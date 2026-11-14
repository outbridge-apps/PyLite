using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // collections.Counter. A DictValue whose values are counts; a miss returns 0 WITHOUT
    // inserting an entry (unlike defaultdict). Binary +-&| drop counts <= 0; update/subtract keep them.
    internal sealed class CounterValue : DictValue
    {
        private enum CombineOp { Add, Sub, And, Or }

        private static readonly ScriptTypeInfo CType = new ScriptTypeInfo("collections.Counter", BuildSlots());

        internal CounterValue(OrderedTable table) : base(table) { }

        public override ScriptTypeInfo TypeInfo { get { return CType; } }
        internal override string ReprPrefix(EvalContext ctx) { return "Counter("; }
        internal override string ReprSuffix(EvalContext ctx) { return ")"; }

        internal override string ReprEmpty(EvalContext ctx) { return "Counter()"; }

        public override bool TryGetItem(ScriptValue key, EvalContext ctx, out ScriptValue value)
        {
            if (!TryGet(key, ctx, out value))
                value = ctx.Values.Int(0);   // 0 without insertion
            return true;
        }

        // CPython's __iadd__, __isub__, __ior__ and __iand__: this object, then only the positive counts.
        internal ScriptValue InPlace(PyBinOp op, ScriptValue other, EvalContext ctx)
        {
            if (op == PyBinOp.BitAnd)
            {
                foreach (KeyValuePair<ScriptValue, ScriptValue> e in Entries(this, ctx))
                {
                    ScriptValue theirs = PyOps.GetItem(other, e.Key, ctx);
                    if (PyOps.RichCompare(PyCmpOp.Lt, theirs, e.Value, ctx, 0).IsTruthy(ctx))
                        SetItem(e.Key, theirs, ctx);
                }
            }
            else
            {
                ChainMapValue cm = other as ChainMapValue;
                DictValue d = cm != null ? cm.Merged(ctx) : other as DictValue;
                if (d == null)
                    throw Raise.Make(ctx, PyExceptionTypes.AttributeError, "'" + other.PyTypeName + "' object has no attribute 'items'");
                foreach (KeyValuePair<ScriptValue, ScriptValue> e in Entries(d, ctx))
                {
                    ScriptValue mine;
                    TryGetItem(e.Key, ctx, out mine);
                    if (op == PyBinOp.Add)
                        SetItem(e.Key, PyOps.BinaryOp(PyBinOp.Add, mine, e.Value, ctx), ctx);
                    else if (op == PyBinOp.Sub)
                        SetItem(e.Key, PyOps.BinaryOp(PyBinOp.Sub, mine, e.Value, ctx), ctx);
                    else if (PyOps.RichCompare(PyCmpOp.Gt, e.Value, mine, ctx, 0).IsTruthy(ctx))
                        SetItem(e.Key, e.Value, ctx);
                }
            }
            ScriptValue zero = ctx.Values.Int(0);
            foreach (KeyValuePair<ScriptValue, ScriptValue> e in Entries(this, ctx))
            {
                if (!PyOps.RichCompare(PyCmpOp.Gt, e.Value, zero, ctx, 0).IsTruthy(ctx))
                    DelItemOrThrow(e.Key, ctx);
            }
            return this;
        }

        // A snapshot: an operator's own updates must not disturb its walk.
        private static List<KeyValuePair<ScriptValue, ScriptValue>> Entries(DictValue d, EvalContext ctx)
        {
            ctx.Values.PreCharge(16L * d.Count);
            var all = new List<KeyValuePair<ScriptValue, ScriptValue>>(d.Count);
            for (int p = d.Table.WalkStart(ctx); p < d.Table.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, v;
                if (d.Table.TryGetEntryAt(p, out h, out k, out v))
                    all.Add(new KeyValuePair<ScriptValue, ScriptValue>(k, v));
            }
            return all;
        }

        // repr shows most_common's order; counts that do not compare keep the insertion order, as CPython's __repr__ does.
        internal ScriptValue[] PairsByCount(ScriptValue[] pairs, EvalContext ctx)
        {
            ScriptValue[] fast = PairsBySmallCount(pairs, ctx);
            if (fast != null)
                return fast;
            int n = pairs.Length / 2;
            var order = new ScriptValue[n];
            var keys = new ScriptValue[n];
            try
            {
                for (int i = 0; i < n; i++)
                {
                    order[i] = ctx.Values.Tuple(new[] { pairs[2 * i], pairs[2 * i + 1] });
                    keys[i] = pairs[2 * i + 1];
                }
                SortByCountDescending(ctx, order, keys);
            }
            catch (ScriptException e) when (e.Value.ExcType.IsSubtypeOf(PyExceptionTypes.TypeError))
            {
                return pairs;
            }
            var sorted = new ScriptValue[pairs.Length];
            for (int i = 0; i < n; i++)
            {
                ScriptValue[] pair = ((TupleValue)order[i]).Items;
                sorted[2 * i] = pair[0];
                sorted[2 * i + 1] = pair[1];
            }
            return sorted;
        }

        // Every count a machine int (the usual Counter): sort the positions by count, ties in insertion order, then move
        // the pairs into that order in place. Only the positions are new: a tuple and a negated int per key doubled the
        // steps of a 1M-key repr and outgrew the default MaxAllocBytes.
        private static ScriptValue[] PairsBySmallCount(ScriptValue[] pairs, EvalContext ctx)
        {
            int n = pairs.Length / 2;
            for (int i = 0; i < n; i++)
            {
                IntValue iv = pairs[2 * i + 1] as IntValue;
                if (iv == null || !iv.IsSmall)
                    return null;
            }
            ctx.Values.PreCharge(4L * n);
            ctx.Budget.ChargeLinear((long)n * (1 + (int)Math.Log(n + 1, 2)));
            var at = new int[n];
            for (int i = 0; i < n; i++)
                at[i] = i;
            Array.Sort(at, (x, y) =>
            {
                long cx = ((IntValue)pairs[2 * x + 1]).Small, cy = ((IntValue)pairs[2 * y + 1]).Small;
                return cx != cy ? cy.CompareTo(cx) : x.CompareTo(y);
            });
            ctx.Budget.CheckDeadlineAfterCall();
            // slot j takes the pair at at[j]: each cycle is followed once, a done slot marked by its complement
            for (int i = 0; i < n; i++)
            {
                if (at[i] < 0)
                    continue;
                ScriptValue k0 = pairs[2 * i], v0 = pairs[2 * i + 1];
                int j = i;
                while (true)
                {
                    int src = at[j];
                    at[j] = ~src;
                    if (src == i)
                    {
                        pairs[2 * j] = k0;
                        pairs[2 * j + 1] = v0;
                        break;
                    }
                    pairs[2 * j] = pairs[2 * src];
                    pairs[2 * j + 1] = pairs[2 * src + 1];
                    j = src;
                }
            }
            return pairs;
        }

        // sorted(items, key=count, reverse=True) as CPython runs it: the reversed input sorted ascending and reversed
        // back. The counts compare as they are (str counts sort, mixed ones are the TypeError); ties keep their order.
        private static void SortByCountDescending(EvalContext ctx, ScriptValue[] items, ScriptValue[] counts)
        {
            Array.Reverse(items);
            Array.Reverse(counts);
            StableSort.Sort(ctx, items, counts);
            Array.Reverse(items);
        }

        // Since 3.10 a missing key counts as zero: Counter(a=3) == Counter(a=3, b=0).
        internal static bool EqualCounts(CounterValue a, CounterValue b, EvalContext ctx, int depth)
        {
            return ContainedEqual(a, b, ctx, depth) && ContainedEqual(b, a, ctx, depth);
        }

        private static bool ContainedEqual(CounterValue a, CounterValue b, EvalContext ctx, int depth)
        {
            ScriptValue zero = ctx.Values.Int(0);
            for (int p = a.Table.WalkStart(ctx); p < a.Table.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, av, bv;
                if (!a.Table.TryGetEntryAt(p, out h, out k, out av))
                    continue;
                if (!b.TryGet(k, ctx, out bv))
                    bv = zero;
                if (!PyOps.Equals(av, bv, ctx, depth + 1))
                    return false;
            }
            return true;
        }

        // Counter(iterable_or_mapping=None, **kwargs) and update/subtract, as CPython words them: a mapping into an
        // empty counter is a plain dict update (the values go in as they are, a str or None included), into a filled
        // one each is `count + self.get(key, 0)`; subtract is `self.get(key, 0) - count`. The keywords are a second
        // update with a mapping, so the emptiness is read again after the positional part.
        internal static void UpdateFrom(EvalContext ctx, CounterValue c, ScriptValue source, KwArgs kw, int sign)
        {
            ChainMapValue chain = source as ChainMapValue;
            if (chain != null)
                source = chain.Merged(ctx);   // a Mapping: its counts, not one each per key
            if (source != null && source.Kind != ValueKind.None)
            {
                if (source.Kind != ValueKind.Dict)
                {
                    ScriptValue one = ctx.Values.Int(1);
                    PyBinOp op = sign > 0 ? PyBinOp.Add : PyBinOp.Sub;
                    IScriptIterator it = PyOps.GetIterator(source, ctx);
                    ScriptValue el;
                    while (it.MoveNext(ctx, out el))
                        c.SetItem(el, PyOps.BinaryOp(op, c.CountOf(el, ctx), one, ctx), ctx);
                }
                else if (sign > 0 && c.Count == 0)
                {
                    DictMethods.Merge(ctx, c, source);
                }
                else
                {
                    var it = new DictItemsIterator(((DictValue)source).Table);
                    ScriptValue item;
                    while (it.MoveNext(ctx, out item))
                    {
                        TupleValue t = (TupleValue)item;
                        Bump(ctx, c, t.Items[0], t.Items[1], sign);
                    }
                }
            }
            bool plain = sign > 0 && c.Count == 0;
            for (int j = 0; j < kw.Count; j++)
            {
                ScriptValue key = ctx.Values.Str(kw.NameAt(j));
                if (plain)
                    c.SetItem(key, kw.ValueAt(j), ctx);
                else
                    Bump(ctx, c, key, kw.ValueAt(j), sign);
            }
        }

        private ScriptValue CountOf(ScriptValue key, EvalContext ctx)
        {
            ScriptValue cur;
            return TryGet(key, ctx, out cur) ? cur : ctx.Values.Int(0);
        }

        // One (key, count) of a mapping: the count is the left operand of the sum, as in CPython's update.
        private static void Bump(EvalContext ctx, CounterValue c, ScriptValue key, ScriptValue count, int sign)
        {
            ScriptValue cur = c.CountOf(key, ctx);
            ScriptValue nv = sign > 0
                ? PyOps.BinaryOp(PyBinOp.Add, count, cur, ctx)
                : PyOps.BinaryOp(PyBinOp.Sub, cur, count, ctx);
            c.SetItem(key, nv, ctx);   // keeps zeros/negatives (unlike the binary operators)
        }

        // c1 + c2 / - / & / | -> a new Counter dropping counts <= 0 (_keep_positive parity).
        protected internal override ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        {
            CounterValue o = other as CounterValue;
            if (o == null)
                return null;   // Counter op non-Counter -> unsupported (CPython returns NotImplemented)
            CounterValue left = reflected ? o : this;
            CounterValue right = reflected ? this : o;
            switch (op)
            {
                case PyBinOp.Add: return Combine(ctx, left, right, CombineOp.Add);
                case PyBinOp.Sub: return Combine(ctx, left, right, CombineOp.Sub);
                case PyBinOp.BitAnd: return Combine(ctx, left, right, CombineOp.And);
                case PyBinOp.BitOr: return Combine(ctx, left, right, CombineOp.Or);
                default: return null;
            }
        }

        // +c == Counter() + c (keep positives); -c == Counter() - c (negate, keep where negative).
        protected internal override ScriptValue UnaryOpCore(PyUnaryOp op, EvalContext ctx)
        {
            if (op == PyUnaryOp.Pos)
                return Combine(ctx, ctx.Values.Counter(Count), this, CombineOp.Add);
            if (op == PyUnaryOp.Neg)
                return Combine(ctx, ctx.Values.Counter(Count), this, CombineOp.Sub);
            return null;
        }

        private static CounterValue Combine(EvalContext ctx, CounterValue a, CounterValue b, CombineOp op)
        {
            CounterValue result = ctx.Values.Counter(a.Count + b.Count);
            ScriptValue zero = ctx.Values.Int(0);
            var ita = new DictItemsIterator(a.Table);
            ScriptValue item;
            while (ita.MoveNext(ctx, out item))
            {
                TupleValue t = (TupleValue)item;
                ScriptValue k = t.Items[0], av = t.Items[1];
                ScriptValue bv;
                if (!b.TryGet(k, ctx, out bv))
                    bv = zero;
                ScriptValue nv;
                switch (op)
                {
                    case CombineOp.Add: nv = PyOps.BinaryOp(PyBinOp.Add, av, bv, ctx); break;
                    case CombineOp.Sub: nv = PyOps.BinaryOp(PyBinOp.Sub, av, bv, ctx); break;
                    case CombineOp.And: nv = Less(ctx, av, bv) ? av : bv; break;
                    default: nv = Less(ctx, av, bv) ? bv : av; break;   // Or: max
                }
                if (Positive(ctx, nv))
                    result.SetItem(k, nv, ctx);
            }
            var itb = new DictItemsIterator(b.Table);
            while (itb.MoveNext(ctx, out item))
            {
                TupleValue t = (TupleValue)item;
                ScriptValue k = t.Items[0], bv = t.Items[1];
                ScriptValue ignore;
                if (a.TryGet(k, ctx, out ignore))
                    continue;
                ScriptValue nv;
                switch (op)
                {
                    case CombineOp.Add: nv = bv; break;
                    case CombineOp.Sub: nv = Less(ctx, bv, zero) ? PyOps.BinaryOp(PyBinOp.Sub, zero, bv, ctx) : zero; break;   // CPython asks `count < 0` first
                    case CombineOp.And: nv = zero; break;
                    default: nv = bv; break;   // Or
                }
                if (Positive(ctx, nv))
                    result.SetItem(k, nv, ctx);
            }
            return result;
        }

        // 3.10 rich comparisons: multiset inclusion with missing keys counting as 0; a partial order, so
        // an incomparable pair answers False to every operator.
        // depth: the nesting this comparison runs at; counts compare one level deeper (a Counter of Counters).
        internal static bool Inclusion(PyCmpOp op, CounterValue a, CounterValue b, EvalContext ctx, int depth)
        {
            bool le = AllLe(a, b, ctx, depth + 1), ge = AllLe(b, a, ctx, depth + 1);
            switch (op)
            {
                case PyCmpOp.Le: return le;
                case PyCmpOp.Ge: return ge;
                case PyCmpOp.Lt: return le && !ge;
                default: return ge && !le;   // Gt
            }
        }

        private static bool AllLe(CounterValue a, CounterValue b, EvalContext ctx, int depth)
        {
            ScriptValue zero = ctx.Values.Int(0);
            for (int p = a.Table.WalkStart(ctx); p < a.Table.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, av, bv;
                if (!a.Table.TryGetEntryAt(p, out h, out k, out av))
                    continue;
                if (!b.TryGet(k, ctx, out bv))
                    bv = zero;
                if (!PyOps.RichCompare(PyCmpOp.Le, av, bv, ctx, depth).IsTruthy(ctx))
                    return false;
            }
            for (int p = b.Table.WalkStart(ctx); p < b.Table.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, bv, ignore;
                if (!b.Table.TryGetEntryAt(p, out h, out k, out bv) || a.TryGet(k, ctx, out ignore))
                    continue;
                if (!PyOps.RichCompare(PyCmpOp.Le, zero, bv, ctx, depth).IsTruthy(ctx))
                    return false;
            }
            return true;
        }

        private static bool Positive(EvalContext ctx, ScriptValue v)
        {
            return PyOps.RichCompare(PyCmpOp.Gt, v, ctx.Values.Int(0), ctx).IsTruthy(ctx);
        }

        private static bool Less(EvalContext ctx, ScriptValue a, ScriptValue b)
        {
            return PyOps.RichCompare(PyCmpOp.Lt, a, b, ctx).IsTruthy(ctx);
        }

        // ---- instance method slots ----

        private static ScriptValue MostCommon(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            CounterValue c = (CounterValue)self;
            var items = new List<ScriptValue>();
            var counts = new List<ScriptValue>();
            for (int p = c.Table.WalkStart(ctx); p < c.Table.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, v;
                if (!c.Table.TryGetEntryAt(p, out h, out k, out v))
                    continue;
                items.Add(ctx.Values.Tuple(new[] { k, v }));
                counts.Add(v);
            }
            ScriptValue[] itemArr = items.ToArray();
            SortByCountDescending(ctx, itemArr, counts.ToArray());
            int take = itemArr.Length;
            if (args.Length >= 1 && args[0].Kind != ValueKind.None)
            {
                PyOps.RichCompare(PyCmpOp.Ge, args[0], ctx.Values.Int(itemArr.Length), ctx, 0);   // heapq.nlargest's first read: a str fails here
                BigInteger n = Coerce.ToIndex(ctx, args[0], "most_common");
                take = n < 0 ? 0 : (n > itemArr.Length ? itemArr.Length : (int)n);
            }
            ListValue result = ctx.Values.List(take);
            for (int i = 0; i < take; i++)
                result.Add(itemArr[i], ctx);
            return result;
        }

        // CPython's chain.from_iterable(starmap(repeat, self.items())): lazy over the live table, so a
        // mutation is the dict iterator's RuntimeError at the next key. A copy of the keys and counts per
        // call was about 48 bytes an entry, uncharged: 12 kept over a million keys held 565 MB.
        private static ScriptValue Elements(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            return ctx.Values.EngineIterator(new CounterElementsIterator(((CounterValue)self).Table), "itertools.chain");
        }

        private static ScriptValue Update(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Unpack(ctx, args, "update", 0, 1);
            UpdateFrom(ctx, (CounterValue)self, args.Length == 1 ? args[0] : null, kw, +1);
            return ctx.Values.None;
        }

        private static ScriptValue Subtract(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Unpack(ctx, args, "subtract", 0, 1);
            UpdateFrom(ctx, (CounterValue)self, args.Length == 1 ? args[0] : null, kw, -1);
            return ctx.Values.None;
        }

        // total() (3.10): sum(self.values()), through the value model so non-int counts add as they would.
        private static ScriptValue Total(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.None(ctx, args, kw, "total");
            ScriptValue acc = ctx.Values.Int(0);
            var t = ((CounterValue)self).Table;
            for (int p = t.WalkStart(ctx); p < t.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, v;
                if (t.TryGetEntryAt(p, out h, out k, out v))
                    acc = PyOps.BinaryOp(PyBinOp.Add, acc, v, ctx);
            }
            return acc;
        }

        internal static ScriptValue FromKeys(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            throw Raise.NotImplemented(ctx, "Counter.fromkeys() is undefined.  Use Counter(iterable) instead.");
        }

        private static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            var s = new Slots(v => ((CounterValue)v).Count, DictMethods.BuildSlots());
            s.Linear("most_common", Args.BoundKeywords("most_common", MostCommon, "n"));
            s.Linear("elements", 0, 0, Elements);
            s.Linear("total", Total);
            s.Method("update", Update);
            s.Method("subtract", Subtract);
            s.Linear("copy", 0, 0, (self, a, kw, ctx) =>
            {
                return ctx.Values.CopyDict((CounterValue)self);
            });
            return s.Table;
        }
    }

    // Counter.elements(): each key repeated count times (count<=0 skipped); lazy and step-charged. A count that is
    // not an int is repeat()'s TypeError when the walk reaches its key, and that error ends the walk (chain() drops
    // its source on any error).
    internal sealed class CounterElementsIterator : ScriptIteratorBase
    {
        private readonly ItemSource _items;
        private ScriptValue _key;
        private BigInteger _rem;
        private bool _ended;

        internal CounterElementsIterator(OrderedTable table)
        {
            _items = new ItemSource(table);   // the version is taken now, as items() is iterated from the call on
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            while (_rem <= 0)
            {
                ScriptValue k;
                if (_ended || !_items.MoveNext(ctx, out k))
                {
                    value = null;
                    return false;
                }
                _ended = true;   // stays set when the count is refused
                long n = Coerce.ToSsize64(ctx, Coerce.ToIndex(ctx, _items.Count, "elements"));
                _ended = false;
                _key = k;
                _rem = n;
            }
            _rem -= 1;
            value = _key;
            return true;
        }

        // The table's (key, count) pairs, version-guarded as dict.items() is; the count is kept aside.
        private sealed class ItemSource : TableViewIterator
        {
            internal ScriptValue Count;

            internal ItemSource(OrderedTable t) : base(t) { }

            protected override string MutationText(int at, int now) { return DictMutation.Text(at, now); }

            protected override ScriptValue Project(EvalContext ctx, long hash, ScriptValue key, ScriptValue value)
            {
                Count = value;
                return key;
            }
        }
    }
}

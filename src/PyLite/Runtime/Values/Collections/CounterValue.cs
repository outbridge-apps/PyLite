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

        // Counter(iterable_or_mapping=None, **kwargs) and update/subtract share this add/subtract loop.
        internal static void UpdateFrom(EvalContext ctx, CounterValue c, ScriptValue source, KwArgs kw, int sign)
        {
            if (source != null && source.Kind != ValueKind.None)
            {
                if (source.Kind == ValueKind.Dict)
                {
                    var it = new DictItemsIterator(((DictValue)source).Table);
                    ScriptValue item;
                    while (it.MoveNext(ctx, out item))
                    {
                        TupleValue t = (TupleValue)item;
                        Bump(ctx, c, t.Items[0], t.Items[1], sign);
                    }
                }
                else
                {
                    IScriptIterator it = PyOps.GetIterator(source, ctx);
                    ScriptValue el;
                    while (it.MoveNext(ctx, out el))
                        Bump(ctx, c, el, ctx.Values.Int(1), sign);
                }
            }
            for (int j = 0; j < kw.Count; j++)
                Bump(ctx, c, ctx.Values.Str(kw.NameAt(j)), kw.ValueAt(j), sign);
        }

        private static void Bump(EvalContext ctx, CounterValue c, ScriptValue key, ScriptValue delta, int sign)
        {
            ScriptValue cur;
            if (!c.TryGet(key, ctx, out cur))
                cur = ctx.Values.Int(0);
            ScriptValue nv = sign > 0
                ? PyOps.BinaryOp(PyBinOp.Add, cur, delta, ctx)
                : PyOps.BinaryOp(PyBinOp.Sub, cur, delta, ctx);
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
                    case CombineOp.Sub: nv = PyOps.BinaryOp(PyBinOp.Sub, zero, bv, ctx); break;
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
        internal static bool Inclusion(PyCmpOp op, CounterValue a, CounterValue b, EvalContext ctx)
        {
            bool le = AllLe(a, b, ctx), ge = AllLe(b, a, ctx);
            switch (op)
            {
                case PyCmpOp.Le: return le;
                case PyCmpOp.Ge: return ge;
                case PyCmpOp.Lt: return le && !ge;
                default: return ge && !le;   // Gt
            }
        }

        private static bool AllLe(CounterValue a, CounterValue b, EvalContext ctx)
        {
            ScriptValue zero = ctx.Values.Int(0);
            for (int p = 0; p < a.Table.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, av, bv;
                if (!a.Table.TryGetEntryAt(p, out h, out k, out av))
                    continue;
                if (!b.TryGet(k, ctx, out bv))
                    bv = zero;
                if (!PyOps.RichCompare(PyCmpOp.Le, av, bv, ctx).IsTruthy(ctx))
                    return false;
            }
            for (int p = 0; p < b.Table.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, bv, ignore;
                if (!b.Table.TryGetEntryAt(p, out h, out k, out bv) || a.TryGet(k, ctx, out ignore))
                    continue;
                if (!PyOps.RichCompare(PyCmpOp.Le, zero, bv, ctx).IsTruthy(ctx))
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
            var negKeys = new List<ScriptValue>();
            ScriptValue zero = ctx.Values.Int(0);
            for (int p = 0; p < c.Table.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, v;
                if (!c.Table.TryGetEntryAt(p, out h, out k, out v))
                    continue;
                items.Add(ctx.Values.Tuple(new[] { k, v }));
                negKeys.Add(PyOps.BinaryOp(PyBinOp.Sub, zero, v, ctx));   // sort ascending by -count = desc count
            }
            ScriptValue[] itemArr = items.ToArray();
            ScriptValue[] keyArr = negKeys.ToArray();
            StableSort.Sort(ctx, itemArr, keyArr);   // stable -> ties keep insertion order
            int take = itemArr.Length;
            if (args.Length >= 1 && args[0].Kind != ValueKind.None)
            {
                IntValue iv = args[0] as IntValue;
                BigInteger n = iv != null ? iv.Value : (args[0] is BoolValue && ((BoolValue)args[0]).Value ? BigInteger.One : BigInteger.Zero);
                take = n < 0 ? 0 : (n > itemArr.Length ? itemArr.Length : (int)n);
            }
            ListValue result = ctx.Values.List(take);
            for (int i = 0; i < take; i++)
                result.Add(itemArr[i], ctx);
            return result;
        }

        private static ScriptValue Elements(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            CounterValue c = (CounterValue)self;
            var keys = new List<ScriptValue>();
            var counts = new List<BigInteger>();
            for (int p = 0; p < c.Table.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, v;
                if (!c.Table.TryGetEntryAt(p, out h, out k, out v))
                    continue;
                BigInteger n;
                if (TryPositiveCount(v, out n))
                {
                    keys.Add(k);
                    counts.Add(n);
                }
            }
            return ctx.Values.Iterator(new CounterElementsIterator(keys, counts), "itertools.chain");
        }

        private static bool TryPositiveCount(ScriptValue v, out BigInteger n)
        {
            IntValue iv = v as IntValue;
            if (iv != null)
            {
                n = iv.Value;
                return n > 0;
            }
            if (v is BoolValue && ((BoolValue)v).Value)
            {
                n = BigInteger.One;
                return true;
            }
            n = BigInteger.Zero;
            return false;
        }

        private static ScriptValue Update(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (args.Length > 1)
                throw Raise.TypeError(ctx, "update expected at most 1 argument, got " + args.Length);
            UpdateFrom(ctx, (CounterValue)self, args.Length == 1 ? args[0] : null, kw, +1);
            return ctx.Values.None;
        }

        private static ScriptValue Subtract(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            if (args.Length > 1)
                throw Raise.TypeError(ctx, "subtract expected at most 1 argument, got " + args.Length);
            UpdateFrom(ctx, (CounterValue)self, args.Length == 1 ? args[0] : null, kw, -1);
            return ctx.Values.None;
        }

        // total() (3.10): sum(self.values()), through the value model so non-int counts add as they would.
        private static ScriptValue Total(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.None(ctx, args, kw, "total");
            ScriptValue acc = ctx.Values.Int(0);
            var t = ((CounterValue)self).Table;
            for (int p = 0; p < t.EntriesUsed; p++)
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
            s.Linear("most_common", MostCommon);
            s.Linear("elements", Elements);
            s.Linear("total", Total);
            s.Method("update", Update);
            s.Method("subtract", Subtract);
            s.Linear("copy", (self, a, kw, ctx) =>
            {
                CounterValue src = (CounterValue)self;
                CounterValue r = ctx.Values.Counter(src.Count);
                DictMethods.Merge(ctx, r, src);
                return r;
            });
            return s.Table;
        }
    }

    // Counter.elements(): each key repeated count times (count<=0 skipped); lazy and step-charged.
    internal sealed class CounterElementsIterator : ScriptIteratorBase
    {
        private readonly List<ScriptValue> _keys;
        private readonly List<BigInteger> _counts;
        private int _idx = -1;
        private BigInteger _rem;

        internal CounterElementsIterator(List<ScriptValue> keys, List<BigInteger> counts)
        {
            _keys = keys;
            _counts = counts;
        }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            while (_rem <= 0)
            {
                _idx++;
                if (_idx >= _keys.Count)
                {
                    value = null;
                    return false;
                }
                _rem = _counts[_idx];
            }
            _rem -= 1;
            value = _keys[_idx];
            return true;
        }
    }
}

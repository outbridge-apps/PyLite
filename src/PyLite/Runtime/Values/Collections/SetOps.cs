using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // The one set algebra: operators take one operand (a set, or any iterable for the dict views), methods
    // any number of iterables. Every result keeps the left table's insertion order.
    internal static class SetOps
    {
        // Every element of src into t. A set's stored hashes are reused: no iterator, no rehash.
        internal static void AddAll(OrderedTable t, ScriptValue src, EvalContext ctx)
        {
            SetValue s = src as SetValue;
            if (s != null)
            {
                AddTable(t, s.Table, ctx);
                return;
            }
            IScriptIterator it = PyOps.GetIterator(src, ctx);
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
                t.InsertOrUpdate(v, PyOps.Hash(v, ctx, 0), null, ctx, 0);
        }

        private static void AddTable(OrderedTable t, OrderedTable src, EvalContext ctx)
        {
            if (ReferenceEquals(src, t))
                return;
            for (int p = 0; p < src.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, unused;
                if (src.TryGetEntryAt(p, out h, out k, out unused))
                    t.InsertOrUpdate(k, h, null, ctx, 0);
            }
        }

        internal static OrderedTable Union(OrderedTable a, ScriptValue[] others, EvalContext ctx)
        {
            OrderedTable t = a.CloneShallow(ctx);
            foreach (ScriptValue o in others)
                AddAll(t, o, ctx);
            return t;
        }

        // The elements of `a` found in `b` (intersection) or not found in it (difference).
        internal static OrderedTable Filter(OrderedTable a, ScriptValue b, bool keepFound, EvalContext ctx)
        {
            return Keep(a, Membership(b, ctx), keepFound, ctx);
        }

        private static OrderedTable Keep(OrderedTable a, OrderedTable m, bool keepFound, EvalContext ctx)
        {
            var t = new OrderedTable(ctx, a.Count);
            for (int p = 0; p < a.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, unused;
                if (a.TryGetEntryAt(p, out h, out k, out unused) && m.ContainsKey(k, h, ctx, 0) == keepFound)
                    t.InsertOrUpdate(k, h, null, ctx, 0);
            }
            return t;
        }

        // Intersection and difference are associative, so N operands filter one after another.
        internal static OrderedTable Filter(OrderedTable a, ScriptValue[] others, bool keepFound, EvalContext ctx)
        {
            OrderedTable t = null;
            foreach (ScriptValue o in others)
                t = Filter(t ?? a, o, keepFound, ctx);
            return t ?? a.CloneShallow(ctx);
        }

        internal static OrderedTable SymmetricDifference(OrderedTable a, ScriptValue b, EvalContext ctx)
        {
            OrderedTable m = Membership(b, ctx);
            OrderedTable t = Keep(a, m, false, ctx);
            for (int p = 0; p < m.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, unused;
                if (m.TryGetEntryAt(p, out h, out k, out unused) && !a.ContainsKey(k, h, ctx, 0))
                    t.InsertOrUpdate(k, h, null, ctx, 0);
            }
            return t;
        }

        // Every element of a is in b; stops at the first miss.
        internal static bool AllContained(OrderedTable a, OrderedTable b, EvalContext ctx, int depth)
        {
            for (int p = 0; p < a.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, unused;
                if (a.TryGetEntryAt(p, out h, out k, out unused) && !b.ContainsKey(k, h, ctx, depth + 1))
                    return false;
            }
            return true;
        }

        internal static bool Disjoint(OrderedTable a, ScriptValue b, EvalContext ctx)
        {
            IScriptIterator it = PyOps.GetIterator(b, ctx);
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
            {
                if (a.ContainsKey(v, PyOps.Hash(v, ctx, 0), ctx, 0))
                    return false;
            }
            return true;
        }

        // A set's own table, or a fresh table of any other iterable's elements.
        internal static OrderedTable Membership(ScriptValue b, EvalContext ctx)
        {
            SetValue s = b as SetValue;
            if (s != null)
                return s.Table;
            var t = new OrderedTable(ctx, 8);
            AddAll(t, b, ctx);
            return t;
        }

        // The four set operators; null when op is not one of them. `b` may be any iterable (dict views use this).
        internal static SetValue Apply(PyBinOp op, SetValue a, ScriptValue b, EvalContext ctx, bool frozen)
        {
            OrderedTable t;
            switch (op)
            {
                case PyBinOp.BitOr:
                    t = a.Table.CloneShallow(ctx);
                    AddAll(t, b, ctx);
                    break;
                case PyBinOp.BitAnd: t = Filter(a.Table, b, true, ctx); break;
                case PyBinOp.Sub: t = Filter(a.Table, b, false, ctx); break;
                case PyBinOp.BitXor: t = SymmetricDifference(a.Table, b, ctx); break;
                default: return null;
            }
            return frozen ? ctx.Values.FrozenSet(t) : ctx.Values.Set(t);
        }

        // s |= t, &=, -=, ^= on the same set: |= adds in place, the others replace the contents.
        internal static void ApplyInPlace(PyBinOp op, SetValue s, ScriptValue b, EvalContext ctx)
        {
            switch (op)
            {
                case PyBinOp.BitOr: AddAll(s.Table, b, ctx); break;
                case PyBinOp.BitAnd: ReplaceWith(s, Filter(s.Table, b, true, ctx), ctx); break;
                case PyBinOp.Sub: ReplaceWith(s, Filter(s.Table, b, false, ctx), ctx); break;
                default: ReplaceWith(s, SymmetricDifference(s.Table, b, ctx), ctx); break;
            }
        }

        internal static void ReplaceWith(SetValue s, OrderedTable computed, EvalContext ctx)
        {
            s.Table.Clear(ctx);
            AddTable(s.Table, computed, ctx);
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
                return AllContained(sa.Table, sb.Table, ctx, 0);
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
    }
}

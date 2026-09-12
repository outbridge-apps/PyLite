using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Runtime.Values
{
    // set / frozenset method slots. Methods accept ANY iterables (operators, handled in
    // SetValue.BinaryOpCore, require set/frozenset). Result type = type of self. frozenset has no mutators.
    internal static class SetMethods
    {
        internal static IDictionary<string, SlotDescriptor> BuildSetSlots()
        {
            Slots s = NonMutating();
            s.Method("add", (self, a, kw, c) => { St(self).AddItem(Args.One(c, a, "add"), c); return c.Values.None; });
            s.Method("remove", (self, a, kw, c) => RemoveItem(c, St(self), a, true));
            s.Method("discard", (self, a, kw, c) => RemoveItem(c, St(self), a, false));
            s.Method("clear", (self, a, kw, c) => { St(self).Table.Clear(c); return c.Values.None; });
            s.Method("pop", (self, a, kw, c) => PopItem(c, St(self)));
            s.Method("update", (self, a, kw, c) => { foreach (ScriptValue o in a) AddAll(c, St(self), o); return c.Values.None; });
            s.Linear("intersection_update", (self, a, kw, c) => ReplaceWith(c, St(self), Intersect(c, St(self), a)));
            s.Linear("difference_update", (self, a, kw, c) => ReplaceWith(c, St(self), Diff(c, St(self), a)));
            s.Linear("symmetric_difference_update", (self, a, kw, c) => ReplaceWith(c, St(self), SymDiff(c, St(self), OneSet(c, a, "symmetric_difference_update"))));
            return s.Table;
        }

        internal static IDictionary<string, SlotDescriptor> BuildFrozenSlots()
        {
            Slots s = NonMutating();
            s.Method("copy", (self, a, kw, c) => self);   // frozenset copy is the same object
            return s.Table;
        }

        // Every non-mutating method walks self or builds a copy of it; the argument walks charge themselves.
        private static Slots NonMutating()
        {
            var s = new Slots(v => ((SetValue)v).Count);
            s.Linear("union", (self, a, kw, c) => Wrap(c, St(self), Union(c, St(self), a)));
            s.Linear("intersection", (self, a, kw, c) => Wrap(c, St(self), Intersect(c, St(self), a)));
            s.Linear("difference", (self, a, kw, c) => Wrap(c, St(self), Diff(c, St(self), a)));
            s.Linear("symmetric_difference", (self, a, kw, c) => Wrap(c, St(self), SymDiff(c, St(self), OneSet(c, a, "symmetric_difference"))));
            s.Linear("issubset", (self, a, kw, c) => c.Values.Bool(Subset(c, St(self).Table, Membership(c, OneSet(c, a, "issubset")))));
            s.Linear("issuperset", (self, a, kw, c) => c.Values.Bool(Subset(c, Membership(c, OneSet(c, a, "issuperset")), St(self).Table)));
            s.Linear("isdisjoint", (self, a, kw, c) => c.Values.Bool(Disjoint(c, St(self), OneSet(c, a, "isdisjoint"))));
            s.Linear("copy", (self, a, kw, c) => Wrap(c, St(self), CopyTable(c, St(self).Table)));   // frozenset overrides
            return s;
        }

        private static SetValue St(ScriptValue self) { return (SetValue)self; }

        private static ScriptValue OneSet(EvalContext c, ScriptValue[] a, string name)
        {
            return Args.One(c, a, name);
        }

        private static ScriptValue RemoveItem(EvalContext c, SetValue s, ScriptValue[] a, bool strict)
        {
            ScriptValue x = Args.One(c, a, strict ? "remove" : "discard");
            bool removed = s.RemoveItem(x, c);
            if (!removed && strict)
                throw Raise.KeyError(c, x);
            return c.Values.None;
        }

        private static ScriptValue PopItem(EvalContext c, SetValue s)
        {
            ScriptValue v = s.PopFirst(c);
            if (v == null)
                throw Raise.Make(c, PyExceptionTypes.KeyError, "pop from an empty set");
            return v;
        }

        private static void AddAll(EvalContext c, SetValue s, ScriptValue iterable)
        {
            IScriptIterator it = PyOps.GetIterator(iterable, c);
            ScriptValue v;
            while (it.MoveNext(c, out v))
                s.AddItem(v, c);
        }

        // ---- set algebra producing OrderedTables (order = self's insertion order) ----

        private static OrderedTable Union(EvalContext c, SetValue self, ScriptValue[] others)
        {
            OrderedTable t = self.Table.CloneShallow(c);
            foreach (ScriptValue o in others)
            {
                SetValue so = o as SetValue;
                if (so != null)
                {
                    // set.union(set): the other table already stores every element's hash, so this is
                    // the operator's entry walk — the iterator with a re-hash per element measured 2x
                    // the cost of a | b for the same result.
                    OrderedTable ot = so.Table;
                    for (int p = 0; p < ot.EntriesUsed; p++)
                    {
                        long h;
                        ScriptValue k, unused;
                        if (ot.TryGetEntryAt(p, out h, out k, out unused))
                            t.InsertOrUpdate(k, h, null, c, 0);
                    }
                    continue;
                }
                IScriptIterator it = PyOps.GetIterator(o, c);
                ScriptValue v;
                while (it.MoveNext(c, out v))
                    t.InsertOrUpdate(v, PyOps.Hash(v, c, 0), null, c, 0);
            }
            return t;
        }

        private static OrderedTable Intersect(EvalContext c, SetValue self, ScriptValue[] others)
        {
            var mems = new OrderedTable[others.Length];
            for (int i = 0; i < others.Length; i++)
                mems[i] = Membership(c, others[i]);
            var t = new OrderedTable(c, self.Count);
            ForEach(self.Table, (h, k) =>
            {
                for (int i = 0; i < mems.Length; i++)
                {
                    if (!mems[i].ContainsKey(k, h, c, 0))
                        return;
                }
                t.InsertOrUpdate(k, h, null, c, 0);
            });
            return t;
        }

        private static OrderedTable Diff(EvalContext c, SetValue self, ScriptValue[] others)
        {
            var mems = new OrderedTable[others.Length];
            for (int i = 0; i < others.Length; i++)
                mems[i] = Membership(c, others[i]);
            var t = new OrderedTable(c, self.Count);
            ForEach(self.Table, (h, k) =>
            {
                for (int i = 0; i < mems.Length; i++)
                {
                    if (mems[i].ContainsKey(k, h, c, 0))
                        return;
                }
                t.InsertOrUpdate(k, h, null, c, 0);
            });
            return t;
        }

        private static OrderedTable SymDiff(EvalContext c, SetValue self, ScriptValue other)
        {
            OrderedTable m = Membership(c, other);
            var t = new OrderedTable(c, self.Count);
            ForEach(self.Table, (h, k) => { if (!m.ContainsKey(k, h, c, 0)) t.InsertOrUpdate(k, h, null, c, 0); });
            ForEach(m, (h, k) => { if (!self.Table.ContainsKey(k, h, c, 0)) t.InsertOrUpdate(k, h, null, c, 0); });
            return t;
        }

        private static OrderedTable CopyTable(EvalContext c, OrderedTable t) { return t.CloneShallow(c); }

        private static bool Subset(EvalContext c, OrderedTable sub, OrderedTable super)
        {
            bool ok = true;
            ForEach(sub, (h, k) => { if (!super.ContainsKey(k, h, c, 0)) ok = false; });
            return ok;
        }

        private static bool Disjoint(EvalContext c, SetValue self, ScriptValue other)
        {
            IScriptIterator it = PyOps.GetIterator(other, c);
            ScriptValue v;
            while (it.MoveNext(c, out v))
            {
                if (self.Table.ContainsKey(v, PyOps.Hash(v, c, 0), c, 0))
                    return false;
            }
            return true;
        }

        private static OrderedTable Membership(EvalContext c, ScriptValue x)
        {
            if (x.Kind == ValueKind.Set || x.Kind == ValueKind.FrozenSet)
                return ((SetValue)x).Table;
            var t = new OrderedTable(c, 8);
            IScriptIterator it = PyOps.GetIterator(x, c);
            ScriptValue v;
            while (it.MoveNext(c, out v))
                t.InsertOrUpdate(v, PyOps.Hash(v, c, 0), null, c, 0);
            return t;
        }

        private static ScriptValue Wrap(EvalContext c, SetValue self, OrderedTable t)
        {
            if (self.Kind == ValueKind.FrozenSet)
                return c.Values.FrozenSet(t);
            c.Budget.ChargeAllocation(32);
            return new SetValue(t);
        }

        internal static ScriptValue ReplaceWith(EvalContext c, SetValue self, OrderedTable computed)
        {
            self.Table.Clear(c);
            ForEach(computed, (h, k) => self.Table.InsertOrUpdate(k, h, null, c, 0));
            return c.Values.None;
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
    }
}

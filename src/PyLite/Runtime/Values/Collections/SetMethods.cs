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
            s.Method("add", 1, 1, (self, a, kw, c) => { St(self).AddItem(Args.One(c, a, "add"), c); return c.Values.None; });
            s.Method("remove", 1, 1, (self, a, kw, c) => RemoveItem(c, St(self), a, true));
            s.Method("discard", 1, 1, (self, a, kw, c) => RemoveItem(c, St(self), a, false));
            s.Method("clear", 0, 0, (self, a, kw, c) => { St(self).Table.Clear(c); return c.Values.None; });
            s.Method("pop", 0, 0, (self, a, kw, c) => PopItem(c, St(self)));
            s.Method("update", 0, Slots.Many, (self, a, kw, c) =>
            {
                foreach (ScriptValue o in a)
                    SetOps.AddAll(St(self).Table, o, c);
                return c.Values.None;
            });
            s.Linear("intersection_update", 0, Slots.Many, (self, a, kw, c) => Replace(c, St(self), SetOps.Filter(St(self).Table, a, true, c)));
            s.Linear("difference_update", 0, Slots.Many, (self, a, kw, c) => Replace(c, St(self), SetOps.Filter(St(self).Table, a, false, c)));
            s.Linear("symmetric_difference_update", 1, 1, (self, a, kw, c) => Replace(c, St(self), SetOps.SymmetricDifference(St(self).Table, a[0], c)));
            return s.Table;
        }

        internal static IDictionary<string, SlotDescriptor> BuildFrozenSlots()
        {
            Slots s = NonMutating();
            s.Method("copy", 0, 0, (self, a, kw, c) => self);   // frozenset copy is the same object
            return s.Table;
        }

        // Every non-mutating method walks self or builds a copy of it; the argument walks charge themselves.
        private static Slots NonMutating()
        {
            var s = new Slots(v => ((SetValue)v).Count);
            s.Linear("union", 0, Slots.Many, (self, a, kw, c) => Wrap(c, St(self), SetOps.Union(St(self).Table, a, c)));
            s.Linear("intersection", 0, Slots.Many, (self, a, kw, c) => Wrap(c, St(self), SetOps.Filter(St(self).Table, a, true, c)));
            s.Linear("difference", 0, Slots.Many, (self, a, kw, c) => Wrap(c, St(self), SetOps.Filter(St(self).Table, a, false, c)));
            s.Linear("symmetric_difference", 1, 1, (self, a, kw, c) => Wrap(c, St(self), SetOps.SymmetricDifference(St(self).Table, a[0], c)));
            s.Linear("issubset", 1, 1, (self, a, kw, c) => c.Values.Bool(SetOps.AllContained(St(self).Table, SetOps.Membership(a[0], c), c, 0)));
            s.Linear("issuperset", 1, 1, (self, a, kw, c) => c.Values.Bool(SetOps.AllContained(SetOps.Membership(a[0], c), St(self).Table, c, 0)));
            s.Linear("isdisjoint", 1, 1, (self, a, kw, c) => c.Values.Bool(SetOps.Disjoint(St(self).Table, a[0], c)));
            s.Linear("copy", 0, 0, (self, a, kw, c) => Wrap(c, St(self), St(self).Table.CloneShallow(c)));   // frozenset overrides
            return s;
        }

        private static SetValue St(ScriptValue self) { return (SetValue)self; }

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

        // The result has self's type (set or frozenset).
        private static ScriptValue Wrap(EvalContext c, SetValue self, OrderedTable t)
        {
            return self.IsFrozen ? c.Values.FrozenSet(t) : c.Values.Set(t);
        }

        private static ScriptValue Replace(EvalContext c, SetValue self, OrderedTable computed)
        {
            SetOps.ReplaceWith(self, computed, c);
            return c.Values.None;
        }
    }
}

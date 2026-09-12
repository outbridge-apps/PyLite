using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // list and tuple method slots. Mutators return None; index/count use the core
    // Equal protocol (so 1/True/1.0 unify). Lives with the value model.
    internal static class ListMethods
    {
        // insert and pop charge the shifted tail themselves: their cost is the distance to the end,
        // not the length, and a stack's append/pop stays constant.
        internal static IDictionary<string, SlotDescriptor> BuildListSlots()
        {
            var s = new Slots(v => ((ListValue)v).Items.Count);
            s.Method("append", (self, a, kw, c) => Append(c, L(self), a));
            s.Method("extend", (self, a, kw, c) => Extend(c, L(self), a));
            s.Method("insert", (self, a, kw, c) => Insert(c, L(self), a));
            s.Linear("remove", (self, a, kw, c) => Remove(c, L(self), a));
            s.Method("pop", (self, a, kw, c) => Pop(c, L(self), a));
            s.Method("clear", (self, a, kw, c) => { L(self).Items.Clear(); return c.Values.None; });
            s.Linear("copy", (self, a, kw, c) => Copy(c, L(self)));
            s.Linear("reverse", (self, a, kw, c) => { L(self).Items.Reverse(); return c.Values.None; });
            s.Linear("count", (self, a, kw, c) => c.Values.Int(SequenceOps.Count(new ListSeq(L(self).Items), Args.One(c, a, "count"), c)));
            s.Linear("index", (self, a, kw, c) => IndexOf(c, new ListSeq(L(self).Items), a, "list"));
            s.Linear("sort", (self, a, kw, c) => Sort(c, L(self), kw));
            return s.Table;
        }

        internal static IDictionary<string, SlotDescriptor> BuildTupleSlots()
        {
            var s = new Slots(v => ((TupleValue)v).Items.Length);
            s.Linear("count", (self, a, kw, c) => c.Values.Int(SequenceOps.Count(new ArraySeq(((TupleValue)self).Items), Args.One(c, a, "count"), c)));
            s.Linear("index", (self, a, kw, c) => IndexOf(c, new ArraySeq(((TupleValue)self).Items), a, "tuple"));
            return s.Table;
        }

        private static ListValue L(ScriptValue self) { return (ListValue)self; }

        private static ScriptValue Append(EvalContext c, ListValue l, ScriptValue[] a)
        {
            l.Add(Args.One(c, a, "append"), c);
            return c.Values.None;
        }

        private static ScriptValue Extend(EvalContext c, ListValue l, ScriptValue[] a)
        {
            l.InPlaceConcat(Args.One(c, a, "extend"), c);
            return c.Values.None;
        }

        private static ScriptValue Insert(EvalContext c, ListValue l, ScriptValue[] a)
        {
            Args.Exactly(c, a, "insert", 2);
            int i = Coerce.ToClampedIndex(Coerce.ToIndex(c, a[0], "insert"), l.Items.Count);
            c.Budget.ChargeAllocation(8);
            c.Budget.ChargeLinear(l.Items.Count - i);
            l.Items.Insert(i, a[1]);
            return c.Values.None;
        }

        private static ScriptValue Remove(EvalContext c, ListValue l, ScriptValue[] a)
        {
            int i = SequenceOps.IndexOf(new ListSeq(l.Items), Args.One(c, a, "remove"), 0, l.Items.Count, c);
            if (i < 0)
                throw Raise.ValueError(c, "list.remove(x): x not in list");
            l.Items.RemoveAt(i);
            return c.Values.None;
        }

        private static ScriptValue Pop(EvalContext c, ListValue l, ScriptValue[] a)
        {
            if (l.Items.Count == 0)
                throw Raise.IndexError(c, "pop from empty list");
            BigInteger idx = a.Length >= 1 ? Coerce.ToIndex(c, a[0], "pop") : BigInteger.MinusOne;
            if (idx < 0)
                idx += l.Items.Count;
            if (idx < 0 || idx >= l.Items.Count)
                throw Raise.IndexError(c, "pop index out of range");
            int i = (int)idx;
            ScriptValue v = l.Items[i];
            c.Budget.ChargeLinear(l.Items.Count - i);
            l.Items.RemoveAt(i);
            return v;
        }

        private static ScriptValue Copy(EvalContext c, ListValue l)
        {
            ListValue r = c.Values.List(l.Items.Count);
            for (int i = 0; i < l.Items.Count; i++)
                r.Add(l.Items[i], c);
            return r;
        }

        private static ScriptValue IndexOf<TSeq>(EvalContext c, TSeq items, ScriptValue[] a, string typeName) where TSeq : struct, ISequence
        {
            Args.AtLeast(c, a, "index", 1);
            ScriptValue x = a[0];
            int start, end;
            Coerce.AdjustIndices(c, a.Length > 1 ? a[1] : null, a.Length > 2 ? a[2] : null, items.Count, out start, out end);
            int i = SequenceOps.IndexOf(items, x, start, end, c);
            if (i >= 0)
                return c.Values.Int(i);
            if (typeName == "tuple")
                throw Raise.ValueError(c, "tuple.index(x): x not in tuple");
            throw Raise.ValueError(c, x.Repr(c) + " is not in list");
        }

        private static ScriptValue Sort(EvalContext c, ListValue l, KwArgs kw)
        {
            ScriptValue key = null;
            bool reverse = false;
            var r = new KwReader(c, kw, "sort");
            key = r.GetOrNull("key");
            reverse = r.Bool("reverse", reverse);
            r.RejectInvalid();

            ScriptValue[] saved = l.Items.ToArray();
            l.Items.Clear();
            if (reverse)
                Array.Reverse(saved);
            ScriptValue[] keys = null;
            if (key != null)
            {
                keys = new ScriptValue[saved.Length];
                for (int i = 0; i < saved.Length; i++)
                    keys[i] = c.CallHook1(key, saved[i]);
            }
            StableSort.Sort(c, saved, keys);
            if (reverse)
                Array.Reverse(saved);

            if (l.Items.Count != 0)
            {
                l.Items.Clear();
                l.Items.AddRange(saved);
                throw Raise.ValueError(c, "list modified during sort");
            }
            l.Items.AddRange(saved);
            return c.Values.None;
        }
    }
}

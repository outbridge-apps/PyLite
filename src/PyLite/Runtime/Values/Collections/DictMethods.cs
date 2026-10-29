using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Runtime.Values
{
    // dict method slots. Views are the existing DictViewValue (live, version-guarded).
    internal static class DictMethods
    {
        internal static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            var s = new Slots(v => ((DictValue)v).Count);
            s.Method("get", 1, 2, (self, a, kw, c) => Get(c, D(self), a));
            s.Method("pop", 1, 2, (self, a, kw, c) => Pop(c, D(self), a));
            s.Method("popitem", 0, 0, (self, a, kw, c) => PopItem(c, D(self)));
            s.Method("setdefault", 1, 2, (self, a, kw, c) => SetDefault(c, D(self), a));
            s.Method("update", (self, a, kw, c) => Update(c, D(self), a, kw));
            s.Method("clear", 0, 0, (self, a, kw, c) => { D(self).Table.Clear(c); return c.Values.None; });
            s.Linear("copy", 0, 0, (self, a, kw, c) => CopyDict(c, D(self)));
            s.Method("keys", 0, 0, (self, a, kw, c) => D(self).KeysView(c));
            s.Method("values", 0, 0, (self, a, kw, c) => D(self).ValuesView(c));
            s.Method("items", 0, 0, (self, a, kw, c) => D(self).ItemsView(c));
            return s.Table;
        }

        // The set-like views (keys/items) expose the one Set method that takes an arbitrary iterable.
        internal static IDictionary<string, SlotDescriptor> BuildViewSlots()
        {
            var s = new Slots(v => ((DictViewValue)v).Owner.Count);
            s.Method("isdisjoint", 1, 1, (self, a, kw, c) => c.Values.Bool(IsDisjoint(c, self, a)));
            return s.Table;
        }

        private static bool IsDisjoint(EvalContext c, ScriptValue view, ScriptValue[] a)
        {
            Args.Exactly(c, a, "isdisjoint", 1);
            IScriptIterator it = PyOps.GetIterator(a[0], c);
            ScriptValue v;
            while (it.MoveNext(c, out v))
                if (PyOps.Contains(v, view, c))
                    return false;
            return true;
        }

        private static DictValue D(ScriptValue self) { return (DictValue)self; }

        private static ScriptValue Get(EvalContext c, DictValue d, ScriptValue[] a)
        {
            ScriptValue v;
            if (d.TryGet(a[0], c, out v))
                return v;
            return a.Length == 2 ? a[1] : c.Values.None;
        }

        private static ScriptValue Pop(EvalContext c, DictValue d, ScriptValue[] a)
        {
            ScriptValue v;
            if (d.TryGet(a[0], c, out v))
            {
                d.DelItemOrThrow(a[0], c);
                return v;
            }
            if (a.Length == 2)
                return a[1];
            throw Raise.KeyError(c, a[0]);
        }

        private static ScriptValue PopItem(EvalContext c, DictValue d)
        {
            OrderedTable t = d.Table;
            int p = t.FindLastLive();
            if (p >= 0)
            {
                long h;
                ScriptValue k, v;
                t.TryGetEntryAt(p, out h, out k, out v);
                t.Delete(k, h, c, 0);
                return c.Values.Tuple(new[] { k, v });
            }
            throw Raise.Make(c, PyExceptionTypes.KeyError, "popitem(): dictionary is empty");
        }

        private static ScriptValue SetDefault(EvalContext c, DictValue d, ScriptValue[] a)
        {
            ScriptValue v;
            if (d.TryGet(a[0], c, out v))
                return v;
            ScriptValue dflt = a.Length == 2 ? a[1] : c.Values.None;
            d.SetItem(a[0], dflt, c);
            return dflt;
        }

        private static ScriptValue Update(EvalContext c, DictValue d, ScriptValue[] a, KwArgs kw)
        {
            Args.Unpack(c, a, "update", 0, 1);
            if (a.Length == 1)
                Merge(c, d, a[0]);
            for (int j = 0; j < kw.Count; j++)
                d.SetItem(c.Values.Str(kw.NameAt(j)), kw.ValueAt(j), c);
            return c.Values.None;
        }

        private static ScriptValue CopyDict(EvalContext c, DictValue d)
        {
            return c.Values.CopyDict(d);
        }

        // dict(source, **kw) and the collections subtypes' constructors: the positional mapping or pairs, then
        // the keywords as str keys.
        internal static void InitFrom(EvalContext c, DictValue d, ScriptValue source, KwArgs kw)
        {
            if (source != null)
                Merge(c, d, source);
            for (int j = 0; j < kw.Count; j++)
                d.SetItem(c.Values.Str(kw.NameAt(j)), kw.ValueAt(j), c);
        }

        // dict.fromkeys(iterable[, value]) into d: every key maps to the same value object.
        internal static DictValue FillFromKeys(EvalContext c, DictValue d, ScriptValue keys, ScriptValue value)
        {
            IScriptIterator it = PyOps.GetIterator(keys, c);
            ScriptValue key;
            while (it.MoveNext(c, out key))
                d.SetItem(key, value, c);
            return d;
        }

        // Shared dict-population semantics (mapping copy or 2-sequence iterable); reused by dict() ctor.
        internal static void Merge(EvalContext c, DictValue d, ScriptValue source)
        {
            ChainMapValue cm = source as ChainMapValue;   // dict(chainmap) merges its key union
            if (cm != null)
                source = cm.Merged(c);
            DictValue srcDict = source as DictValue;
            if (srcDict != null)
            {
                IScriptIterator kit = new DictKeyIterator(srcDict.Table);
                ScriptValue key;
                while (kit.MoveNext(c, out key))
                    d.SetItem(key, srcDict.GetItemOrThrow(key, c), c);
                return;
            }
            IScriptIterator it = PyOps.GetIterator(source, c);
            ScriptValue element;
            int i = 0;
            while (it.MoveNext(c, out element))
            {
                IScriptIterator pair = PyOps.TryGetIterator(element, c);
                if (pair == null)
                    throw Raise.TypeError(c, "cannot convert dictionary update sequence element #" + i + " to a sequence");
                ScriptValue k = null, v = null, extra;
                bool has1 = pair.MoveNext(c, out k);
                bool has2 = has1 && pair.MoveNext(c, out v);
                int len = (has1 ? 1 : 0) + (has2 ? 1 : 0);
                if (has2)
                {
                    while (pair.MoveNext(c, out extra))
                        len++;
                }
                if (len != 2)
                    throw Raise.ValueError(c, "dictionary update sequence element #" + i + " has length " + len + "; 2 is required");
                d.SetItem(k, v, c);
                i++;
            }
        }
    }
}

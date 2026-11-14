using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Runtime.Values
{
    // xml.etree.ElementTree.Element. Kind==Opaque. THE PARITY TRAP: bool(el) == has
    // children (an element with only text is falsy). Tag uses Clark notation {uri}local for namespaced.
    internal sealed class ElementValue : ScriptValue, Modules.IScriptCopyable
    {
        // CPython's __copy__: a new element over the same attrib dict (an empty plain one is not shared: CPython keeps none)
        // and children.
        public ScriptValue CopyShallow(EvalContext ctx)
        {
            ctx.Values.PreCharge(8L * Children.Count);
            var e = new ElementValue(Tag, KeepsAttrib ? _attrib : null, ctx) { Text = Text, Tail = Tail };
            foreach (ScriptValue c in Children)
                Placing(ctx, c);   // every child is in two lists now
            e.Children.AddRange(Children);
            e.Changed(ctx);
            return e;
        }

        // A copy keeps the attrib unless it is an empty plain dict (CPython holds none for that one): an
        // empty OrderedDict or defaultdict the script assigned stays, with its type.
        private bool KeepsAttrib
        {
            get { return _attrib != null && (_attrib.Count > 0 || _attrib.GetType() != typeof(DictValue)); }
        }

        public ScriptValue EmptyClone(EvalContext ctx)
        {
            return new ElementValue(Tag, null, ctx);
        }

        public IReadOnlyList<ScriptValue> DeepChildren()
        {
            var all = new List<ScriptValue>(Children.Count + 4) { Tag, Text, Tail };
            if (_attrib != null)
                all.Add(_attrib);
            all.AddRange(Children);
            return all;
        }

        public void FillClone(EvalContext ctx, ScriptValue clone, Func<ScriptValue, ScriptValue> copyOf)
        {
            var e = (ElementValue)clone;
            e.Tag = copyOf(Tag);
            e.Text = copyOf(Text);
            e.Tail = copyOf(Tail);
            e._attrib = KeepsAttrib ? (DictValue)copyOf(_attrib) : null;
            ctx.Values.PreCharge(8L * Children.Count);
            foreach (ScriptValue c in Children)
            {
                ScriptValue copy = copyOf(c);   // a child twice in the original is one copy twice
                Placing(ctx, copy);
                e.Children.Add(copy);
            }
            e.Changed(ctx);
        }

        internal static readonly ScriptTypeInfo ElementType = new ScriptTypeInfo("xml.etree.ElementTree.Element", BuildSlots());

        internal ScriptValue Tag;                    // StrValue
        private DictValue _attrib;                   // live; null until asked for, as CPython's `extra` (a dict cost ~290 B)
        internal ScriptValue Text;                   // StrValue or None
        internal ScriptValue Tail;                   // StrValue or None
        internal readonly List<ScriptValue> Children = new List<ScriptValue>();

        // Before one more child: capped by MaxCollectionItems and the list growth charged (extend of
        // 4M-element lists ten times made 40M children for 30 MB charged).
        internal void Grow(EvalContext ctx)
        {
            if (Children.Count < Children.Capacity)
                return;
            ctx.Values.EnsureCollectionSize(Children.Count + 1L);
            ctx.Values.PreCharge(8L * Math.Max(4, Children.Count));
            Children.Capacity = (int)Math.Min(Math.Max(4L, 2L * Children.Count), ctx.Limits.MaxCollectionItems);   // doubling must not jump the cap
        }
        internal int Version;
        private int _lists;   // the children-list slots that hold the element now

        // Before the element goes into a children list, and after it leaves one. EvalContext.XmlShared counts the slots
        // past each element's first: while it is 0 every element is in at most one list, so a search knows a child's
        // parent without building ElementPath's parent map. A move (out of one list, into another) leaves it 0: a mark
        // that never cleared turned the fast path off for the rest of the run after one move (review 10, B-1).
        internal static void Placing(EvalContext ctx, ScriptValue child)
        {
            var e = (ElementValue)child;
            if (++e._lists >= 2)
                ctx.XmlShared++;
        }

        internal static void Unplacing(EvalContext ctx, ScriptValue child)
        {
            var e = (ElementValue)child;
            if (e._lists >= 2)
                ctx.XmlShared--;
            if (e._lists > 0)
                e._lists--;
        }

        internal bool InSeveralLists { get { return _lists >= 2; } }

        // Every change to a children list, here or anywhere in the run (EvalContext.XmlTreeVersion): ElementPath's parent
        // maps are shared by the run's searches while no list of their subtree has changed since one was built.
        internal void Changed(EvalContext ctx)
        {
            Version++;
            ctx.XmlTreeVersion++;
            if (ctx.XmlParentMaps != null)
                ctx.XmlParentMaps.ListChanged(ctx, this, null);
        }

        // No children and in no list: appended, it is one entry more in a parent map (AppendedLeaf).
        internal bool LooseLeaf { get { return _lists == 0 && Children.Count == 0; } }

        // Changed, for a change that only put leaf (a LooseLeaf until then) at the end of the children.
        internal void AppendedLeaf(EvalContext ctx, ElementValue leaf)
        {
            Version++;
            ctx.XmlTreeVersion++;
            if (ctx.XmlParentMaps != null)
                ctx.XmlParentMaps.ListChanged(ctx, this, ReferenceEquals(leaf, this) ? null : leaf);
        }

        // The element and its children list, as the CLR holds them (an empty attrib-less element measured 112 B).
        private const long ElementBytes = 112;

        // attrib: null for none (one is made when a script asks for it).
        internal ElementValue(ScriptValue tag, DictValue attrib, EvalContext ctx)
        {
            ctx.Values.PreCharge(ElementBytes);
            Tag = tag;
            _attrib = attrib;
            Text = ctx.Values.None;
            Tail = ctx.Values.None;
        }

        // The attributes to read: null when there are none.
        internal DictValue AttribOrNull { get { return _attrib; } }

        // The live attrib dict, made on first need.
        internal DictValue Attrib(EvalContext ctx)
        {
            if (_attrib == null)
                _attrib = ctx.Values.Dict(4);
            return _attrib;
        }

        public override ScriptTypeInfo TypeInfo { get { return ElementType; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        protected internal override bool IsTruthyCore(EvalContext ctx) { return Children.Count > 0; }
        protected internal override bool TryLengthCore(out long length) { length = Children.Count; return true; }
        protected internal override IScriptIterator GetIteratorCore(EvalContext ctx) { return new ElementChildIterator(this); }

        protected internal override ScriptValue GetItemCore(ScriptValue index, EvalContext ctx)
        {
            SliceValue sl = index as SliceValue;
            if (sl != null)
            {
                // e[a:b] -> a list of the selected children (ElementTree parity)
                SliceIndices ind = sl.Indices(Children.Count, ctx);
                ListValue result = ctx.Values.List((int)ind.Length);
                int step = ind.StepInt;
                for (long k = 0, pos = (long)ind.Start; k < (long)ind.Length; k++, pos += step)
                    result.Add(Children[(int)pos], ctx);
                return result;
            }
            int i = NormIndex(ctx, index);
            return Children[i];
        }

        protected internal override bool TrySetItemCore(ScriptValue index, ScriptValue value, EvalContext ctx)
        {
            SliceValue setSlice = index as SliceValue;
            if (setSlice != null)
                return SetSlice(setSlice, value, ctx);
            int i = NormIndex(ctx, index, "child assignment index out of range");
            // as append does: a non-Element child made every walker fail with an internal error
            ScriptValue child = RequireElement(ctx, value);
            Placing(ctx, child);
            Unplacing(ctx, Children[i]);
            Children[i] = child;
            Changed(ctx);
            return true;
        }

        protected internal override bool TryDelItemCore(ScriptValue index, EvalContext ctx)
        {
            SliceValue delSlice = index as SliceValue;
            if (delSlice != null)
                return DelSlice(delSlice, ctx);
            int i = NormIndex(ctx, index, "child assignment index out of range");
            ctx.Budget.ChargeLinear(Children.Count - i);   // the later children move down
            Unplacing(ctx, Children[i]);
            Children.RemoveAt(i);
            Changed(ctx);
            return true;
        }

        private bool SetSlice(SliceValue slice, ScriptValue value, EvalContext ctx)
        {
            if (PyOps.TryGetIterator(value, ctx) == null)
                throw Raise.TypeError(ctx, "assignment expects an iterable");
            ScriptValue[] items = Iter.ToArray(ctx, value);
            SliceIndices ind = slice.Indices(Children.Count, ctx);
            int start = (int)ind.Start, length = (int)ind.Length, step = ind.StepInt;
            if (step != 1 && items.Length != length)
                throw Raise.ValueError(ctx, "attempt to assign sequence of size " + items.Length + " to extended slice of size " + length);
            foreach (ScriptValue item in items)
                RequireChild(ctx, item);
            ctx.Budget.ChargeLinear(Children.Count + items.Length);
            if (step == 1)
            {
                ctx.Values.EnsureCollectionSize((long)Children.Count - length + items.Length);
                ctx.Values.PreCharge(8L * items.Length);
                foreach (ScriptValue item in items)   // once nothing can refuse the change: a refused one placed no child
                    Placing(ctx, item);
                for (int k = start; k < start + length; k++)
                    Unplacing(ctx, Children[k]);
                Children.RemoveRange(start, length);
                Children.InsertRange(start, items);
            }
            else
            {
                foreach (ScriptValue item in items)
                    Placing(ctx, item);
                for (int k = 0; k < length; k++)
                {
                    Unplacing(ctx, Children[start + k * step]);
                    Children[start + k * step] = items[k];
                }
            }
            Changed(ctx);
            return true;
        }

        private bool DelSlice(SliceValue slice, EvalContext ctx)
        {
            SliceIndices ind = slice.Indices(Children.Count, ctx);
            int start = (int)ind.Start, length = (int)ind.Length, step = ind.StepInt;
            ctx.Budget.ChargeLinear(Children.Count);
            if (step == 1)
            {
                for (int k = start; k < start + length; k++)
                    Unplacing(ctx, Children[k]);
                Children.RemoveRange(start, length);
            }
            else
            {
                var dropped = new HashSet<int>();
                for (int k = 0; k < length; k++)
                {
                    dropped.Add(start + k * step);
                    Unplacing(ctx, Children[start + k * step]);
                }
                int at = 0;
                Children.RemoveAll(_ => dropped.Contains(at++));
            }
            Changed(ctx);
            return true;
        }

        protected internal override bool TrySetAttrCore(string name, ScriptValue value, EvalContext ctx)
        {
            switch (name)
            {
                case "tag": Tag = value; return true;
                case "text": Text = value; return true;
                case "tail": Tail = value; return true;
                case "attrib":
                    DictValue d = value as DictValue;
                    if (d == null)
                        throw Raise.TypeError(ctx, "attrib must be a dict");
                    _attrib = d;
                    return true;
                default:
                    return false;
            }
        }

        private int NormIndex(EvalContext ctx, ScriptValue index, string outOfRange = "child index out of range")
        {
            if (index.Kind != ValueKind.Int && index.Kind != ValueKind.Bool)
                throw Raise.TypeError(ctx, "list indices must be integers or slices, not " + index.PyTypeName);
            BigInteger i = NumericOps.AsBigInteger(index);
            if (i < 0)
                i += Children.Count;
            if (i < 0 || i >= Children.Count)
                throw Raise.IndexError(ctx, outOfRange);
            return (int)i;
        }

        // ---- method slots ----

        private static ScriptValue Get_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ElementValue e = (ElementValue)self;
            ScriptValue[] named = Args.Named(ctx, a, kw, "get", "key", "default");
            if (named.Length == 0 || named[0] == null)
                throw Raise.TypeError(ctx, "get() missing required argument 'key' (pos 1)");
            ScriptValue v;
            if (e._attrib != null && e._attrib.TryGet(named[0], ctx, out v))
                return v;
            return named.Length == 2 && named[1] != null ? named[1] : ctx.Values.None;

        }

        private static ScriptValue Set_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ElementValue e = (ElementValue)self;
            Args.Exactly(ctx, a, "set", 2);
            e.Attrib(ctx).SetItem(Modules.XmlModule.UnQName(a[0]), Modules.XmlModule.UnQName(a[1]), ctx);
            return ctx.Values.None;
        }

        // makeelement(tag, attrib) -> a new element of the same kind, NOT attached to this one. CPython's
        // factory hook for subclasses; here it is the plain constructor, and the attrib dict is copied.
        private static ScriptValue MakeElement_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, a, kw, "makeelement", 2);
            DictValue src = a[1] as DictValue;
            if (src == null)
                throw Raise.TypeError(ctx, "makeelement() argument 2 must be dict, not " + (a[1].Kind == ValueKind.None ? "None" : a[1].PyTypeName));
            if (src.Count == 0)
                return new ElementValue(Modules.XmlModule.UnQName(a[0]), null, ctx);
            DictValue copy = ctx.Values.Dict(src.Count);
            var t = src.Table;
            for (int p = t.WalkStart(ctx); p < t.EntriesUsed; p++)
            {
                ctx.Budget.Step();
                long h;
                ScriptValue k, v;
                if (t.TryGetEntryAt(p, out h, out k, out v))
                    copy.SetItem(k, v, ctx);
            }
            return new ElementValue(Modules.XmlModule.UnQName(a[0]), copy, ctx);
        }

        private static ScriptValue Keys_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ElementValue e = (ElementValue)self;
            if (e._attrib == null)
                return ctx.Values.List(0);
            ListValue list = ctx.Values.List(e._attrib.Count);
            var t = e._attrib.Table;
            for (int p = t.WalkStart(ctx); p < t.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, v;
                if (t.TryGetEntryAt(p, out h, out k, out v))
                    list.Add(k, ctx);
            }
            return list;
        }

        private static ScriptValue Items_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ElementValue e = (ElementValue)self;
            if (e._attrib == null)
                return ctx.Values.List(0);
            ListValue list = ctx.Values.List(e._attrib.Count);
            var t = e._attrib.Table;
            for (int p = t.WalkStart(ctx); p < t.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, v;
                if (t.TryGetEntryAt(p, out h, out k, out v))
                    list.Add(ctx.Values.Tuple(new[] { k, v }), ctx);
            }
            return list;
        }

        private static ScriptValue Append_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ElementValue e = (ElementValue)self;
            var child = (ElementValue)RequireElement(ctx, Args.One(ctx, a, "append"));
            bool leaf = child.LooseLeaf;
            e.Grow(ctx);
            Placing(ctx, child);
            e.Children.Add(child);
            if (leaf)
                e.AppendedLeaf(ctx, child);
            else
                e.Changed(ctx);
            return ctx.Values.None;
        }

        private static ScriptValue Extend_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ElementValue e = (ElementValue)self;
            ScriptValue elements = Args.One(ctx, a, "extend");
            if (PyOps.TryGetIterator(elements, ctx) == null)
                throw Raise.TypeError(ctx, "'elements' must be an iterable");
            ScriptValue[] items = Iter.ToArray(ctx, elements);   // a copy first: e.extend(e) doubles it, as PySequence_Fast does
            try
            {
                foreach (ScriptValue v in items)
                {
                    ScriptValue child = RequireChild(ctx, v);
                    e.Grow(ctx);
                    Placing(ctx, child);
                    e.Children.Add(child);
                }
            }
            finally
            {
                e.Changed(ctx);   // a non-element part way raises with the earlier ones added, as CPython
            }
            return ctx.Values.None;
        }

        private static ScriptValue Insert_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ElementValue e = (ElementValue)self;
            Args.Exactly(ctx, a, "insert", 2);
            // list.insert's rules: an int index (a float was cast unchecked, an engine fault), negative from the end
            int i = Coerce.ToClampedIndex(Coerce.ToIndex(ctx, a[0], "insert"), e.Children.Count);
            ScriptValue inserted = RequireElement(ctx, a[1]);
            e.Grow(ctx);
            ctx.Budget.ChargeLinear(e.Children.Count - i);   // the later children move up
            Placing(ctx, inserted);
            e.Children.Insert(i, inserted);
            e.Changed(ctx);
            return ctx.Values.None;
        }

        private static ScriptValue Remove_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ElementValue e = (ElementValue)self;
            ScriptValue target = Args.One(ctx, a, "remove");
            ctx.Budget.ChargeLinear(e.Children.Count);   // the search, then the later children move down
            for (int i = 0; i < e.Children.Count; i++)
            {
                if (ReferenceEquals(e.Children[i], target))   // remove by identity (ET parity)
                {
                    Unplacing(ctx, target);
                    e.Children.RemoveAt(i);
                    e.Changed(ctx);
                    return ctx.Values.None;
                }
            }
            throw Raise.ValueError(ctx, "list.remove(x): x not in list");
        }

        private static ScriptValue Clear_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ElementValue e = (ElementValue)self;
            ctx.Budget.ChargeLinear(e.Children.Count);
            foreach (ScriptValue c in e.Children)
                Unplacing(ctx, c);
            e.Children.Clear();
            e._attrib = null;
            e.Text = ctx.Values.None;
            e.Tail = ctx.Values.None;
            e.Changed(ctx);
            return ctx.Values.None;
        }

        private static ScriptValue RequireChild(EvalContext ctx, ScriptValue v)
        {
            if (!(v is ElementValue))
                throw Raise.TypeError(ctx, "expected an Element, not \"" + v.PyTypeName + "\"");
            return v;
        }

        internal static ScriptValue RequireElement(EvalContext ctx, ScriptValue v)
        {
            if (!(v is ElementValue))
                throw Raise.TypeError(ctx, "expected an Element, not " + v.PyTypeName);
            return v;
        }

        private static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            // The subtree walks (find*/iter*) step per node in ElementXPath; the child-list edits are
            // the receiver passes charged here.
            var s = new Slots(v => ((ElementValue)v).Children.Count);
            s.Property("tag", (self, ctx) => ((ElementValue)self).Tag);
            s.Property("attrib", (self, ctx) => ((ElementValue)self).Attrib(ctx));
            s.Property("text", (self, ctx) => ((ElementValue)self).Text);
            s.Property("tail", (self, ctx) => ((ElementValue)self).Tail);
            s.Method("get", Get_);
            s.Method("set", 2, 2, Set_);
            s.Method("keys", 0, 0, Keys_);
            s.Method("items", 0, 0, Items_);
            s.Method("append", 1, 1, Append_);
            s.Method("extend", 1, 1, Extend_);
            s.Linear("insert", 2, 2, Insert_);
            s.Linear("remove", 1, 1, Remove_);
            s.Method("clear", 0, 0, Clear_);
            s.Method("makeelement", 2, 2, MakeElement_);
            ElementXPath.AddSlots(s.Table);   // find/findall/findtext/iter
            return s.Table;
        }
    }

    // Element child iteration with a version guard.
    internal sealed class ElementChildIterator : ScriptIteratorBase
    {
        private readonly ElementValue _el;
        private readonly int _version;
        private int _idx;

        internal ElementChildIterator(ElementValue el) { _el = el; _version = el.Version; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_el.Version != _version)
                throw Raise.RuntimeError(ctx, "Element changed during iteration");
            if (_idx >= _el.Children.Count)
            {
                value = null;
                return false;
            }
            value = _el.Children[_idx];
            _idx++;
            return true;
        }
    }
}

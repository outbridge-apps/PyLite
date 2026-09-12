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
    internal sealed class ElementValue : ScriptValue
    {
        internal static readonly ScriptTypeInfo ElementType = new ScriptTypeInfo("xml.etree.ElementTree.Element", BuildSlots());

        internal ScriptValue Tag;                    // StrValue
        internal DictValue Attrib;                   // live
        internal ScriptValue Text;                   // StrValue or None
        internal ScriptValue Tail;                   // StrValue or None
        internal readonly List<ScriptValue> Children = new List<ScriptValue>();
        internal int Version;

        internal ElementValue(ScriptValue tag, DictValue attrib, EvalContext ctx)
        {
            Tag = tag;
            Attrib = attrib;
            Text = ctx.Values.None;
            Tail = ctx.Values.None;
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
            int i = NormIndex(ctx, index);
            Children[i] = value;
            Version++;
            return true;
        }

        protected internal override bool TryDelItemCore(ScriptValue index, EvalContext ctx)
        {
            int i = NormIndex(ctx, index);
            Children.RemoveAt(i);
            Version++;
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
                    Attrib = d;
                    return true;
                default:
                    return false;
            }
        }

        private int NormIndex(EvalContext ctx, ScriptValue index)
        {
            if (index.Kind != ValueKind.Int && index.Kind != ValueKind.Bool)
                throw Raise.TypeError(ctx, "list indices must be integers or slices, not " + index.PyTypeName);
            BigInteger i = NumericOps.AsBigInteger(index);
            if (i < 0)
                i += Children.Count;
            if (i < 0 || i >= Children.Count)
                throw Raise.IndexError(ctx, "child index out of range");
            return (int)i;
        }

        // ---- method slots ----

        private static ScriptValue Get_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ElementValue e = (ElementValue)self;
            Args.Between(ctx, a, "get", 1, 2);
            ScriptValue v;
            if (e.Attrib.TryGet(a[0], ctx, out v))
                return v;
            return a.Length == 2 ? a[1] : ctx.Values.None;
        }

        private static ScriptValue Set_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ElementValue e = (ElementValue)self;
            Args.Exactly(ctx, a, "set", 2);
            e.Attrib.SetItem(Modules.XmlModule.UnQName(a[0]), Modules.XmlModule.UnQName(a[1]), ctx);
            return ctx.Values.None;
        }

        // makeelement(tag, attrib) -> a new element of the same kind, NOT attached to this one. CPython's
        // factory hook for subclasses; here it is the plain constructor, and the attrib dict is copied.
        private static ScriptValue MakeElement_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, a, kw, "makeelement", 2);
            DictValue src = a[1] as DictValue;
            if (src == null)
                throw Raise.TypeError(ctx, "makeelement() argument 'attrib' must be a dict, not " + a[1].PyTypeName);
            DictValue copy = ctx.Values.Dict(src.Count);
            var t = src.Table;
            for (int p = 0; p < t.EntriesUsed; p++)
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
            ListValue list = ctx.Values.List(e.Attrib.Count);
            var t = e.Attrib.Table;
            for (int p = 0; p < t.EntriesUsed; p++)
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
            ListValue list = ctx.Values.List(e.Attrib.Count);
            var t = e.Attrib.Table;
            for (int p = 0; p < t.EntriesUsed; p++)
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
            e.Children.Add(RequireElement(ctx, Args.One(ctx, a, "append")));
            e.Version++;
            return ctx.Values.None;
        }

        private static ScriptValue Extend_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ElementValue e = (ElementValue)self;
            IScriptIterator it = PyOps.GetIterator(Args.One(ctx, a, "extend"), ctx);
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
                e.Children.Add(RequireElement(ctx, v));
            e.Version++;
            return ctx.Values.None;
        }

        private static ScriptValue Insert_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ElementValue e = (ElementValue)self;
            Args.Exactly(ctx, a, "insert", 2);
            BigInteger idx = NumericOps.AsBigInteger(a[0]);
            int i = idx < 0 ? 0 : (idx > e.Children.Count ? e.Children.Count : (int)idx);
            e.Children.Insert(i, RequireElement(ctx, a[1]));
            e.Version++;
            return ctx.Values.None;
        }

        private static ScriptValue Remove_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ElementValue e = (ElementValue)self;
            ScriptValue target = Args.One(ctx, a, "remove");
            for (int i = 0; i < e.Children.Count; i++)
            {
                if (ReferenceEquals(e.Children[i], target))   // remove by identity (ET parity)
                {
                    e.Children.RemoveAt(i);
                    e.Version++;
                    return ctx.Values.None;
                }
            }
            throw Raise.ValueError(ctx, "list.remove(x): x not in list");
        }

        private static ScriptValue Clear_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ElementValue e = (ElementValue)self;
            e.Children.Clear();
            e.Attrib = ctx.Values.Dict(4);
            e.Text = ctx.Values.None;
            e.Tail = ctx.Values.None;
            e.Version++;
            return ctx.Values.None;
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
            s.Property("attrib", (self, ctx) => ((ElementValue)self).Attrib);
            s.Property("text", (self, ctx) => ((ElementValue)self).Text);
            s.Property("tail", (self, ctx) => ((ElementValue)self).Tail);
            s.Method("get", Get_);
            s.Method("set", Set_);
            s.Method("keys", Keys_);
            s.Method("items", Items_);
            s.Method("append", Append_);
            s.Method("extend", Extend_);
            s.Linear("insert", Insert_);
            s.Linear("remove", Remove_);
            s.Method("clear", Clear_);
            s.Method("makeelement", MakeElement_);
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

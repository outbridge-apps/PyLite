using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // collections.ChainMap. A LIVE view over a list of dicts: lookups search maps in order
    // writes/deletes go to maps[0] only. .maps is the live ListValue (mutations are visible). Iteration and
    // len() use the merged key union built back-to-front, so the FIRST map's order/values win (CPython).
    internal sealed class ChainMapValue : ScriptValue
    {
        private static readonly ScriptTypeInfo CMType = new ScriptTypeInfo("collections.ChainMap", BuildSlots());

        internal readonly ListValue Maps;   // live; each element must behave as a dict

        internal ChainMapValue(ListValue maps) { Maps = maps; }

        public override ScriptTypeInfo TypeInfo { get { return CMType; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        private DictValue MapAt(EvalContext ctx, int i)
        {
            DictValue d = Maps.Items[i] as DictValue;
            if (d == null)
                throw Raise.TypeError(ctx, "ChainMap maps must be dicts, not " + Maps.Items[i].PyTypeName);
            return d;
        }

        // Union snapshot: back-to-front updates keep the first map's insertion order and values on top.
        internal DictValue Merged(EvalContext ctx)
        {
            DictValue merged = ctx.Values.Dict(8);
            for (int i = Maps.Items.Count - 1; i >= 0; i--)
                DictMethods.Merge(ctx, merged, MapAt(ctx, i));
            return merged;
        }

        // ---- protocol hooks ----

        protected internal override bool IsTruthyCore(EvalContext ctx)
        {
            for (int i = 0; i < Maps.Items.Count; i++)
                if (MapAt(ctx, i).Count > 0)
                    return true;
            return false;
        }

        protected internal override bool TryLengthWithContextCore(EvalContext ctx, out long length)
        {
            length = Merged(ctx).Count;
            return true;
        }

        protected internal override IScriptIterator GetIteratorCore(EvalContext ctx)
        {
            return new DictKeyIterator(Merged(ctx).Table);
        }

        // Mapping equality: a ChainMap equals another ChainMap or a dict with the same merged items.
        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            ChainMapValue o = other as ChainMapValue;
            return o != null && PyOps.Equals(Merged(ctx), o.Merged(ctx), ctx, depth + 1);
        }

        protected internal override bool TryEqualsAcrossKindCore(ScriptValue other, EvalContext ctx, int depth, out bool equal)
        {
            if (other is DictValue)
            {
                equal = PyOps.Equals(Merged(ctx), other, ctx, depth + 1);
                return true;
            }
            equal = false;
            return false;
        }

        protected internal override bool TryContainsCore(ScriptValue item, EvalContext ctx, int depth, out bool found)
        {
            for (int i = 0; i < Maps.Items.Count; i++)
            {
                ScriptValue ignored;
                if (MapAt(ctx, i).TryGet(item, ctx, out ignored))
                {
                    found = true;
                    return true;
                }
            }
            found = false;
            return true;
        }

        protected internal override ScriptValue GetItemCore(ScriptValue key, EvalContext ctx)
        {
            // CPython does mapping[key] per map (a defaultdict first map DOES run its factory, a Counter
            // answers 0), asked without the KeyError a miss would otherwise cost per map.
            for (int i = 0; i < Maps.Items.Count; i++)
            {
                ScriptValue v;
                if (MapAt(ctx, i).TryGetItem(key, ctx, out v))
                    return v;
            }
            throw Raise.KeyError(ctx, key);
        }

        protected internal override bool TrySetItemCore(ScriptValue key, ScriptValue value, EvalContext ctx)
        {
            First(ctx).SetItem(key, value, ctx);
            return true;
        }

        protected internal override bool TryDelItemCore(ScriptValue key, EvalContext ctx)
        {
            ScriptValue ignored;
            if (!First(ctx).TryGet(key, ctx, out ignored))
                throw Raise.KeyError(ctx, key);
            First(ctx).DelItemOrThrow(key, ctx);
            return true;
        }

        private DictValue First(EvalContext ctx)
        {
            if (Maps.Items.Count == 0)
                throw Raise.KeyError(ctx, ctx.Values.Str("ChainMap has no maps"));
            return MapAt(ctx, 0);
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("ChainMap(");
            for (int i = 0; i < Maps.Items.Count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(Maps.Items[i].Repr(ctx, depth + 1));
            }
            sb.Append(")");
        }

        // ---- method/property slots ----

        private static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            var s = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
            s["maps"] = SlotDescriptor.MakeProperty("maps", (self, ctx) => ((ChainMapValue)self).Maps);
            s["parents"] = SlotDescriptor.MakeProperty("parents", (self, ctx) => Parents((ChainMapValue)self, ctx));
            s["new_child"] = SlotDescriptor.MakeMethod("new_child", NewChild);
            s["get"] = SlotDescriptor.MakeMethod("get", Get_);
            s["keys"] = SlotDescriptor.MakeMethod("keys", (self, a, kw, ctx) => ((ChainMapValue)self).Merged(ctx).KeysView(ctx));
            s["values"] = SlotDescriptor.MakeMethod("values", (self, a, kw, ctx) => ((ChainMapValue)self).Merged(ctx).ValuesView(ctx));
            s["items"] = SlotDescriptor.MakeMethod("items", (self, a, kw, ctx) => ((ChainMapValue)self).Merged(ctx).ItemsView(ctx));
            s["copy"] = SlotDescriptor.MakeMethod("copy", Copy_);
            s["update"] = SlotDescriptor.MakeMethod("update", Update_);
            s["pop"] = SlotDescriptor.MakeMethod("pop", Pop_);
            s["popitem"] = SlotDescriptor.MakeMethod("popitem", PopItem_);
            s["setdefault"] = SlotDescriptor.MakeMethod("setdefault", SetDefault_);
            s["clear"] = SlotDescriptor.MakeMethod("clear", (self, a, kw, ctx) =>
            {
                ((ChainMapValue)self).First(ctx).Table.Clear(ctx);   // first map only (MutableMapping semantics)
                return ctx.Values.None;
            });
            return s;
        }

        private static ScriptValue Parents(ChainMapValue cm, EvalContext ctx)
        {
            ListValue rest = ctx.Values.List(Math.Max(1, cm.Maps.Items.Count - 1));
            for (int i = 1; i < cm.Maps.Items.Count; i++)
                rest.Add(cm.Maps.Items[i], ctx);
            if (rest.Items.Count == 0)
                rest.Add(ctx.Values.Dict(4), ctx);
            ctx.Values.PreCharge(32);
            return new ChainMapValue(rest);
        }

        private static ScriptValue NewChild(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ChainMapValue cm = (ChainMapValue)self;
            DictValue child;
            if (a.Length >= 1 && a[0].Kind != ValueKind.None)
            {
                child = a[0] as DictValue;
                if (child == null)
                    throw Raise.TypeError(ctx, "new_child() argument must be a dict, not " + a[0].PyTypeName);
            }
            else
                child = ctx.Values.Dict(kw.Count + 4);
            for (int j = 0; j < kw.Count; j++)
                child.SetItem(ctx.Values.Str(kw.NameAt(j)), kw.ValueAt(j), ctx);
            ListValue maps = ctx.Values.List(cm.Maps.Items.Count + 1);
            maps.Add(child, ctx);
            for (int i = 0; i < cm.Maps.Items.Count; i++)
                maps.Add(cm.Maps.Items[i], ctx);
            ctx.Values.PreCharge(32);
            return new ChainMapValue(maps);
        }

        private static ScriptValue Get_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ChainMapValue cm = (ChainMapValue)self;
            if (a.Length < 1 || a.Length > 2)
                throw Raise.TypeError(ctx, "get expected at most 2 arguments, got " + a.Length);
            for (int i = 0; i < cm.Maps.Items.Count; i++)
            {
                ScriptValue v;
                if (cm.MapAt(ctx, i).TryGet(a[0], ctx, out v))
                    return v;
            }
            return a.Length == 2 ? a[1] : ctx.Values.None;
        }

        // update: writes route to the first map (MutableMapping over __setitem__).
        private static ScriptValue Update_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ChainMapValue cm = (ChainMapValue)self;
            if (a.Length > 1)
                throw Raise.TypeError(ctx, "update expected at most 1 argument, got " + a.Length);
            DictValue first = cm.First(ctx);
            if (a.Length == 1 && a[0].Kind != ValueKind.None)
                DictMethods.Merge(ctx, first, a[0]);
            for (int j = 0; j < kw.Count; j++)
                first.SetItem(ctx.Values.Str(kw.NameAt(j)), kw.ValueAt(j), ctx);
            return ctx.Values.None;
        }

        // pop/popitem operate on the FIRST map only (CPython ChainMap overrides, not the generic mixin).
        private static ScriptValue Pop_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ChainMapValue cm = (ChainMapValue)self;
            if (a.Length < 1 || a.Length > 2)
                throw Raise.TypeError(ctx, "pop expected at most 2 arguments, got " + a.Length);
            DictValue first = cm.First(ctx);
            ScriptValue v;
            if (first.TryGet(a[0], ctx, out v))
            {
                first.DelItemOrThrow(a[0], ctx);
                return v;
            }
            if (a.Length == 2)
                return a[1];
            throw Raise.KeyError(ctx, ctx.Values.Str("Key not found in the first mapping: " + a[0].Repr(ctx)));
        }

        private static ScriptValue PopItem_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ChainMapValue cm = (ChainMapValue)self;
            DictValue first = cm.First(ctx);
            int pos = first.Table.FindLastLive();
            if (pos < 0)
                throw Raise.KeyError(ctx, ctx.Values.Str("No keys found in the first mapping."));
            long h;
            ScriptValue k, v;
            first.Table.TryGetEntryAt(pos, out h, out k, out v);
            first.Table.Delete(k, h, ctx, 0);
            return ctx.Values.Tuple(new[] { k, v });
        }

        // setdefault: an existing key in ANY map returns that value; otherwise writes default to the first.
        private static ScriptValue SetDefault_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ChainMapValue cm = (ChainMapValue)self;
            if (a.Length < 1 || a.Length > 2)
                throw Raise.TypeError(ctx, "setdefault expected at most 2 arguments, got " + a.Length);
            for (int i = 0; i < cm.Maps.Items.Count; i++)
            {
                ScriptValue v;
                if (cm.MapAt(ctx, i).TryGet(a[0], ctx, out v))
                    return v;
            }
            ScriptValue deflt = a.Length == 2 ? a[1] : ctx.Values.None;
            cm.First(ctx).SetItem(a[0], deflt, ctx);
            return deflt;
        }

        // ChainMap(maps[0].copy(), *maps[1:]) — first map copied, the rest shared (CPython).
        private static ScriptValue Copy_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ChainMapValue cm = (ChainMapValue)self;
            ListValue maps = ctx.Values.List(cm.Maps.Items.Count);
            for (int i = 0; i < cm.Maps.Items.Count; i++)
                maps.Add(i == 0 ? Outbridge.PyLite.Modules.CopyModule.CloneDict(ctx, cm.MapAt(ctx, i)) : cm.Maps.Items[i], ctx);
            ctx.Values.PreCharge(32);
            return new ChainMapValue(maps);
        }
    }
}

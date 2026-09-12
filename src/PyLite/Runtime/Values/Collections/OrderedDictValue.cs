using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Runtime.Values
{
    // collections.OrderedDict. Our DictValue is already insertion-ordered, so this adds
    // only move_to_end/popitem and an order-sensitive == (only OD-vs-OD; OD-vs-plain-dict stays orderless).
    internal sealed class OrderedDictValue : DictValue
    {
        private static readonly ScriptTypeInfo ODType = new ScriptTypeInfo("collections.OrderedDict", BuildSlots());

        internal OrderedDictValue(OrderedTable table) : base(table) { }

        public override ScriptTypeInfo TypeInfo { get { return ODType; } }
        internal override string ReprPrefix(EvalContext ctx) { return "OrderedDict("; }
        internal override string ReprSuffix(EvalContext ctx) { return ")"; }

        internal override string ReprEmpty(EvalContext ctx) { return "OrderedDict()"; }

        private static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            IDictionary<string, SlotDescriptor> slots = DictMethods.BuildSlots();   // inherit keys/values/items/get/pop/update/...
            slots["move_to_end"] = SlotDescriptor.MakeMethod("move_to_end", MoveToEnd);
            slots["popitem"] = SlotDescriptor.MakeMethod("popitem", PopItem);
            slots["copy"] = SlotDescriptor.MakeMethod("copy", (self, a, kw, ctx) =>
            {
                OrderedDictValue src = (OrderedDictValue)self;
                OrderedDictValue r = ctx.Values.OrderedDict(src.Count);
                DictMethods.Merge(ctx, r, src);
                return r;
            });
            return slots;
        }

        // Order-sensitive equality: pairs compared position-by-position (both traversal orders).
        internal static bool OrderedEquals(OrderedDictValue a, OrderedDictValue b, EvalContext ctx, int depth)
        {
            if (a.Count != b.Count)
                return false;
            var ai = new DictItemsIterator(a.Table);
            var bi = new DictItemsIterator(b.Table);
            ScriptValue ea, eb;
            while (ai.MoveNext(ctx, out ea))
            {
                bi.MoveNext(ctx, out eb);
                TupleValue ta = (TupleValue)ea;
                TupleValue tb = (TupleValue)eb;
                if (!PyOps.Equals(ta.Items[0], tb.Items[0], ctx, depth + 1))
                    return false;
                if (!PyOps.Equals(ta.Items[1], tb.Items[1], ctx, depth + 1))
                    return false;
            }
            return true;
        }

        private static ScriptValue MoveToEnd(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            OrderedDictValue od = (OrderedDictValue)self;
            Args.Between(ctx, args, "move_to_end", 1, 2);
            ScriptValue key = args[0];
            bool last = ResolveLast(ctx, args.Length == 2 ? args[1] : null, kw);
            ScriptValue val;
            if (!od.TryGet(key, ctx, out val))
                throw Raise.KeyError(ctx, key);
            long h = PyOps.Hash(key, ctx, 0);
            if (last)
            {
                od.Table.Delete(key, h, ctx, 0);
                od.Table.InsertOrUpdate(key, h, val, ctx, 0);
            }
            else
            {
                // No prepend primitive: rebuild with the moved key first, the rest in order.
                var rest = new List<TableEntry>();
                for (int p = 0; p < od.Table.EntriesUsed; p++)
                {
                    long eh;
                    ScriptValue ek, ev;
                    if (od.Table.TryGetEntryAt(p, out eh, out ek, out ev)
                        && !(ReferenceEquals(ek, key) || PyOps.Equals(ek, key, ctx, 0)))
                        rest.Add(new TableEntry { Hash = eh, Key = ek, Value = ev });
                }
                od.Table.Clear(ctx);
                od.Table.InsertOrUpdate(key, h, val, ctx, 0);
                foreach (TableEntry e in rest)
                    od.Table.InsertOrUpdate(e.Key, e.Hash, e.Value, ctx, 0);
            }
            return ctx.Values.None;
        }

        private static ScriptValue PopItem(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            OrderedDictValue od = (OrderedDictValue)self;
            Args.AtMost(ctx, args, "popitem", 1);
            bool last = ResolveLast(ctx, args.Length == 1 ? args[0] : null, kw);
            if (od.Count == 0)
                throw Raise.KeyError(ctx, ctx.Values.Str("dictionary is empty"));
            int pos = last ? od.Table.FindLastLive() : FindFirstLive(od.Table);
            long h;
            ScriptValue k, v;
            od.Table.TryGetEntryAt(pos, out h, out k, out v);
            od.Table.Delete(k, h, ctx, 0);
            return ctx.Values.Tuple(new[] { k, v });
        }

        private static bool ResolveLast(EvalContext ctx, ScriptValue positional, KwArgs kw)
        {
            ScriptValue lastArg = positional;
            ScriptValue lkw;
            if (kw.TryGet("last", out lkw))
                lastArg = lkw;
            return lastArg == null || lastArg.IsTruthy(ctx);
        }

        private static int FindFirstLive(OrderedTable t)
        {
            for (int p = 0; p < t.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, v;
                if (t.TryGetEntryAt(p, out h, out k, out v))
                    return p;
            }
            return -1;
        }
    }
}

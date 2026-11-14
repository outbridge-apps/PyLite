using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // Iterative repr/str via an explicit work stack. Cycle detection is by reference
    // identity on the CURRENT path (a DAG prints in full); every emitted node charges a step, so a DAG
    // bomb aborts by budget instead of hanging. No C# recursion over user data.
    internal static class ReprEngine
    {
        public static string Render(ScriptValue v, EvalContext ctx, bool useStrAtTop)
        {
            return Render(v, ctx, useStrAtTop, 0);
        }

        // depth: the nesting a leaf hook re-enters at. The work stack below is unbounded (a deep list
        // prints in full); what is capped at MaxDataDepth is re-entry through a leaf (a deque of
        // deques, a slice of slices), which is a C# frame per level and so a RecursionError instead.
        public static string Render(ScriptValue v, EvalContext ctx, bool useStrAtTop, int depth)
        {
            if (depth > ctx.Limits.MaxDataDepth)
                throw Raise.RecursionError(ctx, "maximum recursion depth exceeded while getting the repr of an object");
            // Past the plain values, anything can re-enter here from C# at depth 0 (str of an exception
            // wrapping an exception, repr of a repeat of a repeat): every such entry probes the stack.
            bool container = IsContainer(v);
            if (depth > 0 || v.Kind > ValueKind.Bytes)
                StackGuard.ProbeData(ctx);
            if (!container)
            {
                string quick = QuickLeaf(v, ctx, useStrAtTop);
                if (quick != null)
                    return quick;
            }
            var sb = new BudgetStringBuilder(ctx);
            // A record whose auto-repr would descend through the stack below is still a LEAF for str()
            // when it defines __str__: that method answers for the whole value.
            if (!container || (useStrAtTop && HasUserStr(v)))
            {
                if (useStrAtTop)
                    v.StrLeafCore(sb, ctx, depth);
                else
                    v.ReprLeafCore(sb, ctx, depth);
                return sb.ToString();
            }

            // One open set for the whole repr: a deque or ChainMap prints its children through Render again, and a
            // container already open outside it is marked, not printed a second time (CPython's Py_ReprEnter).
            HashSet<ScriptValue> open = ctx.ReprOpen;
            bool owner = open == null;
            if (owner)
                ctx.ReprOpen = open = new HashSet<ScriptValue>(RefComparer.Instance);
            else if (open.Contains(v))
                return CycleMarker(v);
            var stack = new Stack<Item>();
            bool done = false;
            try
            {
                string s = Walk(v, ctx, depth, sb, open, stack);
                done = true;
                return s;
            }
            finally
            {
                // a raise in the middle (a __repr__ the script catches): what this walk opened is closed again
                if (!done)
                {
                    foreach (Item it in stack)
                    {
                        if (it.Kind == ItemKind.Close)
                            open.Remove(it.V);
                    }
                }
                if (owner)
                    ctx.ReprOpen = null;
            }
        }

        private static string Walk(ScriptValue v, EvalContext ctx, int depth, BudgetStringBuilder sb, HashSet<ScriptValue> open, Stack<Item> stack)
        {
            int outer = open.Count;   // the depth counts this walk's open containers, not the enclosing walks'
            if (!(v is NamedTupleValue))
                open.Add(v);
            PushSequence(stack, v, ctx);

            while (stack.Count > 0)
            {
                ctx.Budget.Step();
                Item it = stack.Pop();
                if (it.Kind == ItemKind.Seq)
                {
                    ScriptValue[] elems = it.Elems;
                    if (it.Index == elems.Length)
                        continue;
                    if (it.Index > 0)
                        sb.Append(", ");
                    stack.Push(new Item { Kind = ItemKind.Seq, O = elems, Index = it.Index + 1 });
                    stack.Push(Render(elems[it.Index]));
                    continue;
                }
                if (it.Kind == ItemKind.Pairs)
                {
                    ScriptValue[] pairs = it.Elems;
                    if (it.Index == pairs.Length)
                        continue;
                    if (it.Index > 0)
                        sb.Append(", ");
                    stack.Push(new Item { Kind = ItemKind.Pairs, O = pairs, Index = it.Index + 2 });
                    stack.Push(Render(pairs[it.Index + 1]));
                    stack.Push(Emit(": "));
                    stack.Push(Render(pairs[it.Index]));
                    continue;
                }
                if (it.Kind == ItemKind.Emit)
                {
                    sb.Append(it.S);
                    continue;
                }

                if (it.Kind == ItemKind.Close)
                {
                    open.Remove(it.V);
                    continue;
                }

                ScriptValue child = it.V;
                if (it.Kind == ItemKind.Factory && open.Contains(child))
                {
                    sb.Append("...");   // a factory already being printed, as CPython's defaultdict marks it
                    continue;
                }
                if (!IsContainer(child))
                {
                    child.ReprLeafCore(sb, ctx, depth + open.Count - outer);
                    continue;
                }

                if (open.Contains(child))
                {
                    DefaultDictValue ddc = child as DefaultDictValue;
                    if (ddc != null)
                    {
                        // CPython re-enters defaultdict_repr once: its dict part is the one that is marked
                        stack.Push(Emit(", {...})"));
                        stack.Push(ddc.DefaultFactory == null ? Emit("None") : new Item { Kind = ItemKind.Factory, O = ddc.DefaultFactory });
                        stack.Push(Emit("defaultdict("));
                        continue;
                    }
                    sb.Append(CycleMarker(child));
                    continue;
                }

                // a namedtuple has no repr guard of its own: a cycle through one is marked at its mutable part
                if (!(child is NamedTupleValue))
                    open.Add(child);
                PushSequence(stack, child, ctx);
            }
            return sb.ToString();
        }

        // A small int, or a str under str(): its text without a builder and its copy. Not charged, as a builder's
        // first 256 chars are not: at most 20 digits, or the str itself; an enum member keeps the general path.
        private static string QuickLeaf(ScriptValue v, EvalContext ctx, bool useStr)
        {
            if (ctx.EnumValues != null)
                return null;
            string s;
            if (v.Kind == ValueKind.Int)
            {
                IntValue iv = (IntValue)v;
                if (!iv.IsSmall)
                    return null;
                s = iv.DecimalDigits(ctx);
            }
            else if (useStr && v.Kind == ValueKind.Str)
            {
                s = ((StrValue)v).Value;
            }
            else
            {
                return null;
            }
            if (s.Length > ctx.Limits.MaxStrChars)
                return null;
            return s;
        }

        private static bool HasUserStr(ScriptValue v)
        {
            RecordInstanceValue rec = v as RecordInstanceValue;
            return rec != null && rec.Class.Dunder(RecordDunder.Str) != null;
        }

        private static bool IsContainer(ScriptValue v)
        {
            switch (v.Kind)
            {
                case ValueKind.List:
                case ValueKind.Tuple:
                case ValueKind.Dict:
                case ValueKind.Set:
                case ValueKind.FrozenSet:
                case ValueKind.DictView:
                    return true;
                case ValueKind.RecordInstance:
                    // Auto-repr records descend through this cycle-detecting work stack; a record with a
                    // user __repr__ stays a leaf (its call is StackGuard-probed via ctx.CallHook).
                    RecordInstanceValue rec = (RecordInstanceValue)v;
                    return rec.Class.Dunder(RecordDunder.Repr) == null && (rec.Class.Dataclass == null || rec.Class.Dataclass.Repr);
                default:
                    return false;
            }
        }

        // A leaf that prints its children through Render again (deque, ChainMap): false when it is already being
        // printed, and the caller writes its marker instead. ExitLeaf runs in the caller's finally.
        internal static bool EnterLeaf(EvalContext ctx, ScriptValue v)
        {
            if (ctx.LeafReprOpen == null)
                ctx.LeafReprOpen = new HashSet<ScriptValue>(RefComparer.Instance);
            return ctx.LeafReprOpen.Add(v);
        }

        internal static void ExitLeaf(EvalContext ctx, ScriptValue v)
        {
            ctx.LeafReprOpen.Remove(v);
        }

        private static string CycleMarker(ScriptValue v)
        {
            switch (v.Kind)
            {
                case ValueKind.List: return "[...]";
                case ValueKind.Tuple: return "(...)";
                case ValueKind.RecordInstance: return "...";   // e.g. dataclass P(x=...)
                default: return v is OrderedDictValue ? "..." : "{...}";
            }
        }

        private static void PushSequence(Stack<Item> stack, ScriptValue v, EvalContext ctx)
        {
            var items = new List<Item>();
            BuildInto(items, v, ctx);
            for (int i = items.Count - 1; i >= 0; i--)
                stack.Push(items[i]);
        }

        private static void BuildInto(List<Item> items, ScriptValue v, EvalContext ctx)
        {
            switch (v.Kind)
            {
                case ValueKind.List:
                    BuildSeq(items, ((ListValue)v).Items.ToArray(), "[", "]", "[]", false); break;
                case ValueKind.Tuple:
                    if (v is NamedTupleValue)
                        BuildNamedTuple(items, (NamedTupleValue)v);
                    else
                        BuildSeq(items, ((TupleValue)v).Items, "(", ")", "()", true);
                    break;
                case ValueKind.Dict:
                    BuildDict(items, (DictValue)v, ctx); break;
                case ValueKind.Set:
                    BuildSet(items, (SetValue)v, "{", "}", "set()", ctx); break;
                case ValueKind.FrozenSet:
                    BuildSet(items, (SetValue)v, "frozenset({", "})", "frozenset()", ctx); break;
                case ValueKind.RecordInstance:
                    BuildRecord(items, (RecordInstanceValue)v); break;
                default:
                    BuildView(items, (DictViewValue)v, ctx); break;
            }
            items.Add(Close(v));
        }

        // Auto (no user __repr__) record repr: ClassName(slot0=<v0>, slot1=<v1>, ...) with each value
        // pushed as a Render item so the shared open-set breaks self/mutual reference cycles (-> "...").
        private static void BuildRecord(List<Item> items, RecordInstanceValue r)
        {
            items.Add(Emit(r.Class.Name + "("));
            IReadOnlyList<string> slots = r.Class.AllSlots;
            bool first = true;
            for (int i = 0; i < slots.Count; i++)
            {
                if (!r.Participates(i, RecordInstanceValue.FieldRole.Repr))
                    continue;   // field(repr=False) — must match RecordInstanceValue.ReprLeafCore
                if (!first)
                    items.Add(Emit(", "));
                first = false;
                items.Add(Emit(slots[i] + "="));
                ScriptValue sv = r.Slots[i];
                items.Add(sv == null ? Emit("<unset>") : Render(sv));
            }
            items.Add(Emit(")"));
        }

        private static void BuildSeq(List<Item> items, ScriptValue[] elems, string open, string close, string empty, bool tupleComma)
        {
            if (elems.Length == 0)
            {
                items.Add(Emit(empty));
                return;
            }
            items.Add(Emit(open));
            items.Add(new Item { Kind = ItemKind.Seq, O = elems });
            items.Add(Emit(tupleComma && elems.Length == 1 ? ",)" : close));
        }

        private static void BuildDict(List<Item> items, DictValue d, EvalContext ctx)
        {
            // Subtypes (OrderedDict/Counter/defaultdict) wrap the {k: v} body with a prefix/suffix.
            string prefix = d.ReprPrefix(ctx);
            string suffix = d.ReprSuffix(ctx);
            string empty = d.Count == 0 ? d.ReprEmpty(ctx) : null;
            if (empty != null)
            {
                items.Add(Emit(empty));   // Counter() / OrderedDict(): the subtype's own empty form
                return;
            }
            if (prefix != null)
                items.Add(Emit(prefix));
            DefaultDictValue dd = d as DefaultDictValue;
            if (dd != null)
            {
                // The factory is a child of the walk like the items: a defaultdict that is its own factory,
                // or a chain of a million of them, prints without a C# frame per level.
                items.Add(dd.DefaultFactory == null ? Emit("None") : new Item { Kind = ItemKind.Factory, O = dd.DefaultFactory });
                items.Add(Emit(", "));
            }
            if (d.Count == 0)
            {
                items.Add(Emit("{}"));
            }
            else
            {
                items.Add(Emit("{"));
                ctx.Values.PreCharge(16L * d.Count);
                var pairs = new ScriptValue[2 * d.Count];
                int at = 0;
                for (int p = d.Table.WalkStart(ctx); p < d.Table.EntriesUsed; p++)
                {
                    long h;
                    ScriptValue k, val;
                    if (!d.Table.TryGetEntryAt(p, out h, out k, out val))
                        continue;
                    pairs[at++] = k;
                    pairs[at++] = val;
                }
                CounterValue counter = d as CounterValue;
                if (counter != null)
                    pairs = counter.PairsByCount(pairs, ctx);
                items.Add(new Item { Kind = ItemKind.Pairs, O = pairs });
                items.Add(Emit("}"));
            }
            if (suffix != null)
                items.Add(Emit(suffix));
        }

        // namedtuple repr: TypeName(field1=repr1, field2=repr2, ...).
        private static void BuildNamedTuple(List<Item> items, NamedTupleValue nt)
        {
            string[] fields = nt.NtInfo.Fields;
            ScriptValue[] vals = nt.Items;
            items.Add(Emit(nt.NtInfo.TypeName + "("));
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0)
                    items.Add(Emit(", "));
                items.Add(Emit(fields[i] + "="));
                items.Add(Render(vals[i]));
            }
            items.Add(Emit(")"));
        }

        private static void BuildSet(List<Item> items, SetValue s, string open, string close, string empty, EvalContext ctx)
        {
            if (s.Count == 0)
            {
                items.Add(Emit(empty));
                return;
            }
            items.Add(Emit(open));
            ctx.Values.PreCharge(8L * s.Count);
            var elems = new ScriptValue[s.Count];
            int at = 0;
            for (int p = s.Table.WalkStart(ctx); p < s.Table.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, val;
                if (s.Table.TryGetEntryAt(p, out h, out k, out val))
                    elems[at++] = k;
            }
            items.Add(new Item { Kind = ItemKind.Seq, O = elems });
            items.Add(Emit(close));
        }

        private static void BuildView(List<Item> items, DictViewValue view, EvalContext ctx)
        {
            string name = view.PyTypeName;
            DictValue owner = view.Owner;
            if (owner.Count == 0)
            {
                items.Add(Emit(name + "([])"));
                return;
            }
            items.Add(Emit(name + "(["));
            ctx.Values.PreCharge(8L * owner.Count);
            var elems = new ScriptValue[owner.Count];
            int at = 0;
            for (int p = owner.Table.WalkStart(ctx); p < owner.Table.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, val;
                if (!owner.Table.TryGetEntryAt(p, out h, out k, out val))
                    continue;
                switch (view.View)
                {
                    case DictViewValue.ViewKind.Keys:
                        elems[at++] = k;
                        break;
                    case DictViewValue.ViewKind.Values:
                        elems[at++] = val;
                        break;
                    default:
                        elems[at++] = ctx.Values.Tuple(new[] { k, val });
                        break;
                }
            }
            items.Add(new Item { Kind = ItemKind.Seq, O = elems });
            items.Add(Emit("])"));
        }

        private enum ItemKind { Emit, Render, Close, Seq, Pairs, Factory }
        // Seq: the elements of a list or tuple, one per visit (Index the next), so the stack grows with the
        // nesting and not the length: two items per element made repr([0] * 5000000) take 1 GB.
        // One reference for the payload (the text, the value or the elements): 16 bytes, smaller than the three
        // fields it replaced, which the stack and the item lists copy for every element.
        private struct Item
        {
            public ItemKind Kind;
            public int Index;
            public object O;
            public string S { get { return (string)O; } }
            public ScriptValue V { get { return (ScriptValue)O; } }
            public ScriptValue[] Elems { get { return (ScriptValue[])O; } }
        }
        private static Item Emit(string s) { return new Item { Kind = ItemKind.Emit, O = s }; }
        private static Item Render(ScriptValue v) { return new Item { Kind = ItemKind.Render, O = v }; }
        private static Item Close(ScriptValue v) { return new Item { Kind = ItemKind.Close, O = v }; }

        private sealed class RefComparer : IEqualityComparer<ScriptValue>
        {
            public static readonly RefComparer Instance = new RefComparer();
            public bool Equals(ScriptValue a, ScriptValue b) { return ReferenceEquals(a, b); }
            public int GetHashCode(ScriptValue v) { return RuntimeHelpers.GetHashCode(v); }
        }
    }
}

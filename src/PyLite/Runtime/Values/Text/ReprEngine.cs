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
            var sb = new BudgetStringBuilder(ctx);
            // A record whose auto-repr would descend through the stack below is still a LEAF for str()
            // when it defines __str__: that method answers for the whole value.
            if (!IsContainer(v) || (useStrAtTop && HasUserStr(v)))
            {
                if (useStrAtTop)
                    v.StrLeafCore(sb, ctx, depth);
                else
                    v.ReprLeafCore(sb, ctx, depth);
                return sb.ToString();
            }

            var open = new HashSet<ScriptValue>(RefComparer.Instance);
            var stack = new Stack<Item>();
            open.Add(v);
            PushSequence(stack, v, ctx);

            while (stack.Count > 0)
            {
                ctx.Budget.Step();
                Item it = stack.Pop();
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
                if (!IsContainer(child))
                {
                    child.ReprLeafCore(sb, ctx, depth + open.Count);
                    continue;
                }

                if (open.Contains(child))
                {
                    sb.Append(CycleMarker(child));
                    continue;
                }

                open.Add(child);
                PushSequence(stack, child, ctx);
            }
            return sb.ToString();
        }

        public static string RenderCapped(ScriptValue v, EvalContext ctx, int maxLen)
        {
            string s = Render(v, ctx, false);
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
        }

        private static bool HasUserStr(ScriptValue v)
        {
            RecordInstanceValue rec = v as RecordInstanceValue;
            return rec != null && rec.Class.FindMethod("__str__") != null;
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
                    return rec.Class.FindMethod("__repr__") == null && (rec.Class.Dataclass == null || rec.Class.Dataclass.Repr);
                default:
                    return false;
            }
        }

        private static string CycleMarker(ScriptValue v)
        {
            switch (v.Kind)
            {
                case ValueKind.List: return "[...]";
                case ValueKind.RecordInstance: return "...";   // e.g. dataclass P(x=...)
                default: return "{...}";
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
                    BuildSet(items, (SetValue)v, "{", "}", "set()"); break;
                case ValueKind.FrozenSet:
                    BuildSet(items, (SetValue)v, "frozenset({", "})", "frozenset()"); break;
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
            for (int i = 0; i < elems.Length; i++)
            {
                if (i > 0)
                    items.Add(Emit(", "));
                items.Add(Render(elems[i]));
            }
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
            if (d.Count == 0)
            {
                items.Add(Emit("{}"));
            }
            else
            {
                items.Add(Emit("{"));
                bool first = true;
                for (int p = 0; p < d.Table.EntriesUsed; p++)
                {
                    long h;
                    ScriptValue k, val;
                    if (!d.Table.TryGetEntryAt(p, out h, out k, out val))
                        continue;
                    if (!first)
                        items.Add(Emit(", "));
                    first = false;
                    items.Add(Render(k));
                    items.Add(Emit(": "));
                    items.Add(Render(val));
                }
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

        private static void BuildSet(List<Item> items, SetValue s, string open, string close, string empty)
        {
            if (s.Count == 0)
            {
                items.Add(Emit(empty));
                return;
            }
            items.Add(Emit(open));
            bool first = true;
            for (int p = 0; p < s.Table.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, val;
                if (!s.Table.TryGetEntryAt(p, out h, out k, out val))
                    continue;
                if (!first)
                    items.Add(Emit(", "));
                first = false;
                items.Add(Render(k));
            }
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
            bool first = true;
            for (int p = 0; p < owner.Table.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, val;
                if (!owner.Table.TryGetEntryAt(p, out h, out k, out val))
                    continue;
                if (!first)
                    items.Add(Emit(", "));
                first = false;
                switch (view.View)
                {
                    case DictViewValue.ViewKind.Keys:
                        items.Add(Render(k));
                        break;
                    case DictViewValue.ViewKind.Values:
                        items.Add(Render(val));
                        break;
                    default:
                        items.Add(Render(ctx.Values.Tuple(new[] { k, val })));
                        break;
                }
            }
            items.Add(Emit("])"));
        }

        private enum ItemKind { Emit, Render, Close }
        private struct Item { public ItemKind Kind; public string S; public ScriptValue V; }
        private static Item Emit(string s) { return new Item { Kind = ItemKind.Emit, S = s }; }
        private static Item Render(ScriptValue v) { return new Item { Kind = ItemKind.Render, V = v }; }
        private static Item Close(ScriptValue v) { return new Item { Kind = ItemKind.Close, V = v }; }

        private sealed class RefComparer : IEqualityComparer<ScriptValue>
        {
            public static readonly RefComparer Instance = new RefComparer();
            public bool Equals(ScriptValue a, ScriptValue b) { return ReferenceEquals(a, b); }
            public int GetHashCode(ScriptValue v) { return RuntimeHelpers.GetHashCode(v); }
        }
    }
}

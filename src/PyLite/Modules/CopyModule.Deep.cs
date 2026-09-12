using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // copy.deepcopy: iterative, two-phase, explicit stack (zero C# recursion over data).
    // A container is cloned empty into memo first, then filled, so cycles converge; shared references are
    // deduplicated via a reference-identity memo; tuples use the CPython identity optimization.
    public static partial class CopyModule
    {
        private enum Phase { CreateClone, Fill, BuildTuple }

        private struct WorkItem
        {
            public ScriptValue Source;
            public Phase Phase;
            public ScriptValue Clone;
        }

        private sealed class RefEq : IEqualityComparer<ScriptValue>
        {
            internal static readonly RefEq Instance = new RefEq();
            public bool Equals(ScriptValue a, ScriptValue b) { return ReferenceEquals(a, b); }
            public int GetHashCode(ScriptValue v) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(v); }
        }

        private static ScriptValue DeepCopyFn(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            Args.Between(ctx, args, "deepcopy", 1, 2);
            if (args.Length == 1 || args[1].Kind == ValueKind.None)
                return DeepCopy(ctx, args[0]);
            DictValue memoDict = args[1] as DictValue;
            if (memoDict == null)
                throw Raise.TypeError(ctx, "deepcopy() argument 2 must be dict, not " + args[1].PyTypeName);
            // The script-visible memo dict acts as a session key: repeated deepcopy(x, memo) calls with the
            // SAME dict share one internal identity memo, so cross-call shared references deduplicate
            // (CPython keys its memo by id(); our dict stays empty — a documented deviation).
            return DeepCopyWith(ctx, args[0], InternalMemoFor(ctx, memoDict));
        }

        private static Dictionary<ScriptValue, ScriptValue> InternalMemoFor(EvalContext ctx, DictValue memoDict)
        {
            const string key = "copy.deepcopy.memoTable";
            object raw;
            if (!ctx.ModuleState.TryGetValue(key, out raw))
            {
                raw = new System.Runtime.CompilerServices.ConditionalWeakTable<DictValue, Dictionary<ScriptValue, ScriptValue>>();
                ctx.ModuleState[key] = raw;
            }
            var table = (System.Runtime.CompilerServices.ConditionalWeakTable<DictValue, Dictionary<ScriptValue, ScriptValue>>)raw;
            Dictionary<ScriptValue, ScriptValue> memo;
            if (!table.TryGetValue(memoDict, out memo))
            {
                memo = new Dictionary<ScriptValue, ScriptValue>(RefEq.Instance);
                table.Add(memoDict, memo);
            }
            return memo;
        }

        // A value is atomic (deep-copies to itself) unless it is a mutable container or a tuple (whose
        // elements may be mutable). Tuples still route through the stack so nested mutables are copied.
        private static bool IsAtomic(ScriptValue v)
        {
            switch (v.Kind)
            {
                case ValueKind.List:
                case ValueKind.Set:
                case ValueKind.Dict:
                case ValueKind.Tuple:
                case ValueKind.RecordInstance:
                    return false;
                case ValueKind.Opaque:
                    return !(v is DequeValue);
                default:
                    return true;
            }
        }

        internal static ScriptValue DeepCopy(EvalContext ctx, ScriptValue root)
        {
            return DeepCopyWith(ctx, root, new Dictionary<ScriptValue, ScriptValue>(RefEq.Instance));
        }

        private static ScriptValue DeepCopyWith(EvalContext ctx, ScriptValue root, Dictionary<ScriptValue, ScriptValue> memo)
        {
            if (IsAtomic(root))
                return root;
            var stack = new Stack<WorkItem>();
            stack.Push(new WorkItem { Source = root, Phase = Phase.CreateClone });
            while (stack.Count > 0)
            {
                ctx.Budget.Step();
                ctx.Budget.CheckDeadlineNow();
                WorkItem w = stack.Pop();
                switch (w.Phase)
                {
                    case Phase.CreateClone:
                        CreateClone(ctx, w.Source, memo, stack);
                        break;
                    case Phase.Fill:
                        Fill(ctx, w.Source, w.Clone, memo);
                        break;
                    default:
                        BuildTuple(ctx, w.Source, memo);
                        break;
                }
            }
            return memo[root];
        }

        private static ScriptValue Resolve(ScriptValue child, Dictionary<ScriptValue, ScriptValue> memo)
        {
            ScriptValue c;
            return memo.TryGetValue(child, out c) ? c : child;   // atomic child -> itself
        }

        private static void PushChild(Stack<WorkItem> stack, ScriptValue child)
        {
            if (!IsAtomic(child))
                stack.Push(new WorkItem { Source = child, Phase = Phase.CreateClone });
        }

        private static void CreateClone(EvalContext ctx, ScriptValue source,
            Dictionary<ScriptValue, ScriptValue> memo, Stack<WorkItem> stack)
        {
            if (IsAtomic(source) || memo.ContainsKey(source))
                return;

            if (source.Kind == ValueKind.Tuple)
            {
                // Tuples are immutable: schedule element copies, then build (identity optimization) in BuildTuple.
                stack.Push(new WorkItem { Source = source, Phase = Phase.BuildTuple });
                ScriptValue[] items = ((TupleValue)source).Items;
                for (int i = items.Length - 1; i >= 0; i--)
                    PushChild(stack, items[i]);
                return;
            }

            ScriptValue clone;
            if (source.Kind == ValueKind.List)
                clone = ctx.Values.List(((ListValue)source).Items.Count);
            else if (source.Kind == ValueKind.Set)
                clone = ctx.Values.Set(((SetValue)source).Count);
            else if (source.Kind == ValueKind.Dict)
                clone = NewSameDict(ctx, (DictValue)source);
            else if (source.Kind == ValueKind.RecordInstance)
            {
                RecordInstanceValue ri = (RecordInstanceValue)source;
                ctx.Values.PreCharge(24 + 8L * ri.Slots.Length);
                clone = new RecordInstanceValue(ri.Class, new ScriptValue[ri.Slots.Length]);
            }
            else
                clone = new DequeValue(((DequeValue)source).MaxLenRaw);

            memo[source] = clone;
            stack.Push(new WorkItem { Source = source, Phase = Phase.Fill, Clone = clone });
            PushChildren(source, stack);
        }

        private static void PushChildren(ScriptValue source, Stack<WorkItem> stack)
        {
            if (source.Kind == ValueKind.List)
            {
                var items = ((ListValue)source).Items;
                for (int i = items.Count - 1; i >= 0; i--)
                    PushChild(stack, items[i]);
            }
            else if (source.Kind == ValueKind.Set)
            {
                var t = ((SetValue)source).Table;
                for (int p = t.EntriesUsed - 1; p >= 0; p--)
                {
                    long h;
                    ScriptValue k, v;
                    if (t.TryGetEntryAt(p, out h, out k, out v))
                        PushChild(stack, k);
                }
            }
            else if (source.Kind == ValueKind.Dict)
            {
                var t = ((DictValue)source).Table;
                for (int p = t.EntriesUsed - 1; p >= 0; p--)
                {
                    long h;
                    ScriptValue k, v;
                    if (t.TryGetEntryAt(p, out h, out k, out v))
                    {
                        PushChild(stack, v);
                        PushChild(stack, k);
                    }
                }
            }
            else if (source.Kind == ValueKind.RecordInstance)
            {
                ScriptValue[] slots = ((RecordInstanceValue)source).Slots;
                for (int i = slots.Length - 1; i >= 0; i--)
                    if (slots[i] != null)
                        PushChild(stack, slots[i]);
            }
            else // deque
            {
                DequeValue d = (DequeValue)source;
                for (int i = d.Count - 1; i >= 0; i--)
                    PushChild(stack, d.GetLogical(i));
            }
        }

        private static void Fill(EvalContext ctx, ScriptValue source, ScriptValue clone,
            Dictionary<ScriptValue, ScriptValue> memo)
        {
            if (source.Kind == ValueKind.List)
            {
                var items = ((ListValue)source).Items;
                ListValue dst = (ListValue)clone;
                for (int i = 0; i < items.Count; i++)
                    dst.Add(Resolve(items[i], memo), ctx);
            }
            else if (source.Kind == ValueKind.Set)
            {
                var t = ((SetValue)source).Table;
                SetValue dst = (SetValue)clone;
                for (int p = 0; p < t.EntriesUsed; p++)
                {
                    long h;
                    ScriptValue k, v;
                    if (t.TryGetEntryAt(p, out h, out k, out v))
                        dst.AddItem(Resolve(k, memo), ctx);
                }
            }
            else if (source.Kind == ValueKind.Dict)
            {
                var t = ((DictValue)source).Table;
                DictValue dst = (DictValue)clone;
                for (int p = 0; p < t.EntriesUsed; p++)
                {
                    long h;
                    ScriptValue k, v;
                    if (t.TryGetEntryAt(p, out h, out k, out v))
                        dst.SetItem(Resolve(k, memo), Resolve(v, memo), ctx);
                }
            }
            else if (source.Kind == ValueKind.RecordInstance)
            {
                ScriptValue[] slots = ((RecordInstanceValue)source).Slots;
                RecordInstanceValue dst = (RecordInstanceValue)clone;
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i] != null)
                        dst.InitSlot(i, Resolve(slots[i], memo));
            }
            else // deque
            {
                DequeValue src = (DequeValue)source;
                DequeValue dst = (DequeValue)clone;
                for (int i = 0; i < src.Count; i++)
                    dst.AppendRawPublic(Resolve(src.GetLogical(i), memo), ctx);
            }
        }

        private static void BuildTuple(EvalContext ctx, ScriptValue source, Dictionary<ScriptValue, ScriptValue> memo)
        {
            ScriptValue[] src = ((TupleValue)source).Items;
            var resolved = new ScriptValue[src.Length];
            bool allSame = true;
            for (int i = 0; i < src.Length; i++)
            {
                resolved[i] = Resolve(src[i], memo);
                if (!ReferenceEquals(resolved[i], src[i]))
                    allSame = false;
            }
            ScriptValue result;
            if (allSame)
                result = source;   // identity optimization: unchanged tuple stays itself
            else if (source is NamedTupleValue)
                result = new NamedTupleValue(((NamedTupleValue)source).NtInfo, resolved);
            else
                result = ctx.Values.Tuple(resolved);
            memo[source] = result;
        }
    }
}

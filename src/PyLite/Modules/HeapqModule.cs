using System;
using System.Collections.Generic;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // heapq: the binary min-heap of Lib/heapq.py over a list, plus nlargest / nsmallest / merge. Ordering
    // is the script's own '<', so a TypeError on unorderable items surfaces exactly where CPython's would.
    public static class HeapqModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["heappush"] = BuiltinFunctionValue.Make("heappush", (s, a, k, c) => HeapPush(c, a)),
                ["heappop"] = BuiltinFunctionValue.Make("heappop", (s, a, k, c) => HeapPop(c, a)),
                ["heapify"] = BuiltinFunctionValue.Make("heapify", (s, a, k, c) => Heapify(c, a)),
                ["heapreplace"] = BuiltinFunctionValue.Make("heapreplace", (s, a, k, c) => HeapReplace(c, a)),
                ["heappushpop"] = BuiltinFunctionValue.Make("heappushpop", (s, a, k, c) => HeapPushPop(c, a)),
                ["nlargest"] = BuiltinFunctionValue.Make("nlargest", (s, a, k, c) => NBest(c, a, k, "nlargest", true)),
                ["nsmallest"] = BuiltinFunctionValue.Make("nsmallest", (s, a, k, c) => NBest(c, a, k, "nsmallest", false)),
                ["merge"] = BuiltinFunctionValue.Make("merge", (s, a, k, c) => Merge(c, a, k)),
            };
            return ctx.Values.Module("heapq", m);
        }

        private static bool Lt(EvalContext ctx, ScriptValue a, ScriptValue b)
        {
            return PyOps.Truth(PyOps.RichCompare(PyCmpOp.Lt, a, b, ctx), ctx);
        }

        private static ListValue Heap(EvalContext ctx, ScriptValue[] a, string name, int expected)
        {
            Args.Exactly(ctx, a, name, expected);
            ListValue heap = a[0] as ListValue;
            if (heap == null)
                throw Raise.TypeError(ctx, "heap argument must be a list");
            return heap;
        }

        private static ScriptValue HeapPush(EvalContext ctx, ScriptValue[] a)
        {
            ListValue heap = Heap(ctx, a, "heappush", 2);
            heap.Add(a[1], ctx);
            SiftDown(ctx, heap.Items, 0, heap.Items.Count - 1);
            return ctx.Values.None;
        }

        private static ScriptValue HeapPop(EvalContext ctx, ScriptValue[] a)
        {
            ListValue heap = Heap(ctx, a, "heappop", 1);
            List<ScriptValue> h = heap.Items;
            if (h.Count == 0)
                throw Raise.IndexError(ctx, "index out of range");
            ScriptValue last = h[h.Count - 1];
            h.RemoveAt(h.Count - 1);
            if (h.Count == 0)
                return last;
            ScriptValue top = h[0];
            h[0] = last;
            SiftUp(ctx, h, 0);
            return top;
        }

        private static ScriptValue Heapify(EvalContext ctx, ScriptValue[] a)
        {
            List<ScriptValue> h = Heap(ctx, a, "heapify", 1).Items;
            for (int i = h.Count / 2 - 1; i >= 0; i--)
                SiftUp(ctx, h, i);
            return ctx.Values.None;
        }

        private static ScriptValue HeapReplace(EvalContext ctx, ScriptValue[] a)
        {
            List<ScriptValue> h = Heap(ctx, a, "heapreplace", 2).Items;
            if (h.Count == 0)
                throw Raise.IndexError(ctx, "index out of range");
            ScriptValue top = h[0];
            h[0] = a[1];
            SiftUp(ctx, h, 0);
            return top;
        }

        private static ScriptValue HeapPushPop(EvalContext ctx, ScriptValue[] a)
        {
            List<ScriptValue> h = Heap(ctx, a, "heappushpop", 2).Items;
            ScriptValue item = a[1];
            if (h.Count == 0 || !Lt(ctx, h[0], item))
                return item;
            ScriptValue top = h[0];
            h[0] = item;
            SiftUp(ctx, h, 0);
            return top;
        }

        // _siftdown of Lib/heapq.py: the new leaf at pos bubbles up toward startpos.
        private static void SiftDown(EvalContext ctx, List<ScriptValue> h, int startpos, int pos)
        {
            ScriptValue item = h[pos];
            while (pos > startpos)
            {
                ctx.Budget.Step();
                int parent = (pos - 1) >> 1;
                if (!Lt(ctx, item, h[parent]))
                    break;
                h[pos] = h[parent];
                pos = parent;
            }
            h[pos] = item;
        }

        // _siftup of Lib/heapq.py: the hole at pos sinks to a leaf along the smaller child, then the item
        // bubbles back up - fewer comparisons than the textbook version when the item is large.
        private static void SiftUp(EvalContext ctx, List<ScriptValue> h, int pos)
        {
            int end = h.Count;
            int start = pos;
            ScriptValue item = h[pos];
            int child = 2 * pos + 1;
            while (child < end)
            {
                ctx.Budget.Step();
                int right = child + 1;
                if (right < end && !Lt(ctx, h[child], h[right]))
                    child = right;
                h[pos] = h[child];
                pos = child;
                child = 2 * pos + 1;
            }
            h[pos] = item;
            SiftDown(ctx, h, start, pos);
        }

        // nlargest(n, it, key=) == sorted(it, key=key, reverse=True)[:n] and the mirror for nsmallest,
        // ties included, so the stable sort the builtins use answers both.
        private static ScriptValue NBest(EvalContext ctx, ScriptValue[] a, KwArgs kw, string name, bool largest)
        {
            Args.Exactly(ctx, a, name, 2);
            ScriptValue key = null;
            var r = new KwReader(ctx, kw, name);
            key = r.GetOrNull("key");
            r.RejectUnknown();
            int n = Coerce.ToInt32(ctx, a[0], "n must be an integer");
            var values = new List<ScriptValue>();
            IScriptIterator it = PyOps.GetIterator(a[1], ctx);
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
            {
                ctx.Budget.Step();
                values.Add(v);
            }
            if (n <= 0)
                return ctx.Values.List(0);
            ScriptValue[] arr = values.ToArray();
            ScriptValue[] keys = null;
            if (key != null)
            {
                keys = new ScriptValue[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                    keys[i] = ctx.CallHook(key, new[] { arr[i] }, KwArgs.Empty);
            }
            if (largest)
            {
                Array.Reverse(arr);                         // reverse, sort, reverse again keeps ties stable
                if (keys != null)
                    Array.Reverse(keys);
            }
            StableSort.Sort(ctx, arr, keys);
            if (largest)
                Array.Reverse(arr);
            int take = Math.Min(n, arr.Length);
            ListValue result = ctx.Values.List(take);
            for (int i = 0; i < take; i++)
                result.Add(arr[i], ctx);
            return result;
        }

        // merge(*iterables, key=None, reverse=False): lazy, as CPython's - each input is pulled one item
        // at a time, so an infinite or a huge input costs only what is consumed. The front is found by
        // a scan over the inputs (few in practice); a tie goes to the earlier input, which keeps the
        // merge stable in both directions.
        private static ScriptValue Merge(EvalContext ctx, ScriptValue[] a, KwArgs kw)
        {
            ScriptValue key = null;
            bool reverse = false;
            var r = new KwReader(ctx, kw, "merge");
            key = r.GetOrNull("key");
            reverse = r.Bool("reverse", reverse);
            r.RejectUnknown();
            var its = new IScriptIterator[a.Length];
            for (int i = 0; i < a.Length; i++)
                its[i] = PyOps.GetIterator(a[i], ctx);
            ctx.Values.PreCharge(48 + 32L * a.Length);
            return ctx.Values.Iterator(new MergeIterator(its, key, reverse), "heapq.merge");
        }

        private sealed class MergeIterator : ScriptIteratorBase
        {
            private readonly IScriptIterator[] _its;
            private readonly ScriptValue[] _head;   // the front item of each input; null once exhausted
            private readonly ScriptValue[] _key;    // its key (the item itself without key=)
            private readonly ScriptValue _keyFn;
            private readonly bool _reverse;
            private bool _primed;

            internal MergeIterator(IScriptIterator[] its, ScriptValue keyFn, bool reverse)
            {
                _its = its;
                _head = new ScriptValue[its.Length];
                _key = new ScriptValue[its.Length];
                _keyFn = keyFn;
                _reverse = reverse;
            }

            private void Advance(EvalContext ctx, int i)
            {
                ScriptValue v;
                if (_its[i] != null && _its[i].MoveNext(ctx, out v))
                {
                    _head[i] = v;
                    _key[i] = _keyFn == null ? v : ctx.CallHook(_keyFn, new[] { v }, KwArgs.Empty);
                    return;
                }
                _its[i] = null;
                _head[i] = null;
                _key[i] = null;
            }

            protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
            {
                if (!_primed)
                {
                    _primed = true;
                    for (int i = 0; i < _its.Length; i++)
                        Advance(ctx, i);
                }
                int best = -1;
                for (int i = 0; i < _its.Length; i++)
                {
                    if (_head[i] == null)
                        continue;
                    ctx.Budget.Step();
                    // a later input replaces the front only when strictly better: ties keep input order
                    if (best < 0 || (_reverse ? Lt(ctx, _key[best], _key[i]) : Lt(ctx, _key[i], _key[best])))
                        best = i;
                }
                if (best < 0)
                {
                    value = null;
                    return false;
                }
                value = _head[best];
                Advance(ctx, best);
                return true;
            }
        }
    }
}

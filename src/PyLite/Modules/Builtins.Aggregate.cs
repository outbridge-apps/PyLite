using System.Collections.Generic;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    internal static partial class Builtins
    {
        static partial void RegisterAggregate(Dictionary<string, ScriptValue> d)
        {
            Add(d, "any", (self, args, kw, c) => c.Values.Bool(AnyAll(c, args, "any", true)));
            Add(d, "all", (self, args, kw, c) => c.Values.Bool(AnyAll(c, args, "all", false)));
            Add(d, "sum", (self, args, kw, c) => SumBuiltin(c, args, kw));
            Add(d, "min", (self, args, kw, c) => MinMax(c, args, kw, true));
            Add(d, "max", (self, args, kw, c) => MinMax(c, args, kw, false));
            Add(d, "sorted", (self, args, kw, c) => SortedBuiltin(c, args, kw));
        }

        // any: true on the first truthy; all: false on the first falsy.
        private static bool AnyAll(EvalContext c, ScriptValue[] args, string name, bool isAny)
        {
            ScriptValue x = Args.One(c, args, name);
            IScriptIterator it = PyOps.GetIterator(x, c);
            ScriptValue v;
            while (it.MoveNext(c, out v))
            {
                if (v.IsTruthy(c) == isAny)
                    return isAny;
            }
            return !isAny;
        }

        private static ScriptValue SumBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            ScriptValue start = null;
            if (kw.Count > 0 && (kw.Count != 1 || !kw.TryGet("start", out start)))
                throw Raise.TypeError(c, "sum() takes at most 1 keyword argument ('start')");
            if (args.Length < 1 || args.Length > 2)
                throw Raise.TypeError(c, "sum expected at most 2 arguments, got " + args.Length);
            if (args.Length == 2 && start != null)
                throw Raise.TypeError(c, "sum() got multiple values for argument 'start'");
            if (start == null)
                start = args.Length == 2 ? args[1] : c.Values.Int(0);
            if (start.Kind == ValueKind.Str)
                throw Raise.TypeError(c, "sum() can't sum strings [use ''.join(seq) instead]");
            ScriptValue fast = TrySumSmall(c, args[0], start);
            if (fast != null)
                return fast;
            ScriptValue acc = start;
            IScriptIterator it = PyOps.GetIterator(args[0], c);
            ScriptValue v;
            while (it.MoveNext(c, out v))
                acc = PyOps.BinaryOp(PyBinOp.Add, acc, v, c);
            return acc;
        }

        // sum() fast path: a list/tuple whose elements are all long-backed ints accumulates in long.
        // Overflow or any non-conforming element falls back to the exact path from scratch (no side
        // effects were taken). The Step mirrors the iterator path's n+1 MoveNext charges.
        private static ScriptValue TrySumSmall(EvalContext c, ScriptValue seq, ScriptValue start)
        {
            IntValue sv = start as IntValue;
            if (sv == null || !sv.IsSmall)
                return null;
            ListValue lv = seq as ListValue;
            TupleValue tv = lv == null ? seq as TupleValue : null;
            if (lv == null && tv == null)
                return null;
            long acc = sv.Small;
            int n = lv != null ? lv.Items.Count : tv.Items.Length;
            for (int i = 0; i < n; i++)
            {
                IntValue iv = (lv != null ? lv.Items[i] : tv.Items[i]) as IntValue;
                if (iv == null || !iv.IsSmall)
                    return null;
                long r = unchecked(acc + iv.Small);
                if (((acc ^ r) & (iv.Small ^ r)) < 0)
                    return null;   // overflow -> exact BigInteger path
                acc = r;
            }
            c.Budget.Step(n + 1);
            return c.Values.Int(acc);
        }

        private static ScriptValue MinMax(EvalContext c, ScriptValue[] args, KwArgs kw, bool isMin)
        {
            string name = isMin ? "min" : "max";
            ScriptValue key = null;
            ScriptValue dflt = null;
            bool hasDefault = false;
            var r = new KwReader(c, kw, name);
            key = r.GetOrNull("key");
            hasDefault = r.TryGet("default", out dflt);
            r.RejectInvalid();

            IScriptIterator it;
            if (args.Length == 0)
                throw Raise.TypeError(c, name + " expected 1 argument, got 0");
            if (args.Length > 1)
            {
                if (hasDefault)
                    throw Raise.TypeError(c, "Cannot specify a default for " + name + "() with multiple positional arguments");
                it = new ArrayIterator(args);
            }
            else
            {
                if (key == null)
                {
                    ScriptValue fast = TryMinMaxSmall(c, args[0], isMin);
                    if (fast != null)
                        return fast;
                }
                it = PyOps.GetIterator(args[0], c);
            }

            ScriptValue best = null, bestKey = null;
            ScriptValue item;
            while (it.MoveNext(c, out item))
            {
                ScriptValue k = key == null ? item : Invoke(c, key, item);
                if (best == null)
                {
                    best = item;
                    bestKey = k;
                    continue;
                }
                c.Budget.Step();
                bool replace = isMin ? Less(c, k, bestKey) : Less(c, bestKey, k);
                if (replace)
                {
                    best = item;
                    bestKey = k;
                }
            }
            if (best != null)
                return best;
            if (hasDefault)
                return dflt;
            throw Raise.ValueError(c, name + "() arg is an empty sequence");
        }

        // min/max fast path (no key): a non-empty list/tuple of long-backed ints compares in long,
        // returning the FIRST extremal element (strict-replace keeps first-wins like the generic path).
        // Steps mirror the generic path: n+1 iterator moves + 2 per comparison (explicit + Order).
        private static ScriptValue TryMinMaxSmall(EvalContext c, ScriptValue seq, bool isMin)
        {
            ListValue lv = seq as ListValue;
            TupleValue tv = lv == null ? seq as TupleValue : null;
            if (lv == null && tv == null)
                return null;
            int n = lv != null ? lv.Items.Count : tv.Items.Length;
            if (n == 0)
                return null;   // generic path raises/returns default
            int bestIdx = 0;
            IntValue first = (lv != null ? lv.Items[0] : tv.Items[0]) as IntValue;
            if (first == null || !first.IsSmall)
                return null;
            long best = first.Small;
            for (int i = 1; i < n; i++)
            {
                IntValue iv = (lv != null ? lv.Items[i] : tv.Items[i]) as IntValue;
                if (iv == null || !iv.IsSmall)
                    return null;
                if (isMin ? iv.Small < best : iv.Small > best)
                {
                    best = iv.Small;
                    bestIdx = i;
                }
            }
            c.Budget.Step(n + 1 + 2 * (n - 1));
            return lv != null ? lv.Items[bestIdx] : tv.Items[bestIdx];
        }

        private static ScriptValue SortedBuiltin(EvalContext c, ScriptValue[] args, KwArgs kw)
        {
            Args.Exactly(c, args, "sorted", 1);
            ScriptValue key = null;
            bool reverse = false;
            var r = new KwReader(c, kw, "sorted");
            key = r.GetOrNull("key");
            reverse = r.Bool("reverse", reverse);
            r.RejectInvalid();

            var buf = new List<ScriptValue>();
            IScriptIterator it = PyOps.GetIterator(args[0], c);
            ScriptValue v;
            while (it.MoveNext(c, out v))
            {
                c.Budget.Step();
                buf.Add(v);
            }
            ScriptValue[] values = buf.ToArray();

            if (reverse)
                System.Array.Reverse(values);   // pre-reverse to keep stability of equal keys

            ScriptValue[] keys = null;
            if (key != null)
            {
                keys = new ScriptValue[values.Length];
                for (int i = 0; i < values.Length; i++)
                    keys[i] = Invoke(c, key, values[i]);
            }

            StableSort.Sort(c, values, keys);

            if (reverse)
                System.Array.Reverse(values);

            ListValue result = c.Values.List(values.Length);
            for (int i = 0; i < values.Length; i++)
                result.Add(values[i], c);
            return result;
        }

        private static bool Less(EvalContext c, ScriptValue a, ScriptValue b)
        {
            return PyOps.RichCompare(PyCmpOp.Lt, a, b, c).IsTruthy(c);
        }

        // Iterates a plain ScriptValue[] (for min/max over positional arguments); charges a step per item.
        private sealed class ArrayIterator : ScriptIteratorBase
        {
            private readonly ScriptValue[] _items;
            private int _pos;
            internal ArrayIterator(ScriptValue[] items) { _items = items; }

            protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
            {
                if (_pos < _items.Length)
                {
                    value = _items[_pos++];
                    return true;
                }
                value = null;
                return false;
            }
        }
    }
}

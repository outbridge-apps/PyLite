using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // bisect: the four searches and two insertions of Lib/bisect.py, with the lo / hi / key keywords of
    // CPython 3.10+. Searches take a list or a tuple; insertions take a list, the only thing with insert.
    public static class BisectModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["bisect_left"] = BuiltinFunctionValue.Make("bisect_left", (s, a, k, c) => Search(c, a, k, "bisect_left", false)),
                ["bisect_right"] = BuiltinFunctionValue.Make("bisect_right", (s, a, k, c) => Search(c, a, k, "bisect_right", true)),
                ["bisect"] = BuiltinFunctionValue.Make("bisect", (s, a, k, c) => Search(c, a, k, "bisect", true)),
                ["insort_left"] = BuiltinFunctionValue.Make("insort_left", (s, a, k, c) => Insort(c, a, k, "insort_left", false)),
                ["insort_right"] = BuiltinFunctionValue.Make("insort_right", (s, a, k, c) => Insort(c, a, k, "insort_right", true)),
                ["insort"] = BuiltinFunctionValue.Make("insort", (s, a, k, c) => Insort(c, a, k, "insort", true)),
            };
            return ctx.Values.Module("bisect", m);
        }

        private sealed class Call
        {
            public ScriptValue Seq;
            public ScriptValue X;
            public int Lo, Hi;
            public ScriptValue Key;   // null = compare the items themselves
        }

        private static ScriptValue Search(EvalContext ctx, ScriptValue[] a, KwArgs kw, string name, bool right)
        {
            Call c = Parse(ctx, a, kw, name);
            return ctx.Values.Int(Locate(ctx, c, right));
        }

        // insort keys the NEW item once and compares it against keyed elements, exactly like CPython.
        private static ScriptValue Insort(EvalContext ctx, ScriptValue[] a, KwArgs kw, string name, bool right)
        {
            Call c = Parse(ctx, a, kw, name);
            ListValue list = c.Seq as ListValue;
            if (list == null)
                throw Raise.TypeError(ctx, name + "() argument 1 must be a list, not " + c.Seq.PyTypeName);
            ScriptValue item = c.X;
            if (c.Key != null)
                c.X = ctx.CallHook(c.Key, new[] { item }, KwArgs.Empty);
            int at = Locate(ctx, c, right);
            if (list.Items.Count >= ctx.Limits.MaxCollectionItems)
                SequenceOps.CheckCap((long)list.Items.Count + 1, ctx);
            ctx.Budget.ChargeAllocation(8);
            ctx.Budget.ChargeLinear(list.Items.Count - at);   // the tail moves up by one
            list.Items.Insert(at > list.Items.Count ? list.Items.Count : at, item);   // lo past the end: append, as list.insert
            return ctx.Values.None;
        }

        // The binary search of Lib/bisect.py: right finds the slot after equal items, left the one before.
        private static int Locate(EvalContext ctx, Call c, bool right)
        {
            int lo = c.Lo, hi = c.Hi;
            while (lo < hi)
            {
                ctx.Budget.Step();
                int mid = lo + ((hi - lo) >> 1);
                ScriptValue item = ItemAt(ctx, c.Seq, mid);
                if (c.Key != null)
                    item = ctx.CallHook(c.Key, new[] { item }, KwArgs.Empty);
                bool goRight = right
                    ? !PyOps.Truth(PyOps.RichCompare(PyCmpOp.Lt, c.X, item, ctx), ctx)
                    : PyOps.Truth(PyOps.RichCompare(PyCmpOp.Lt, item, c.X, ctx), ctx);
                if (goRight)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return lo;
        }

        private static Call Parse(EvalContext ctx, ScriptValue[] a, KwArgs kw, string name)
        {
            if (a.Length < 2)
                throw Raise.TypeError(ctx, name + "() missing required argument '" + (a.Length == 0 ? "a" : "x") + "'");
            Args.AtMost(ctx, a, name, 4);
            var c = new Call { Seq = a[0], X = a[1] };
            int n = Length(ctx, c.Seq, name);
            ScriptValue lo = a.Length > 2 ? a[2] : null, hi = a.Length > 3 ? a[3] : null, v;
            if (kw.TryGet("lo", out v))
                lo = v;
            if (kw.TryGet("hi", out v))
                hi = v;
            if (kw.TryGet("key", out v) && v.Kind != ValueKind.None)
                c.Key = v;
            KwReader.RejectUnknownExcept(ctx, kw, name, "lo", "hi", "key");
            c.Lo = lo == null ? 0 : Index(ctx, lo, "lo");
            if (c.Lo < 0)
                throw Raise.ValueError(ctx, "lo must be non-negative");
            c.Hi = hi == null || hi.Kind == ValueKind.None ? n : Index(ctx, hi, "hi");
            if (c.Hi > n)
                c.Hi = n;
            return c;
        }

        private static int Length(EvalContext ctx, ScriptValue seq, string name)
        {
            ListValue l = seq as ListValue;
            if (l != null)
                return l.Items.Count;
            TupleValue t = seq as TupleValue;
            if (t != null)
                return t.Items.Length;
            throw Raise.TypeError(ctx, name + "() argument 1 must be a list or a tuple, not " + seq.PyTypeName);
        }

        private static ScriptValue ItemAt(EvalContext ctx, ScriptValue seq, int i)
        {
            ListValue l = seq as ListValue;
            return l != null ? l.Items[i] : ((TupleValue)seq).Items[i];
        }

        private static int Index(EvalContext ctx, ScriptValue v, string what)
        {
            if (v.Kind != ValueKind.Int && v.Kind != ValueKind.Bool)
                throw Raise.TypeError(ctx, "'" + v.PyTypeName + "' object cannot be interpreted as an integer");
            System.Numerics.BigInteger n = NumericOps.AsBigInteger(v);
            if (n > int.MaxValue)
                return int.MaxValue;
            if (n < int.MinValue)
                return int.MinValue;
            return (int)n;
        }
    }
}

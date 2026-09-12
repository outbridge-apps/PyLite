using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // The sequence protocol list, tuple and struct_time share: membership, index and count with the
    // small-int fast path, index normalisation and its two error texts, slicing, and the size arithmetic
    // of concat and repeat. The container itself stays with the type (a List<T> for list, an array for
    // the immutable two): the loops are generic over a struct view of either, so the JIT specialises them
    // and the element read inlines; through IReadOnlyList the same loop paid an interface call per element.
    internal interface ISequence
    {
        int Count { get; }
        ScriptValue this[int i] { get; }
    }

    internal struct ArraySeq : ISequence
    {
        private readonly ScriptValue[] _a;
        internal ArraySeq(ScriptValue[] a) { _a = a; }
        public int Count { get { return _a.Length; } }
        public ScriptValue this[int i] { get { return _a[i]; } }
    }

    internal struct ListSeq : ISequence
    {
        private readonly List<ScriptValue> _l;
        internal ListSeq(List<ScriptValue> l) { _l = l; }
        public int Count { get { return _l.Count; } }
        public ScriptValue this[int i] { get { return _l[i]; } }
    }

    internal static class SequenceOps
    {
        internal static bool IsIndex(ScriptValue index)
        {
            return index.Kind == ValueKind.Int || index.Kind == ValueKind.Bool;
        }

        internal static bool Contains<TSeq>(TSeq items, ScriptValue item, EvalContext ctx) where TSeq : struct, ISequence
        {
            return IndexOf(items, item, 0, items.Count, ctx) >= 0;
        }

        // First index of x in [start, end), or -1. SmallIntEq answers 0/1 for an int-like element and -1
        // when the element needs the full protocol (float / user __eq__).
        internal static int IndexOf<TSeq>(TSeq items, ScriptValue x, int start, int end, EvalContext ctx) where TSeq : struct, ISequence
        {
            long needle;
            bool fast = PyOps.TryGetSmallInt(x, out needle);
            for (int i = start; i < end && i < items.Count; i++)
            {
                ctx.Budget.Step();
                ScriptValue el = items[i];
                if (fast)
                {
                    int r = PyOps.SmallIntEq(needle, el);
                    if (r == 0)
                        continue;
                    if (r == 1)
                        return i;
                }
                if (PyOps.Equals(x, el, ctx, 0))
                    return i;
            }
            return -1;
        }

        internal static long Count<TSeq>(TSeq items, ScriptValue x, EvalContext ctx) where TSeq : struct, ISequence
        {
            long needle;
            bool fast = PyOps.TryGetSmallInt(x, out needle);
            long n = 0;   // items.Count <= int.MaxValue — a long cannot overflow
            for (int i = 0; i < items.Count; i++)
            {
                ctx.Budget.Step();
                ScriptValue el = items[i];
                if (fast)
                {
                    int r = PyOps.SmallIntEq(needle, el);
                    if (r >= 0)
                    {
                        n += r;
                        continue;
                    }
                }
                if (PyOps.Equals(x, el, ctx, 0))
                    n++;
            }
            return n;
        }

        // seq[i] for an int-like index: negative counts from the end; "<type> index out of range" otherwise.
        internal static int NormIndex(ScriptValue index, int count, string typeName, EvalContext ctx)
        {
            BigInteger i = NumericOps.AsBigInteger(index);
            if (i < 0)
                i += count;
            if (i < 0 || i >= count)
                throw Raise.IndexError(ctx, typeName + " index out of range");
            return (int)i;
        }

        internal static ScriptException IndexTypeError(ScriptValue index, string typeName, EvalContext ctx)
        {
            return Raise.TypeError(ctx, typeName + " indices must be integers, not " + index.PyTypeName);
        }

        internal static ScriptValue[] Slice<TSeq>(TSeq items, SliceValue sl, EvalContext ctx) where TSeq : struct, ISequence
        {
            SliceIndices ind = sl.Indices(items.Count, ctx);
            int len = (int)ind.Length;
            if (len == 0)
                return Array.Empty<ScriptValue>();
            int idx = (int)ind.Start, step = ind.StepInt;
            var arr = new ScriptValue[len];
            for (int k = 0; k < len; k++)
            {
                arr[k] = items[idx];
                idx += step;
            }
            return arr;
        }

        // seq * n: the repeat count (0 for n <= 0) once the product passed the collection cap; -1 when the
        // operand is not an int, so the caller declines the operator. A float gets CPython's text. An empty
        // sequence answers before the cast: `[] * 10**30` is [] and its count would not fit an int.
        internal static int RepeatCount(int len, ScriptValue other, EvalContext ctx)
        {
            if (other.Kind == ValueKind.Float)
                throw Raise.TypeError(ctx, "can't multiply sequence by non-int of type 'float'");
            if (!IsIndex(other))
                return -1;
            BigInteger n = NumericOps.AsBigInteger(other);
            if (n <= 0 || len == 0)
                return 0;
            CheckCap((BigInteger)len * n, ctx);
            return (int)n;
        }

        internal static ScriptException ConcatTypeError(ScriptValue other, string typeName, EvalContext ctx)
        {
            return Raise.TypeError(ctx, "can only concatenate " + typeName + " (not \"" + other.PyTypeName + "\") to " + typeName);
        }

        internal static void CheckCap(BigInteger total, EvalContext ctx)
        {
            if (total > ctx.Limits.MaxCollectionItems)
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxCollectionItems", ctx.Limits.MaxCollectionItems,
                    total > long.MaxValue ? long.MaxValue : (long)total);
        }
    }
}

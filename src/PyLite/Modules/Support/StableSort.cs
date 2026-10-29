using System;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // Our own stable sort: Timsort's natural merge sort without galloping (runs found and extended to
    // minrun by binary insertion, merged under Timsort's stack invariants, merges trimmed by binary
    // search). Presorted or reversed input costs n-1 comparisons. Sorts `values` in place, ordered by
    // `keys` (keys == null => the values); `keys` is left in an unspecified order.
    //
    // When every key is a small int, a str or a non-NaN float, the keys are copied out into a primitive
    // array and compared directly (CPython's pre-sort check): no script code can run, so the work is
    // charged up front as n*ceil(log2 n) steps. Otherwise each comparison charges one step and may run
    // script code; whatever it raises propagates unchanged, and the arrays then still hold every element.
    internal static class StableSort
    {
        internal static void Sort(EvalContext ctx, ScriptValue[] values, ScriptValue[] keys)
        {
            int n = values.Length;
            if (n < 2)
                return;
            ctx.Budget.CheckDeadlineNow();
            ScriptValue[] k = keys ?? values;
            long tmpLen = n >= MinMerge ? n / 2 + 1 : 0;
            switch (Classify(k))
            {
                case KeyKind.SmallInt:
                {
                    ctx.Values.PreCharge(8L * n + 16L * tmpLen);
                    ChargeTyped(ctx, n);
                    var a = new long[n];
                    for (int i = 0; i < n; i++)
                        a[i] = ((IntValue)k[i]).Small;
                    new Sorter<long, LongLess>(a, values, default(LongLess), ctx, true).Run();
                    break;
                }
                case KeyKind.Float:
                {
                    ctx.Values.PreCharge(8L * n + 16L * tmpLen);
                    ChargeTyped(ctx, n);
                    var a = new double[n];
                    for (int i = 0; i < n; i++)
                        a[i] = ((FloatValue)k[i]).Value;
                    new Sorter<double, DoubleLess>(a, values, default(DoubleLess), ctx, true).Run();
                    break;
                }
                case KeyKind.Str:
                {
                    ctx.Values.PreCharge(8L * n + 16L * tmpLen);
                    ChargeTyped(ctx, n);
                    var a = new string[n];
                    for (int i = 0; i < n; i++)
                        a[i] = ((StrValue)k[i]).Value;
                    new Sorter<string, StrLess>(a, values, default(StrLess), ctx, true).Run();
                    break;
                }
                default:
                    ctx.Values.PreCharge((keys != null ? 16L : 8L) * tmpLen);
                    new Sorter<ScriptValue, ScriptLess>(k, keys != null ? values : null, new ScriptLess(ctx), ctx, false).Run();
                    break;
            }
            ctx.Budget.CheckDeadlineNow();
        }

        private enum KeyKind { Generic, SmallInt, Float, Str }

        // One pass over the keys; any bool, big int, NaN, mixed kinds or other type keeps the generic path.
        private static KeyKind Classify(ScriptValue[] k)
        {
            ScriptValue first = k[0];
            IntValue fi = first as IntValue;
            if (fi != null)
            {
                for (int i = 0; i < k.Length; i++)
                {
                    IntValue iv = k[i] as IntValue;
                    if (iv == null || !iv.IsSmall)
                        return KeyKind.Generic;
                }
                return KeyKind.SmallInt;
            }
            if (first is StrValue)
            {
                for (int i = 0; i < k.Length; i++)
                {
                    if (!(k[i] is StrValue))
                        return KeyKind.Generic;
                }
                return KeyKind.Str;
            }
            if (first is FloatValue)
            {
                for (int i = 0; i < k.Length; i++)
                {
                    FloatValue fv = k[i] as FloatValue;
                    if (fv == null || double.IsNaN(fv.Value))
                        return KeyKind.Generic;
                }
                return KeyKind.Float;
            }
            return KeyKind.Generic;
        }

        // A typed sort is bounded and cannot call script code: charge its comparisons before it starts.
        private static void ChargeTyped(EvalContext ctx, int n)
        {
            int log = 1;
            while ((1L << log) < n)
                log++;
            long steps = (long)n * log;
            ctx.Budget.Step(steps > int.MaxValue ? int.MaxValue : (int)steps);
        }

        private const int MinMerge = 64;   // CPython: below this a whole slice is one binary insertion sort
        private const int MaxPending = 85;  // CPython's MAX_MERGE_PENDING, enough for any array length

        private interface ILess<T>
        {
            bool Less(T a, T b);
        }

        private struct LongLess : ILess<long>
        {
            public bool Less(long a, long b) { return a < b; }
        }

        // -0.0 and 0.0 compare equal and so keep their input order, as the generic path does.
        private struct DoubleLess : ILess<double>
        {
            public bool Less(double a, double b) { return a < b; }
        }

        // UTF-16 ordinal, the order the generic path's str fast path has always used.
        private struct StrLess : ILess<string>
        {
            public bool Less(string a, string b) { return string.CompareOrdinal(a, b) < 0; }
        }

        // One step per comparison: the int/str fast path charges it here, RichCompare charges its own.
        private struct ScriptLess : ILess<ScriptValue>
        {
            private readonly EvalContext _ctx;
            internal ScriptLess(EvalContext ctx) { _ctx = ctx; }

            public bool Less(ScriptValue a, ScriptValue b)
            {
                IntValue ia = a as IntValue, ib = b as IntValue;
                if (ia != null && ib != null && ia.IsSmall && ib.IsSmall)
                {
                    _ctx.Budget.Step();
                    return ia.Small < ib.Small;
                }
                if (a.Kind == ValueKind.Str && b.Kind == ValueKind.Str)
                {
                    _ctx.Budget.Step();
                    return string.CompareOrdinal(((StrValue)a).Value, ((StrValue)b).Value) < 0;
                }
                return PyOps.RichCompare(PyCmpOp.Lt, a, b, _ctx).IsTruthy(_ctx);
            }
        }

        // Sorts `_a` and moves `_v` (when not null) in step with it.
        private sealed class Sorter<T, TLess> where TLess : struct, ILess<T>
        {
            private readonly T[] _a;
            private readonly ScriptValue[] _v;
            private readonly EvalContext _ctx;
            private readonly bool _typed;
            private TLess _less;
            private T[] _tmpA;
            private ScriptValue[] _tmpV;
            private readonly int[] _runBase = new int[MaxPending];
            private readonly int[] _runLen = new int[MaxPending];
            private int _stackSize;

            internal Sorter(T[] a, ScriptValue[] v, TLess less, EvalContext ctx, bool typed)
            {
                _a = a;
                _v = v;
                _less = less;
                _ctx = ctx;
                _typed = typed;
            }

            internal void Run()
            {
                int n = _a.Length;
                int minRun = MinRunLength(n);
                int lo = 0, remaining = n;
                do
                {
                    int runLen = CountRunAndMakeAscending(lo, n);
                    if (runLen < minRun)
                    {
                        int force = remaining <= minRun ? remaining : minRun;
                        BinarySort(lo, lo + force, lo + runLen);
                        runLen = force;
                    }
                    _runBase[_stackSize] = lo;
                    _runLen[_stackSize] = runLen;
                    _stackSize++;
                    MergeCollapse();
                    lo += runLen;
                    remaining -= runLen;
                }
                while (remaining != 0);
                MergeForceCollapse();
            }

            private static int MinRunLength(int n)
            {
                int r = 0;
                while (n >= MinMerge)
                {
                    r |= n & 1;
                    n >>= 1;
                }
                return n + r;
            }

            // A maximal non-descending run, or a strictly descending one reversed in place (strictly, so
            // equal elements never swap).
            private int CountRunAndMakeAscending(int lo, int hi)
            {
                int runHi = lo + 1;
                if (runHi == hi)
                    return 1;
                if (_less.Less(_a[runHi++], _a[lo]))
                {
                    while (runHi < hi && _less.Less(_a[runHi], _a[runHi - 1]))
                        runHi++;
                    Reverse(lo, runHi);
                }
                else
                {
                    while (runHi < hi && !_less.Less(_a[runHi], _a[runHi - 1]))
                        runHi++;
                }
                return runHi - lo;
            }

            private void Reverse(int lo, int hi)
            {
                Array.Reverse(_a, lo, hi - lo);
                if (_v != null)
                    Array.Reverse(_v, lo, hi - lo);
            }

            // [lo, start) is sorted; insert each later element after its equals. The search compares
            // before anything moves, so an exception leaves the slice intact.
            private void BinarySort(int lo, int hi, int start)
            {
                for (; start < hi; start++)
                {
                    T pivot = _a[start];
                    int left = lo, right = start;
                    while (left < right)
                    {
                        int mid = (left + right) >> 1;
                        if (_less.Less(pivot, _a[mid]))
                            right = mid;
                        else
                            left = mid + 1;
                    }
                    int count = start - left;
                    if (count == 0)
                        continue;
                    Array.Copy(_a, left, _a, left + 1, count);
                    _a[left] = pivot;
                    if (_v != null)
                    {
                        ScriptValue pv = _v[start];
                        Array.Copy(_v, left, _v, left + 1, count);
                        _v[left] = pv;
                    }
                }
            }

            // Timsort's invariants with the 2015 correction (de Gouw et al.): every run is longer than the
            // two above it combined, and longer than the one above it.
            private void MergeCollapse()
            {
                while (_stackSize > 1)
                {
                    int n = _stackSize - 2;
                    if ((n > 0 && _runLen[n - 1] <= _runLen[n] + _runLen[n + 1])
                        || (n > 1 && _runLen[n - 2] <= _runLen[n] + _runLen[n - 1]))
                    {
                        if (_runLen[n - 1] < _runLen[n + 1])
                            n--;
                    }
                    else if (_runLen[n] > _runLen[n + 1])
                    {
                        break;
                    }
                    MergeAt(n);
                }
            }

            private void MergeForceCollapse()
            {
                while (_stackSize > 1)
                {
                    int n = _stackSize - 2;
                    if (n > 0 && _runLen[n - 1] < _runLen[n + 1])
                        n--;
                    MergeAt(n);
                }
            }

            private void MergeAt(int i)
            {
                if (_typed)
                    _ctx.Budget.CheckDeadlineNow();   // the typed path charged its steps up front
                int base1 = _runBase[i], len1 = _runLen[i];
                int base2 = _runBase[i + 1], len2 = _runLen[i + 1];
                _runLen[i] = len1 + len2;
                if (i == _stackSize - 3)
                {
                    _runBase[i + 1] = _runBase[i + 2];
                    _runLen[i + 1] = _runLen[i + 2];
                }
                _stackSize--;

                // Run 1's elements not above run 2's first are already in place, and so are run 2's
                // elements not below run 1's last.
                int k = UpperBound(_a[base2], base1, len1) - base1;
                base1 += k;
                len1 -= k;
                if (len1 == 0)
                    return;
                len2 = LowerBound(_a[base1 + len1 - 1], base2, len2) - base2;
                if (len2 == 0)
                    return;
                if (len1 <= len2)
                    MergeLo(base1, len1, base2, len2);
                else
                    MergeHi(base1, len1, base2, len2);
            }

            // First index in [lo, lo+len) whose element is above key (after the equals).
            private int UpperBound(T key, int lo, int len)
            {
                int hi = lo + len;
                while (lo < hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (_less.Less(key, _a[mid]))
                        hi = mid;
                    else
                        lo = mid + 1;
                }
                return lo;
            }

            // First index in [lo, lo+len) whose element is not below key (before the equals).
            private int LowerBound(T key, int lo, int len)
            {
                int hi = lo + len;
                while (lo < hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (_less.Less(_a[mid], key))
                        lo = mid + 1;
                    else
                        hi = mid;
                }
                return lo;
            }

            private void EnsureTmp(int len)
            {
                if (_tmpA != null && _tmpA.Length >= len)
                    return;
                int size = Math.Max(len, _a.Length / 2 + 1);   // charged up front by Sort
                _tmpA = new T[size];
                if (_v != null)
                    _tmpV = new ScriptValue[size];
            }

            // Run 1 (the shorter) goes to the buffer and is merged forwards. Run 2 is taken only when
            // strictly below, which is the stability rule. The finally puts back whatever the buffer still
            // holds, so a comparison that raises leaves every element in the array.
            private void MergeLo(int base1, int len1, int base2, int len2)
            {
                EnsureTmp(len1);
                Array.Copy(_a, base1, _tmpA, 0, len1);
                if (_v != null)
                    Array.Copy(_v, base1, _tmpV, 0, len1);
                int c1 = 0, c2 = base2, end2 = base2 + len2, dest = base1;
                try
                {
                    while (c1 < len1 && c2 < end2)
                    {
                        if (_less.Less(_a[c2], _tmpA[c1]))
                        {
                            _a[dest] = _a[c2];
                            if (_v != null)
                                _v[dest] = _v[c2];
                            c2++;
                        }
                        else
                        {
                            _a[dest] = _tmpA[c1];
                            if (_v != null)
                                _v[dest] = _tmpV[c1];
                            c1++;
                        }
                        dest++;
                    }
                }
                finally
                {
                    if (c1 < len1)
                    {
                        Array.Copy(_tmpA, c1, _a, dest, len1 - c1);
                        if (_v != null)
                            Array.Copy(_tmpV, c1, _v, dest, len1 - c1);
                    }
                }
            }

            // Run 2 (the shorter) goes to the buffer and is merged backwards; on a tie its element is the
            // later one and goes last.
            private void MergeHi(int base1, int len1, int base2, int len2)
            {
                EnsureTmp(len2);
                Array.Copy(_a, base2, _tmpA, 0, len2);
                if (_v != null)
                    Array.Copy(_v, base2, _tmpV, 0, len2);
                int c1 = base1 + len1 - 1, c2 = len2 - 1, dest = base2 + len2 - 1;
                try
                {
                    while (c2 >= 0 && c1 >= base1)
                    {
                        if (_less.Less(_tmpA[c2], _a[c1]))
                        {
                            _a[dest] = _a[c1];
                            if (_v != null)
                                _v[dest] = _v[c1];
                            c1--;
                        }
                        else
                        {
                            _a[dest] = _tmpA[c2];
                            if (_v != null)
                                _v[dest] = _tmpV[c2];
                            c2--;
                        }
                        dest--;
                    }
                }
                finally
                {
                    if (c2 >= 0)
                    {
                        Array.Copy(_tmpA, 0, _a, dest - c2, c2 + 1);
                        if (_v != null)
                            Array.Copy(_tmpV, 0, _v, dest - c2, c2 + 1);
                    }
                }
            }
        }
    }
}

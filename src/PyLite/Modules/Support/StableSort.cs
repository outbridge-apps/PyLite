using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // Our own stable bottom-up merge sort. List<T>.Sort (unstable, wraps comparator
    // exceptions) and LINQ are forbidden. Every comparison charges a step; comparator exceptions
    // propagate unwrapped. Sorts `values` in place, ordered by `keys` (keys==null => the values).
    internal static class StableSort
    {
        internal static void Sort(EvalContext ctx, ScriptValue[] values, ScriptValue[] keys)
        {
            int n = values.Length;
            if (n < 2)
                return;
            ctx.Budget.CheckDeadlineNow();
            ctx.Values.PreCharge(8L * n + (keys != null ? 8L * n : 0));
            var bufV = new ScriptValue[n];
            var bufK = keys != null ? new ScriptValue[n] : null;

            ScriptValue[] srcV = values, srcK = keys, dstV = bufV, dstK = bufK;
            for (int width = 1; width < n; width *= 2)
            {
                for (int lo = 0; lo < n; lo += 2 * width)
                {
                    int mid = System.Math.Min(lo + width, n);
                    int hi = System.Math.Min(lo + 2 * width, n);
                    Merge(ctx, srcV, srcK, dstV, dstK, lo, mid, hi);
                }
                ScriptValue[] tV = srcV;
                srcV = dstV;
                dstV = tV;
                ScriptValue[] tK = srcK;
                srcK = dstK;
                dstK = tK;
            }
            if (srcV != values)
                System.Array.Copy(srcV, values, n);
            ctx.Budget.CheckDeadlineNow();
        }

        private static void Merge(EvalContext ctx, ScriptValue[] srcV, ScriptValue[] srcK,
            ScriptValue[] dstV, ScriptValue[] dstK, int lo, int mid, int hi)
        {
            int i = lo, j = mid, k = lo;
            while (i < mid && j < hi)
            {
                ctx.Budget.Step();
                // stability: take the right run only when it is STRICTLY less than the left head
                bool takeRight = Less(ctx, Key(srcV, srcK, j), Key(srcV, srcK, i));
                if (takeRight)
                {
                    dstV[k] = srcV[j];
                    if (dstK != null)
                        dstK[k] = srcK[j];
                    j++;
                }
                else
                {
                    dstV[k] = srcV[i];
                    if (dstK != null)
                        dstK[k] = srcK[i];
                    i++;
                }
                k++;
            }
            while (i < mid)
            {
                dstV[k] = srcV[i];
                if (dstK != null)
                    dstK[k] = srcK[i];
                i++;
                k++;
            }
            while (j < hi)
            {
                dstV[k] = srcV[j];
                if (dstK != null)
                    dstK[k] = srcK[j];
                j++;
                k++;
            }
        }

        private static ScriptValue Key(ScriptValue[] values, ScriptValue[] keys, int i)
        {
            return keys != null ? keys[i] : values[i];
        }

        private static bool Less(EvalContext ctx, ScriptValue a, ScriptValue b)
        {
            // P4: direct compare for the two dominant key shapes; the Step mirrors Order()'s per-compare
            // charge so budget accounting is identical to the RichCompare route.
            IntValue ia = a as IntValue, ib = b as IntValue;
            if (ia != null && ib != null && ia.IsSmall && ib.IsSmall)
            {
                ctx.Budget.Step();
                return ia.Small < ib.Small;
            }
            if (a.Kind == ValueKind.Str && b.Kind == ValueKind.Str)
            {
                ctx.Budget.Step();
                return string.CompareOrdinal(((StrValue)a).Value, ((StrValue)b).Value) < 0;
            }
            return PyOps.RichCompare(PyCmpOp.Lt, a, b, ctx).IsTruthy(ctx);
        }
    }
}

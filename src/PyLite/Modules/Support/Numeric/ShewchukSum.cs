using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Modules.Support
{
    // Exact summation, a port of Modules/mathmodule.c::math_fsum (Shewchuk exact-partials, NOT
    // Neumaier). Used by math.fsum and statistics._sum's float path. Non-finite inputs accumulate into
    // infSum (modern: [inf,-inf] -> nan, no ValueError); finite summands overflowing mid-2Sum -> OverflowError.
    internal static class ShewchukSum
    {
        public static double Sum(EvalContext ctx, IEnumerable<double> source)
        {
            var p = new List<double>();   // partials; invariant: p.Count == n
            int n = 0;
            bool specialSeen = false;
            double infSum = 0.0;
            int since = 0;

            foreach (double item in source)
            {
                ctx.Budget.Step();
                if (++since >= 4096)
                {
                    since = 0;
                    ctx.Budget.CheckDeadlineNow();
                }
                double x = item;
                double xsave = x;
                int i = 0;
                for (int j = 0; j < n; j++)
                {
                    double y = p[j];
                    if (Math.Abs(x) < Math.Abs(y))
                    {
                        double t = x;
                        x = y;
                        y = t;
                    }
                    double hi = x + y;
                    double lo = y - (hi - x);
                    if (lo != 0.0)
                    {
                        p[i] = lo;
                        i++;
                    }
                    x = hi;
                }
                if (i < n)
                    p.RemoveRange(i, n - i);
                n = i;

                if (x != 0.0)
                {
                    if (!IsFinite(x))
                    {
                        if (IsFinite(xsave))
                            throw Raise.Overflow(ctx, "intermediate overflow in fsum");
                        infSum += xsave;
                        specialSeen = true;
                        p.Clear();
                        n = 0;
                    }
                    else
                    {
                        ctx.Values.PreCharge(8);
                        p.Add(x);
                        n++;
                    }
                }
            }

            if (specialSeen)
                return infSum;

            double result = 0.0;
            if (n > 0)
            {
                result = p[--n];
                double lo = 0.0;
                while (n > 0)
                {
                    double x = result;
                    double y = p[--n];
                    result = x + y;
                    lo = y - (result - x);
                    if (lo != 0.0)
                        break;
                }
                // half-even rounding across multiple partials (e.g. [1e-16, 1, 1e16]).
                if (n > 0 && ((lo < 0.0 && p[n - 1] < 0.0) || (lo > 0.0 && p[n - 1] > 0.0)))
                {
                    double y = lo * 2.0;
                    double x = result + y;
                    if (y == x - result)
                        result = x;
                }
            }
            return result;
        }

        private static bool IsFinite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
    }
}

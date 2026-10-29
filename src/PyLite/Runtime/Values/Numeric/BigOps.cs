using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Outbridge.PyLite.Runtime.Values
{
    // Long native BigInteger operations, bounded by the deadline. A native call runs to its end once
    // started (a 1M-bit gcd, division or modular squaring takes 1-2 s), so one past LongWork word products
    // (~10 ms) runs on a helper thread while the run thread polls the deadline: an abort ends the run on
    // time, and the abandoned helper finishes on its own, its cost bounded by the MaxIntBits-sized operands.
    // Callers keep their own MaxIntBits gates and charges; the long paths sit in separate methods so the
    // fast path allocates no closure.
    internal static class BigOps
    {
        internal const long LongWork = 1L << 22;
        private const int PollMs = 10;

        // 64-bit words of |x| in O(1) (BigInteger.Log reads the top words and the length).
        internal static long Words(BigInteger x)
        {
            if (x >= long.MinValue && x <= long.MaxValue)
                return 1;
            return (long)(BigInteger.Log(x.Sign < 0 ? -x : x, 2) / 64) + 1;
        }

        internal static BigInteger Multiply(EvalContext ctx, BigInteger a, BigInteger b)
        {
            return Words(a) * Words(b) <= LongWork ? a * b : MultiplyLong(ctx, a, b);
        }

        internal static BigInteger MultiplyLong(EvalContext ctx, BigInteger a, BigInteger b)
        {
            return RunLong(ctx, () => a * b);
        }

        internal static BigInteger Divide(EvalContext ctx, BigInteger a, BigInteger b)
        {
            return DivWork(a, b) <= LongWork ? a / b : DivideLong(ctx, a, b);
        }

        private static BigInteger DivideLong(EvalContext ctx, BigInteger a, BigInteger b)
        {
            return RunLong(ctx, () => a / b);
        }

        internal static BigInteger DivRem(EvalContext ctx, BigInteger a, BigInteger b, out BigInteger r)
        {
            if (DivWork(a, b) <= LongWork)
                return BigInteger.DivRem(a, b, out r);
            BigInteger[] qr = DivRemLong(ctx, a, b);
            r = qr[1];
            return qr[0];
        }

        private static BigInteger[] DivRemLong(EvalContext ctx, BigInteger a, BigInteger b)
        {
            return RunLong(ctx, () =>
            {
                BigInteger rem;
                BigInteger q = BigInteger.DivRem(a, b, out rem);
                return new[] { q, rem };
            });
        }

        // Python's floored quotient (b != 0).
        internal static BigInteger FloorDiv(EvalContext ctx, BigInteger a, BigInteger b)
        {
            BigInteger r;
            BigInteger q = DivRem(ctx, a, b, out r);
            return !r.IsZero && r.Sign != b.Sign ? q - 1 : q;
        }

        internal static BigInteger Gcd(EvalContext ctx, BigInteger a, BigInteger b)
        {
            long w = Math.Max(Words(a), Words(b));
            return w * w <= LongWork ? BigInteger.GreatestCommonDivisor(a, b) : GcdLong(ctx, a, b);
        }

        private static BigInteger GcdLong(EvalContext ctx, BigInteger a, BigInteger b)
        {
            return RunLong(ctx, () => BigInteger.GreatestCommonDivisor(a, b));
        }

        // a**n with about resultBits bits (the caller has checked them against MaxIntBits).
        internal static BigInteger Pow(EvalContext ctx, BigInteger a, int n, double resultBits)
        {
            double w = resultBits / 64 + 1;
            return w * w <= LongWork ? BigInteger.Pow(a, n) : PowLong(ctx, a, n);
        }

        private static BigInteger PowLong(EvalContext ctx, BigInteger a, int n)
        {
            return RunLong(ctx, () => BigInteger.Pow(a, n));
        }

        // One ModPow call of the given work (its exponent bits times the modulus's words squared).
        internal static BigInteger ModPow(EvalContext ctx, BigInteger b, BigInteger e, BigInteger m, long work)
        {
            return work <= LongWork ? BigInteger.ModPow(b, e, m) : ModPowLong(ctx, b, e, m);
        }

        private static BigInteger ModPowLong(EvalContext ctx, BigInteger b, BigInteger e, BigInteger m)
        {
            return RunLong(ctx, () => BigInteger.ModPow(b, e, m));
        }

        // Decimal digits of v (net472's conversion is quadratic).
        internal static string ToDecimalString(EvalContext ctx, BigInteger v)
        {
            long w = Words(v);
            return w * w <= LongWork ? v.ToString(System.Globalization.CultureInfo.InvariantCulture) : ToDecimalStringLong(ctx, v);
        }

        private static string ToDecimalStringLong(EvalContext ctx, BigInteger v)
        {
            return RunLong(ctx, () => v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static long DivWork(BigInteger a, BigInteger b)
        {
            long wa = Words(a), wb = Words(b);
            return wa < wb ? wa : wb * (wa - wb + 1);
        }

        private static T RunLong<T>(EvalContext ctx, Func<T> op)
        {
            ctx.Budget.CheckDeadlineNow();
            Task<T> task = Task.Factory.StartNew(op, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            bool done = false;
            try
            {
                while (!WaitQuietly(task, PollMs))
                    ctx.Budget.CheckDeadlineNow();
                done = true;
            }
            finally
            {
                // Aborted: the helper runs on; a late fault is observed so it never surfaces as unobserved.
                if (!done)
                    task.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            }
            return task.GetAwaiter().GetResult();
        }

        // Task.Wait blocks on a slim event (AsyncWaitHandle would make a kernel handle per call that only
        // the finalizer frees); a fault ends the wait and GetResult rethrows the original exception.
        private static bool WaitQuietly(Task task, int ms)
        {
            try
            {
                return task.Wait(ms);
            }
            catch (AggregateException)
            {
                return true;
            }
        }
    }
}

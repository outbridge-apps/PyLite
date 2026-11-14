using System;

namespace Outbridge.PyLite.Runtime.Values
{
    // Ordinal substring search. The BCL's IndexOf(string) is a plain O(n*m) scan inside one native call,
    // so a long needle over a long haystack used to run for minutes past the deadline and leak the runner
    // thread. Two paths now: a search whose worst case fits one Window stays a single BCL call (the fast,
    // vectorised, overwhelmingly common case), and a larger one runs Knuth-Morris-Pratt, which is linear
    // in the window and answers instead of aborting. Bytes take the same pair with a managed first-byte
    // scan, since the BCL has no ordinal byte search.
    internal static class PySearch
    {
        private const long Window = 1L << 24;   // BCL compares per uninterrupted call (~10 ms)
        private const int StepMask = (1 << 16) - 1;   // KMP positions between two budget checks

        // First index in [start, start + count) where needle occurs entirely inside the window, or -1.
        internal static int IndexOf(EvalContext ctx, string s, string needle, int start, int count)
        {
            int m = needle.Length;
            if (m == 0)
                return start;
            if (count < m)
                return -1;
            int r;
            if (m == 1)
                r = s.IndexOf(needle[0], start, count);
            else if ((long)count * m <= Window)
                r = s.IndexOf(needle, start, count, StringComparison.Ordinal);
            else
                return Kmp(ctx, s, needle, start, start + count, false);
            Scanned(ctx, r < 0 ? count : r - start + m);
            return r;
        }

        // A single BCL search charged after the fact by what it scanned, so `x in s` in a loop over a long s
        // reaches the deadline; a hit near the start costs what it scanned, not the whole string.
        private static void Scanned(EvalContext ctx, long units)
        {
            if (units > BudgetCost.LinearChunkSize)
                ctx.Budget.ChargeLinear(units);
        }

        // Last index in [from, to) where needle occurs entirely inside the window, or -1.
        internal static int LastIndexOf(EvalContext ctx, string s, string needle, int from, int to)
        {
            int m = needle.Length;
            if (m == 0)
                return to;
            if (to - from < m)
                return -1;
            int r;
            if (m == 1)
                r = s.LastIndexOf(needle[0], to - 1, to - from);
            else if ((long)(to - from) * m <= Window)
                r = s.LastIndexOf(needle, to - 1, to - from, StringComparison.Ordinal);
            else
                return KmpLast(ctx, s, needle, from, to);
            Scanned(ctx, r < 0 ? to - from : to - r);
            return r;
        }

        internal static int IndexOf(EvalContext ctx, byte[] hay, byte[] needle, int from, int to)
        {
            int m = needle.Length;
            if (m == 0)
                return from <= to ? from : -1;
            if (to - from < m)
                return -1;
            if ((long)(to - from) * m <= Window)
            {
                int r = Scan(ctx, hay, needle, from, to, false);
                Scanned(ctx, r < 0 ? to - from : r - from + m);
                return r;
            }
            return Kmp(ctx, hay, needle, from, to, false);
        }

        internal static int LastIndexOf(EvalContext ctx, byte[] hay, byte[] needle, int from, int to)
        {
            int m = needle.Length;
            if (m == 0)
                return from <= to ? to : -1;
            if (to - from < m)
                return -1;
            if ((long)(to - from) * m <= Window)
            {
                int r = Scan(ctx, hay, needle, from, to, true);
                Scanned(ctx, r < 0 ? to - from : to - r);
                return r;
            }
            return KmpLast(ctx, hay, needle, from, to);
        }

        // ---- Knuth-Morris-Pratt ----

        // The last match, right to left: its cost is what it scans, so rsplit stays linear; walking from the left
        // to report the final match rescanned the whole rest of the string for every piece.
        private static int KmpLast(EvalContext ctx, string s, string needle, int from, int to)
        {
            int m = needle.Length;
            int[] fail = ReversedFailure(ctx, needle);
            int k = 0;
            for (int i = to - 1; i >= from; i--)
            {
                char c = s[i];
                while (k > 0 && needle[m - 1 - k] != c)
                    k = fail[k - 1];
                if (needle[m - 1 - k] == c)
                    k++;
                if (k == m)
                    return i;
                if ((i & StepMask) == 0)
                    Pause(ctx);
            }
            return -1;
        }

        private static int KmpLast(EvalContext ctx, byte[] hay, byte[] needle, int from, int to)
        {
            int m = needle.Length;
            int[] fail = ReversedFailure(ctx, needle);
            int k = 0;
            for (int i = to - 1; i >= from; i--)
            {
                byte c = hay[i];
                while (k > 0 && needle[m - 1 - k] != c)
                    k = fail[k - 1];
                if (needle[m - 1 - k] == c)
                    k++;
                if (k == m)
                    return i;
                if ((i & StepMask) == 0)
                    Pause(ctx);
            }
            return -1;
        }

        // The failure table of the reversed needle, kept for the next piece of the same split.
        private static int[] ReversedFailure(EvalContext ctx, string p)
        {
            if (ReferenceEquals(ctx.ReversedNeedle, p))
                return ctx.ReversedFail;
            char[] r = p.ToCharArray();
            Array.Reverse(r);
            int[] fail = Failure(ctx, new string(r));
            ctx.ReversedNeedle = p;
            ctx.ReversedFail = fail;
            return fail;
        }

        private static int[] ReversedFailure(EvalContext ctx, byte[] p)
        {
            if (ReferenceEquals(ctx.ReversedNeedle, p))
                return ctx.ReversedFail;
            var r = (byte[])p.Clone();
            Array.Reverse(r);
            int[] fail = Failure(ctx, r);
            ctx.ReversedNeedle = p;
            ctx.ReversedFail = fail;
            return fail;
        }

        private static int Kmp(EvalContext ctx, string s, string needle, int from, int to, bool last)
        {
            int m = needle.Length;
            int[] fail = Failure(ctx, needle);
            int k = 0;
            int found = -1;
            for (int i = from; i < to; i++)
            {
                char c = s[i];
                while (k > 0 && needle[k] != c)
                    k = fail[k - 1];
                if (needle[k] == c)
                    k++;
                if (k == m)
                {
                    found = i - m + 1;
                    if (!last)
                        return found;
                    k = fail[k - 1];
                }
                if ((i & StepMask) == 0)
                    Pause(ctx);
            }
            return found;
        }

        private static int Kmp(EvalContext ctx, byte[] hay, byte[] needle, int from, int to, bool last)
        {
            int m = needle.Length;
            int[] fail = Failure(ctx, needle);
            int k = 0;
            int found = -1;
            for (int i = from; i < to; i++)
            {
                byte c = hay[i];
                while (k > 0 && needle[k] != c)
                    k = fail[k - 1];
                if (needle[k] == c)
                    k++;
                if (k == m)
                {
                    found = i - m + 1;
                    if (!last)
                        return found;
                    k = fail[k - 1];
                }
                if ((i & StepMask) == 0)
                    Pause(ctx);
            }
            return found;
        }

        // fail[i] = the length of the longest proper prefix of needle[0..i] that is also its suffix.
        private static int[] Failure(EvalContext ctx, string p)
        {
            ctx.Values.PreCharge(4L * p.Length + 32);
            var fail = new int[p.Length];
            int k = 0;
            for (int i = 1; i < p.Length; i++)
            {
                while (k > 0 && p[i] != p[k])
                    k = fail[k - 1];
                if (p[i] == p[k])
                    k++;
                fail[i] = k;
                if ((i & StepMask) == 0)
                    Pause(ctx);
            }
            return fail;
        }

        private static int[] Failure(EvalContext ctx, byte[] p)
        {
            ctx.Values.PreCharge(4L * p.Length + 32);
            var fail = new int[p.Length];
            int k = 0;
            for (int i = 1; i < p.Length; i++)
            {
                while (k > 0 && p[i] != p[k])
                    k = fail[k - 1];
                if (p[i] == p[k])
                    k++;
                fail[i] = k;
                if ((i & StepMask) == 0)
                    Pause(ctx);
            }
            return fail;
        }

        // ---- byte scan (the BCL has no ordinal byte search) ----

        private static int Scan(EvalContext ctx, byte[] hay, byte[] needle, int from, int to, bool last)
        {
            int m = needle.Length;
            byte b0 = needle[0];
            if (!last)
            {
                for (int i = from; i <= to - m; i++)
                {
                    if (hay[i] == b0 && Same(hay, i, needle))
                        return i;
                }
                return -1;
            }
            for (int i = to - m; i >= from; i--)
            {
                if (hay[i] == b0 && Same(hay, i, needle))
                    return i;
            }
            return -1;
        }

        private static bool Same(byte[] hay, int at, byte[] needle)
        {
            for (int k = 1; k < needle.Length; k++)
            {
                if (hay[at + k] != needle[k])
                    return false;
            }
            return true;
        }

        private static void Pause(EvalContext ctx)
        {
            ctx.Budget.Step();
            ctx.Budget.CheckDeadlineNow();
        }
    }
}

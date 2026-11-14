
namespace Outbridge.PyLite.Runtime.Values
{
    // UAX #15 over the Unicode 16.0.0 tables (PyUnicodeProps), the version CPython 3.14 normalizes with; the
    // operating system's Normalize() answered from its own, older data. NFD/NFKD decompose fully (Hangul by
    // formula) and put each run of combining marks in canonical order; NFC/NFKC then compose. A lone
    // surrogate or a noncharacter passes through as itself. Linear in the text: a long run of marks is
    // sorted, not inserted.
    internal static class PyUnicodeNormalize
    {
        private const int SBase = 0xAC00, LBase = 0x1100, VBase = 0x1161, TBase = 0x11A7;
        private const int LCount = 19, VCount = 21, TCount = 28, NCount = VCount * TCount, SCount = LCount * NCount;

        // A piece of the text decomposed, as code points: an array and a count, grown by doubling
        private sealed class Buffer
        {
            internal int[] Items;
            internal int Count;
            private readonly EvalContext _ctx;

            internal Buffer(int capacity, EvalContext ctx)
            {
                Items = new int[capacity];
                _ctx = ctx;
            }

            internal void Add(int cp)
            {
                if (Count == Items.Length)
                {
                    if (_ctx != null)   // a piece with no character below U+0300 can be the whole text
                        _ctx.Values.PreCharge(4L * Items.Length);
                    System.Array.Resize(ref Items, Items.Length * 2);
                }
                Items[Count++] = cp;
            }

            // Emptied for the next piece, with room for what it takes in the text: a long piece does not double
            // its way up to that.
            internal void Restart(int capacity)
            {
                Count = 0;
                if (capacity > Items.Length)
                {
                    if (_ctx != null)
                        _ctx.Values.PreCharge(4L * (capacity - Items.Length));
                    Items = new int[capacity];
                }
            }
        }

        // The normal form as it is built: UTF-16, grown by doubling, never past the limit
        private sealed class Output
        {
            private const int FreeChars = 256;   // as BudgetStringBuilder: a short text becomes a str that charges itself
            internal char[] Chars = System.Array.Empty<char>();
            internal int Count;
            private readonly int _first;
            private readonly long _limit;
            private readonly EvalContext _ctx;

            internal Output(int capacity, long limit, EvalContext ctx)
            {
                _first = capacity;
                _limit = limit;
                _ctx = ctx;
            }

            // Room for units more, or false when the text would pass the limit. The first array is the capacity
            // asked for, or all of a text that is one piece: such a text takes one array of its own length, as it
            // did before the pieces, not the capacity and then twice it.
            internal bool Reserve(long units)
            {
                long need = Count + units;
                if (need > _limit)
                    return false;
                if (need > Chars.Length)
                {
                    long want = System.Math.Max(Chars.Length == 0 ? _first : 2L * Chars.Length, need);
                    int size = (int)System.Math.Min(want, 0x7FFFFFC7);
                    if (_ctx != null && size > FreeChars)
                        _ctx.Values.PreCharge(2L * (size - System.Math.Max(Chars.Length, FreeChars)));
                    System.Array.Resize(ref Chars, size);
                }
                return true;
            }
        }

        // A character below U+0300 is not a mark and no second half of a composition, and neither is the first
        // character of its decomposition: the text before it and the text from it on normalize apart.
        internal const int CutBelow = 0x300;

        // The longest canonical decomposition, in code points (U+1F82 and its like; a Hangul syllable is 3): the
        // most that one composed character takes in.
        internal const int MaxCanonicalLength = 4;

        // limit: the most UTF-16 units the result may take; null when the normal form is longer.
        // ctx, when given, is charged for the buffers and has its deadline read every 4,096 characters.
        internal static string Normalize(string s, bool compat, bool compose, long limit = long.MaxValue, EvalContext ctx = null)
        {
            return IsStable(s, compat, compose) ? s : Rebuild(s, compat, compose, limit, ctx, CutBelow);
        }

        // The text piece by piece, each from one character below cutBelow to the next: a piece that is one fixed
        // character is copied, any other is decomposed, ordered and composed in the code point buffer. The limit is
        // read on what the result takes, so NFC of a text whose NFD is too long is not refused, and the buffer is
        // the longest piece's, not the text's (review 12, rows 13 and 17). cutBelow 0 is the whole text as one piece.
        internal static string Rebuild(string s, bool compat, bool compose, long limit, EvalContext ctx, int cutBelow)
        {
            int free = FreeBelow(compat, compose);
            var cps = new Buffer(32, ctx);
            var text = new Output(s.Length + 16, limit, ctx);
            bool fits = true;
            int tick = 0;
            for (int i = 0; i < s.Length && fits; )
            {
                int end = i + 1;
                while (end < s.Length && s[end] >= cutBelow)
                    end++;
                if (end == i + 1 && IsFixed(s[i], free, compat, compose))
                {
                    fits = text.Reserve(1);
                    if (fits)
                        text.Chars[text.Count++] = s[i];
                    if (ctx != null && (++tick & 4095) == 0)
                        ctx.Budget.CheckDeadlineNow();
                    i = end;
                    continue;
                }
                cps.Restart(end - i + 8);   // a few characters that decompose do not double a long piece
                int counted = 0;            // of cps, read for what composing keeps
                long kept = 0;
                for (int k = i; k < end && fits; k++)
                {
                    int cp = s[k];
                    if (cp < 0xA0)
                    {
                        cps.Add(cp);   // nothing below U+00A0 decomposes
                    }
                    else
                    {
                        if (char.IsHighSurrogate(s[k]) && k + 1 < end && char.IsLowSurrogate(s[k + 1]))
                            cp = char.ConvertToUtf32(s[k], s[++k]);
                        Decompose(cp, compat, cps);
                        long room = limit - text.Count;
                        if (cps.Count > room)
                        {
                            // A decomposed piece only grows. Composed, it keeps every code point that is no second and
                            // a quarter of the whole at least: a long piece past the limit is refused before it is
                            // composed, as an earlier build refused it (review 13, row 28).
                            while (compose && counted < cps.Count)
                            {
                                int c = cps.Items[counted++];
                                if (!IsSecond(c, ClassOf(c)))
                                    kept++;
                            }
                            fits = compose && kept <= room && cps.Count / MaxCanonicalLength <= room;
                        }
                    }
                    if (ctx != null && (++tick & 4095) == 0)
                        ctx.Budget.CheckDeadlineNow();
                }
                if (fits)
                {
                    Reorder(cps, ctx);
                    if (compose)
                        Compose(cps, ctx);
                    fits = Append(text, cps);
                }
                i = end;
            }
            string r = fits ? new string(text.Chars, 0, text.Count) : null;
            return r == s ? s : r;
        }

        private static bool Append(Output text, Buffer cps)
        {
            long units = cps.Count;
            for (int k = 0; k < cps.Count; k++)
            {
                if (cps.Items[k] > 0xFFFF)
                    units++;
            }
            bool fits = text.Reserve(units);
            if (fits)
            {
                char[] to = text.Chars;
                int w = text.Count;
                for (int k = 0; k < cps.Count; k++)
                {
                    int cp = cps.Items[k];
                    if (cp > 0xFFFF)
                    {
                        to[w++] = (char)(0xD800 + ((cp - 0x10000) >> 10));
                        to[w++] = (char)(0xDC00 + ((cp - 0x10000) & 0x3FF));
                    }
                    else
                    {
                        to[w++] = (char)cp;
                    }
                }
                text.Count = w;
            }
            return fits;
        }

        // Below it no character of the form changes, but for the compatibility ones of NFKC (IsFixed).
        private static int FreeBelow(bool compat, bool compose)
        {
            return compose ? 0x300 : compat ? 0xA0 : 0xC0;
        }

        private static bool IsFixed(int cp, int free, bool compat, bool compose)
        {
            return cp < free && !(compat && compose && cp >= 0xA0 && PyUnicodeProps.HasCompatDecomposition(cp));
        }

        // A string of characters that no form changes and that no neighbour joins is its own normal form:
        // below U+0300 nothing is a mark or the second half of a composition (and below U+00C0 nothing has a
        // canonical decomposition, below U+00A0 none at all); above, a starter with no decomposition that never
        // ends a composition. The common text skips the code point buffer entirely.
        private static bool IsStable(string s, bool compat, bool compose)
        {
            int free = FreeBelow(compat, compose);
            for (int i = 0; i < s.Length; i++)
            {
                int cp = s[i];
                if (IsFixed(cp, free, compat, compose))
                    continue;
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                    cp = char.ConvertToUtf32(s[i], s[++i]);
                else if (char.IsSurrogate((char)cp))
                    continue;   // a lone surrogate passes through
                if ((cp >= SBase && cp < SBase + SCount) || (cp >= LBase && cp < LBase + LCount)
                    || PyUnicodeProps.Combining(cp) != 0 || PyUnicodeProps.HasDecomposition(cp, compat)
                    || (compose && PyUnicodeProps.IsCompositionSecond(cp)))
                    return false;
            }
            return true;
        }

        private static void Decompose(int cp, bool compat, Buffer into)
        {
            if (cp >= SBase && cp < SBase + SCount)
            {
                int si = cp - SBase;
                into.Add(LBase + si / NCount);
                into.Add(VBase + si % NCount / TCount);
                if (si % TCount != 0)
                    into.Add(TBase + si % TCount);
                return;
            }
            int[] d;
            if ((cp >= 0xD800 && cp <= 0xDFFF) || (d = PyUnicodeProps.FullDecompositionOf(cp, compat)) == null)
            {
                into.Add(cp);
                return;
            }
            foreach (int c in d)
                into.Add(c);
        }

        private static int ClassOf(int cp)
        {
            return cp < 0x300 ? 0 : PyUnicodeProps.Combining(cp);
        }

        // Canonical ordering: each run of non-starters by combining class, stably. A short run (nearly all of them) in
        // place; a long one counted, since classes are 0-255: linear, charged, the deadline read as it goes.
        private static void Reorder(Buffer cps, EvalContext ctx)
        {
            const int ShortRun = 16;
            int[] items = cps.Items;
            int[] scratch = null;
            int n = cps.Count, i = 0, tick = 0;
            while (i < n)
            {
                if (ctx != null && (++tick & 4095) == 0)
                    ctx.Budget.CheckDeadlineNow();
                if (ClassOf(items[i]) == 0)
                {
                    i++;
                    continue;
                }
                int start = i;
                while (i < n && ClassOf(items[i]) != 0)
                    i++;
                if (i - start <= ShortRun)
                    InsertionSort(items, start, i);
                else
                    CountingSort(items, start, i - start, ref scratch, ctx);
            }
        }

        private static void InsertionSort(int[] items, int start, int end)
        {
            for (int k = start + 1; k < end; k++)
            {
                int cp = items[k], cls = ClassOf(cp), j = k - 1;
                while (j >= start && ClassOf(items[j]) > cls)
                {
                    items[j + 1] = items[j];
                    j--;
                }
                items[j + 1] = cp;
            }
        }

        private static void CountingSort(int[] items, int start, int len, ref int[] scratch, EvalContext ctx)
        {
            if (scratch == null || scratch.Length < len)
            {
                if (ctx != null)
                    ctx.Values.PreCharge(4L * len);
                scratch = new int[len];
            }
            var next = new int[257];
            for (int k = 0; k < len; k++)
                next[ClassOf(items[start + k]) + 1]++;
            for (int c = 1; c < next.Length; c++)
                next[c] += next[c - 1];
            for (int k = 0; k < len; k++)
            {
                if (ctx != null && (k & 4095) == 4095)
                    ctx.Budget.CheckDeadlineNow();
                int cp = items[start + k];
                scratch[next[ClassOf(cp)]++] = cp;
            }
            System.Array.Copy(scratch, 0, items, start, len);
        }

        // Canonical composition: a mark joins the last starter unless something between them blocks it
        private static void Compose(Buffer cps, EvalContext ctx)
        {
            int[] items = cps.Items;
            int starter = -1, lastClass = 0, w = 0;
            for (int r = 0; r < cps.Count; r++)
            {
                if (ctx != null && (r & 4095) == 4095)
                    ctx.Budget.CheckDeadlineNow();
                int cp = items[r];
                int cc = ClassOf(cp);
                if (IsSecond(cp, cc) && starter >= 0 && (w - 1 == starter || (lastClass != 0 && lastClass < cc)))
                {
                    int composite = ComposeOne(items[starter], cp);
                    if (composite >= 0)
                    {
                        items[starter] = composite;
                        continue;
                    }
                }
                if (cc == 0)
                    starter = w;
                lastClass = cc;
                items[w++] = cp;
            }
            cps.Count = w;
        }

        // Whether cp may join the starter before it: only such a code point is ever taken out by Compose.
        private static bool IsSecond(int cp, int cc)
        {
            return cc != 0 || (cp >= 0x300 && PyUnicodeProps.IsCompositionSecond(cp));   // a starter seldom pairs
        }

        private static int ComposeOne(int first, int second)
        {
            if (first >= LBase && first < LBase + LCount && second >= VBase && second < VBase + VCount)
                return SBase + ((first - LBase) * VCount + (second - VBase)) * TCount;
            if (first >= SBase && first < SBase + SCount && (first - SBase) % TCount == 0 && second > TBase && second < TBase + TCount)
                return first + (second - TBase);
            return PyUnicodeProps.ComposePair(first, second);
        }
    }
}

namespace Outbridge.PyLite.Runtime.Values
{
    // Case mapping against the Unicode tables in PyUnicodeCase.Tables.g.cs rather than against
    // char.ToUpperInvariant, which disagrees with Unicode on 427 of the 2373 cased BMP characters
    // (Georgian, Cherokee, the Cyrillic supplement, Latin Ext-D/E), carries no length-changing mappings
    // at all, and is not pinned to the version CPython uses.
    //
    // Pure ASCII keeps the BCL call: for ASCII the two agree exactly, and it is the overwhelmingly common
    // shape, so the common path costs what it did before. A string that needs no change at all is
    // returned as-is, which the BCL never does.
    internal static partial class PyUnicodeCase
    {
        private const int FCased = 1, FUpper = 2, FLower = 4, FTitleCat = 8, FIgnorable = 16, FFull = 32;

        private enum Mode { Upper, Lower, Title, Fold }

        // ---- character properties (CPython takes these from DerivedCoreProperties, not the category) ----

        private static int Flags(char c)
        {
            byte p = FlagPage[c >> 8];
            return p == 0 ? 0 : FlagData[((p - 1) << 8) | (c & 0xFF)];
        }

        internal static bool IsCased(char c) { return (Flags(c) & FCased) != 0; }
        internal static bool IsUpper(char c) { return (Flags(c) & FUpper) != 0; }
        internal static bool IsLower(char c) { return (Flags(c) & FLower) != 0; }
        internal static bool IsTitleCategory(char c) { return (Flags(c) & FTitleCat) != 0; }
        private static bool IsCaseIgnorable(char c) { return (Flags(c) & FIgnorable) != 0; }

        // ---- identifier properties (PEP 3131: XID_Start then XID_Continue, plus '_' as a start) ----

        internal static bool IsXidStart(int cp)
        {
            return cp <= 0xFFFF
                ? (XidStartBits[cp >> 4] & (1 << (cp & 15))) != 0
                : InRanges(XidStartAstral, cp);
        }

        internal static bool IsXidContinue(int cp)
        {
            return cp <= 0xFFFF
                ? (XidContinueBits[cp >> 4] & (1 << (cp & 15))) != 0
                : InRanges(XidContinueAstral, cp);
        }

        // Flattened, sorted lo/hi pairs.
        private static bool InRanges(int[] ranges, int cp)
        {
            int lo = 0, hi = ranges.Length / 2 - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (cp < ranges[mid * 2])
                    hi = mid - 1;
                else if (cp > ranges[mid * 2 + 1])
                    lo = mid + 1;
                else
                    return true;
            }
            return false;
        }

        // ---- single-character mapping ----

        private static char Simple(Mode mode, char c)
        {
            byte[] page;
            string data;
            switch (mode)
            {
                case Mode.Upper: page = UpperPage; data = UpperData; break;
                case Mode.Lower: page = LowerPage; data = LowerData; break;
                case Mode.Title: page = TitlePage; data = TitleData; break;
                default: page = FoldPage; data = FoldData; break;
            }
            byte p = page[c >> 8];
            return p == 0 ? c : data[((p - 1) << 8) | (c & 0xFF)];
        }

        private static bool HasFull(char c)
        {
            return (Flags(c) & FFull) != 0;
        }

        // Only called once HasFull says there is an entry; the value falls back to the simple mapping for
        // a direction this character does not expand in.
        private static string Full(Mode mode, char c)
        {
            int i = FullKeys.IndexOf(c);
            switch (mode)
            {
                case Mode.Upper: return FullUpperV[i];
                case Mode.Lower: return FullLowerV[i];
                case Mode.Title: return FullTitleV[i];
                default: return FullFoldV[i];
            }
        }

        // GREEK CAPITAL LETTER SIGMA is the one context-dependent rule CPython applies by default: it
        // lowercases to the final form when it ends a word. Port of CPython's handle_capital_sigma.
        private static char LowerSigma(string s, int at)
        {
            int j;
            char c = '\0';
            for (j = at - 1; j >= 0; j--)
            {
                c = s[j];
                if (!IsCaseIgnorable(c))
                    break;
            }
            if (j < 0 || !IsCased(c))
                return 'σ';
            for (j = at + 1; j < s.Length; j++)
            {
                c = s[j];
                if (!IsCaseIgnorable(c))
                    break;
            }
            return j == s.Length || !IsCased(c) ? 'ς' : 'σ';
        }

        // ---- string operations ----

        internal static string Upper(string s) { return Map(s, Mode.Upper); }
        internal static string Lower(string s) { return Map(s, Mode.Lower); }
        internal static string Fold(string s) { return Map(s, Mode.Fold); }

        // Copy-on-first-difference over one pass: a string that needs no change is returned as-is with no
        // allocation, an ASCII one never consults a table, and only the tail from the first non-ASCII
        // character walks them.
        private static string Map(string s, Mode mode)
        {
            int n = s.Length;
            int i = 0;
            for (; i < n; i++)
            {
                char c = s[i];
                if (c >= 0x80)
                    return Walk(s, i, null, mode);
                if (AsciiMap(mode, c) != c)
                    break;
            }
            if (i == n)
                return s;
            var buf = new char[n];
            for (int k = 0; k < i; k++)
                buf[k] = s[k];
            for (int k = i; k < n; k++)
            {
                char c = s[k];
                if (c >= 0x80)
                    return Walk(s, k, buf, mode);
                buf[k] = AsciiMap(mode, c);
            }
            return new string(buf);
        }

        private static string Walk(string s, int from, char[] buf, Mode mode)
        {
            int n = s.Length;
            if (buf == null)
            {
                buf = new char[n];
                for (int k = 0; k < from; k++)
                    buf[k] = s[k];
            }
            for (int k = from; k < n; k++)
            {
                char c = s[k];
                if (c < 0x80)
                {
                    buf[k] = AsciiMap(mode, c);
                    continue;
                }
                if (char.IsHighSurrogate(c) && k + 1 < n && char.IsLowSurrogate(s[k + 1]))
                    return Build(s, k, buf, mode);
                if (HasFull(c))
                {
                    string full = Full(mode, c);
                    if (full.Length != 1)
                        return Build(s, k, buf, mode);
                    buf[k] = full[0];
                    continue;
                }
                buf[k] = mode == Mode.Lower && c == 'Σ' ? LowerSigma(s, k) : Simple(mode, c);
            }
            return new string(buf);
        }

        // The slow tail: a length-changing mapping or an astral pair means the result is no longer
        // one unit per input unit.
        private static string Build(string s, int from, char[] head, Mode mode)
        {
            var sb = new System.Text.StringBuilder(s.Length + 8);
            sb.Append(head, 0, from);
            for (int k = from; k < s.Length; k++)
            {
                char c = s[k];
                if (c < 0x80)
                {
                    sb.Append(AsciiMap(mode, c));
                    continue;
                }
                if (char.IsHighSurrogate(c) && k + 1 < s.Length && char.IsLowSurrogate(s[k + 1]))
                {
                    sb.Append(char.ConvertFromUtf32(Astral(mode, char.ConvertToUtf32(c, s[k + 1]))));
                    k++;
                    continue;
                }
                if (HasFull(c))
                {
                    sb.Append(Full(mode, c));
                    continue;
                }
                sb.Append(mode == Mode.Lower && c == 'Σ' ? LowerSigma(s, k) : Simple(mode, c));
            }
            return sb.ToString();
        }

        private static int Astral(Mode mode, int cp)
        {
            int i = System.Array.BinarySearch(AstralKeys, cp);
            if (i < 0)
                return cp;
            switch (mode)
            {
                case Mode.Upper: return AstralUpper[i];
                case Mode.Lower: return AstralLower[i];
                case Mode.Title: return AstralTitle[i];
                default: return AstralFold[i];
            }
        }

        // Over ASCII the titlecase mapping IS the uppercase one; fold is lowercase.
        private static char AsciiMap(Mode mode, char c)
        {
            if (mode == Mode.Upper || mode == Mode.Title)
                return c >= 'a' && c <= 'z' ? (char)(c - 32) : c;
            return c >= 'A' && c <= 'Z' ? (char)(c + 32) : c;
        }

        // Titlecase the first character, lowercase the rest (CPython's do_capitalize -- the FIRST
        // character takes the TITLE mapping, so 'ssa' comes out of 'ssa' and not 'SSa').
        internal static string Capitalize(string s)
        {
            if (s.Length == 0)
                return s;
            var sb = new System.Text.StringBuilder(s.Length + 4);
            AppendMapped(sb, s, 0, Mode.Title, out int used);
            for (int k = used; k < s.Length; k++)
            {
                AppendMapped(sb, s, k, Mode.Lower, out int u);
                k += u - 1;
            }
            return sb.ToString();
        }

        // A character starts a word when the previous cased character was not cased: it takes the TITLE
        // mapping, every other cased character takes LOWER.
        internal static string Title(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length + 4);
            bool prevCased = false;
            for (int k = 0; k < s.Length; k++)
            {
                char c = s[k];
                bool cased = IsCased(c);
                AppendMapped(sb, s, k, cased ? (prevCased ? Mode.Lower : Mode.Title) : (Mode?)null, out int used);
                prevCased = cased;
                k += used - 1;
            }
            return sb.ToString();
        }

        // Upper <-> lower only. A titlecase-category character is neither Uppercase nor Lowercase in the
        // derived properties, so CPython leaves it alone and so do we.
        internal static string SwapCase(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length + 4);
            for (int k = 0; k < s.Length; k++)
            {
                char c = s[k];
                Mode? mode = IsUpper(c) ? Mode.Lower : IsLower(c) ? Mode.Upper : (Mode?)null;
                AppendMapped(sb, s, k, mode, out int used);
                k += used - 1;
            }
            return sb.ToString();
        }

        // Appends the image of the character at `at` under `mode` (null = copy unchanged), reporting how
        // many UTF-16 units of the input it consumed.
        private static void AppendMapped(System.Text.StringBuilder sb, string s, int at, Mode? mode, out int used)
        {
            char c = s[at];
            used = 1;
            if (char.IsHighSurrogate(c) && at + 1 < s.Length && char.IsLowSurrogate(s[at + 1]))
            {
                used = 2;
                int cp = char.ConvertToUtf32(c, s[at + 1]);
                sb.Append(char.ConvertFromUtf32(mode.HasValue ? Astral(mode.Value, cp) : cp));
                return;
            }
            if (!mode.HasValue)
            {
                sb.Append(c);
                return;
            }
            if (c < 0x80)
            {
                sb.Append(AsciiMap(mode.Value, c));
                return;
            }
            if (HasFull(c))
            {
                sb.Append(Full(mode.Value, c));
                return;
            }
            sb.Append(mode.Value == Mode.Lower && c == 'Σ' ? LowerSigma(s, at) : Simple(mode.Value, c));
        }
    }
}

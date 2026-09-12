using System;

namespace Outbridge.PyLite.Syntax
{
    // The Unicode character-name database behind \N{NAME}. The tables live in
    // PyUnicodeNames.Tables.g.cs, generated from the UCD by tools/Outbridge.PyLite.UnicodeGen. It sits in the
    // syntax layer, not next to PyUnicodeCase, because the lexer is its only consumer and this layer
    // does not depend on the runtime.
    //
    // Names that a rule can produce are not stored: Hangul syllables are composed from their jamo,
    // and the families whose name is a prefix plus the code point in hex (CJK, Tangut, Egyptian
    // hieroglyphs, Nushu, Khitan) are matched against their ranges. That is ~110000 characters no
    // table has to carry, and it is how CPython answers them too.
    internal static partial class PyUnicodeNames
    {
        private const string HangulPrefix = "HANGUL SYLLABLE ";
        private const int HangulBase = 0xAC00;

        // Jamo short names (Jamo.txt). A syllable name is the three concatenated, and both the
        // choseong ieung and the absent jongseong contribute nothing.
        private static readonly string[] JamoL =
        {
            "G", "GG", "N", "D", "DD", "R", "M", "B", "BB", "S", "SS", "", "J", "JJ", "C", "K", "T", "P", "H",
        };

        private static readonly string[] JamoV =
        {
            "A", "AE", "YA", "YAE", "EO", "E", "YEO", "YE", "O", "WA", "WAE", "OE",
            "YO", "U", "WEO", "WE", "WI", "YU", "EU", "YI", "I",
        };

        private static readonly string[] JamoT =
        {
            "", "G", "GG", "GS", "N", "NJ", "NH", "D", "L", "LG", "LM", "LB", "LS", "LT",
            "LP", "LH", "M", "B", "BS", "S", "SS", "NG", "J", "C", "K", "T", "P", "H",
        };

        // The value is a string, not a code point: a named sequence is more than one character.
        // CPython matches the name case-insensitively over ASCII and nothing else, so the key folds
        // to bytes once and every comparison below is ordinal.
        internal static bool TryLookup(string name, out string value)
        {
            value = null;
            if (name.Length == 0 || name.Length > MaxNameLength)
                return false;
            var key = new byte[name.Length];
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c >= 'a' && c <= 'z')
                    c = (char)(c - 32);
                if (c < 0x20 || c > 0x7E)
                    return false;
                key[i] = (byte)c;
            }

            int composed = Hangul(key);
            if (composed < 0)
                composed = HexNamed(key);
            if (composed >= 0)
            {
                value = char.ConvertFromUtf32(composed);
                return true;
            }

            int v = Search(key);
            if (v < 0)
                return false;
            if (v >= AliasTag)
                value = char.ConvertFromUtf32(v - AliasTag);
            else if (v >= SeqTag)
                value = SeqData[v - SeqTag];
            else
                value = char.ConvertFromUtf32(v);
            return true;
        }

        private const int SeqTag = 0x110000;
        private const int AliasTag = 0x200000;

        // The reverse direction, code point -> its own name (never an alias, never a sequence), for the
        // namereplace error handler. The rules answer first; the table is indexed lazily on first use as
        // two parallel arrays sorted by code point - the record's byte offset, not its text, so a name is
        // decoded only when asked for by re-walking its block.
        internal static bool TryName(int cp, out string name)
        {
            name = null;
            if (cp < 0 || cp > 0x10FFFF)
                return false;
            if (cp >= HangulBase && cp < HangulBase + 19 * 21 * 28)
            {
                int s = cp - HangulBase;
                name = HangulPrefix + JamoL[s / (21 * 28)] + JamoV[(s / 28) % 21] + JamoT[s % 28];
                return true;
            }
            for (int i = 0; i < HexRange.Length; i += 3)
            {
                if (cp >= HexRange[i + 1] && cp <= HexRange[i + 2])
                {
                    name = HexPrefix[HexRange[i]] + cp.ToString("X4", System.Globalization.CultureInfo.InvariantCulture);
                    return true;
                }
            }
            Reverse index = _reverse ?? (_reverse = BuildReverse());
            int[] keys = index.CodePoints;
            int page = cp >> 8;
            int lo = index.PageStart[page], hi = index.PageStart[page + 1] - 1;
            while (lo <= hi)
            {
                int mid = (int)(((uint)lo + (uint)hi) >> 1);
                int k = keys[mid];
                if (k == cp)
                {
                    name = DecodeAt(index.Offsets[mid], index.Blocks[mid]);
                    return true;
                }
                if (k < cp)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }
            return false;
        }

        private sealed class Reverse
        {
            public int[] CodePoints;
            public int[] Offsets;
            public int[] Blocks;
            public int[] PageStart;   // first index of each 256-code-point page, plus a terminator
        }

        private static Reverse _reverse;   // built once per process; a benign race builds it twice

        private static Reverse BuildReverse()
        {
            var cps = new int[NameCount];
            var offs = new int[NameCount];
            int n = 0;
            int p = 0;
            while (p < NameByteCount)
            {
                int at = p;
                int suffix = B(p + 1);
                p += 2 + suffix;
                int value = (B(p) << 16) | (B(p + 1) << 8) | B(p + 2);
                p += 3;
                if (value < SeqTag)
                {
                    cps[n] = value;
                    offs[n] = at;
                    n++;
                }
            }
            System.Array.Resize(ref cps, n);
            System.Array.Resize(ref offs, n);
            System.Array.Sort(cps, offs);
            // The owning block, resolved once here so a lookup never searches NameIndex.
            var blocks = new int[n];
            for (int i = 0; i < n; i++)
            {
                int lo = 0, hi = NameIndex.Length - 1;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) >> 1;
                    if (NameIndex[mid] <= offs[i])
                        lo = mid;
                    else
                        hi = mid - 1;
                }
                blocks[i] = NameIndex[lo];
            }
            // Page directory over the sorted code points: a lookup searches one 256-code-point page
            // instead of probing 140 KB of keys.
            const int Pages = (0x110000 >> 8) + 1;
            var pageStart = new int[Pages + 1];
            int seen = 0;
            for (int page = 0; page <= Pages; page++)
            {
                while (seen < n && (cps[seen] >> 8) < page)
                    seen++;
                pageStart[page] = seen;
            }
            return new Reverse { CodePoints = cps, Offsets = offs, Blocks = blocks, PageStart = pageStart };
        }

        // The block holding the record is the last one that starts at or before it; the names are
        // rebuilt from that head up to the record itself.
        private static string DecodeAt(int offset)
        {
            int lo = 0, hi = NameIndex.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (NameIndex[mid] <= offset)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            return DecodeAt(offset, NameIndex[lo]);
        }

        [System.ThreadStatic] private static char[] _decodeBuf;
        [System.ThreadStatic] private static byte[] _scanBuf;

        // The packed table unpacked to one byte per index: the decode and scan loops read it a byte at a
        // time, and a plain array read beats a shift and a mask. Same memory, built on first use.
        private static byte[] _bytes;

        private static byte[] Bytes()
        {
            byte[] d = _bytes;
            if (d == null)
            {
                d = new byte[NameByteCount];
                for (int i = 0; i < NameByteCount; i++)
                    d[i] = (byte)B(i);
                _bytes = d;
            }
            return d;
        }

        private static string DecodeAt(int offset, int blockStart)
        {
            char[] buf = _decodeBuf ?? (_decodeBuf = new char[MaxNameLength]);
            byte[] data = Bytes();
            int p = blockStart;
            while (true)
            {
                int shared = data[p], suffix = data[p + 1];
                for (int i = 0; i < suffix; i++)
                    buf[shared + i] = (char)data[p + 2 + i];
                if (p == offset)
                    return new string(buf, 0, shared + suffix);
                p += 2 + suffix + 3;
            }
        }

        private static int Hangul(byte[] key)
        {
            if (!Match(key, 0, HangulPrefix))
                return -1;
            int at = HangulPrefix.Length;
            int l = Longest(JamoL, key, ref at);
            int v = Longest(JamoV, key, ref at);
            int t = Longest(JamoT, key, ref at);
            if (l < 0 || v < 0 || t < 0 || at != key.Length)
                return -1;
            return HangulBase + ((l * 21) + v) * 28 + t;
        }

        // Longest match wins, because the alphabets are not prefix-free ("G" and "GG"). Consonants
        // and vowels never overlap, so the three parts cannot steal characters from each other.
        private static int Longest(string[] jamo, byte[] key, ref int at)
        {
            int best = -1, len = 0;
            for (int i = 0; i < jamo.Length; i++)
            {
                if (jamo[i].Length >= len && Match(key, at, jamo[i]))
                {
                    best = i;
                    len = jamo[i].Length;
                }
            }
            if (best < 0)
                return -1;
            at += len;
            return best;
        }

        private static int HexNamed(byte[] key)
        {
            for (int i = 0; i < HexPrefix.Length; i++)
            {
                string prefix = HexPrefix[i];
                int digits = key.Length - prefix.Length;
                if (digits < 4 || digits > 6 || !Match(key, 0, prefix))
                    continue;
                int cp = 0;
                for (int k = prefix.Length; k < key.Length; k++)
                {
                    int d = HexVal(key[k]);
                    if (d < 0)
                    {
                        cp = -1;
                        break;
                    }
                    cp = (cp * 16) + d;
                }
                if (cp >= 0 && InFamily(i, cp))
                    return cp;
            }
            return -1;
        }

        // The ranges are exact, so an unassigned code point inside a block cannot be named by a rule.
        private static bool InFamily(int prefix, int cp)
        {
            for (int i = 0; i < HexRange.Length; i += 3)
                if (HexRange[i] == prefix && cp >= HexRange[i + 1] && cp <= HexRange[i + 2])
                    return true;
            return false;
        }

        private static int Search(byte[] key)
        {
            int lo = 0, hi = NameIndex.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (CompareHead(NameIndex[mid], key) > 0)
                    hi = mid - 1;
                else
                    lo = mid;
            }
            return Scan(lo, key);
        }

        // A block opens with a full name, which is what makes the binary search possible.
        private static int CompareHead(int at, byte[] key)
        {
            byte[] data = Bytes();
            int len = data[at + 1];
            int n = Math.Min(len, key.Length);
            for (int i = 0; i < n; i++)
            {
                int d = data[at + 2 + i] - key[i];
                if (d != 0)
                    return d;
            }
            return len - key.Length;
        }

        // Walk one block, rebuilding each name from the part it shares with its predecessor.
        private static int Scan(int block, byte[] key)
        {
            int p = NameIndex[block];
            int end = block + 1 < NameIndex.Length ? NameIndex[block + 1] : NameByteCount;
            byte[] data = Bytes();
            byte[] buf = _scanBuf ?? (_scanBuf = new byte[MaxNameLength]);
            while (p < end)
            {
                int shared = data[p], suffix = data[p + 1];
                p += 2;
                for (int i = 0; i < suffix; i++)
                    buf[shared + i] = data[p + i];
                p += suffix;
                int len = shared + suffix;
                int value = (data[p] << 16) | (data[p + 1] << 8) | data[p + 2];
                p += 3;
                int cmp = Compare(buf, len, key);
                if (cmp == 0)
                    return value;
                if (cmp > 0)
                    return -1;
            }
            return -1;
        }

        private static int Compare(byte[] name, int len, byte[] key)
        {
            int n = Math.Min(len, key.Length);
            for (int i = 0; i < n; i++)
            {
                int d = name[i] - key[i];
                if (d != 0)
                    return d;
            }
            return len - key.Length;
        }

        private static bool Match(byte[] key, int at, string s)
        {
            if (at + s.Length > key.Length)
                return false;
            for (int i = 0; i < s.Length; i++)
                if (key[at + i] != s[i])
                    return false;
            return true;
        }

        // Two table bytes live in one char, high byte first.
        private static int B(int i)
        {
            char c = NameData[i >> 1];
            return (i & 1) == 0 ? c >> 8 : c & 0xFF;
        }

        private static int HexVal(int c)
        {
            if (c >= '0' && c <= '9')
                return c - '0';
            if (c >= 'A' && c <= 'F')
                return c - 'A' + 10;
            return -1;
        }
    }
}

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // IGNORECASE as CPython's sre has it, over .NET's own. sre folds a cased character to its simple lowercase
    // (Unicode 16) and adds the letters of re/_casefix.py that share an uppercase with it: x matches c when lower(x) is
    // lower(c) or one of those. .NET compares its own invariant lowercase (Unicode 8), which misses the Kelvin sign
    // against k, dotless i, long s, the Greek symbol forms, Georgian Mtavruli and more. The translator keeps .NET's
    // IgnoreCase and adds what sre takes beyond it to each literal and class member. Under re.A sre folds ASCII only.
    internal static class RegexIgnoreCase
    {
        // re/_casefix.py _EXTRA_CASES (CPython 3.14, Unicode 16.0): a lowercase letter -> the other lowercase letters
        // with its uppercase.
        private static readonly int[][] ExtraCases =
        {
            new[] { 0x0069, 0x0131 }, new[] { 0x0073, 0x017F }, new[] { 0x00B5, 0x03BC }, new[] { 0x0131, 0x0069 },
            new[] { 0x017F, 0x0073 }, new[] { 0x0345, 0x03B9, 0x1FBE }, new[] { 0x0390, 0x1FD3 }, new[] { 0x03B0, 0x1FE3 },
            new[] { 0x03B2, 0x03D0 }, new[] { 0x03B5, 0x03F5 }, new[] { 0x03B8, 0x03D1 }, new[] { 0x03B9, 0x0345, 0x1FBE },
            new[] { 0x03BA, 0x03F0 }, new[] { 0x03BC, 0x00B5 }, new[] { 0x03C0, 0x03D6 }, new[] { 0x03C1, 0x03F1 },
            new[] { 0x03C2, 0x03C3 }, new[] { 0x03C3, 0x03C2 }, new[] { 0x03C6, 0x03D5 }, new[] { 0x03D0, 0x03B2 },
            new[] { 0x03D1, 0x03B8 }, new[] { 0x03D5, 0x03C6 }, new[] { 0x03D6, 0x03C0 }, new[] { 0x03F0, 0x03BA },
            new[] { 0x03F1, 0x03C1 }, new[] { 0x03F5, 0x03B5 }, new[] { 0x0432, 0x1C80 }, new[] { 0x0434, 0x1C81 },
            new[] { 0x043E, 0x1C82 }, new[] { 0x0441, 0x1C83 }, new[] { 0x0442, 0x1C84, 0x1C85 }, new[] { 0x044A, 0x1C86 },
            new[] { 0x0463, 0x1C87 }, new[] { 0x1C80, 0x0432 }, new[] { 0x1C81, 0x0434 }, new[] { 0x1C82, 0x043E },
            new[] { 0x1C83, 0x0441 }, new[] { 0x1C84, 0x0442, 0x1C85 }, new[] { 0x1C85, 0x0442, 0x1C84 },
            new[] { 0x1C86, 0x044A }, new[] { 0x1C87, 0x0463 }, new[] { 0x1C88, 0xA64B }, new[] { 0x1E61, 0x1E9B },
            new[] { 0x1E9B, 0x1E61 }, new[] { 0x1FBE, 0x0345, 0x03B9 }, new[] { 0x1FD3, 0x0390 }, new[] { 0x1FE3, 0x03B0 },
            new[] { 0xA64B, 0x1C88 }, new[] { 0xFB05, 0xFB06 }, new[] { 0xFB06, 0xFB05 },
        };

        private static readonly object Gate = new object();
        private static Dictionary<char, string> _extras;   // built on first use: a pass over the BMP
        private static Dictionary<char, string> _rawExtras;   // the same extras as plain characters
        private static KeyValuePair<char, string>[] _ordered;   // the same, sorted by character
        private static string _asciiFolded;                // ASCII letters .NET also matches against a non-ASCII character

        // What sre's IGNORECASE matches against c beyond .NET's, as class members (Member's form), or null.
        internal static string Extras(char c)
        {
            string s;
            return Table().TryGetValue(c, out s) ? s : null;
        }

        // The same, as the characters themselves: what a subject must hold for the extras to change an answer.
        internal static string RawExtras(char c)
        {
            Table();
            string s;
            return _rawExtras.TryGetValue(c, out s) ? s : null;
        }

        // Adds c's extras to `risk` (a pattern's case-risk set), if any.
        internal static void NoteRisk(HashSet<char> risk, char c)
        {
            string raw = RawExtras(c);
            if (risk == null || raw == null)
                return;
            foreach (char x in raw)
                risk.Add(x);
        }

        // The extras of each character in lo..hi not yet in `added`, in character order: a sorted table searched for
        // the range: walking the whole table per range made a class of 1,360 ranges compile 7-15 times slower than 1.1.0.
        internal static void AppendRangeExtras(StringBuilder pos, char lo, char hi, HashSet<char> added, HashSet<char> risk)
        {
            KeyValuePair<char, string>[] t = Ordered();
            int a = 0, b = t.Length;
            while (a < b)
            {
                int mid = (a + b) >> 1;
                if (t[mid].Key < lo)
                    a = mid + 1;
                else
                    b = mid;
            }
            for (int i = a; i < t.Length && t[i].Key <= hi; i++)
            {
                if (added.Add(t[i].Key))
                {
                    pos.Append(t[i].Value);
                    NoteRisk(risk, t[i].Key);
                }
            }
        }

        private static KeyValuePair<char, string>[] Ordered()
        {
            KeyValuePair<char, string>[] t = Volatile.Read(ref _ordered);
            if (t != null)
                return t;
            lock (Gate)
            {
                if (_ordered == null)
                {
                    var list = new List<KeyValuePair<char, string>>(Table());
                    list.Sort((x, y) => x.Key.CompareTo(y.Key));
                    Volatile.Write(ref _ordered, list.ToArray());
                }
                return _ordered;
            }
        }

        // Under re.A: whether .NET's IgnoreCase would match this character against another one sre does not (sre folds
        // ASCII letters only, and only against ASCII).
        internal static bool AsciiModeNeedsExact(char c)
        {
            if (c >= 0x80)
                return char.ToLowerInvariant(c) != c || char.ToUpperInvariant(c) != c;
            AsciiTables();
            return _asciiFolded.IndexOf(char.ToLowerInvariant(c)) >= 0;
        }

        // An ASCII letter with its other case, for a case-sensitive class under re.A; other characters as they are.
        internal static string AsciiBothCases(char c)
        {
            if (c >= 'a' && c <= 'z')
                return Member(c) + Member((char)(c - 32));
            if (c >= 'A' && c <= 'Z')
                return Member(c) + Member((char)(c + 32));
            return Member(c);
        }

        // A class member: a letter or digit as itself, anything else escaped. .NET parses the letter faster, and an
        // IGNORECASE literal s is [sſ], 4 characters, not 14.
        internal static string Member(char c)
        {
            if (char.IsLetterOrDigit(c))
                return c.ToString();
            return "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture);
        }

        private static void AsciiTables()
        {
            if (Volatile.Read(ref _asciiFolded) != null)
                return;
            var folded = new bool[0x80];
            for (int c = 0x80; c <= 0xFFFF; c++)
            {
                char lo = char.ToLowerInvariant((char)c);
                if (lo < 0x80)
                    folded[lo] = true;
            }
            var sb = new StringBuilder();
            for (int c = 0; c < 0x80; c++)
            {
                if (folded[c])
                    sb.Append((char)c);
            }
            Volatile.Write(ref _asciiFolded, sb.ToString());
        }

        private static Dictionary<char, string> Table()
        {
            Dictionary<char, string> t = Volatile.Read(ref _extras);
            if (t != null)
                return t;
            lock (Gate)
            {
                if (_extras == null)
                {
                    Dictionary<char, string> raw;
                    Dictionary<char, string> built = Build(out raw);
                    _rawExtras = raw;
                    Volatile.Write(ref _extras, built);
                }
                return _extras;
            }
        }

        private static Dictionary<char, string> Build(out Dictionary<char, string> rawResult)
        {
            var fixes = new Dictionary<char, int[]>();
            foreach (int[] row in ExtraCases)
                fixes[(char)row[0]] = row;
            var byLower = new Dictionary<char, List<char>>();
            for (int i = 0; i <= 0xFFFF; i++)
            {
                if (i >= 0xD800 && i <= 0xDFFF)
                    continue;
                char lo = PyUnicodeCase.SimpleLower((char)i);
                List<char> list;
                if (!byLower.TryGetValue(lo, out list))
                    byLower[lo] = list = new List<char>(2);
                list.Add((char)i);
            }
            var result = new Dictionary<char, string>();
            rawResult = new Dictionary<char, string>();
            var sb = new StringBuilder();
            var raw = new StringBuilder();
            var seen = new HashSet<char>();
            for (int i = 0; i <= 0xFFFF; i++)
            {
                if (i >= 0xD800 && i <= 0xDFFF)
                    continue;
                char c = (char)i;
                char lo = PyUnicodeCase.SimpleLower(c);
                // sre's iscased: its upper is the first character of the full mapping (U+0390, ß, the st ligatures)
                if (lo == c && PyUnicodeCase.SimpleUpper(c) == c && PyUnicodeCase.Upper(c.ToString())[0] == c)
                    continue;   // not cased: sre matches it exactly
                sb.Clear();
                raw.Clear();
                seen.Clear();
                char net = char.ToLowerInvariant(c);
                int[] row;
                AddSameLower(byLower, lo, net, sb, raw, seen);
                if (fixes.TryGetValue(lo, out row))
                {
                    for (int k = 1; k < row.Length; k++)
                        AddSameLower(byLower, (char)row[k], net, sb, raw, seen);
                }
                if (sb.Length > 0)
                {
                    result[c] = sb.ToString();
                    rawResult[c] = raw.ToString();
                }
            }
            return result;
        }

        private static void AddSameLower(Dictionary<char, List<char>> byLower, char lower, char net, StringBuilder sb, StringBuilder raw, HashSet<char> seen)
        {
            List<char> list;
            if (!byLower.TryGetValue(lower, out list))
                return;
            foreach (char x in list)
            {
                if (char.ToLowerInvariant(x) != net && seen.Add(x))
                {
                    sb.Append(Member(x));
                    raw.Append(x);
                }
            }
        }
    }
}

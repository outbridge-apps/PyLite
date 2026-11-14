using System.Collections.Generic;
using System.Globalization;

namespace Outbridge.PyLite.Runtime.Values
{
    // str.isprintable/isalpha/isnumeric/isdigit/isdecimal over Unicode 16.0.0 (the version CPython 3.14
    // ships), where .NET Framework answers from Unicode 8. The ranges are parsed on first use.
    internal static partial class PyUnicodeClass
    {
        // The BMP as a bitmap (8 KB, one load per test: repr walks every character), the astral planes searched
        private sealed class Ranges
        {
            internal readonly int[] Starts, Ends;
            private readonly ulong[] _bmp = new ulong[0x10000 / 64];

            internal Ranges(string text)
            {
                string[] parts = text.Split(' ');
                Starts = new int[parts.Length];
                Ends = new int[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    int dash = parts[i].IndexOf('-');
                    Starts[i] = int.Parse(dash < 0 ? parts[i] : parts[i].Substring(0, dash), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    Ends[i] = dash < 0 ? Starts[i] : int.Parse(parts[i].Substring(dash + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    for (int cp = Starts[i]; cp <= Ends[i] && cp < 0x10000; cp++)
                        _bmp[cp >> 6] |= 1UL << (cp & 63);
                }
            }

            internal bool Contains(int cp)
            {
                if (cp < 0x10000)
                    return (_bmp[cp >> 6] & (1UL << (cp & 63))) != 0;
                int lo = 0, hi = Starts.Length - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (cp < Starts[mid])
                        hi = mid - 1;
                    else if (cp > Ends[mid])
                        lo = mid + 1;
                    else
                        return true;
                }
                return false;
            }
        }

        // Built on first use, also on a host's thread (the lexer's messages and NFKC), and published only when whole:
        // a build cut short by an abort publishes nothing, where a Lazy<T> kept the exception for the process.
        private static Ranges _printable, _alpha, _numeric, _digit, _decimal;

        // ASCII is answered without the tables: the common case costs what it did
        internal static bool IsPrintable(int cp) { return cp < 0x80 ? cp >= 0x20 && cp < 0x7F : (_printable ?? (_printable = new Ranges(PrintableRanges))).Contains(cp); }
        internal static bool IsAlpha(int cp) { return cp < 0x80 ? (cp | 0x20) >= 'a' && (cp | 0x20) <= 'z' : (_alpha ?? (_alpha = new Ranges(AlphaRanges))).Contains(cp); }
        internal static bool IsNumeric(int cp) { return cp < 0x80 ? cp >= '0' && cp <= '9' : (_numeric ?? (_numeric = new Ranges(NumericRanges))).Contains(cp); }
        internal static bool IsDigit(int cp) { return cp < 0x80 ? cp >= '0' && cp <= '9' : (_digit ?? (_digit = new Ranges(DigitRanges))).Contains(cp); }
        internal static bool IsDecimal(int cp) { return cp < 0x80 ? cp >= '0' && cp <= '9' : (_decimal ?? (_decimal = new Ranges(DecimalRanges))).Contains(cp); }

        private static HashSet<int> _exclusions;

        private static HashSet<int> BuildExclusions()
        {
            var set = new HashSet<int>();
            foreach (string hex in CompositionExclusions.Split(' '))
                set.Add(int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            return set;
        }

        // CompositionExclusions.txt: script-specific and post-composition-version exclusions
        internal static bool IsExcludedFromComposition(int cp) { return (_exclusions ?? (_exclusions = BuildExclusions())).Contains(cp); }
    }
}

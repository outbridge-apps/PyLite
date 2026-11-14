using System.Globalization;
using System.Text;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    internal static partial class StrMethods
    {
        // ---- case + alignment ----

        private static void AddCaseSlots(Slots s)
        {
            // Case mapping goes through PyUnicodeCase (the generated Unicode tables), not the BCL: a
            // mapping may change LENGTH ('ss'.upper() == 'SS') and the BCL's tables are both incomplete
            // and unpinned. See PyUnicodeCase.cs.
            s.Linear("upper", 0, 0, (self, a, kw, c) => c.Values.Str(PyUnicodeCase.Upper(S(self), c.Budget)));
            s.Linear("lower", 0, 0, (self, a, kw, c) => c.Values.Str(PyUnicodeCase.Lower(S(self), c.Budget)));
            s.Linear("casefold", 0, 0, (self, a, kw, c) => c.Values.Str(PyUnicodeCase.Fold(S(self), c.Budget)));
            s.Linear("capitalize", 0, 0, (self, a, kw, c) => c.Values.Str(PyUnicodeCase.Capitalize(S(self), c.Budget)));
            s.Linear("title", 0, 0, (self, a, kw, c) => c.Values.Str(PyUnicodeCase.Title(S(self), c.Budget)));
            s.Linear("istitle", 0, 0, (self, a, kw, c) => c.Values.Bool(IsTitle(S(self))));
            s.Linear("swapcase", 0, 0, (self, a, kw, c) => c.Values.Str(PyUnicodeCase.SwapCase(S(self), c.Budget)));
            s.Linear("zfill", 1, 1, (self, a, kw, c) => ZFill(c, self, a));
            s.Linear("ljust", 1, 2, (self, a, kw, c) => Justify(c, self, a, '<'));
            s.Linear("rjust", 1, 2, (self, a, kw, c) => Justify(c, self, a, '>'));
            s.Linear("center", 1, 2, (self, a, kw, c) => Justify(c, self, a, '^'));
            s.Linear("expandtabs", (self, a, kw, c) => c.Values.StrPrecharged(ExpandTabs(c, S(self), ArgInt(c, WithKw(c, a, kw, "expandtabs", "tabsize"), 0, 8))));
        }

        // Port of unicode_istitle: an uppercase or titlecase letter must not follow a cased one, a
        // lowercase letter must follow one, and at least one cased letter is required.
        private static bool IsTitle(string s)
        {
            bool cased = false, prevCased = false;
            for (int i = 0; i < s.Length; i++)
            {
                int width;
                int ch = CodePointAt(s, i, out width);
                i += width - 1;
                if (IsUpperCp(ch) || IsTitleCp(ch))
                {
                    if (prevCased)
                        return false;
                    prevCased = true;
                    cased = true;
                }
                else if (IsLowerCp(ch))
                {
                    if (!prevCased)
                        return false;
                    cased = true;
                }
                else
                {
                    prevCased = false;
                }
            }
            return cased;
        }

        private static ScriptValue ZFill(EvalContext c, ScriptValue selfVal, ScriptValue[] a)
        {
            string s = S(selfVal);
            int width = ArgInt(c, a, 0, 0);
            if (width <= s.Length)
                return selfVal;
            c.Values.EnsureStrLen(width);
            int pad = width - s.Length;
            var sb = new StringBuilder(width);
            int i = 0;
            if (s.Length > 0 && (s[0] == '+' || s[0] == '-'))
            {
                sb.Append(s[0]);
                i = 1;
            }
            sb.Append('0', pad);
            sb.Append(s, i, s.Length - i);
            return c.Values.StrPrecharged(sb.ToString());
        }

        private static ScriptValue Justify(EvalContext c, ScriptValue selfVal, ScriptValue[] a, char align)
        {
            string s = S(selfVal);
            int width = ArgInt(c, a, 0, 0);
            char fill = ' ';
            if (a.Length > 1)
            {
                StrValue f = a[1] as StrValue;
                if (f == null)
                    throw Raise.TypeError(c, "The fill character must be a unicode character, not " + a[1].PyTypeName);
                if (f.Value.Length != 1)
                    throw Raise.TypeError(c, "The fill character must be exactly one character long");
                fill = f.Value[0];
            }
            if (width <= s.Length)
                return selfVal;
            c.Values.EnsureStrLen(width);
            int marg = width - s.Length;
            int left = align == '<' ? 0 : align == '>' ? marg : marg / 2 + (marg & width & 1);
            var sb = new StringBuilder(width);
            sb.Append(fill, left);
            sb.Append(s);
            sb.Append(fill, marg - left);
            return c.Values.StrPrecharged(sb.ToString());
        }

        // Two passes: the result length is charged and capped once, then the exact buffer is filled
        // (charging the running length per tab cost tabs^2 and failed on a few thousand tabs).
        private static string ExpandTabs(EvalContext c, string s, int tabsize)
        {
            long len = 0, column = 0;
            for (int i = 0; i < s.Length && len <= c.Limits.MaxStrChars; i++)
            {
                char ch = s[i];
                long n = ch != '\t' ? 1 : tabsize > 0 ? tabsize - column % tabsize : 0;
                len += n;
                column = ch == '\n' || ch == '\r' ? 0 : column + n;
            }
            c.Values.EnsureStrLen(len);
            var sb = new StringBuilder((int)len);
            int col = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\t')
                {
                    if (tabsize > 0)
                    {
                        int n = tabsize - col % tabsize;
                        sb.Append(' ', n);
                        col += n;
                    }
                }
                else if (ch == '\n' || ch == '\r')
                {
                    sb.Append(ch);
                    col = 0;
                }
                else
                {
                    sb.Append(ch);
                    col++;
                }
            }
            return sb.ToString();
        }

        // ---- is* predicates (all false on the empty string) ----

        private static void AddPredicateSlots(Slots s)
        {
            s.Linear("isdecimal", 0, 0, (self, a, kw, c) => c.Values.Bool(All(c.Budget, S(self), IsDecimal)));
            s.Linear("isdigit", 0, 0, (self, a, kw, c) => c.Values.Bool(All(c.Budget, S(self), IsDigit)));
            s.Linear("isnumeric", 0, 0, (self, a, kw, c) => c.Values.Bool(All(c.Budget, S(self), IsNumeric)));
            s.Linear("isalpha", 0, 0, (self, a, kw, c) => c.Values.Bool(All(c.Budget, S(self), IsAlpha)));
            s.Linear("isalnum", 0, 0, (self, a, kw, c) => c.Values.Bool(All(c.Budget, S(self), IsAlnum)));
            s.Linear("isspace", 0, 0, (self, a, kw, c) => c.Values.Bool(All(c.Budget, S(self), cp => cp < 0x10000 && PyUnicode.IsPythonSpace((char)cp))));
            s.Linear("islower", 0, 0, (self, a, kw, c) => c.Values.Bool(IsLowerUpper(c.Budget, S(self), false)));
            s.Linear("isupper", 0, 0, (self, a, kw, c) => c.Values.Bool(IsLowerUpper(c.Budget, S(self), true)));
            // isascii/isprintable are true on the EMPTY string (CPython), unlike the All() family.
            s.Linear("isascii", 0, 0, (self, a, kw, c) => c.Values.Bool(AllOrEmpty(c.Budget, S(self), ch => ch < 0x80)));
            s.Linear("isprintable", 0, 0, (self, a, kw, c) => c.Values.Bool(AllOrEmpty(c.Budget, S(self), IsPrintable)));
            s.Linear("isidentifier", 0, 0, (self, a, kw, c) => c.Values.Bool(IsIdentifier(S(self))));
        }

        // The predicates walk code POINTS: a surrogate pair is one character (as len() does not count
        // it), a lone surrogate keeps its Cs category and fails every test, as in CPython.
        // A deadline check every 64K chars: an astral char's properties cost tens of ns each.
        private static void Tick(Budget b, int i)
        {
            if ((i & 0xFFFE) == 0xFFFE)   // i steps by 1 or 2: one of the two is hit in every 64K, never index 0
                b.CheckDeadlineNow();
        }

        private static bool AllOrEmpty(Budget b, string s, System.Func<int, bool> pred)
        {
            for (int i = 0; i < s.Length; i++)
            {
                Tick(b, i);
                int width;
                if (!pred(CodePointAt(s, i, out width)))
                    return false;
                i += width - 1;
            }
            return true;
        }

        private static int CodePointAt(string s, int i, out int width)
        {
            char ch = s[i];
            if (char.IsHighSurrogate(ch) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                width = 2;
                return char.ConvertToUtf32(ch, s[i + 1]);
            }
            width = 1;
            return ch;
        }

        private static UnicodeCategory CategoryOf(int cp)
        {
            return cp < 0x10000 ? PyUnicode.Category((char)cp) : AstralCategory(cp);
        }

        // The BCL answers for an astral code point only through a string, one per char: 16M chars of one
        // astral letter took seconds. The answers are fixed tables, so one process-wide copy is cached:
        // a category byte per code point (1 MB, made on first use) and the few number characters' values.
        private static byte[] _astralCategories;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, double> AstralNumbers =
            new System.Collections.Concurrent.ConcurrentDictionary<int, double>();

        private static UnicodeCategory AstralCategory(int cp)
        {
            byte[] table = _astralCategories ?? (_astralCategories = new byte[0x100000]);
            int i = cp - 0x10000;
            byte b = table[i];
            if (b == 0)
                table[i] = b = (byte)(CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(cp), 0) + 1);
            return (UnicodeCategory)(b - 1);
        }

        private static bool IsNumberCategory(UnicodeCategory cat)
        {
            return cat == UnicodeCategory.DecimalDigitNumber || cat == UnicodeCategory.LetterNumber
                || cat == UnicodeCategory.OtherNumber;
        }

        private static int DigitValueOf(int cp)
        {
            if (cp < 0x10000)
                return PyUnicode.DigitValue((char)cp);
            if (!IsNumberCategory(AstralCategory(cp)))
                return -1;
            return (int)AstralNumbers.GetOrAdd(-cp, k => CharUnicodeInfo.GetDigitValue(char.ConvertFromUtf32(-k), 0));   // keyed -cp: digits beside values
        }

        // A few astral ideographs (Lo) carry a numeric value too (U+2F890 is 9).
        private static double NumericValueOf(int cp)
        {
            if (cp < 0x10000)
                return PyUnicode.NumericValue((char)cp);
            UnicodeCategory cat = AstralCategory(cp);
            if (!IsNumberCategory(cat) && cat != UnicodeCategory.OtherLetter)
                return -1;
            double known;
            if (AstralNumbers.TryGetValue(cp, out known))
                return known;
            double v = CharUnicodeInfo.GetNumericValue(char.ConvertFromUtf32(cp), 0);
            if (v != -1)
                AstralNumbers.TryAdd(cp, v);   // the numeric ones only: a script walking 60K ideographs filled it for good
            return v;
        }

        // The class predicates answer from Unicode 16.0.0, as CPython 3.14 does (PyUnicodeClass).
        internal static bool IsPrintable(int c) { return PyUnicodeClass.IsPrintable(c); }

        // PEP 3131 exactly: the first code point must be XID_Start (or '_', which the property itself
        // does not carry), the rest XID_Continue. Walks code POINTS, so an astral letter counts once.
        internal static bool IsIdentifier(string s)
        {
            if (s.Length == 0)
                return false;
            int i = 0;
            while (i < s.Length)
            {
                char ch = s[i];
                int cp = ch;
                int width = 1;
                if (char.IsHighSurrogate(ch) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    cp = char.ConvertToUtf32(ch, s[i + 1]);
                    width = 2;
                }
                bool ok = i == 0
                    ? PyUnicodeCase.IsXidStart(cp) || cp == '_'
                    : PyUnicodeCase.IsXidContinue(cp);
                if (!ok)
                    return false;
                i += width;
            }
            return true;
        }

        private static bool All(Budget b, string s, System.Func<int, bool> pred)
        {
            if (s.Length == 0)
                return false;
            for (int i = 0; i < s.Length; i++)
            {
                Tick(b, i);
                int width;
                if (!pred(CodePointAt(s, i, out width)))
                    return false;
                i += width - 1;
            }
            return true;
        }

        private static bool IsDecimal(int c) { return PyUnicodeClass.IsDecimal(c); }
        private static bool IsDigit(int c) { return PyUnicodeClass.IsDigit(c); }
        private static bool IsNumeric(int c) { return PyUnicodeClass.IsNumeric(c); }
        private static bool IsAlpha(int c) { return PyUnicodeClass.IsAlpha(c); }
        private static bool IsAlnum(int c) { return IsAlpha(c) || IsNumeric(c); }   // decimal and digit are numeric

        // The case properties by code point: the BMP from the generated flag table, an astral letter
        // (Deseret, Adlam, ...) from its general category.
        private static bool IsUpperCp(int cp) { return cp < 0x10000 ? PyUnicodeCase.IsUpper((char)cp) : InRanges(AstralUpper, cp); }
        private static bool IsLowerCp(int cp) { return cp < 0x10000 ? PyUnicodeCase.IsLower((char)cp) : InRanges(AstralLower, cp); }
        private static bool IsTitleCp(int cp) { return cp >= 0x10000 ? false : PyUnicodeCase.IsTitleCategory((char)cp); }

        private static bool InRanges(int[] ranges, int cp)
        {
            int i = System.Array.BinarySearch(ranges, cp);
            return i >= 0 || (~i & 1) == 1;   // between a range's start and its end
        }

        // Unicode 16's Uppercase and Lowercase past the BMP, from CPython 3.14: start and end of each range, flat.
        // The BCL's categories stop at Unicode 8 and hold neither the derived property nor the newer scripts.
        private static readonly int[] AstralUpper =
        {
            0x10400, 0x10427, 0x104b0, 0x104d3, 0x10570, 0x1057a, 0x1057c, 0x1058a, 0x1058c, 0x10592, 0x10594, 0x10595, 0x10c80, 0x10cb2,
            0x10d50, 0x10d65, 0x118a0, 0x118bf, 0x16e40, 0x16e5f, 0x1d400, 0x1d419, 0x1d434, 0x1d44d, 0x1d468, 0x1d481, 0x1d49c, 0x1d49c,
            0x1d49e, 0x1d49f, 0x1d4a2, 0x1d4a2, 0x1d4a5, 0x1d4a6, 0x1d4a9, 0x1d4ac, 0x1d4ae, 0x1d4b5, 0x1d4d0, 0x1d4e9, 0x1d504, 0x1d505,
            0x1d507, 0x1d50a, 0x1d50d, 0x1d514, 0x1d516, 0x1d51c, 0x1d538, 0x1d539, 0x1d53b, 0x1d53e, 0x1d540, 0x1d544, 0x1d546, 0x1d546,
            0x1d54a, 0x1d550, 0x1d56c, 0x1d585, 0x1d5a0, 0x1d5b9, 0x1d5d4, 0x1d5ed, 0x1d608, 0x1d621, 0x1d63c, 0x1d655, 0x1d670, 0x1d689,
            0x1d6a8, 0x1d6c0, 0x1d6e2, 0x1d6fa, 0x1d71c, 0x1d734, 0x1d756, 0x1d76e, 0x1d790, 0x1d7a8, 0x1d7ca, 0x1d7ca, 0x1e900, 0x1e921,
            0x1f130, 0x1f149, 0x1f150, 0x1f169, 0x1f170, 0x1f189,
        };

        private static readonly int[] AstralLower =
        {
            0x10428, 0x1044f, 0x104d8, 0x104fb, 0x10597, 0x105a1, 0x105a3, 0x105b1, 0x105b3, 0x105b9, 0x105bb, 0x105bc, 0x10780, 0x10780,
            0x10783, 0x10785, 0x10787, 0x107b0, 0x107b2, 0x107ba, 0x10cc0, 0x10cf2, 0x10d70, 0x10d85, 0x118c0, 0x118df, 0x16e60, 0x16e7f,
            0x1d41a, 0x1d433, 0x1d44e, 0x1d454, 0x1d456, 0x1d467, 0x1d482, 0x1d49b, 0x1d4b6, 0x1d4b9, 0x1d4bb, 0x1d4bb, 0x1d4bd, 0x1d4c3,
            0x1d4c5, 0x1d4cf, 0x1d4ea, 0x1d503, 0x1d51e, 0x1d537, 0x1d552, 0x1d56b, 0x1d586, 0x1d59f, 0x1d5ba, 0x1d5d3, 0x1d5ee, 0x1d607,
            0x1d622, 0x1d63b, 0x1d656, 0x1d66f, 0x1d68a, 0x1d6a5, 0x1d6c2, 0x1d6da, 0x1d6dc, 0x1d6e1, 0x1d6fc, 0x1d714, 0x1d716, 0x1d71b,
            0x1d736, 0x1d74e, 0x1d750, 0x1d755, 0x1d770, 0x1d788, 0x1d78a, 0x1d78f, 0x1d7aa, 0x1d7c2, 0x1d7c4, 0x1d7c9, 0x1d7cb, 0x1d7cb,
            0x1df00, 0x1df09, 0x1df0b, 0x1df1e, 0x1df25, 0x1df2a, 0x1e030, 0x1e06d, 0x1e922, 0x1e943,
        };

        // Port of unicode_islower/isupper: needs >=1 cased letter, no letter of the opposite case.
        // Uppercase/Lowercase are the DERIVED properties CPython uses, so the Other_Lowercase characters
        // (modifier letters, circled forms) answer as they do there rather than by general category.
        private static bool IsLowerUpper(Budget b, string s, bool upper)
        {
            bool cased = false;
            for (int i = 0; i < s.Length; i++)
            {
                Tick(b, i);
                int width;
                int cp = CodePointAt(s, i, out width);
                i += width - 1;
                if (IsTitleCp(cp))
                    return false;
                if (upper)
                {
                    if (IsLowerCp(cp))
                        return false;
                    if (IsUpperCp(cp))
                        cased = true;
                }
                else
                {
                    if (IsUpperCp(cp))
                        return false;
                    if (IsLowerCp(cp))
                        cased = true;
                }
            }
            return cased;
        }
    }
}

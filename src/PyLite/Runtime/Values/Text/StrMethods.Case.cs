using System.Collections.Generic;
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
            s.Linear("upper", (self, a, kw, c) => c.Values.Str(PyUnicodeCase.Upper(S(self))));
            s.Linear("lower", (self, a, kw, c) => c.Values.Str(PyUnicodeCase.Lower(S(self))));
            s.Linear("casefold", (self, a, kw, c) => c.Values.Str(PyUnicodeCase.Fold(S(self))));
            s.Linear("capitalize", (self, a, kw, c) => c.Values.Str(PyUnicodeCase.Capitalize(S(self))));
            s.Linear("title", (self, a, kw, c) => c.Values.Str(PyUnicodeCase.Title(S(self))));
            s.Linear("istitle", (self, a, kw, c) => c.Values.Bool(IsTitle(S(self))));
            s.Linear("swapcase", (self, a, kw, c) => c.Values.Str(PyUnicodeCase.SwapCase(S(self))));
            s.Linear("zfill", (self, a, kw, c) => ZFill(c, self, a));
            s.Linear("ljust", (self, a, kw, c) => Justify(c, self, a, '<'));
            s.Linear("rjust", (self, a, kw, c) => Justify(c, self, a, '>'));
            s.Linear("center", (self, a, kw, c) => Justify(c, self, a, '^'));
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

        private static string ExpandTabs(EvalContext c, string s, int tabsize)
        {
            var sb = new StringBuilder(s.Length);
            int col = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\t')
                {
                    if (tabsize > 0)
                    {
                        int n = tabsize - col % tabsize;
                        c.Values.EnsureStrLen((long)sb.Length + n);   // a huge tabsize would otherwise OOM here
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
            s.Linear("isdecimal", (self, a, kw, c) => c.Values.Bool(All(S(self), IsDecimal)));
            s.Linear("isdigit", (self, a, kw, c) => c.Values.Bool(All(S(self), IsDigit)));
            s.Linear("isnumeric", (self, a, kw, c) => c.Values.Bool(All(S(self), IsNumeric)));
            s.Linear("isalpha", (self, a, kw, c) => c.Values.Bool(All(S(self), IsAlpha)));
            s.Linear("isalnum", (self, a, kw, c) => c.Values.Bool(All(S(self), IsAlnum)));
            s.Linear("isspace", (self, a, kw, c) => c.Values.Bool(All(S(self), cp => cp < 0x10000 && PyUnicode.IsPythonSpace((char)cp))));
            s.Linear("islower", (self, a, kw, c) => c.Values.Bool(IsLowerUpper(S(self), false)));
            s.Linear("isupper", (self, a, kw, c) => c.Values.Bool(IsLowerUpper(S(self), true)));
            // isascii/isprintable are true on the EMPTY string (CPython), unlike the All() family.
            s.Linear("isascii", (self, a, kw, c) => c.Values.Bool(AllOrEmpty(S(self), ch => ch < 0x80)));
            s.Linear("isprintable", (self, a, kw, c) => c.Values.Bool(AllOrEmpty(S(self), IsPrintable)));
            s.Linear("isidentifier", (self, a, kw, c) => c.Values.Bool(IsIdentifier(S(self))));
        }

        // The predicates walk code POINTS: a surrogate pair is one character (as len() does not count
        // it), a lone surrogate keeps its Cs category and fails every test, as in CPython.
        private static bool AllOrEmpty(string s, System.Func<int, bool> pred)
        {
            for (int i = 0; i < s.Length; i++)
            {
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
            return cp < 0x10000 ? PyUnicode.Category((char)cp) : CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(cp), 0);
        }

        private static int DigitValueOf(int cp)
        {
            return cp < 0x10000 ? PyUnicode.DigitValue((char)cp) : CharUnicodeInfo.GetDigitValue(char.ConvertFromUtf32(cp), 0);
        }

        private static double NumericValueOf(int cp)
        {
            return cp < 0x10000 ? PyUnicode.NumericValue((char)cp) : CharUnicodeInfo.GetNumericValue(char.ConvertFromUtf32(cp), 0);
        }

        // Printable = not a control/format/surrogate/private/unassigned char or a separator other than ' '.
        private static bool IsPrintable(int c)
        {
            if (c == ' ')
                return true;
            switch (CategoryOf(c))
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.Surrogate:
                case UnicodeCategory.PrivateUse:
                case UnicodeCategory.OtherNotAssigned:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                case UnicodeCategory.SpaceSeparator:
                    return false;
                default:
                    return true;
            }
        }

        // PEP 3131 exactly: the first code point must be XID_Start (or '_', which the property itself
        // does not carry), the rest XID_Continue. Walks code POINTS, so an astral letter counts once.
        private static bool IsIdentifier(string s)
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

        private static bool All(string s, System.Func<int, bool> pred)
        {
            if (s.Length == 0)
                return false;
            for (int i = 0; i < s.Length; i++)
            {
                int width;
                if (!pred(CodePointAt(s, i, out width)))
                    return false;
                i += width - 1;
            }
            return true;
        }

        private static bool IsDecimal(int c) { return CategoryOf(c) == UnicodeCategory.DecimalDigitNumber; }
        private static bool IsDigit(int c) { return IsDecimal(c) || DigitValueOf(c) >= 0; }

        private static bool IsNumeric(int c)
        {
            if (IsDigit(c) || NumericValueOf(c) >= 0)
                return true;
            UnicodeCategory cat = CategoryOf(c);
            return cat == UnicodeCategory.LetterNumber || cat == UnicodeCategory.OtherNumber;
        }

        private static bool IsAlpha(int c)
        {
            UnicodeCategory cat = CategoryOf(c);
            return cat == UnicodeCategory.UppercaseLetter || cat == UnicodeCategory.LowercaseLetter
                || cat == UnicodeCategory.TitlecaseLetter || cat == UnicodeCategory.ModifierLetter
                || cat == UnicodeCategory.OtherLetter;
        }

        private static bool IsAlnum(int c) { return IsAlpha(c) || IsDecimal(c) || IsDigit(c) || IsNumeric(c); }

        // The case properties by code point: the BMP from the generated flag table, an astral letter
        // (Deseret, Adlam, ...) from its general category.
        private static bool IsUpperCp(int cp) { return cp < 0x10000 ? PyUnicodeCase.IsUpper((char)cp) : CategoryOf(cp) == UnicodeCategory.UppercaseLetter; }
        private static bool IsLowerCp(int cp) { return cp < 0x10000 ? PyUnicodeCase.IsLower((char)cp) : CategoryOf(cp) == UnicodeCategory.LowercaseLetter; }
        private static bool IsTitleCp(int cp) { return cp < 0x10000 ? PyUnicodeCase.IsTitleCategory((char)cp) : CategoryOf(cp) == UnicodeCategory.TitlecaseLetter; }

        // Port of unicode_islower/isupper: needs >=1 cased letter, no letter of the opposite case.
        // Uppercase/Lowercase are the DERIVED properties CPython uses, so the Other_Lowercase characters
        // (modifier letters, circled forms) answer as they do there rather than by general category.
        private static bool IsLowerUpper(string s, bool upper)
        {
            bool cased = false;
            for (int i = 0; i < s.Length; i++)
            {
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

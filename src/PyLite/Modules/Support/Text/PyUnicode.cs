using System.Globalization;

namespace Outbridge.PyLite.Modules.Support
{
    // CPython Unicode predicate tables (3.4-era source, version-stable). Deliberately NOT char.IsWhiteSpace /
    // char.IsDigit — those diverge from CPython's sets. Everything works by UTF-16 code unit.
    internal static class PyUnicode
    {
        // The exact CPython "space_white" property. Differs from char.IsWhiteSpace: we INCLUDE U+001C..U+001F
        // and we do NOT include U+180E.
        internal static bool IsPythonSpace(char c)
        {
            int u = c;
            if (u >= 0x09 && u <= 0x0D) return true;   // \t \n \v \f \r
            if (u >= 0x1C && u <= 0x1F) return true;   // FS GS RS US
            if (u >= 0x2000 && u <= 0x200A) return true;
            switch (u)
            {
                case 0x20: case 0x85: case 0xA0: case 0x1680:
                case 0x2028: case 0x2029: case 0x202F: case 0x205F: case 0x3000:
                    return true;
                default:
                    return false;
            }
        }

        // The splitlines/line-boundary set (Py_UNICODE_ISLINEBREAK). Note \x1c..\x1e ARE line breaks
        // here even though they are also spaces; \x1f is a space but NOT a line break.
        internal static bool IsLineBreak(char c)
        {
            int u = c;
            if (u >= 0x0A && u <= 0x0D) return true;   // LF VT FF CR
            if (u >= 0x1C && u <= 0x1E) return true;   // FS GS RS
            return u == 0x85 || u == 0x2028 || u == 0x2029;   // NEL LS PS
        }

        // Cased = category Lu/Ll/Lt.
        internal static bool IsCased(char c)
        {
            UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
            return cat == UnicodeCategory.UppercaseLetter
                || cat == UnicodeCategory.LowercaseLetter
                || cat == UnicodeCategory.TitlecaseLetter;
        }

        internal static int DecimalDigitValue(char c) { return CharUnicodeInfo.GetDecimalDigitValue(c); }
        internal static int DigitValue(char c) { return CharUnicodeInfo.GetDigitValue(c); }
        internal static double NumericValue(char c) { return CharUnicodeInfo.GetNumericValue(c); }
        internal static UnicodeCategory Category(char c) { return CharUnicodeInfo.GetUnicodeCategory(c); }
    }
}

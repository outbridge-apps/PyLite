using System.Text.RegularExpressions;

namespace Outbridge.PyLite.Modules.Support
{
    // re flag bitmask values (exactly CPython sre) + the projection to RegexOptions.
    internal static class ReFlags
    {
        public const int A = 256;   // ASCII       (applied by class substitution in RegexTranslator)
        public const int I = 2;     // IGNORECASE
        public const int L = 4;     // LOCALE      (bytes-only: re.error)
        public const int M = 8;     // MULTILINE
        public const int S = 16;    // DOTALL
        public const int U = 32;    // UNICODE     (no-op, the Py3 default)
        public const int X = 64;    // VERBOSE

        public const int KnownMask = A | I | L | M | S | U | X;

        // CultureInvariant is ALWAYS set; Compiled is NEVER set. L is surfaced to the
        // caller, which raises re.error; A has no RegexOptions counterpart — the translator rewrites the
        // \w/\d/\s/\b classes instead (asciiRequested reports it).
        public static RegexOptions ToRegexOptions(int flags, out bool localeError, out bool asciiRequested)
        {
            localeError = (flags & L) != 0;
            asciiRequested = (flags & A) != 0;
            RegexOptions o = RegexOptions.CultureInvariant;
            if ((flags & I) != 0) o |= RegexOptions.IgnoreCase;
            if ((flags & M) != 0) o |= RegexOptions.Multiline;
            if ((flags & S) != 0) o |= RegexOptions.Singleline;
            if ((flags & X) != 0) o |= RegexOptions.IgnorePatternWhitespace;
            return o;
        }
    }
}

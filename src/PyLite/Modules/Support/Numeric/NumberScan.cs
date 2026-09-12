namespace Outbridge.PyLite.Modules.Support
{
    // The scanner the Decimal and Fraction literal grammars share: an ASCII-whitespace trim and a cursor
    // that reads a sign, a run of ASCII digits, and an exponent. Each parser keeps its own grammar on
    // top (Fraction's "n/d", Decimal's specials) and its own error.
    internal struct NumberScan
    {
        private readonly string _s;
        private int _i;

        internal enum Exp { Absent, Read, Malformed }

        internal NumberScan(string original)
        {
            _s = Trim(original);
            _i = 0;
        }

        internal string Text { get { return _s; } }
        internal int Pos { get { return _i; } }
        internal bool AtEnd { get { return _i >= _s.Length; } }
        internal char Cur { get { return _i < _s.Length ? _s[_i] : '\0'; } }

        // An optional '+' or '-'; true when it was '-'.
        internal bool Sign()
        {
            if (AtEnd || (_s[_i] != '+' && _s[_i] != '-'))
                return false;
            bool neg = _s[_i] == '-';
            _i++;
            return neg;
        }

        internal bool Accept(char c)
        {
            if (AtEnd || _s[_i] != c)
                return false;
            _i++;
            return true;
        }

        // The run of ASCII digits at the cursor, possibly empty.
        internal string Digits()
        {
            int start = _i;
            while (_i < _s.Length && IsDigit(_s[_i]))
                _i++;
            return _s.Substring(start, _i - start);
        }

        // [eE][+-]digits: Absent when there is no 'e' (the cursor stays), Malformed when the digits are
        // missing or do not fit an int.
        internal Exp Exponent(out int exp)
        {
            exp = 0;
            if (AtEnd || (_s[_i] != 'e' && _s[_i] != 'E'))
                return Exp.Absent;
            _i++;
            bool neg = Sign();
            string digits = Digits();
            int e;
            if (digits.Length == 0 || !int.TryParse(digits, out e))
                return Exp.Malformed;
            exp = neg ? -e : e;
            return Exp.Read;
        }

        internal static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        private static string Trim(string s)
        {
            int a = 0;
            int b = s.Length;
            while (a < b && IsSpace(s[a]))
                a++;
            while (b > a && IsSpace(s[b - 1]))
                b--;
            return s.Substring(a, b - a);
        }

        private static bool IsSpace(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v';
        }
    }
}

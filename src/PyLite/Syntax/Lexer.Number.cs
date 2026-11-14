using System.Globalization;
using System.Numerics;

namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class Lexer
    {
        private Token ScanNumber()
        {
            int start = _pos, line0 = _line, col0 = CurCol;

            if (Cur == '.')
                return ScanFloatTail(start, line0, col0);

            if (Cur == '0')
            {
                _pos++;
                char c = Cur;
                if (c == 'x' || c == 'X')
                    return ScanRadix(start, line0, col0, 16);
                if (c == 'o' || c == 'O')
                    return ScanRadix(start, line0, col0, 8);
                if (c == 'b' || c == 'B')
                    return ScanRadix(start, line0, col0, 2);
                bool sawNonZero = false;
                while (true)
                {
                    if (IsAsciiDigit(Cur))
                    {
                        if (Cur != '0')
                            sawNonZero = true;
                        _pos++;
                        continue;
                    }
                    if (Cur == '_' && IsAsciiDigit(At(_pos + 1)))
                    {
                        _pos++;
                        continue;
                    }
                    break;
                }

                if (Cur == '.' || ExponentFollows(line0, col0))
                    return ScanFloatTail(start, line0, col0);
                CheckIllegalSuffix(line0, col0, "decimal");
                if (sawNonZero)
                    throw PySyntaxErrorException.Syntax("leading zeros in decimal integer literals are not permitted; use an 0o prefix for octal integers", line0, col0);
                return EmitInt(start, line0, col0, BigInteger.Zero);
            }

            ConsumeAsciiDigits();
            if (Cur == '.' || ExponentFollows(line0, col0))
                return ScanFloatTail(start, line0, col0);
            CheckIllegalSuffix(line0, col0, "decimal");
            int digits = _pos - start;
            if (digits > _limits.MaxNumericLiteralDigits)
                throw PySyntaxErrorException.Syntax("integer literal too long (limit is " + Num(_limits.MaxNumericLiteralDigits) + " digits)", line0, col0);
            return EmitInt(start, line0, col0, ParseDecimal(_src, start, _pos));
        }

        private Token ScanRadix(int start, int line0, int col0, int radix)
        {
            _pos++; // consume x/o/b
            int digitsStart = _pos;
            int n = 0;
            while (true)
            {
                if (IsRadixDigit(Cur, radix))
                {
                    _pos++;
                    n++;
                    continue;
                }
                if (Cur == '_' && IsRadixDigit(At(_pos + 1), radix))   // '_' between radix digits / after prefix
                {
                    _pos++;
                    continue;
                }
                break;
            }
            string kind = radix == 16 ? "hexadecimal" : radix == 8 ? "octal" : "binary";
            if (IsAsciiDigit(Cur))   // only for octal and binary: every ASCII digit is a hex one
                throw PySyntaxErrorException.Syntax("invalid digit '" + Cur + "' in " + kind + " literal", _line, CurCol);
            if (n == 0)
                throw PySyntaxErrorException.Syntax("invalid " + kind + " literal", _line, Cur == '_' ? CurCol : CurCol - 1);
            CheckIllegalSuffix(line0, col0, kind);
            string digits = _src.Substring(digitsStart, _pos - digitsStart).Replace("_", "");
            BigInteger v = radix == 16
                ? BigInteger.Parse("0" + digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : ParseRadix(digits, radix);
            return EmitInt(start, line0, col0, v);
        }

        // 18 digits at a time: BigInteger.Parse took 7.6 MB for a 4,300-digit literal, on the host's thread while
        // compiling and outside any budget (115 of them: 0.86 GB).
        internal static BigInteger ParseDecimal(string src, int start, int end)
        {
            BigInteger acc = BigInteger.Zero;
            bool big = false;
            long chunk = 0, mul = 1;
            for (int i = start; i < end; i++)
            {
                char c = src[i];
                if (c == '_')
                    continue;
                chunk = chunk * 10 + (c - '0');
                mul *= 10;
                if (mul == 1000000000000000000L)
                {
                    acc = big ? acc * mul + chunk : chunk;
                    big = true;
                    chunk = 0;
                    mul = 1;
                }
            }
            if (mul > 1)
                acc = big ? acc * mul + chunk : chunk;
            return acc;
        }

        // Octal and binary, linear: the digits' bits packed from the least significant end (a literal has no
        // digit limit in these bases, as in CPython, so a shift-and-or loop would be quadratic in its length).
        private static BigInteger ParseRadix(string digits, int radix)
        {
            int log2 = radix == 8 ? 3 : 1;
            var bytes = new byte[(digits.Length * log2 + 7) / 8 + 1];   // the last byte stays 0: the sign
            int k = 0, bits = 0;
            uint buf = 0;
            for (int i = digits.Length - 1; i >= 0; i--)
            {
                buf |= (uint)(digits[i] - '0') << bits;
                bits += log2;
                if (bits >= 8)
                {
                    bytes[k++] = (byte)buf;
                    buf >>= 8;
                    bits -= 8;
                }
            }
            if (bits > 0)
                bytes[k] = (byte)buf;
            return new BigInteger(bytes);
        }

        // true if a legal exponent follows. An 'e' with no digits is CPython's error at the last character
        // it read, unless it begins 'else' (1else is a number and a keyword, with CPython's warning).
        private bool ExponentFollows(int line0, int col0)
        {
            if (Cur != 'e' && Cur != 'E')
                return false;
            int k = _pos + 1;
            if (At(k) == '+' || At(k) == '-')
            {
                if (!IsAsciiDigit(At(k + 1)))
                    throw PySyntaxErrorException.Syntax("invalid decimal literal", _line, CurCol + 1);
                return true;
            }
            if (IsAsciiDigit(At(k)))
                return true;
            if (KeywordFollows())
                return false;
            throw PySyntaxErrorException.Syntax("invalid decimal literal", _line, CurCol - 1);
        }

        // verify_end_of_number's keywords: a literal may run straight into and/else/for/if/in/is/not/or.
        private bool KeywordFollows()
        {
            switch (Cur)
            {
                case 'a': return At(_pos + 1) == 'n' && At(_pos + 2) == 'd';
                case 'e': return At(_pos + 1) == 'l' && At(_pos + 2) == 's' && At(_pos + 3) == 'e';
                case 'f': return At(_pos + 1) == 'o' && At(_pos + 2) == 'r';
                case 'i': return At(_pos + 1) == 'f' || At(_pos + 1) == 'n' || At(_pos + 1) == 's';
                case 'n': return At(_pos + 1) == 'o' && At(_pos + 2) == 't';
                case 'o': return At(_pos + 1) == 'r';
                default: return false;
            }
        }

        private Token ScanFloatTail(int start, int line0, int col0)
        {
            if (Cur == '.')
            {
                _pos++;
                if (Cur == '_')   // PEP 515: no '_' next to the point (1._0, 1.__class__); CPython reports the point
                    throw PySyntaxErrorException.Syntax("invalid decimal literal", _line, CurCol - 1);
                ConsumeAsciiDigits();
            }
            if (ExponentFollows(line0, col0))
            {
                _pos++;
                if (Cur == '+' || Cur == '-')
                    _pos++;
                ConsumeAsciiDigits();
            }
            CheckIllegalSuffix(line0, col0, "decimal");
            if (_pos - start > _limits.MaxNumericLiteralDigits)
                throw PySyntaxErrorException.Syntax("integer literal too long (limit is " + Num(_limits.MaxNumericLiteralDigits) + " digits)", line0, col0);
            string text = _src.Substring(start, _pos - start);
            double v;
            // the scan above admitted only the literal grammar; past the float range the literal is inf
            Outbridge.PyLite.Modules.Support.NearestDouble.TryParse(text.Replace("_", ""), out v);
            return Emit(TokenKind.FloatLiteral, text, v, line0, col0);
        }

        // PEP 515: '_' is legal only between digits; a dangling one (trailing/doubled/adjacent to . or e)
        // is consumed by neither digit run and lands here.
        private void ConsumeAsciiDigits()
        {
            while (true)
            {
                if (IsAsciiDigit(Cur))
                {
                    _pos++;
                    continue;
                }
                if (Cur == '_' && IsAsciiDigit(At(_pos + 1)))
                {
                    _pos++;
                    continue;
                }
                break;
            }
        }

        // CPython's verify_end_of_number: a dangling '_' is reported where it stands, an identifier character
        // straight after the literal at the literal's last character (a keyword may follow).
        private void CheckIllegalSuffix(int line0, int col0, string kind)
        {
            if (Cur == '_')
                throw PySyntaxErrorException.Syntax("invalid " + kind + " literal", _line, CurCol);
            if (Cur == 'j' || Cur == 'J')
                throw PySyntaxErrorException.Syntax("complex literals are not supported in this dialect", line0, col0);
            char c = Cur;
            bool identChar = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
            if (identChar && !KeywordFollows())
                throw PySyntaxErrorException.Syntax("invalid " + kind + " literal", _line, CurCol - 1);
        }

        private Token EmitInt(int start, int line0, int col0, BigInteger value)
        {
            return Emit(TokenKind.IntLiteral, _src.Substring(start, _pos - start), value, line0, col0);
        }

        private static bool IsRadixDigit(char c, int radix)
        {
            if (radix == 16)
                return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (radix == 8)
                return c >= '0' && c <= '7';
            return c == '0' || c == '1';
        }
    }
}

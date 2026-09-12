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
                CheckIllegalSuffix(line0, col0);
                if (sawNonZero)
                    throw PySyntaxErrorException.Syntax("invalid token", line0, col0);
                return EmitInt(start, line0, col0, BigInteger.Zero);
            }

            ConsumeAsciiDigits();
            if (Cur == '.' || ExponentFollows(line0, col0))
                return ScanFloatTail(start, line0, col0);
            CheckIllegalSuffix(line0, col0);
            int digits = _pos - start;
            if (digits > _limits.MaxNumericLiteralDigits)
                throw PySyntaxErrorException.Syntax("integer literal too long (limit is " + Num(_limits.MaxNumericLiteralDigits) + " digits)", line0, col0);
            return EmitInt(start, line0, col0, BigInteger.Parse(_src.Substring(start, digits).Replace("_", ""), CultureInfo.InvariantCulture));
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
            if (n == 0)
                throw PySyntaxErrorException.Syntax("invalid token", line0, col0);
            if (IsAsciiDigit(Cur))
                throw PySyntaxErrorException.Syntax("invalid token", line0, col0);
            CheckIllegalSuffix(line0, col0);
            if (n > _limits.MaxNumericLiteralDigits)
                throw PySyntaxErrorException.Syntax("integer literal too long (limit is " + Num(_limits.MaxNumericLiteralDigits) + " digits)", line0, col0);
            string digits = _src.Substring(digitsStart, _pos - digitsStart).Replace("_", "");
            BigInteger v = radix == 16
                ? BigInteger.Parse("0" + digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : ParseRadix(digits, radix);
            return EmitInt(start, line0, col0, v);
        }

        private static BigInteger ParseRadix(string digits, int radix)
        {
            int per = radix == 8 ? 21 : 63;
            int log2 = radix == 8 ? 3 : 1;
            BigInteger acc = BigInteger.Zero;
            int i = 0, len = digits.Length;
            while (i < len)
            {
                int take = len - i < per ? len - i : per;
                ulong chunk = 0;
                for (int j = 0; j < take; j++)
                    chunk = chunk * (ulong)radix + (ulong)(digits[i + j] - '0');
                acc = (acc << (take * log2)) | chunk;
                i += take;
            }
            return acc;
        }

        // true if a legal exponent follows; throws "invalid token" on 'e' with no exponent digits.
        private bool ExponentFollows(int line0, int col0)
        {
            if (Cur != 'e' && Cur != 'E')
                return false;
            int k = _pos + 1;
            if (At(k) == '+' || At(k) == '-')
                k++;
            if (!IsAsciiDigit(At(k)))
                throw PySyntaxErrorException.Syntax("invalid token", line0, col0);
            return true;
        }

        private Token ScanFloatTail(int start, int line0, int col0)
        {
            if (Cur == '.')
            {
                _pos++;
                ConsumeAsciiDigits();
            }
            if (ExponentFollows(line0, col0))
            {
                _pos++;
                if (Cur == '+' || Cur == '-')
                    _pos++;
                ConsumeAsciiDigits();
            }
            CheckIllegalSuffix(line0, col0);
            if (_pos - start > _limits.MaxNumericLiteralDigits)
                throw PySyntaxErrorException.Syntax("integer literal too long (limit is " + Num(_limits.MaxNumericLiteralDigits) + " digits)", line0, col0);
            string text = _src.Substring(start, _pos - start);
            double v;
            try
            {
                v = double.Parse(Canonicalize(text.Replace("_", "")), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            catch (System.OverflowException)
            {
                v = double.PositiveInfinity;
            }
            return Emit(TokenKind.FloatLiteral, text, v, line0, col0);
        }

        private static string Canonicalize(string text)
        {
            int dot = text.IndexOf('.');
            if (dot < 0)
                return text;
            int after = dot + 1;
            if (after >= text.Length || text[after] == 'e' || text[after] == 'E')
                return text.Insert(after, "0");
            return text;
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

        private void CheckIllegalSuffix(int line0, int col0)
        {
            if (Cur == '_')
                throw PySyntaxErrorException.Syntax("invalid use of '_' in numeric literal", line0, col0);
            if (Cur == 'j' || Cur == 'J')
                throw PySyntaxErrorException.Syntax("complex literals are not supported in this dialect", line0, col0);
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

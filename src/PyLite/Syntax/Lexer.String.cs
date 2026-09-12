namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class Lexer
    {
        private Token ScanString(bool raw, int line0, int col0)
        {
            // PEP 701: a nested string inside a replacement field may reuse the outer f-string quote.
            char q = Cur;
            bool triple = At(_pos + 1) == q && At(_pos + 2) == q;
            _pos += triple ? 3 : 1;
            int contentStart = _pos;
            _sb.Clear();
            while (true)
            {
                char c = Cur;
                RequireLiteralChar(c, triple, line0, col0);
                int close = ClosingRun(c, q, triple);
                if (close > 0)
                {
                    _pos += close;
                    break;
                }

                if (c == '\\')
                {
                    if (InFStringExpr)
                        throw PySyntaxErrorException.Syntax("f-string expression part cannot include a backslash", _line, CurCol);
                    if (raw)
                        AppendRawEscape(triple, line0, col0);
                    else
                        CookEscape(triple, line0, col0, contentStart);
                    continue;
                }

                AppendLiteralChar(c, line0, col0);
            }
            return Emit(TokenKind.StringLiteral, _sb.ToString(), null, line0, col0);
        }

        // bytes literal b'...' / rb'...'. ASCII only (CPython parity, 3.14 included); cooked escapes include \xNN
        // and octal but NOT \u/\U (those stay literal); raw keeps backslashes.
        private Token ScanBytes(bool raw, int line0, int col0)
        {
            char q = Cur;
            bool triple = At(_pos + 1) == q && At(_pos + 2) == q;
            _pos += triple ? 3 : 1;
            var bytes = new System.Collections.Generic.List<byte>();
            while (true)
            {
                char c = Cur;
                RequireLiteralChar(c, triple, line0, col0);
                int close = ClosingRun(c, q, triple);
                if (close > 0)
                {
                    _pos += close;
                    break;
                }
                if (c == '\\' && !raw)
                {
                    CookByteEscape(bytes, triple, line0, col0);
                    continue;
                }
                if (c == '\n' || c == '\r')
                {
                    AddByte(bytes, (byte)'\n', line0, col0);   // a source newline is one LF, as in str (CRLF sources)
                    ConsumeNewline();
                    continue;
                }
                if (c > 0x7F)
                    throw PySyntaxErrorException.Syntax("bytes can only contain ASCII literal characters", line0, col0);
                AddByte(bytes, (byte)c, line0, col0);
                _pos++;
            }
            return Emit(TokenKind.BytesLiteral, null, bytes.ToArray(), line0, col0);
        }

        private void CookByteEscape(System.Collections.Generic.List<byte> bytes, bool triple, int line0, int col0)
        {
            _pos++;   // '\'
            char c = Cur;
            if (c == '\0')
                throw Unterminated(triple, line0, col0);
            if (c == '\n' || c == '\r')
            {
                ConsumeNewline();
                return;
            }
            switch (c)
            {
                case '\\': _pos++; AddByte(bytes, (byte)'\\', line0, col0); return;
                case '\'': _pos++; AddByte(bytes, (byte)'\'', line0, col0); return;
                case '"': _pos++; AddByte(bytes, (byte)'"', line0, col0); return;
                case 'a': _pos++; AddByte(bytes, 7, line0, col0); return;
                case 'b': _pos++; AddByte(bytes, 8, line0, col0); return;
                case 'f': _pos++; AddByte(bytes, 12, line0, col0); return;
                case 'n': _pos++; AddByte(bytes, (byte)'\n', line0, col0); return;
                case 'r': _pos++; AddByte(bytes, (byte)'\r', line0, col0); return;
                case 't': _pos++; AddByte(bytes, (byte)'\t', line0, col0); return;
                case 'v': _pos++; AddByte(bytes, 11, line0, col0); return;
                case 'x':
                    _pos++;
                    int hi = HexVal(Cur), lo = HexVal(At(_pos + 1));
                    if (hi < 0 || lo < 0)
                        throw PySyntaxErrorException.Syntax("invalid \\x escape at position " + _pos, line0, col0);
                    _pos += 2;
                    AddByte(bytes, (byte)(hi * 16 + lo), line0, col0);
                    return;
                default:
                    if (c >= '0' && c <= '7')
                    {
                        int v = 0, k = 0;
                        while (k < 3 && Cur >= '0' && Cur <= '7')
                        {
                            v = v * 8 + (Cur - '0');
                            _pos++;
                            k++;
                        }
                        AddByte(bytes, (byte)(v & 0xFF), line0, col0);
                        return;
                    }
                    AddByte(bytes, (byte)'\\', line0, col0);   // unknown escape: keep backslash + char
                    AddByte(bytes, (byte)c, line0, col0);
                    _pos++;
                    return;
            }
        }

        private void AddByte(System.Collections.Generic.List<byte> bytes, byte b, int line0, int col0)
        {
            if (bytes.Count >= _limits.MaxStringLiteralChars)
                throw PySyntaxErrorException.Syntax("bytes literal too long (limit is " + Num(_limits.MaxStringLiteralChars) + " bytes)", line0, col0);
            bytes.Add(b);
        }

        // The two errors every literal scanner reports first: end of input, and end of line inside a
        // single-quoted literal.
        private void RequireLiteralChar(char c, bool triple, int line0, int col0)
        {
            if (c == '\0')
                throw Unterminated(triple, line0, col0);
            if (!triple && (c == '\n' || c == '\r'))
                throw PySyntaxErrorException.Syntax("EOL while scanning string literal", line0, col0);
        }

        // 1 or 3 when c closes the literal (the quote run to consume); 0 otherwise -- a lone quote inside a
        // triple-quoted literal is content and falls through to the plain-character append.
        private int ClosingRun(char c, char q, bool triple)
        {
            if (c != q)
                return 0;
            if (!triple)
                return 1;
            return At(_pos + 1) == q && At(_pos + 2) == q ? 3 : 0;
        }

        // A plain character of a str or f-string literal: a newline inside a triple-quoted literal is
        // normalised to '\n' and counted as a line, anything else is appended as is.
        private void AppendLiteralChar(char c, int line0, int col0)
        {
            if (c == '\n' || c == '\r')
            {
                AppendChar('\n', line0, col0);
                ConsumeNewline();
                return;
            }
            AppendChar(c, line0, col0);
            _pos++;
        }

        private void AppendRawEscape(bool triple, int line0, int col0)
        {
            char next = At(_pos + 1);
            if (next == '\0')
                throw Unterminated(triple, line0, col0);
            AppendChar('\\', line0, col0);
            if (next == '\n' || next == '\r')
            {
                AppendChar('\n', line0, col0);
                _pos++;
                ConsumeNewline();
            }
            else
            {
                AppendChar(next, line0, col0);
                _pos += 2;
            }
        }

        private void CookEscape(bool triple, int line0, int col0, int contentStart)
        {
            int bs = _pos;
            _pos++;
            char c = Cur;
            if (c == '\0')
                throw Unterminated(triple, line0, col0);
            if (c == '\n' || c == '\r')
            {
                ConsumeNewline();
                return;
            }
            switch (c)
            {
                case '\\': _pos++; AppendChar('\\', line0, col0); return;
                case '\'': _pos++; AppendChar('\'', line0, col0); return;
                case '"': _pos++; AppendChar('"', line0, col0); return;
                case 'a': _pos++; AppendChar('\a', line0, col0); return;
                case 'b': _pos++; AppendChar('\b', line0, col0); return;
                case 'f': _pos++; AppendChar('\f', line0, col0); return;
                case 'n': _pos++; AppendChar('\n', line0, col0); return;
                case 'r': _pos++; AppendChar('\r', line0, col0); return;
                case 't': _pos++; AppendChar('\t', line0, col0); return;
                case 'v': _pos++; AppendChar('\v', line0, col0); return;
                case 'x': _pos++; CookHex(2, "\\xXX", bs, contentStart, line0, col0); return;
                case 'u': _pos++; CookHex(4, "\\uXXXX", bs, contentStart, line0, col0); return;
                case 'U': _pos++; CookHexU(bs, contentStart, line0, col0); return;
                case 'N': _pos++; CookNamed(bs, contentStart, line0, col0); return;
                default:
                    if (c >= '0' && c <= '7')
                    {
                        CookOctal(line0, col0);
                        return;
                    }
                    AppendChar('\\', line0, col0); AppendChar(c, line0, col0); _pos++; return;
            }
        }

        private void CookOctal(int line0, int col0)
        {
            int v = 0, k = 0;
            while (k < 3 && Cur >= '0' && Cur <= '7')
            {
                v = v * 8 + (Cur - '0');
                _pos++;
                k++;
            }
            AppendChar((char)v, line0, col0);
        }

        // \N{NAME}: a named sequence resolves to more than one character, so the lookup answers with a
        // string. The braces are consumed here, which is also what keeps an f-string from reading the
        // '{' as the start of a replacement field.
        private void CookNamed(int bs, int contentStart, int line0, int col0)
        {
            if (Cur != '{')
                throw UnicodeError("malformed \\N character escape", bs, contentStart, line0, col0);
            int start = ++_pos;
            // A name holds nothing but ASCII letters, digits, spaces and hyphens. Stopping at a quote
            // or a newline puts an unterminated escape at the position CPython reports, which lexes
            // the whole literal before decoding it.
            while (Cur != '\0' && Cur != '}' && Cur != '\n' && Cur != '\r' && Cur != '\'' && Cur != '"')
                _pos++;
            if (Cur != '}' || _pos == start)
                throw UnicodeError("malformed \\N character escape", bs, contentStart, line0, col0);
            string name = _src.Substring(start, _pos - start);
            _pos++;
            string value;
            if (!PyUnicodeNames.TryLookup(name, out value))
                throw UnicodeError("unknown Unicode character name", bs, contentStart, line0, col0);
            foreach (char ch in value)
                AppendChar(ch, line0, col0);
        }

        private void CookHex(int count, string kind, int bs, int contentStart, int line0, int col0)
        {
            int v = 0;
            for (int k = 0; k < count; k++)
            {
                int d = HexVal(Cur);
                if (d < 0)
                    throw UnicodeError("truncated " + kind + " escape", bs, contentStart, line0, col0);
                v = v * 16 + d;
                _pos++;
            }
            AppendChar((char)v, line0, col0);
        }

        private void CookHexU(int bs, int contentStart, int line0, int col0)
        {
            int v = 0;
            for (int k = 0; k < 8; k++)
            {
                int d = HexVal(Cur);
                if (d < 0)
                    throw UnicodeError("truncated \\UXXXXXXXX escape", bs, contentStart, line0, col0);
                v = v * 16 + d;
                _pos++;
            }
            if (v > 0x10FFFF)
                throw UnicodeError("illegal Unicode character", bs, contentStart, line0, col0);
            if (v <= 0xFFFF)
                AppendChar((char)v, line0, col0);
            else
                foreach (char ch in char.ConvertFromUtf32(v))
                    AppendChar(ch, line0, col0);
        }

        private PySyntaxErrorException UnicodeError(string tail, int bs, int contentStart, int line0, int col0)
        {
            int i = bs - contentStart;
            int j = _pos - 1 - contentStart;
            return PySyntaxErrorException.Syntax(
                "(unicode error) 'unicodeescape' codec can't decode bytes in position " + i + "-" + j + ": " + tail,
                line0, col0);
        }

        private void AppendChar(char c, int line0, int col0)
        {
            if (_sb.Length >= _limits.MaxStringLiteralChars)
                throw PySyntaxErrorException.Syntax("string literal too long (limit is " + Num(_limits.MaxStringLiteralChars) + " characters)", line0, col0);
            _sb.Append(c);
        }

        private PySyntaxErrorException Unterminated(bool triple, int line0, int col0)
        {
            return triple
                ? PySyntaxErrorException.Syntax("EOF in multi-line string", line0, col0)
                : PySyntaxErrorException.Syntax("EOL while scanning string literal", line0, col0);
        }

        private static int HexVal(char c)
        {
            if (c >= '0' && c <= '9')
                return c - '0';
            if (c >= 'a' && c <= 'f')
                return c - 'a' + 10;
            if (c >= 'A' && c <= 'F')
                return c - 'A' + 10;
            return -1;
        }
    }
}

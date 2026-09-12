namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class Lexer
    {
        private enum FStringMode { Text, Expr, Spec, ExprNested }

        private struct FStringFrame
        {
            public char Quote;
            public bool Triple;
            public bool Raw;
            public FStringMode Mode;
            public int BraceDepth;
            public int ExprTokenCount;
            public int StartLine, StartCol;
            public int ContentStart;
            public int FieldExprStart;   // source index just after '{' (for the self-documenting '=' text)
        }

        private readonly FStringFrame[] _fstack;
        private int _fdepth;

        private bool InFString { get { return _fdepth > 0; } }

        private bool InFStringExpr
        {
            get
            {
                if (_fdepth == 0)
                    return false;
                var m = _fstack[_fdepth - 1].Mode;
                return m == FStringMode.Expr || m == FStringMode.ExprNested;
            }
        }

        private Token ScanFStringStart(bool raw, int start, int line0, int col0)
        {
            if (_fdepth == _limits.MaxFStringNesting)
                throw PySyntaxErrorException.Syntax("f-string: f-strings nested too deeply (limit is " + Num(_limits.MaxFStringNesting) + ")", line0, col0);
            if (InFStringExpr)
                _fstack[_fdepth - 1].ExprTokenCount++;
            char q = Cur;
            bool triple = At(_pos + 1) == q && At(_pos + 2) == q;
            _pos += triple ? 3 : 1;
            string lexeme = _src.Substring(start, _pos - start);
            _fstack[_fdepth] = new FStringFrame
            {
                Quote = q, Triple = triple, Raw = raw, Mode = FStringMode.Text,
                StartLine = line0, StartCol = col0, ContentStart = _pos
            };
            _fdepth++;
            return Emit(TokenKind.FStringStart, lexeme, null, line0, col0);
        }

        // The literal text of an f-string, or (spec) the text of a format spec after ':'. They differ in
        // what ends them -- the closing quote, or the field's '}' -- and in the doubled braces the text
        // reads as content; everything else, including escapes, is the same scan.
        private Token ScanFStringText(bool spec)
        {
            int ti = _fdepth - 1;
            char quote = _fstack[ti].Quote;
            bool triple = _fstack[ti].Triple, raw = _fstack[ti].Raw;
            int cs = _fstack[ti].ContentStart, sl = _fstack[ti].StartLine, sc = _fstack[ti].StartCol;
            int chunkLine = _line, chunkCol = CurCol;
            _sb.Clear();
            while (true)
            {
                char c = Cur;
                RequireLiteralChar(c, triple, sl, sc);

                int close = spec ? 0 : ClosingRun(c, quote, triple);
                if (close > 0)
                {
                    if (_sb.Length > 0)
                        return Emit(TokenKind.FStringMiddle, _sb.ToString(), null, chunkLine, chunkCol);
                    int bl = _line, bc = CurCol;
                    _pos += close;
                    _fdepth--;
                    return Emit(TokenKind.FStringEnd, new string(quote, close), null, bl, bc);
                }

                if (c == '{')
                {
                    if (!spec && At(_pos + 1) == '{')
                    {
                        AppendChar('{', sl, sc);
                        _pos += 2;
                        continue;
                    }

                    if (_sb.Length > 0)
                        return Emit(TokenKind.FStringMiddle, _sb.ToString(), null, chunkLine, chunkCol);
                    return OpenField(ti, spec ? FStringMode.ExprNested : FStringMode.Expr);
                }

                if (c == '}')
                {
                    if (!spec)
                    {
                        if (At(_pos + 1) == '}')
                        {
                            AppendChar('}', sl, sc);
                            _pos += 2;
                            continue;
                        }
                        throw PySyntaxErrorException.Syntax("f-string: single '}' is not allowed", _line, CurCol);
                    }

                    if (_sb.Length > 0)
                        return Emit(TokenKind.FStringMiddle, _sb.ToString(), null, chunkLine, chunkCol);
                    int bl = _line, bc = CurCol;
                    _pos++;
                    _fstack[ti].Mode = FStringMode.Text;
                    return Emit(TokenKind.RightBrace, null, null, bl, bc);
                }

                if (c == '\\')
                {
                    if (At(_pos + 1) == '{' || At(_pos + 1) == '}')
                    {
                        AppendChar('\\', sl, sc);
                        _pos++;
                        continue;
                    }

                    if (raw)
                        AppendRawEscape(triple, sl, sc);
                    else
                        CookEscape(triple, sl, sc, cs);
                    continue;
                }

                AppendLiteralChar(c, sl, sc);
            }
        }

        private Token OpenField(int ti, FStringMode mode)
        {
            int bl = _line, bc = CurCol;
            _pos++;
            _fstack[ti].Mode = mode;
            _fstack[ti].BraceDepth = 0;
            _fstack[ti].FieldExprStart = _pos;
            Token tok = Emit(TokenKind.LeftBrace, null, null, bl, bc);
            _fstack[ti].ExprTokenCount = 0;
            return tok;
        }

        // Frame-aware handling of { } ( ) [ ] : ! inside a field; null => not our token, use the normal switch.
        private Token? FStringExprHook()
        {
            int ti = _fdepth - 1;
            int line0 = _line, col0 = CurCol;
            switch (Cur)
            {
                case '{': _fstack[ti].BraceDepth++; _pos++; return Emit(TokenKind.LeftBrace, null, null, line0, col0);
                case '(': _fstack[ti].BraceDepth++; _pos++; return Emit(TokenKind.LeftParen, null, null, line0, col0);
                case '[': _fstack[ti].BraceDepth++; _pos++; return Emit(TokenKind.LeftBracket, null, null, line0, col0);
                case ')':
                    if (_fstack[ti].BraceDepth == 0)
                        throw PySyntaxErrorException.Syntax("f-string: unmatched ')'", line0, col0);
                    _fstack[ti].BraceDepth--; _pos++; return Emit(TokenKind.RightParen, null, null, line0, col0);
                case ']':
                    if (_fstack[ti].BraceDepth == 0)
                        throw PySyntaxErrorException.Syntax("f-string: unmatched ']'", line0, col0);
                    _fstack[ti].BraceDepth--; _pos++; return Emit(TokenKind.RightBracket, null, null, line0, col0);
                case '}':
                    if (_fstack[ti].BraceDepth > 0)
                    {
                        _fstack[ti].BraceDepth--;
                        _pos++;
                        return Emit(TokenKind.RightBrace, null, null, line0, col0);
                    }
                    if (_fstack[ti].ExprTokenCount == 0)
                        throw PySyntaxErrorException.Syntax("f-string: empty expression not allowed", line0, col0);
                    _fstack[ti].Mode = _fstack[ti].Mode == FStringMode.ExprNested ? FStringMode.Spec : FStringMode.Text;
                    _pos++; return Emit(TokenKind.RightBrace, null, null, line0, col0);
                case ':':
                    if (_fstack[ti].BraceDepth > 0)
                        return null;
                    if (_fstack[ti].ExprTokenCount == 0)
                        throw PySyntaxErrorException.Syntax("f-string: empty expression not allowed", line0, col0);
                    if (_fstack[ti].Mode == FStringMode.ExprNested)
                        throw PySyntaxErrorException.Syntax("f-string: expressions nested too deeply", line0, col0);
                    _fstack[ti].Mode = FStringMode.Spec; _pos++; return Emit(TokenKind.Colon, null, null, line0, col0);
                case '!':
                    if (At(_pos + 1) == '=' || _fstack[ti].BraceDepth > 0)
                        return null;
                    if (_fstack[ti].ExprTokenCount == 0)
                        throw PySyntaxErrorException.Syntax("f-string: empty expression not allowed", line0, col0);
                    _pos++; return Emit(TokenKind.Bang, null, null, line0, col0);
                case '=':
                    // Self-documenting '=' (PEP 501): only at top brace level and not part of '=='. The
                    // literal text is the field source through the '=' plus any trailing spaces (preserved).
                    if (_fstack[ti].BraceDepth > 0 || At(_pos + 1) == '=')
                        return null;
                    if (_fstack[ti].ExprTokenCount == 0)
                        throw PySyntaxErrorException.Syntax("f-string: valid expression required before '='", line0, col0);
                    int exprStart = _fstack[ti].FieldExprStart;
                    _pos++;   // consume '='
                    while (Cur == ' ' || Cur == '\t')
                        _pos++;
                    return Emit(TokenKind.FStringDebug, _src.Substring(exprStart, _pos - exprStart), null, line0, col0);
                default:
                    return null;
            }
        }
    }
}

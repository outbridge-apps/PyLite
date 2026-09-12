using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Outbridge.PyLite.Syntax
{
    // Documented differences from CPython (3.14 reference; feed the migration guide)
    // f-strings as such; async/await reserved; no NFKC on identifiers; stricter tab rules
    // (only spaces OR only tabs per block); special texts for _/j suffixes and bytes literals;
    // escapes in f-strings never produce field delimiters; triple-string newlines normalized to \n.
    internal sealed partial class Lexer
    {
        private readonly string _src;
        private readonly int _len;
        private readonly SyntaxLimits _limits;
        private int _pos;
        private int _line;
        private int _lineStart;
        private int _parenDepth;
        private bool _atLineStart;
        private bool _lineHasTokens;
        private long _tokenCount;
        private readonly Queue<Token> _pending = new Queue<Token>();
        private readonly StringBuilder _sb = new StringBuilder();
        private bool _eofEmitted;
        private Token _eofToken;

        private struct IndentEntry { public int Offset; public int Length; }
        private readonly IndentEntry[] _indentStack;
        private int _indentTop;

        public Lexer(string source, SyntaxLimits limits)
        {
            _limits = limits;
            if (source.Length > limits.MaxSourceChars)
                throw PySyntaxErrorException.Syntax("source too large (limit is " + Num(limits.MaxSourceChars) + " characters)", 1, 1);
            int nul = source.IndexOf('\0');
            if (nul >= 0)
            {
                int line = 1, col = 1;
                for (int i = 0; i < nul; i++)
                {
                    if (source[i] == '\n')
                    {
                        line++;
                        col = 1;
                    }
                    else
                        col++;
                }

                throw PySyntaxErrorException.Syntax("source code string cannot contain null bytes", line, col);
            }
            _src = source;
            _len = source.Length;
            _pos = (_len > 0 && source[0] == '﻿') ? 1 : 0;
            _line = 1;
            _lineStart = _pos;
            _atLineStart = true;
            _indentStack = new IndentEntry[limits.MaxIndentLevels + 1];
            _fstack = new FStringFrame[limits.MaxFStringNesting];
        }

        public static List<Token> Tokenize(string source, SyntaxLimits limits)
        {
            var lex = new Lexer(source, limits);
            var list = new List<Token>();
            Token t;
            do
            {
                t = lex.Next();
                list.Add(t);
            }
            while (t.Kind != TokenKind.EndOfFile);
            return list;
        }

        private char At(int i) { return i < _len ? _src[i] : '\0'; }
        private char Cur { get { return At(_pos); } }
        private int CurCol { get { return _pos - _lineStart + 1; } }
        private static string Num(int n) { return n.ToString(CultureInfo.InvariantCulture); }

        private Token Emit(TokenKind kind, string text, object value, int line, int col)
        {
            _tokenCount++;
            if (_tokenCount > _limits.MaxTokens)
                throw PySyntaxErrorException.Syntax("too many tokens (limit is " + Num(_limits.MaxTokens) + ")", line, col);
            if (kind != TokenKind.NewLine && kind != TokenKind.Indent && kind != TokenKind.Dedent && kind != TokenKind.EndOfFile)
                _lineHasTokens = true;
            if (_fdepth > 0)
            {
                var m = _fstack[_fdepth - 1].Mode;
                if (m == FStringMode.Expr || m == FStringMode.ExprNested)
                    _fstack[_fdepth - 1].ExprTokenCount++;
            }
            return new Token(kind, text, value, line, col);
        }

        private void NewPhysicalLine() { _line++; _lineStart = _pos; }

        private bool ConsumeNewline()
        {
            char c = Cur;
            if (c == '\n')
            {
                _pos++;
                NewPhysicalLine();
                return true;
            }
            if (c == '\r')
            {
                _pos++;
                if (Cur == '\n')
                    _pos++;
                NewPhysicalLine();
                return true;
            }
            return false;
        }

        public Token Next()
        {
            if (_pending.Count > 0)
                return _pending.Dequeue();
            if (_eofEmitted)
                return _eofToken;

            if (InFString)
            {
                var m = _fstack[_fdepth - 1].Mode;
                if (m == FStringMode.Text)
                    return ScanFStringText(false);
                if (m == FStringMode.Spec)
                    return ScanFStringText(true);
            }

            if (_atLineStart && _parenDepth == 0 && !InFString)
            {
                Token? r = HandleLineStart();
                if (r.HasValue)
                    return r.Value;
            }

            while (true)
            {
                char c = Cur;
                if (c == ' ' || c == '\t' || c == '\f')
                {
                    _pos++;
                    continue;
                }

                if (c == '#')
                {
                    if (InFStringExpr)
                        throw PySyntaxErrorException.Syntax("f-string expression part cannot include '#'", _line, CurCol);
                    while (Cur != '\0' && Cur != '\n' && Cur != '\r')
                        _pos++;
                    continue;
                }

                if (c == '\\')
                {
                    if (InFStringExpr)
                        throw PySyntaxErrorException.Syntax("f-string expression part cannot include a backslash", _line, CurCol);
                    char n = At(_pos + 1);
                    if (n == '\n' || n == '\r')
                    {
                        _pos++;
                        ConsumeNewline();
                        continue;
                    }

                    if (n == '\0')
                        throw PySyntaxErrorException.Syntax("unexpected EOF while parsing", _line, CurCol);
                    throw PySyntaxErrorException.Syntax("unexpected character after line continuation character", _line, CurCol + 1);
                }

                if (c == '\n' || c == '\r')
                {
                    if (InFStringExpr)
                    {
                        if (_fstack[_fdepth - 1].Triple)
                        {
                            ConsumeNewline();
                            continue;
                        }

                        var f = _fstack[_fdepth - 1];
                        throw PySyntaxErrorException.Syntax("EOL while scanning string literal", f.StartLine, f.StartCol);
                    }

                    if (_parenDepth > 0)
                    {
                        ConsumeNewline();
                        continue;
                    }

                    if (_lineHasTokens)
                    {
                        int l0 = _line, c0 = CurCol;
                        ConsumeNewline();
                        _lineHasTokens = false;
                        _atLineStart = true;
                        return Emit(TokenKind.NewLine, null, null, l0, c0);
                    }

                    ConsumeNewline();
                    _atLineStart = true;
                    Token? r = HandleLineStart();
                    if (r.HasValue)
                        return r.Value;
                    continue;
                }

                break;
            }

            if (Cur == '\0')
            {
                if (InFString)
                {
                    var f = _fstack[_fdepth - 1];
                    throw f.Triple
                        ? PySyntaxErrorException.Syntax("EOF in multi-line string", f.StartLine, f.StartCol)
                        : PySyntaxErrorException.Syntax("EOL while scanning string literal", f.StartLine, f.StartCol);
                }

                return HandleEof();
            }

            // PEP 701: inside a replacement field a quote (even the outer one) starts a nested string
            // literal; ScanString reports its own EOL/EOF if that nested string is left unterminated.
            char ch = Cur;
            if (IsIdentStart(ch))
                return ScanNameOrPrefixedString();
            if (IsAsciiDigit(ch))
                return ScanNumber();
            if (ch == '.' && IsAsciiDigit(At(_pos + 1)))
                return ScanNumber();
            if (ch == '\'' || ch == '"')
                return ScanString(false, _line, CurCol);
            return ScanOperator();
        }

        private Token HandleEof()
        {
            int line = _line, col = CurCol;
            var tail = new List<Token>();
            if (_lineHasTokens && _parenDepth == 0)
                tail.Add(Emit(TokenKind.NewLine, null, null, line, col));
            while (_indentTop > 0)
            {
                _indentTop--;
                tail.Add(Emit(TokenKind.Dedent, null, null, line, 1));
            }
            _eofToken = Emit(TokenKind.EndOfFile, null, null, line, col);
            tail.Add(_eofToken);
            _eofEmitted = true;
            _lineHasTokens = false;
            for (int i = 1; i < tail.Count; i++)
                _pending.Enqueue(tail[i]);
            return tail[0];
        }

        private Token? HandleLineStart()
        {
            int start;
            while (true)
            {
                start = _pos;
                while (Cur == ' ' || Cur == '\t')
                    _pos++;
                if (Cur == '\f')
                {
                    _pos++;
                    continue;
                }

                if (Cur == '#')
                    while (Cur != '\0' && Cur != '\n' && Cur != '\r')
                        _pos++;
                if (Cur == '\n' || Cur == '\r')
                {
                    ConsumeNewline();
                    continue;
                }

                if (Cur == '\0')
                {
                    _atLineStart = false;
                    return HandleEof();
                }

                break;
            }
            _atLineStart = false;
            return CompareIndent(start, _pos - start);
        }

        private Token? CompareIndent(int offset, int length)
        {
            int line = _line;
            bool hasSpace = false, hasTab = false;
            for (int i = offset; i < offset + length; i++)
            {
                if (_src[i] == ' ')
                    hasSpace = true;
                else if (_src[i] == '\t')
                    hasTab = true;
            }
            if (hasSpace && hasTab)
                throw PySyntaxErrorException.Tab(line, 1);
            if (length > 0)
            {
                char kind = _src[offset];
                for (int t = 1; t <= _indentTop; t++)
                {
                    var e = _indentStack[t];
                    if (e.Length > 0 && _src[e.Offset] != kind)
                        throw PySyntaxErrorException.Tab(line, 1);
                }
            }

            int topLen = _indentStack[_indentTop].Length;
            if (length == topLen)
                return null;
            if (length > topLen)
            {
                if (_indentTop + 1 >= _limits.MaxIndentLevels)
                    throw PySyntaxErrorException.Indentation("too many levels of indentation", line, 1);
                _indentTop++;
                _indentStack[_indentTop] = new IndentEntry
                {
                    Offset = offset,
                    Length = length
                };
                return Emit(TokenKind.Indent, null, null, line, 1);
            }

            int count = 0;
            while (_indentTop > 0 && _indentStack[_indentTop].Length > length)
            {
                _indentTop--;
                count++;
            }
            if (_indentStack[_indentTop].Length != length)
                throw PySyntaxErrorException.Indentation("unindent does not match any outer indentation level", line, 1);
            Token first = Emit(TokenKind.Dedent, null, null, line, 1);
            for (int i = 1; i < count; i++)
                _pending.Enqueue(Emit(TokenKind.Dedent, null, null, line, 1));
            return first;
        }

        private Token ScanOperator()
        {
            if (InFStringExpr)
            {
                Token? hook = FStringExprHook();
                if (hook.HasValue)
                    return hook.Value;
            }
            int line0 = _line, col0 = CurCol;
            char c = Cur;
            char c1 = At(_pos + 1), c2 = At(_pos + 2);
            switch (c)
            {
                case '*': return c1 == '*' && c2 == '=' ? Op(3, TokenKind.DoubleStarAssign, line0, col0)
                                : c1 == '*' ? Op(2, TokenKind.DoubleStar, line0, col0)
                                : c1 == '=' ? Op(2, TokenKind.StarAssign, line0, col0) : Op(1, TokenKind.Star, line0, col0);
                case '/': return c1 == '/' && c2 == '=' ? Op(3, TokenKind.DoubleSlashAssign, line0, col0)
                                : c1 == '/' ? Op(2, TokenKind.DoubleSlash, line0, col0)
                                : c1 == '=' ? Op(2, TokenKind.SlashAssign, line0, col0) : Op(1, TokenKind.Slash, line0, col0);
                case '<': return c1 == '<' && c2 == '=' ? Op(3, TokenKind.LeftShiftAssign, line0, col0)
                                : c1 == '<' ? Op(2, TokenKind.LeftShift, line0, col0)
                                : c1 == '=' ? Op(2, TokenKind.LessEqual, line0, col0) : Op(1, TokenKind.Less, line0, col0);
                case '>': return c1 == '>' && c2 == '=' ? Op(3, TokenKind.RightShiftAssign, line0, col0)
                                : c1 == '>' ? Op(2, TokenKind.RightShift, line0, col0)
                                : c1 == '=' ? Op(2, TokenKind.GreaterEqual, line0, col0) : Op(1, TokenKind.Greater, line0, col0);
                case '=': return c1 == '=' ? Op(2, TokenKind.EqualEqual, line0, col0) : Op(1, TokenKind.Assign, line0, col0);
                case '!':
                    if (c1 == '=')
                        return Op(2, TokenKind.NotEqual, line0, col0);
                    throw PySyntaxErrorException.Syntax("invalid syntax", line0, col0);
                case '+': return c1 == '=' ? Op(2, TokenKind.PlusAssign, line0, col0) : Op(1, TokenKind.Plus, line0, col0);
                case '-': return c1 == '>' ? Op(2, TokenKind.Arrow, line0, col0)
                                : c1 == '=' ? Op(2, TokenKind.MinusAssign, line0, col0) : Op(1, TokenKind.Minus, line0, col0);
                case '%': return c1 == '=' ? Op(2, TokenKind.PercentAssign, line0, col0) : Op(1, TokenKind.Percent, line0, col0);
                case '&': return c1 == '=' ? Op(2, TokenKind.AmpersandAssign, line0, col0) : Op(1, TokenKind.Ampersand, line0, col0);
                case '|': return c1 == '=' ? Op(2, TokenKind.PipeAssign, line0, col0) : Op(1, TokenKind.Pipe, line0, col0);
                case '^': return c1 == '=' ? Op(2, TokenKind.CaretAssign, line0, col0) : Op(1, TokenKind.Caret, line0, col0);
                case '~': return Op(1, TokenKind.Tilde, line0, col0);
                case '@': return Op(1, TokenKind.At, line0, col0);
                case '(': _parenDepth++; return Op(1, TokenKind.LeftParen, line0, col0);
                case '[': _parenDepth++; return Op(1, TokenKind.LeftBracket, line0, col0);
                case '{': _parenDepth++; return Op(1, TokenKind.LeftBrace, line0, col0);
                case ')': if (_parenDepth > 0)
                    _parenDepth--; return Op(1, TokenKind.RightParen, line0, col0);
                case ']': if (_parenDepth > 0)
                    _parenDepth--; return Op(1, TokenKind.RightBracket, line0, col0);
                case '}': if (_parenDepth > 0)
                    _parenDepth--; return Op(1, TokenKind.RightBrace, line0, col0);
                case ',': return Op(1, TokenKind.Comma, line0, col0);
                case ':': return c1 == '=' ? Op(2, TokenKind.ColonEqual, line0, col0) : Op(1, TokenKind.Colon, line0, col0);
                case ';': return Op(1, TokenKind.Semicolon, line0, col0);
                case '.': return c1 == '.' && c2 == '.' ? Op(3, TokenKind.Ellipsis, line0, col0) : Op(1, TokenKind.Dot, line0, col0);
                default:
                    if (c >= 0x80)
                        throw PySyntaxErrorException.Syntax("invalid character in identifier", line0, col0);
                    throw PySyntaxErrorException.Syntax("invalid syntax", line0, col0);
            }
        }

        private Token Op(int width, TokenKind kind, int line0, int col0)
        {
            _pos += width;
            return Emit(kind, null, null, line0, col0);
        }

        private Token ScanNameOrPrefixedString()
        {
            int start = _pos, line0 = _line, col0 = CurCol;
            while (IsIdentCont(Cur))
                _pos++;
            string name = _src.Substring(start, _pos - start);
            if (Cur == '\'' || Cur == '"')
            {
                switch (ClassifyPrefix(name))
                {
                    case Prefix.Raw:
                        return ScanString(true, line0, col0);
                    case Prefix.Plain:
                        return ScanString(false, line0, col0);
                    case Prefix.FString:
                        return ScanFStringStart(false, start, line0, col0);
                    case Prefix.FStringRaw:
                        return ScanFStringStart(true, start, line0, col0);
                    case Prefix.Bytes:
                        return ScanBytes(false, line0, col0);
                    case Prefix.BytesRaw:
                        return ScanBytes(true, line0, col0);
                }
            // NotAPrefix: fall through to Name; the quote becomes a separate literal.
            }
            if (Keywords.TryGet(name, out var kw))
                return Emit(kw, null, null, line0, col0);
            return Emit(TokenKind.Name, name, null, line0, col0);
        }

        private enum Prefix { Raw, Plain, FString, FStringRaw, Bytes, BytesRaw, NotAPrefix }

        private static Prefix ClassifyPrefix(string name)
        {
            if (name.Length == 1)
            {
                char a = Lower(name[0]);
                if (a == 'r')
                    return Prefix.Raw;
                if (a == 'u')
                    return Prefix.Plain;
                if (a == 'f')
                    return Prefix.FString;
                if (a == 'b')
                    return Prefix.Bytes;
                return Prefix.NotAPrefix;
            }
            if (name.Length == 2)
            {
                char a = Lower(name[0]), b = Lower(name[1]);
                if ((a == 'r' && b == 'f') || (a == 'f' && b == 'r'))
                    return Prefix.FStringRaw;
                if ((a == 'r' && b == 'b') || (a == 'b' && b == 'r'))
                    return Prefix.BytesRaw;
            }
            return Prefix.NotAPrefix;
        }

        private static char Lower(char c) { return (c >= 'A' && c <= 'Z') ? (char)(c + 32) : c; }
        private static bool IsIdentStart(char c) { return c == '_' || char.IsLetter(c); }
        private static bool IsIdentCont(char c) { return c == '_' || char.IsLetterOrDigit(c); }
        private static bool IsAsciiDigit(char c) { return c >= '0' && c <= '9'; }
    }
}

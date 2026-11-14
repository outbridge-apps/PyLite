using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Outbridge.PyLite.Syntax
{
    // Documented differences from CPython (3.14 reference; feed the migration guide): async/await reserved;
    // no complex or t-string literals; escapes in f-strings never produce field delimiters; triple-string
    // newlines normalized to \n. Errors, their texts and positions are CPython's tokenizer's.
    internal sealed partial class Lexer
    {
        private readonly string _src;
        private readonly int _len;
        private readonly SyntaxLimits _limits;
        private int _pos;
        private int _line;
        private int _lineStart;
        private int _parenDepth;   // open brackets on _brackets, f-string field brackets included
        private bool _atLineStart;
        private bool _lineHasTokens;
        private long _tokenCount;
        internal long TokenCount { get { return _tokenCount; } }
        private readonly Queue<Token> _pending = new Queue<Token>();
        private readonly StringBuilder _sb = new StringBuilder();
        private bool _eofEmitted;
        private Token _eofToken;
        private int _lastTokenLine;
        private bool _draining;

        private struct Bracket { public char Ch; public int Line; public int Col; }
        private Bracket[] _brackets = new Bracket[16];

        // CPython's two widths: tabs to multiples of 8 (Col) and tabs as one column (AltCol); they must agree.
        private struct IndentEntry { public int Col; public int AltCol; }
        private readonly IndentEntry[] _indentStack;
        private int _indentTop;

        // a lexer error was thrown: the parser's error handling must not tokenize further
        public bool Failed { get; private set; }

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
                if (m == FStringMode.Expr)
                    _fstack[_fdepth - 1].ExprTokenCount++;
            }
            _lastTokenLine = line;
            return new Token(kind, text, value, line, col, _pos - _lineStart + 1);
        }

        private void OpenBracket(char ch, int line, int col)
        {
            if (_parenDepth == _brackets.Length)
                System.Array.Resize(ref _brackets, _parenDepth * 2);
            _brackets[_parenDepth++] = new Bracket { Ch = ch, Line = line, Col = col };
        }

        // CPython's closing-bracket errors, at the closer
        private void CloseBracket(char ch, int line, int col)
        {
            if (_parenDepth == 0)
                throw PySyntaxErrorException.Syntax("unmatched '" + ch + "'", line, col);
            Bracket open = _brackets[--_parenDepth];
            if ((open.Ch == '(' ? ')' : open.Ch == '[' ? ']' : '}') != ch)
                throw PySyntaxErrorException.Syntax("closing parenthesis '" + ch + "' does not match opening parenthesis '" + open.Ch + "'"
                    + (open.Line != line ? " on line " + Num(open.Line) : ""), line, col);
        }

        private PySyntaxErrorException NeverClosed()
        {
            Bracket open = _brackets[_parenDepth - 1];
            return PySyntaxErrorException.Syntax("'" + open.Ch + "' was never closed", open.Line, open.Col);
        }

        // Whether the end of the source was reached (its NEWLINE, DEDENTs and ENDMARKER are being handed out).
        public bool AtEnd { get { return _eofEmitted; } }

        // Where CPython puts an error at the end of the source: past the last character of its last line, the
        // final line break not counted ("if x:\n\n" is line 2, column 1).
        public void EndOfSource(out int line, out int col)
        {
            int end = _len;
            if (end > 0 && _src[end - 1] == '\n')
                end--;
            if (end > 0 && _src[end - 1] == '\r')
                end--;
            line = 1;
            int start = _len > 0 && _src[0] == (char)0xFEFF ? 1 : 0;   // a leading BOM is no column
            for (int i = start; i < end; i++)
            {
                char c = _src[i];
                if (c == '\n' || (c == '\r' && (i + 1 >= end || _src[i + 1] != '\n')))
                {
                    line++;
                    start = i + 1;
                }
            }
            col = end - start + 1;
        }

        // After a parser error CPython tokenizes the rest of the source: its first lexer error wins (unless it
        // falls inside an f-string), and so does a bracket left open on a line before the error's last token.
        public PySyntaxErrorException Drain(PySyntaxErrorException parserError)
        {
            int errLine = _lastTokenLine;
            _draining = true;
            try
            {
                while (NextToken().Kind != TokenKind.EndOfFile)
                {
                }
            }
            catch (PySyntaxErrorException e)
            {
                return _fdepth > 0 ? parserError : e;
            }
            if (_parenDepth > 0 && errLine > _brackets[_parenDepth - 1].Line)
                return NeverClosed();
            return parserError;
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
            try
            {
                return NextToken();
            }
            catch (PySyntaxErrorException)
            {
                Failed = true;
                throw;
            }
        }

        private Token NextToken()
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
                    while (Cur != '\0' && Cur != '\n' && Cur != '\r')
                        _pos++;
                    continue;
                }

                if (c == '\\')
                {
                    char n = At(_pos + 1);
                    if (n == '\n' || n == '\r')
                    {
                        int bl = _line, bc = CurCol;
                        _pos++;
                        ConsumeNewline();
                        if (Cur == '\0')
                            throw PySyntaxErrorException.Syntax("unexpected EOF while parsing", bl, bc + 1);
                        continue;
                    }

                    if (n == '\0')
                        throw PySyntaxErrorException.Syntax("unexpected EOF while parsing", _line, CurCol + 1);
                    throw PySyntaxErrorException.Syntax("unexpected character after line continuation character", _line, CurCol + 1);
                }

                if (c == '\n' || c == '\r')
                {
                    if (_parenDepth > 0)   // a replacement field is a bracket too (PEP 701)
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
                    if (InFStringExpr)
                        throw NeverClosed();
                    var f = _fstack[_fdepth - 1];
                    throw Unterminated(f.Triple, f.StartLine, f.StartCol, "f-string");
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
            if (_parenDepth > 0 && !_draining)
                throw NeverClosed();
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
            int col, altCol;
            while (true)
            {
                col = 0;
                altCol = 0;
                while (Cur == ' ' || Cur == '\t')
                {
                    if (Cur == ' ')
                    {
                        col++;
                        altCol++;
                    }
                    else
                    {
                        col = (col / 8 + 1) * 8;
                        altCol++;
                    }
                    _pos++;
                }
                if (Cur == '\f')
                {
                    _pos++;   // a form feed resets the indentation (CPython, for Emacs)
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
            return CompareIndent(col, altCol);
        }

        // CPython's indentation check; its errors point past the end of the line. An INDENT sits where the
        // statement starts.
        private Token? CompareIndent(int col, int altCol)
        {
            int line = _line;
            IndentEntry top = _indentStack[_indentTop];
            if (col == top.Col)
            {
                if (altCol != top.AltCol)
                    throw PySyntaxErrorException.Tab(line, 1);
                return null;
            }
            if (col > top.Col)
            {
                if (altCol <= top.AltCol)
                    throw PySyntaxErrorException.Tab(line, 1);
                if (_indentTop + 1 >= _limits.MaxIndentLevels)
                    throw PySyntaxErrorException.Indentation("too many levels of indentation", line, 1);
                _indentTop++;
                _indentStack[_indentTop] = new IndentEntry
                {
                    Col = col,
                    AltCol = altCol
                };
                return Emit(TokenKind.Indent, null, null, line, CurCol);
            }

            int count = 0;
            while (_indentTop > 0 && _indentStack[_indentTop].Col > col)
            {
                _indentTop--;
                count++;
            }
            if (_indentStack[_indentTop].Col != col)
                throw PySyntaxErrorException.Indentation("unindent does not match any outer indentation level", line, LineEndCol());
            if (_indentStack[_indentTop].AltCol != altCol)
                throw PySyntaxErrorException.Tab(line, 1);
            Token first = Emit(TokenKind.Dedent, null, null, line, 1);
            for (int i = 1; i < count; i++)
                _pending.Enqueue(Emit(TokenKind.Dedent, null, null, line, 1));
            return first;
        }

        private int LineEndCol()
        {
            int i = _pos;
            while (i < _len && _src[i] != '\n' && _src[i] != '\r')
                i++;
            return i - _lineStart + 1;
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
                case '(': OpenBracket(c, line0, col0); return Op(1, TokenKind.LeftParen, line0, col0);
                case '[': OpenBracket(c, line0, col0); return Op(1, TokenKind.LeftBracket, line0, col0);
                case '{': OpenBracket(c, line0, col0); return Op(1, TokenKind.LeftBrace, line0, col0);
                case ')': CloseBracket(c, line0, col0); return Op(1, TokenKind.RightParen, line0, col0);
                case ']': CloseBracket(c, line0, col0); return Op(1, TokenKind.RightBracket, line0, col0);
                case '}': CloseBracket(c, line0, col0); return Op(1, TokenKind.RightBrace, line0, col0);
                case ',': return Op(1, TokenKind.Comma, line0, col0);
                case ':': return c1 == '=' ? Op(2, TokenKind.ColonEqual, line0, col0) : Op(1, TokenKind.Colon, line0, col0);
                case ';': return Op(1, TokenKind.Semicolon, line0, col0);
                case '.': return c1 == '.' && c2 == '.' ? Op(3, TokenKind.Ellipsis, line0, col0) : Op(1, TokenKind.Dot, line0, col0);
                default:
                    if (c < ' ' || c == (char)0x7F)   // a control character: CPython names it
                        throw PySyntaxErrorException.Syntax("invalid non-printable character U+" + ((int)c).ToString("X4", CultureInfo.InvariantCulture), line0, col0);
                    throw PySyntaxErrorException.Syntax("invalid syntax", line0, col0);   // a non-ASCII char is a name's
            }
        }

        private Token Op(int width, TokenKind kind, int line0, int col0)
        {
            _pos += width;
            return Emit(kind, null, null, line0, col0);
        }

        // Token.Value of a Name spelled with non-ASCII letters: its text is the NFKC form, never a soft keyword.
        internal static readonly object NonAsciiSpelling = new object();

        private Token ScanNameOrPrefixedString()
        {
            int start = _pos, line0 = _line, col0 = CurCol;
            bool ascii = true;
            while (IsIdentCont(Cur))
            {
                ascii &= Cur < 0x80;
                _pos++;
            }
            string name = _src.Substring(start, _pos - start);
            if (!ascii)
            {
                VerifyIdentifier(name, line0, col0);   // no keyword or string prefix is non-ASCII
                // the parser's NFKC (PEP 3131): U+210C is H, U+FB01 x is fix. It names a variable: bold `match` is
                // no soft keyword, bold `if` binds "if", and the three constants cannot be names at all.
                string id = Modules.UnicodedataModule.NfkcIdentifier(name);
                if (id == "True" || id == "False" || id == "None")
                    throw PySyntaxErrorException.Syntax("identifier field can't represent '" + id + "' constant", line0, col0);
                return Emit(TokenKind.Name, id, NonAsciiSpelling, line0, col0);
            }
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
                CheckPrefixLetters(name, line0, col0);
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

        // CPython's maybe_raise_syntax_error_for_string_prefixes: distinct prefix letters before a quote that
        // do not combine. A t-string (PEP 750) is a valid prefix the dialect does not have.
        private static void CheckPrefixLetters(string name, int line, int col)
        {
            bool b = false, u = false, r = false, f = false, t = false;
            foreach (char ch in name)
            {
                switch (Lower(ch))
                {
                    case 'b': if (b) return; b = true; break;
                    case 'u': if (u) return; u = true; break;
                    case 'r': if (r) return; r = true; break;
                    case 'f': if (f) return; f = true; break;
                    case 't': if (t) return; t = true; break;
                    default: return;
                }
            }
            string bad = u && b ? "'u' and 'b'" : u && r ? "'u' and 'r'" : u && f ? "'u' and 'f'" : u && t ? "'u' and 't'"
                : b && f ? "'b' and 'f'" : b && t ? "'b' and 't'" : f && t ? "'f' and 't'" : null;
            if (bad != null)
                throw PySyntaxErrorException.Syntax(bad + " prefixes are incompatible", line, col);
            if (t)
                throw PySyntaxErrorException.Syntax("t-strings are not supported in this dialect", line, col);
        }

        private static char Lower(char c) { return (c >= 'A' && c <= 'Z') ? (char)(c + 32) : c; }
        // CPython's potential identifier: ASCII letters, digits and '_', and every non-ASCII char, which
        // VerifyIdentifier then holds to PEP 3131 (char.IsLetterOrDigit refused the middle dot and the combining
        // marks it takes, and an invalid char was "invalid character in identifier" at the name's start).
        private static bool IsIdentStart(char c) { return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || c >= 0x80; }
        private static bool IsIdentCont(char c) { return IsIdentStart(c) || (c >= '0' && c <= '9'); }

        // verify_identifier: XID_Start or '_' first, XID_Continue after, on the raw text; the first char that
        // is not is CPython's error, at its own column.
        private static void VerifyIdentifier(string name, int line, int col0)
        {
            int i = 0;
            while (i < name.Length)
            {
                int cp = name[i];
                int width = 1;
                if (char.IsHighSurrogate(name[i]) && i + 1 < name.Length && char.IsLowSurrogate(name[i + 1]))
                {
                    cp = char.ConvertToUtf32(name[i], name[i + 1]);
                    width = 2;
                }
                bool ok = i == 0 ? cp == '_' || Runtime.Values.PyUnicodeCase.IsXidStart(cp) : Runtime.Values.PyUnicodeCase.IsXidContinue(cp);
                if (!ok)
                {
                    string hex = cp.ToString("X4", CultureInfo.InvariantCulture);
                    throw PySyntaxErrorException.Syntax(Runtime.Values.StrMethods.IsPrintable(cp)
                        ? "invalid character '" + char.ConvertFromUtf32(cp) + "' (U+" + hex + ")"
                        : "invalid non-printable character U+" + hex, line, col0 + i);
                }
                i += width;
            }
        }
        private static bool IsAsciiDigit(char c) { return c >= '0' && c <= '9'; }
    }
}

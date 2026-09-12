using System.Collections.Generic;

namespace Outbridge.PyLite.Syntax
{
    internal enum TokenKind
    {
        // layout / auxiliary
        EndOfFile, NewLine, Indent, Dedent,
        // literals and names
        Name, IntLiteral, FloatLiteral, StringLiteral, BytesLiteral,
        FStringStart, FStringMiddle, FStringEnd,
        // v1-grammar keywords (30)
        KwFalse, KwNone, KwTrue, KwAnd, KwAs, KwAssert, KwBreak, KwContinue,
        KwDef, KwDel, KwElif, KwElse, KwExcept, KwFinally, KwFor, KwFrom,
        KwGlobal, KwIf, KwImport, KwIn, KwIs, KwLambda, KwNonlocal, KwNot,
        KwOr, KwPass, KwRaise, KwReturn, KwTry, KwWhile,
        // reserved-but-banned (5): the lexer emits them, the parser reports SyntaxError
        KwClass, KwYield, KwWith, KwAsync, KwAwait,
        // operators
        Plus, Minus, Star, DoubleStar, Slash, DoubleSlash, Percent, At,
        LeftShift, RightShift, Ampersand, Pipe, Caret, Tilde,
        Less, Greater, LessEqual, GreaterEqual, EqualEqual, NotEqual,
        // delimiters
        LeftParen, RightParen, LeftBracket, RightBracket, LeftBrace, RightBrace,
        Comma, Colon, Semicolon, Dot, Ellipsis, Arrow,
        // assignments
        Assign, ColonEqual, PlusAssign, MinusAssign, StarAssign, SlashAssign, DoubleSlashAssign,
        PercentAssign, DoubleStarAssign, AmpersandAssign, PipeAssign, CaretAssign,
        LeftShiftAssign, RightShiftAssign,
        // only inside an f-string expression (conversion; self-documenting '=' carries its literal text)
        Bang, FStringDebug
    }

    internal struct Token
    {
        public readonly TokenKind Kind;
        public readonly string Text;    // NAME/STRING/number lexeme; null for layout/operators/keywords
        public readonly object Value;   // IntLiteral: boxed BigInteger; FloatLiteral: boxed double; else null
        public readonly int Line;       // 1-based
        public readonly int Col;        // 1-based, UTF-16 units

        public Token(TokenKind kind, string text, object value, int line, int col)
        {
            Kind = kind;
            Text = text;
            Value = value;
            Line = line;
            Col = col;
        }

        public override string ToString() { return TokenDump.DumpLine(this); }
    }

    internal static class Keywords
    {
        // Immutable static table (allowed by project rules); Ordinal comparison.
        public static readonly Dictionary<string, TokenKind> Table = new Dictionary<string, TokenKind>(System.StringComparer.Ordinal)
        {
            { "False", TokenKind.KwFalse }, { "None", TokenKind.KwNone }, { "True", TokenKind.KwTrue },
            { "and", TokenKind.KwAnd }, { "as", TokenKind.KwAs }, { "assert", TokenKind.KwAssert },
            { "break", TokenKind.KwBreak }, { "class", TokenKind.KwClass }, { "continue", TokenKind.KwContinue },
            { "def", TokenKind.KwDef }, { "del", TokenKind.KwDel }, { "elif", TokenKind.KwElif },
            { "else", TokenKind.KwElse }, { "except", TokenKind.KwExcept }, { "finally", TokenKind.KwFinally },
            { "for", TokenKind.KwFor }, { "from", TokenKind.KwFrom }, { "global", TokenKind.KwGlobal },
            { "if", TokenKind.KwIf }, { "import", TokenKind.KwImport }, { "in", TokenKind.KwIn },
            { "is", TokenKind.KwIs }, { "lambda", TokenKind.KwLambda }, { "nonlocal", TokenKind.KwNonlocal },
            { "not", TokenKind.KwNot }, { "or", TokenKind.KwOr }, { "pass", TokenKind.KwPass },
            { "raise", TokenKind.KwRaise }, { "return", TokenKind.KwReturn }, { "try", TokenKind.KwTry },
            { "while", TokenKind.KwWhile }, { "with", TokenKind.KwWith }, { "yield", TokenKind.KwYield },
            { "async", TokenKind.KwAsync }, { "await", TokenKind.KwAwait },
        };

        public static bool TryGet(string name, out TokenKind kind)
        {
            return Table.TryGetValue(name, out kind);
        }
    }
}

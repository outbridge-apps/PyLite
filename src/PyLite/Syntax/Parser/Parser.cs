using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    // Handwritten recursive-descent (statements) + precedence-climbing (expressions). Built directly on
    // the lexer token stream. Depth checked BEFORE each recursive descent.
    internal sealed partial class Parser
    {
        private readonly Lexer _lexer;
        private readonly SyntaxLimits _limits;
        private Token _cur;
        private Token _peeked;
        private bool _hasPeeked;
        private int _exprDepth, _stmtDepth;
        private Dictionary<string, string> _classRenames;   // class-body constants -> their hidden variables
        private int _classSuspend;                         // > 0 inside a method body, which cannot see them
        private int _classConstSeq;
        private List<NameNode> _classReads;   // class-body reads that no earlier constant or field renamed
        private string _superSelf;   // first parameter of the method being parsed (null outside a method body)
        private List<FromImportNode> _futureImports;   // every from __future__ import, in source order
        private string _superCell;   // the enclosing class's hidden variable, allocated by its first super()
        private bool _inMethod;      // inside a method body, nested functions included: __class__ is the class
        private ExprNode _disj;      // the last disjunction parsed and the token after it (the comma hint)
        private Token _disjNext;
        private int _prevEndCol;     // where the last token taken ends
        private Token _prevTok;      // the last token taken
        private string _mangleClass; // the class whose body (methods included) is being parsed: private names
        private Dictionary<string, string> _mangled;   // that class's private names, each mangled once
        private long _mangledChars;  // the length of every name mangled in the module (MaxMangledChars)
        private int _funcDepth;      // function and lambda bodies being parsed (yield and await outside one)

        private Parser(Lexer lexer, SyntaxLimits limits)
        {
            _lexer = lexer;
            _limits = limits;
            _cur = lexer.Next();
        }

        public static ModuleNode ParseModule(Lexer lexer, SyntaxLimits limits)
        {
            var p = new Parser(lexer, limits);
            try
            {
                return p.ParseModuleInternal();
            }
            catch (PySyntaxErrorException e) when (!lexer.Failed && !IsUnexpectedIndent(e))   // pegen raises those before it reads on
            {
                PySyntaxErrorException replaced = lexer.Drain(e);
                if (replaced == e)
                    throw;
                throw replaced;
            }
        }

        private static bool IsUnexpectedIndent(PySyntaxErrorException e)
        {
            return e.PythonType == "IndentationError" && e.Message == "unexpected indent";
        }

        // ---- token stream (LL(2)) ----
        private Token Peek2()
        {
            if (!_hasPeeked)
            {
                _peeked = _lexer.Next();
                _hasPeeked = true;
            }
            return _peeked;
        }

        private Token Take()
        {
            Token t = _cur;
            _prevEndCol = t.EndCol;
            _prevTok = t;
            if (_hasPeeked)
            {
                _cur = _peeked;
                _hasPeeked = false;
            }
            else
                _cur = _lexer.Next();
            return t;
        }

        private bool Match(TokenKind k)
        {
            if (_cur.Kind == k)
            {
                Take();
                return true;
            }
            return false;
        }

        // CPython's forced token (&&): its own "expected X" message.
        private Token Expect(TokenKind k, string what)
        {
            if (_cur.Kind != k)
                throw Error("expected " + what);
            return Take();
        }

        private Token Expect(TokenKind k)
        {
            if (_cur.Kind != k)
                throw Unexpected(false);
            return Take();
        }

        // a closing bracket, where two expressions side by side are a missing comma
        private Token ExpectClose(TokenKind k)
        {
            if (_cur.Kind != k)
                throw Unexpected(true);
            return Take();
        }

        // CPython's generic error at the furthest token read; a disjunction followed by an expression is
        // "Perhaps you forgot a comma?" inside brackets (at the first), and a legacy print/exec is named.
        private PySyntaxErrorException Unexpected(bool inBrackets, bool namedExpr = true)
        {
            PySyntaxErrorException hint = SideBySide(inBrackets);
            if (hint != null)
                return hint;
            if (namedExpr && _disj != null && SameToken(_disjNext, _cur))
            {
                // CPython's invalid_named_expression, which needs the right operand too
                if ((_cur.Kind == TokenKind.ColonEqual || _cur.Kind == TokenKind.Assign) && !StartsExpression(Peek2().Kind))
                    return ErrorAt(_cur, "invalid syntax");
                if (_cur.Kind == TokenKind.ColonEqual && !(_disj is NameNode))
                    return ErrorAtSrc(_disj.Src, "cannot use assignment expressions with " + ExprName(_disj));
                if (_cur.Kind == TokenKind.Assign && _disj is NameNode)
                    return ErrorAtSrc(_disj.Src, "invalid syntax. Maybe you meant '==' or ':=' instead of '='?");
                if (_cur.Kind == TokenKind.Assign && IsComparable(_disj))
                    return ErrorAtSrc(_disj.Src, "cannot assign to " + ExprName(_disj) + " here. Maybe you meant '==' instead of '='?");
            }
            Token at = _hasPeeked ? _peeked : _cur;
            if (at.Kind == TokenKind.Indent)
                return PySyntaxErrorException.Indentation("unexpected indent", at.Line, at.Col - 1);   // CPython's column
            return ErrorAt(at, "invalid syntax");
        }

        // two expressions side by side: a legacy print/exec statement, or inside brackets a missing comma
        private PySyntaxErrorException SideBySide(bool inBrackets)
        {
            if (_disj == null || !SameToken(_disjNext, _cur) || !StartsExpression(_cur.Kind))
                return null;
            var name = _disj as NameNode;
            if (name != null && (name.Id == "print" || name.Id == "exec") && _cur.Kind != TokenKind.LeftParen)
                return ErrorAtSrc(name.Src, "Missing parentheses in call to '" + name.Id + "'. Did you mean " + name.Id + "(...)?");
            bool excluded = name != null && (_cur.Kind == TokenKind.StringLiteral || _cur.Kind == TokenKind.BytesLiteral
                || _cur.Kind == TokenKind.FStringStart || name.Id == "match" || name.Id == "case" || name.Id == "_" || name.Id == "type");
            return inBrackets && !excluded ? ErrorAtSrc(_disj.Src, "invalid syntax. Perhaps you forgot a comma?") : null;
        }

        private static bool SameToken(Token a, Token b) { return a.Line == b.Line && a.Col == b.Col && a.Kind == b.Kind; }

        // CPython's private name mangling: __x inside class C (and its methods) is _C__x; dunders and names
        // of a class spelled with underscores only are left alone
        private string Mangle(string name)
        {
            if (_mangleClass == null || name.Length < 3 || name[0] != '_' || name[1] != '_'
                || (name[name.Length - 1] == '_' && name[name.Length - 2] == '_'))
                return name;
            string mangled;
            if (_mangled != null && _mangled.TryGetValue(name, out mangled))
                return mangled;
            string cls = _mangleClass.TrimStart('_');
            if (cls.Length == 0)
                return name;
            _mangledChars += 1 + cls.Length + name.Length;
            if (_mangledChars > _limits.MaxMangledChars)
                throw Error("too many or too long private names in class bodies (limit is "
                    + _limits.MaxMangledChars.ToString(System.Globalization.CultureInfo.InvariantCulture) + " characters)");
            mangled = "_" + cls + name;
            (_mangled ?? (_mangled = new Dictionary<string, string>(StringComparer.Ordinal)))[name] = mangled;
            return mangled;
        }

        // StartsExpression plus unary +/-, which it leaves out for the comma hint.
        private static bool StartsOperand(TokenKind k)
        {
            return StartsExpression(k) || k == TokenKind.Minus || k == TokenKind.Plus;
        }

        private static bool StartsExpression(TokenKind k)
        {
            switch (k)
            {
                case TokenKind.Name:
                case TokenKind.IntLiteral:
                case TokenKind.FloatLiteral:
                case TokenKind.StringLiteral:
                case TokenKind.BytesLiteral:
                case TokenKind.FStringStart:
                case TokenKind.KwFalse:
                case TokenKind.KwNone:
                case TokenKind.KwTrue:
                case TokenKind.KwLambda:
                case TokenKind.KwNot:
                case TokenKind.KwAwait:
                case TokenKind.Tilde:
                case TokenKind.LeftParen:
                case TokenKind.LeftBracket:
                case TokenKind.LeftBrace:
                case TokenKind.Ellipsis:
                    return true;
                default:
                    return false;
            }
        }

        private PySyntaxErrorException Error(string msg) { return PySyntaxErrorException.Syntax(msg, _cur.Line, _cur.Col); }
        private PySyntaxErrorException ErrorAt(Token t, string msg) { return PySyntaxErrorException.Syntax(msg, t.Line, t.Col); }
        private static PySyntaxErrorException ErrorAtSrc(SourceInfo s, string msg) { return PySyntaxErrorException.Syntax(msg, s.Line, s.Col); }

        private SourceInfo CurSrc() { return new SourceInfo(_cur.Line, _cur.Col); }
        private static SourceInfo Src(Token t) { return new SourceInfo(t.Line, t.Col); }

        // --- depth management ----
        private void EnterExpr()
        {
            if (++_exprDepth > _limits.MaxExprDepth)
                throw Error("expression too deeply nested (limit " + _limits.MaxExprDepth + ")");
            if ((_exprDepth & 7) == 0)
                ProbeStack();
        }
        private void LeaveExpr() { _exprDepth--; }

        private void EnterStmt()
        {
            if (++_stmtDepth > _limits.MaxStmtDepth)
                throw Error("statement too deeply nested (limit " + _limits.MaxStmtDepth + ")");
            ProbeStack();
        }
        private void LeaveStmt() { _stmtDepth--; }

        // The precedence loops (ParseArith/Term/Shift/BitAnd/BitXor/BitOr) and the postfix-trailer loop grow
        // the AST iteratively without EnterExpr, so a flat chain of N operators/trailers builds a depth-N tree
        // while _exprDepth stays O(1). Depth (recorded exactly on every ExprNode) catches that here — including
        // composition, since a child's depth flows up into every node built on top of it.
        private ExprNode GuardDepth(ExprNode e)
        {
            if (e.Depth > _limits.MaxAstDepth)
                throw Error("expression too deeply nested (limit " + _limits.MaxAstDepth + ")");
            return e;
        }

        private void ProbeStack()
        {
            try
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();
            }
            catch (InsufficientExecutionStackException)
            {
                throw Error("input too complex to parse");
            }
        }

        // ---- BinOp / CompareOp / UnaryOp token maps ----
        private static BinOp BinOpOf(TokenKind k)
        {
            switch (k)
            {
                case TokenKind.Plus: return BinOp.Add;
                case TokenKind.Minus: return BinOp.Sub;
                case TokenKind.Star: return BinOp.Mul;
                case TokenKind.Slash: return BinOp.Div;
                case TokenKind.DoubleSlash: return BinOp.FloorDiv;
                case TokenKind.Percent: return BinOp.Mod;
                case TokenKind.DoubleStar: return BinOp.Pow;
                case TokenKind.LeftShift: return BinOp.LShift;
                case TokenKind.RightShift: return BinOp.RShift;
                case TokenKind.Ampersand: return BinOp.BitAnd;
                case TokenKind.Pipe: return BinOp.BitOr;
                default: return BinOp.BitXor;   // Caret
            }
        }
    }
}

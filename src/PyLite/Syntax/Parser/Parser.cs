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

        private Parser(Lexer lexer, SyntaxLimits limits)
        {
            _lexer = lexer;
            _limits = limits;
            _cur = lexer.Next();
        }

        public static ModuleNode ParseModule(Lexer lexer, SyntaxLimits limits)
        {
            var p = new Parser(lexer, limits);
            return p.ParseModuleInternal();
        }

        // ---- token stream (LL(2)) ----
        private Token Peek() { return _cur; }

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

        private Token Expect(TokenKind k, string what)
        {
            if (_cur.Kind != k)
                throw Error("expected " + what);
            return Take();
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

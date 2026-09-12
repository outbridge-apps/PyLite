using System.Collections.Generic;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class Parser
    {
        // test = or_test ['if' or_test 'else' test] | lambda   (lambda branch added with params phase)
        private ExprNode ParseTest()
        {
            EnterExpr();
            try
            {
                if (_cur.Kind == TokenKind.Name && Peek2().Kind == TokenKind.ColonEqual)
                {
                    Token nm = Take();
                    Take();   // ':='
                    ExprNode val = ParseTest();
                    return new NamedExprNode(Src(nm), new NameNode(Src(nm), nm.Text), val);
                }
                ExprNode lam = TryParseLambda();
                if (lam != null)
                    return lam;
                ExprNode body = ParseOrTest();
                if (_cur.Kind == TokenKind.KwIf)
                {
                    Take();
                    ExprNode test = ParseOrTest();
                    Expect(TokenKind.KwElse, "'else' in conditional expression");
                    ExprNode orelse = ParseTest();
                    return new IfExpNode(body.Src, body, test, orelse);
                }

                return body;
            }
            finally
            {
                LeaveExpr();
            }
        }

        private ExprNode ParseOrTest()
        {
            ExprNode left = ParseAndTest();
            if (_cur.Kind != TokenKind.KwOr)
                return left;
            var values = new List<ExprNode> { left };
            while (Match(TokenKind.KwOr))
                values.Add(ParseAndTest());
            return new BoolOpNode(left.Src, BoolOp.Or, AstList.Freeze(values));
        }

        private ExprNode ParseAndTest()
        {
            ExprNode left = ParseNotTest();
            if (_cur.Kind != TokenKind.KwAnd)
                return left;
            var values = new List<ExprNode> { left };
            while (Match(TokenKind.KwAnd))
                values.Add(ParseNotTest());
            return new BoolOpNode(left.Src, BoolOp.And, AstList.Freeze(values));
        }

        private ExprNode ParseNotTest()
        {
            if (_cur.Kind == TokenKind.KwNot && Peek2().Kind != TokenKind.KwIn)
            {
                EnterExpr();
                try
                {
                    Token t = Take();
                    ExprNode operand = ParseNotTest();
                    return new UnaryOpNode(Src(t), UnaryOp.Not, operand);
                }
                finally
                {
                    LeaveExpr();
                }
            }
            return ParseComparison();
        }

        private ExprNode ParseComparison()
        {
            ExprNode left = ParseBitOr();
            CompareOp op;
            if (!TryReadCompareOp(out op))
                return left;
            var ops = new List<CompareOp>();
            var comps = new List<ExprNode>();
            do
            {
                ops.Add(op);
                comps.Add(ParseBitOr());
            }
            while (TryReadCompareOp(out op));
            return new CompareChainNode(left.Src, left, AstList.Freeze(ops), AstList.Freeze(comps));
        }

        private bool TryReadCompareOp(out CompareOp op)
        {
            switch (_cur.Kind)
            {
                case TokenKind.Less: Take(); op = CompareOp.Lt; return true;
                case TokenKind.Greater: Take(); op = CompareOp.Gt; return true;
                case TokenKind.EqualEqual: Take(); op = CompareOp.Eq; return true;
                case TokenKind.GreaterEqual: Take(); op = CompareOp.Ge; return true;
                case TokenKind.LessEqual: Take(); op = CompareOp.Le; return true;
                case TokenKind.NotEqual: Take(); op = CompareOp.Ne; return true;
                case TokenKind.KwIn: Take(); op = CompareOp.In; return true;
                case TokenKind.KwIs:
                    Take();
                    if (_cur.Kind == TokenKind.KwNot)
                    {
                        Take();
                        op = CompareOp.IsNot;
                    }
                    else
                        op = CompareOp.Is;
                    return true;
                case TokenKind.KwNot:
                    if (Peek2().Kind == TokenKind.KwIn)
                    {
                        Take();
                        Take();
                        op = CompareOp.NotIn;
                        return true;
                    }
                    op = CompareOp.Eq; return false;   // lone 'not' is not a comparison operator
                default:
                    op = CompareOp.Eq; return false;
            }
        }

        private ExprNode ParseBitOr()
        {
            ExprNode l = ParseBitXor();
            while (_cur.Kind == TokenKind.Pipe)
            {
                Take();
                l = GuardDepth(new BinOpNode(l.Src, BinOp.BitOr, l, ParseBitXor()));
            }
            return l;
        }
        private ExprNode ParseBitXor()
        {
            ExprNode l = ParseBitAnd();
            while (_cur.Kind == TokenKind.Caret)
            {
                Take();
                l = GuardDepth(new BinOpNode(l.Src, BinOp.BitXor, l, ParseBitAnd()));
            }
            return l;
        }
        private ExprNode ParseBitAnd()
        {
            ExprNode l = ParseShift();
            while (_cur.Kind == TokenKind.Ampersand)
            {
                Take();
                l = GuardDepth(new BinOpNode(l.Src, BinOp.BitAnd, l, ParseShift()));
            }
            return l;
        }
        private ExprNode ParseShift()
        {
            ExprNode l = ParseArith();
            while (_cur.Kind == TokenKind.LeftShift || _cur.Kind == TokenKind.RightShift)
            {
                Token t = Take();
                l = GuardDepth(new BinOpNode(l.Src, BinOpOf(t.Kind), l, ParseArith()));
            }
            return l;
        }
        private ExprNode ParseArith()
        {
            ExprNode l = ParseTerm();
            while (_cur.Kind == TokenKind.Plus || _cur.Kind == TokenKind.Minus)
            {
                Token t = Take();
                l = GuardDepth(new BinOpNode(l.Src, BinOpOf(t.Kind), l, ParseTerm()));
            }
            return l;
        }
        private ExprNode ParseTerm()
        {
            ExprNode l = ParseFactor();
            while (_cur.Kind == TokenKind.Star || _cur.Kind == TokenKind.Slash
                || _cur.Kind == TokenKind.DoubleSlash || _cur.Kind == TokenKind.Percent)
            {
                Token t = Take();
                l = GuardDepth(new BinOpNode(l.Src, BinOpOf(t.Kind), l, ParseFactor()));
            }
            return l;
        }

        private ExprNode ParseFactor()
        {
            if (_cur.Kind == TokenKind.Plus || _cur.Kind == TokenKind.Minus || _cur.Kind == TokenKind.Tilde)
            {
                EnterExpr();
                try
                {
                    Token t = Take();
                    ExprNode operand = ParseFactor();
                    UnaryOp uop = t.Kind == TokenKind.Plus ? UnaryOp.UAdd : t.Kind == TokenKind.Minus ? UnaryOp.USub : UnaryOp.Invert;
                    return new UnaryOpNode(Src(t), uop, operand);
                }
                finally
                {
                    LeaveExpr();
                }
            }
            return ParsePower();
        }

        private ExprNode ParsePower()
        {
            ExprNode baseExpr = ParseAtomExpr();
            if (_cur.Kind == TokenKind.DoubleStar)
            {
                EnterExpr();
                try
                {
                    Take();
                    ExprNode exp = ParseFactor(); // RHS = factor -> catches unary minus on the right
                    return new BinOpNode(baseExpr.Src, BinOp.Pow, baseExpr, exp);
                }
                finally
                {
                    LeaveExpr();
                }
            }
            return baseExpr;
        }
    }
}

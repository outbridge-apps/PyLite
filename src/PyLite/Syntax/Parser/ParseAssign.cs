using System.Collections.Generic;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class Parser
    {
        // expr_stmt = testlist_star_expr ( augassign testlist | { '=' testlist_star_expr } )
        private StmtNode ParseExprStmt()
        {
            ExprNode lhs = ParseTestListStarExpr();

            if (_cur.Kind == TokenKind.At)
                throw Error("the '@' (matrix multiply) operator is not supported in this dialect");
            if (_cur.Kind == TokenKind.Colon)
            {
                // PEP 526 annotated target 'lhs : annotation [= value]'. The annotation is parsed and
                // discarded; a bare 'x: int' has no runtime effect and binds nothing (documented
                // simplification — annotation-only names do not become locals in this dialect).
                Take();          // ':'
                ParseTest();     // annotation, discarded
                if (lhs is TupleNode || lhs is ListNode)
                    throw Error("only single target (not tuple) can be annotated");
                ValidateAssignTarget(lhs);
                if (Match(TokenKind.Assign))
                {
                    ExprNode value = ParseTestListStarExpr();
                    return new AssignNode(lhs.Src, AstList.Freeze(new List<ExprNode> { lhs }), value);
                }
                return new PassNode(lhs.Src);
            }

            if (IsAugAssign(_cur.Kind))
            {
                Token op = Take();
                ExprNode rhs = ParseTestList();
                ValidateAugTarget(lhs);
                return new AugAssignNode(lhs.Src, lhs, AugBinOp(op.Kind), rhs);
            }

            if (_cur.Kind == TokenKind.Assign)
            {
                var chain = new List<ExprNode>
                {
                    lhs
                };
                while (Match(TokenKind.Assign))
                    chain.Add(ParseTestListStarExpr());
                ExprNode value = chain[chain.Count - 1];
                chain.RemoveAt(chain.Count - 1);
                foreach (var t in chain)
                    ValidateAssignTarget(t);
                return new AssignNode(lhs.Src, AstList.Freeze(chain), value);
            }

            return new ExprStmtNode(lhs.Src, lhs);
        }

        private ExprNode ParseTestListStarExpr()
        {
            ExprNode first = ParseTestOrStar();
            if (_cur.Kind != TokenKind.Comma)
                return first;
            var elts = new List<ExprNode> { first };
            while (Match(TokenKind.Comma))
            {
                if (IsSimpleStmtEnd(_cur.Kind))
                    break;
                elts.Add(ParseTestOrStar());
            }
            return new TupleNode(first.Src, AstList.Freeze(elts));
        }

        private ExprNode ParseTestList()
        {
            ExprNode first = ParseTest();
            if (_cur.Kind != TokenKind.Comma)
                return first;
            var elts = new List<ExprNode> { first };
            while (Match(TokenKind.Comma))
            {
                if (IsSimpleStmtEnd(_cur.Kind))
                    break;
                elts.Add(ParseTest());
            }
            return new TupleNode(first.Src, AstList.Freeze(elts));
        }

        private StmtNode ParseDelStmt()
        {
            Token del = Take();   // 'del'
            var targets = new List<ExprNode> { ParseTestOrStar() };
            while (Match(TokenKind.Comma))
            {
                if (IsSimpleStmtEnd(_cur.Kind))
                    break;
                targets.Add(ParseTestOrStar());
            }
            foreach (var t in targets)
                ValidateDelTarget(t);
            return new DelNode(Src(del), AstList.Freeze(targets));
        }

        private static bool IsSimpleStmtEnd(TokenKind k)
        {
            return k == TokenKind.Assign || k == TokenKind.NewLine || k == TokenKind.Semicolon
                || k == TokenKind.EndOfFile || k == TokenKind.Colon || k == TokenKind.At
                || IsAugAssign(k);
        }

        private static bool IsAugAssign(TokenKind k)
        {
            switch (k)
            {
                case TokenKind.PlusAssign:
                case TokenKind.MinusAssign:
                case TokenKind.StarAssign:
                case TokenKind.SlashAssign:
                case TokenKind.DoubleSlashAssign:
                case TokenKind.PercentAssign:
                case TokenKind.DoubleStarAssign:
                case TokenKind.AmpersandAssign:
                case TokenKind.PipeAssign:
                case TokenKind.CaretAssign:
                case TokenKind.LeftShiftAssign:
                case TokenKind.RightShiftAssign:
                    return true;
                default:
                    return false;
            }
        }

        private static BinOp AugBinOp(TokenKind k)
        {
            switch (k)
            {
                case TokenKind.PlusAssign: return BinOp.Add;
                case TokenKind.MinusAssign: return BinOp.Sub;
                case TokenKind.StarAssign: return BinOp.Mul;
                case TokenKind.SlashAssign: return BinOp.Div;
                case TokenKind.DoubleSlashAssign: return BinOp.FloorDiv;
                case TokenKind.PercentAssign: return BinOp.Mod;
                case TokenKind.DoubleStarAssign: return BinOp.Pow;
                case TokenKind.AmpersandAssign: return BinOp.BitAnd;
                case TokenKind.PipeAssign: return BinOp.BitOr;
                case TokenKind.CaretAssign: return BinOp.BitXor;
                case TokenKind.LeftShiftAssign: return BinOp.LShift;
                default: return BinOp.RShift;   // RightShiftAssign
            }
        }
    }
}

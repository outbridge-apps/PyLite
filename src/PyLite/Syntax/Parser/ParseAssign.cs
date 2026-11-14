using System.Collections.Generic;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class Parser
    {
        // The right of an assignment and an f-string field take a yield in the grammar: its error, not bad syntax.
        private void NoYieldHere()
        {
            if (_cur.Kind == TokenKind.KwYield)
                throw ForbiddenInFunction();
        }

        // expr_stmt = testlist_star_expr ( augassign testlist | { '=' testlist_star_expr } )
        private StmtNode ParseExprStmt()
        {
            bool parenthesized = _cur.Kind == TokenKind.LeftParen;   // `(x): int` is no simple target
            ExprNode lhs = ParseTestListStarExpr();

            if (_cur.Kind == TokenKind.At)
                throw Error("the '@' (matrix multiply) operator is not supported in this dialect");
            if (_cur.Kind == TokenKind.Colon)
            {
                // PEP 526 annotated target 'lhs : annotation [= value]'. The annotation is parsed and
                // discarded; a bare 'x: int' has no runtime effect and binds nothing (documented
                // simplification — annotation-only names do not become locals in this dialect).
                if (lhs is TupleNode || lhs is ListNode)
                    throw ErrorAtSrc(lhs.Src, "only single target (not " + ExprName(lhs) + ") can be annotated");
                if (!(lhs is NameNode || lhs is AttributeNode || lhs is IndexNode))
                    throw ErrorAtSrc(lhs.Src, "illegal target for annotation");
                Take();          // ':'
                ParseTest();     // annotation, discarded
                ValidateAssignTarget(lhs);
                string simple = !parenthesized && lhs is NameNode ? ((NameNode)lhs).Id : null;
                if (Match(TokenKind.Assign))
                {
                    NoYieldHere();
                    ExprNode value = NotLoneStar(ParseTestListStarExpr());
                    return new AssignNode(lhs.Src, AstList.Freeze(new List<ExprNode> { lhs }), value) { AnnotatedName = simple };
                }
                return new PassNode(lhs.Src) { AnnotatedName = simple };
            }

            if (IsAugAssign(_cur.Kind))
            {
                Token op = Take();
                NoYieldHere();
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
                {
                    NoYieldHere();
                    chain.Add(ParseTestListStarExpr());
                }
                ExprNode value = NotLoneStar(chain[chain.Count - 1]);
                chain.RemoveAt(chain.Count - 1);
                if (chain.Count == 1 && IsComparable(lhs))
                    throw ErrorAtSrc(lhs.Src, "cannot assign to " + ExprName(lhs) + " here. Maybe you meant '==' instead of '='?");
                foreach (var t in chain)
                    ValidateAssignTarget(t);
                return new AssignNode(lhs.Src, AstList.Freeze(chain), value);
            }

            return new ExprStmtNode(lhs.Src, NotLoneStar(lhs));
        }

        // CPython's invalid_named_expression: `x = 1` meant as a comparison - an invalid target that is a
        // bitwise_or (not a display, a constant True/None/False, a comparison or a boolean expression).
        private static bool IsComparable(ExprNode t)
        {
            if (t is NameNode || t is AttributeNode || t is IndexNode || t is TupleNode || t is ListNode || t is StarredNode
                || t is CompareChainNode || t is BoolOpNode || t is IfExpNode || t is LambdaNode || t is NamedExprNode)
                return false;
            UnaryOpNode u = t as UnaryOpNode;
            if (u != null && u.Op == UnaryOp.Not)
                return false;
            ComprehensionNode c = t as ComprehensionNode;
            if (c != null && c.Kind == CompKind.Generator)
                return false;
            LiteralNode lit = t as LiteralNode;
            return lit == null || (lit.Kind != LiteralKind.None && lit.Kind != LiteralKind.Bool);
        }

        // A starred expression outside a list or tuple: the parser kept it and the evaluator had no reading for
        // it (`x = *a`, `*a`), an engine fault.
        private ExprNode NotLoneStar(ExprNode e)
        {
            if (e is StarredNode)
                throw ErrorAtSrc(e.Src, "can't use starred expression here");
            return e;
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

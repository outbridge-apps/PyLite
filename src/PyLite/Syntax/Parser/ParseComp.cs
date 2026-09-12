using System.Collections.Generic;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class Parser
    {
        // exprlist for for/comprehension targets (expr level, single or tuple, allows starred).
        private ExprNode ParseTargetList()
        {
            ExprNode first = ParseTargetElement();
            if (_cur.Kind != TokenKind.Comma)
                return first;
            var elts = new List<ExprNode> { first };
            while (Match(TokenKind.Comma))
            {
                if (_cur.Kind == TokenKind.KwIn)
                    break; // trailing comma
                elts.Add(ParseTargetElement());
            }
            return new TupleNode(first.Src, AstList.Freeze(elts));
        }

        private ExprNode ParseTargetElement()
        {
            if (_cur.Kind == TokenKind.Star)
            {
                Token s = Take();
                return new StarredNode(Src(s), ParseBitOr());
            }
            return ParseBitOr();
        }

        // test | '*' expr, for display elements.
        private ExprNode ParseTestOrStar()
        {
            if (_cur.Kind == TokenKind.Star)
            {
                Token s = Take();
                return new StarredNode(Src(s), ParseBitOr());
            }
            return ParseTest();
        }

        // comp_for { comp_iter } : the first clause is always 'for'.
        private IReadOnlyList<CompClause> ParseCompClauses()
        {
            var clauses = new List<CompClause>();
            Token f = Expect(TokenKind.KwFor, "'for'");
            ExprNode target = ParseTargetList();
            Expect(TokenKind.KwIn, "'in'");
            // The iterable/guard use ParseOrTest, which is BELOW ParseTest's EnterExpr — so a comprehension
            // whose iterable is itself a display ([x for x in [x for x in ...]]) recursed the parser without
            // counting depth (parse-time StackOverflow). Guard each here so deep nesting is a clean SyntaxError.
            ExprNode iter = GuardedOrTest();
            clauses.Add(new CompClause(Src(f), true, target, iter, null));

            while (true)
            {
                if (_cur.Kind == TokenKind.KwFor)
                {
                    Token f2 = Take();
                    ExprNode t2 = ParseTargetList();
                    Expect(TokenKind.KwIn, "'in'");
                    ExprNode it2 = GuardedOrTest();
                    clauses.Add(new CompClause(Src(f2), true, t2, it2, null));
                }
                else if (_cur.Kind == TokenKind.KwIf)
                {
                    Token ift = Take();
                    ExprNode cond = GuardedOrTest();
                    clauses.Add(new CompClause(Src(ift), false, null, null, cond));
                }
                else
                    break;
            }
            return AstList.Freeze(clauses);
        }

        // ParseOrTest wrapped in the expr-depth guard (nesting count + stack probe), so a comprehension
        // clause that recurses through nested displays cannot overflow the parser's C# stack.
        private ExprNode GuardedOrTest()
        {
            EnterExpr();
            try
            {
                return ParseOrTest();
            }
            finally
            {
                LeaveExpr();
            }
        }
    }
}

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
                return new StarredNode(Src(s), GuardedBitOr());
            }
            return ParseBitOr();
        }

        // test | '*' expr, for display elements.
        // A comprehension's element: a starred one reached the evaluator, which has no reading for it.
        private ExprNode CompElement(ExprNode e)
        {
            if (e is StarredNode)
                throw ErrorAtSrc(e.Src, "iterable unpacking cannot be used in comprehension");
            return e;
        }

        // alone: the first element of a display, where a bare star before its closer is CPython's "Invalid star expression"
        private ExprNode ParseTestOrStar(bool alone = false)
        {
            if (_cur.Kind == TokenKind.Star)
            {
                Token s = Take();
                TokenKind k = _cur.Kind;
                if (alone && (k == TokenKind.RightParen || k == TokenKind.RightBracket || k == TokenKind.RightBrace))
                    throw Error("Invalid star expression");   // (*), [*], {*}
                return new StarredNode(Src(s), GuardedBitOr());
            }
            return ParseTest();
        }

        // comp_for { comp_iter } : the first clause is always 'for'.
        private IReadOnlyList<CompClause> ParseCompClauses()
        {
            var clauses = new List<CompClause>();
            Token f = Expect(TokenKind.KwFor);
            ExprNode target = ParseTargetList();
            ValidateAssignTarget(target);   // as a for statement's: [0 for 1 in x] reached the evaluator
            Expect(TokenKind.KwIn);
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
                    ValidateAssignTarget(t2);
                    Expect(TokenKind.KwIn);
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
                ExprNode e = ParseOrTest();
                _disj = null;   // a clause takes a disjunction: CPython's comma hint is for a full expression
                return e;
            }
            finally
            {
                LeaveExpr();
            }
        }

        // The same guard for a starred element, which goes below ParseTest too: `[*[*[*...]]]` and its
        // tuple, set and for-target forms recurse through here.
        private ExprNode GuardedBitOr()
        {
            EnterExpr();
            try
            {
                return ParseBitOr();
            }
            finally
            {
                LeaveExpr();
            }
        }
    }
}

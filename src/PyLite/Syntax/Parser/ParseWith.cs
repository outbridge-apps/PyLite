using System.Collections.Generic;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    // `with` is lowered at parse time into the existing nodes (PEP 343), one item at a time, so every
    // later pass sees only try/except/finally. For `with EXPR as TARGET: BODY`:
    //
    //     $mgr = EXPR
    //     $val = $with_enter($mgr)      # checks __enter__ and __exit__, then calls __enter__
    //     $ok = True
    //     try:
    //         TARGET = $val
    //         BODY
    //     except:
    //         $ok = False
    //         if not $with_exit($mgr, True):    # __exit__(type(e), e, None) with the handled exception
    //             raise
    //     finally:
    //         if $ok:
    //             $with_exit($mgr, False)       # __exit__(None, None, None)
    //         $mgr = None
    //
    // Hidden names start with '$', which the lexer refuses, so no script can name, shadow or read them;
    // the two helpers resolve through Environment's engine-helper fallback, not the host's builtins.
    internal sealed partial class Parser
    {
        private int _withSeq;
        private ExprNode _pendingAtom;   // a parenthesized group already consumed, handed to ParseAtom

        private sealed class WithItem
        {
            internal ExprNode Expr;
            internal ExprNode Target;   // null when there is no 'as'
        }

        private void ParseWith(List<StmtNode> into)
        {
            Token kw = Take();
            var items = new List<WithItem>();
            if (_cur.Kind == TokenKind.LeftParen)
                ParseParenthesizedItems(items);
            else
                ParseWithItems(items);
            Expect(TokenKind.Colon, "':'");

            // Each item becomes one nested try, so the body really sits items.Count levels deeper.
            int extra = items.Count - 1;
            if (_stmtDepth + extra > _limits.MaxStmtDepth)
                throw ErrorAt(kw, "statement too deeply nested (limit " + _limits.MaxStmtDepth + ")");
            _stmtDepth += extra;
            IReadOnlyList<StmtNode> body;
            try
            {
                body = ParseSuite();
            }
            finally
            {
                _stmtDepth -= extra;
            }

            for (int i = items.Count - 1; i >= 0; i--)
            {
                List<StmtNode> lowered = LowerWithItem(Src(kw), items[i], body);
                if (i == 0)
                {
                    into.AddRange(lowered);
                }
                else
                {
                    body = AstList.Freeze(lowered);
                }
            }
        }

        // The rest of an unparenthesized item list: EXPR ['as' TARGET] {',' EXPR ['as' TARGET]}.
        private void ParseWithItems(List<WithItem> items)
        {
            do
            {
                var item = new WithItem { Expr = ParseTest() };
                if (Match(TokenKind.KwAs))
                    item.Target = ParseWithTarget();
                items.Add(item);
            }
            while (Match(TokenKind.Comma));
        }

        private ExprNode ParseWithTarget()
        {
            ExprNode t = ParseBitOr();
            ValidateAssignTarget(t);
            return t;
        }

        // `with (` is either the 3.10 parenthesized item list or an expression that starts with a
        // parenthesized group: `with (a, b):` is two items, `with (a) as x:` or `with (f)(x):` one.
        // The group is parsed once; when ':' does not follow, it is handed back as the atom the rest of
        // the expression continues from, and the remaining items are parsed as usual.
        private void ParseParenthesizedItems(List<WithItem> items)
        {
            Token open = Take();   // '('
            var group = new List<WithItem>();
            bool trailingComma = false, anyAs = false;
            ExprNode generator = null;
            while (_cur.Kind != TokenKind.RightParen)
            {
                var item = new WithItem { Expr = ParseTest() };
                if (group.Count == 0 && _cur.Kind == TokenKind.KwFor)
                {
                    generator = new ComprehensionNode(Src(open), CompKind.Generator, item.Expr, null, ParseCompClauses());
                    break;
                }
                if (Match(TokenKind.KwAs))
                {
                    item.Target = ParseWithTarget();
                    anyAs = true;
                }
                group.Add(item);
                trailingComma = Match(TokenKind.Comma);
                if (!trailingComma)
                    break;
            }
            Expect(TokenKind.RightParen, "')'");

            if (generator == null && group.Count > 0 && _cur.Kind == TokenKind.Colon)
            {
                items.AddRange(group);
                return;
            }
            if (anyAs)
                throw Error("invalid syntax");

            ExprNode atom;
            if (generator != null)
            {
                atom = generator;
            }
            else if (group.Count == 1 && !trailingComma)
            {
                atom = group[0].Expr;
            }
            else
            {
                var elts = new List<ExprNode>(group.Count);
                foreach (WithItem it in group)
                    elts.Add(it.Expr);
                atom = new TupleNode(Src(open), AstList.Freeze(elts));
            }
            _pendingAtom = atom;
            ParseWithItems(items);
        }

        private List<StmtNode> LowerWithItem(SourceInfo s, WithItem item, IReadOnlyList<StmtNode> body)
        {
            int seq = ++_withSeq;
            string mgr = "$with" + seq + "_mgr", val = "$with" + seq + "_val", ok = "$with" + seq + "_ok";
            var outer = new List<StmtNode>(5);
            outer.Add(Assign(s, mgr, item.Expr));
            ExprNode enter = HelperCall(s, "$with_enter", new NameNode(s, mgr));
            var tryBody = new List<StmtNode>(body.Count + 1);
            if (item.Target != null)
            {
                outer.Add(Assign(s, val, enter));
                tryBody.Add(new AssignNode(s, AstList.Freeze(new List<ExprNode> { item.Target }), new NameNode(s, val)));
            }
            else
            {
                outer.Add(new ExprStmtNode(s, enter));
            }
            outer.Add(Assign(s, ok, new LiteralNode(s, true, LiteralKind.Bool)));
            tryBody.AddRange(body);

            var handler = new List<StmtNode>
            {
                Assign(s, ok, new LiteralNode(s, false, LiteralKind.Bool)),
                new IfNode(s,
                    new UnaryOpNode(s, UnaryOp.Not, HelperCall(s, "$with_exit", new NameNode(s, mgr), new LiteralNode(s, true, LiteralKind.Bool))),
                    AstList.Freeze(new List<StmtNode> { new RaiseNode(s, null, null) }),
                    AstList.Empty<StmtNode>()),
            };
            var fin = new List<StmtNode>
            {
                new IfNode(s, new NameNode(s, ok),
                    AstList.Freeze(new List<StmtNode>
                    {
                        new ExprStmtNode(s, HelperCall(s, "$with_exit", new NameNode(s, mgr), new LiteralNode(s, false, LiteralKind.Bool))),
                    }),
                    AstList.Empty<StmtNode>()),
                Assign(s, mgr, new LiteralNode(s, NoneMarker.Instance, LiteralKind.None)),
            };
            if (item.Target != null)
                fin.Add(Assign(s, val, new LiteralNode(s, NoneMarker.Instance, LiteralKind.None)));

            outer.Add(new TryNode(s, AstList.Freeze(tryBody),
                AstList.Freeze(new List<ExceptHandler> { new ExceptHandler(s, null, null, AstList.Freeze(handler)) }),
                AstList.Empty<StmtNode>(), AstList.Freeze(fin)));
            return outer;
        }

        private static AssignNode Assign(SourceInfo s, string name, ExprNode value)
        {
            return new AssignNode(s, AstList.Freeze(new List<ExprNode> { new NameNode(s, name) }), value);
        }

        private static CallNode HelperCall(SourceInfo s, string helper, params ExprNode[] args)
        {
            var list = new List<Arg>(args.Length);
            foreach (ExprNode a in args)
                list.Add(new Arg(s, ArgKind.Positional, null, a));
            return new CallNode(s, new NameNode(s, helper), AstList.Freeze(list));
        }
    }
}

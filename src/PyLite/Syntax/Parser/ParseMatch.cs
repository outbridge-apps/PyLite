using System.Collections.Generic;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class Parser
    {
        // 'match' and 'case' are soft keywords. LL(2) can only see the next token, so 'match' starts a
        // match statement when followed by a token that begins a subject and is not an identifier-use
        // ('match = x', 'match.attr', 'match:int', 'match, y', operators). Documented limit: a bare
        // 'match(...)'/'match[...]' statement where 'match' is a variable is read as a match statement.
        private bool LooksLikeMatchStatement()
        {
            if (_cur.Kind != TokenKind.Name || _cur.Text != "match")
                return false;
            switch (Peek2().Kind)
            {
                case TokenKind.Name:
                case TokenKind.IntLiteral:
                case TokenKind.FloatLiteral:
                case TokenKind.StringLiteral:
                case TokenKind.FStringStart:
                case TokenKind.KwNone:
                case TokenKind.KwTrue:
                case TokenKind.KwFalse:
                case TokenKind.LeftParen:
                case TokenKind.LeftBracket:
                case TokenKind.LeftBrace:
                    return true;
                default:
                    return false;
            }
        }

        private StmtNode ParseMatch()
        {
            Token kw = Take();   // 'match' (soft keyword)
            ExprNode subject = ParseMatchSubject();
            Expect(TokenKind.Colon, "':'");
            Expect(TokenKind.NewLine, "newline");
            Expect(TokenKind.Indent, "an indented block");
            var cases = new List<MatchCase>();
            while (_cur.Kind == TokenKind.Name && _cur.Text == "case")
                cases.Add(ParseCaseBlock());
            if (cases.Count == 0)
                throw Error("expected 'case' block");
            Expect(TokenKind.Dedent, "dedent");
            return new MatchNode(Src(kw), subject, AstList.Freeze(cases));
        }

        // subject: named_expression | star_named_expression (',' star_named_expression)* [',']
        private ExprNode ParseMatchSubject()
        {
            ExprNode first = ParseTestOrStar();
            if (_cur.Kind != TokenKind.Comma)
            {
                if (first is StarredNode)
                    throw Error("can't use starred expression here");
                return first;
            }
            var elts = new List<ExprNode> { first };
            while (Match(TokenKind.Comma))
            {
                if (_cur.Kind == TokenKind.Colon)
                    break;
                elts.Add(ParseTestOrStar());
            }
            return new TupleNode(first.Src, AstList.Freeze(elts));
        }

        private MatchCase ParseCaseBlock()
        {
            Token c = Take();   // 'case' (soft keyword)
            PatternNode pattern = ParsePatterns();
            ExprNode guard = Match(TokenKind.KwIf) ? ParseTest() : null;
            Expect(TokenKind.Colon, "':'");
            IReadOnlyList<StmtNode> body = ParseSuite();
            return new MatchCase(Src(c), pattern, guard, body);
        }

        // patterns: open_sequence_pattern | pattern   (a top-level comma makes an unbracketed sequence)
        private PatternNode ParsePatterns()
        {
            PatternNode first = ParseMaybeStarPattern();
            if (_cur.Kind != TokenKind.Comma)
            {
                if (first is StarPattern)
                    throw Error("star pattern cannot be used here");
                return first;
            }
            var elts = new List<PatternNode> { first };
            int star = first is StarPattern ? 0 : -1;
            while (Match(TokenKind.Comma))
            {
                if (_cur.Kind == TokenKind.Colon || _cur.Kind == TokenKind.KwIf)
                    break;
                PatternNode p = ParseMaybeStarPattern();
                if (p is StarPattern)
                {
                    if (star >= 0)
                        throw Error("multiple starred patterns in sequence pattern");
                    star = elts.Count;
                }
                elts.Add(p);
            }
            return new SequencePattern(first.Src, AstList.Freeze(elts), star);
        }

        private PatternNode ParseMaybeStarPattern()
        {
            if (_cur.Kind == TokenKind.Star)
            {
                Token s = Take();
                if (_cur.Kind == TokenKind.Name && _cur.Text == "_")
                {
                    Take();
                    return new StarPattern(Src(s), null);
                }
                Token n = Expect(TokenKind.Name, "name after '*'");
                return new StarPattern(Src(s), new NameNode(Src(n), n.Text));
            }
            return ParsePattern();
        }

        // pattern: or_pattern ['as' NAME]
        // Every pattern-nesting level flows through here (sequence/group/mapping/class recurse via
        // ParseMaybeStarPattern/ParsePattern; star patterns are leaves), so EnterExpr both bounds the
        // parser's C# recursion (a 5000-deep `[[[…]]]` used to be an uncatchable StackOverflow) and
        // composes with nested expression depth on the shared counter.
        private PatternNode ParsePattern()
        {
            EnterExpr();
            try
            {
                PatternNode p = ParseOrPattern();
                if (Match(TokenKind.KwAs))
                {
                    Token n = Expect(TokenKind.Name, "capture name after 'as'");
                    if (n.Text == "_")
                        throw Error("cannot use '_' as a capture target");
                    return new AsPattern(Src(n), p, new NameNode(Src(n), n.Text));
                }
                return p;
            }
            finally
            {
                LeaveExpr();
            }
        }

        private PatternNode ParseOrPattern()
        {
            PatternNode first = ParseClosedPattern();
            if (_cur.Kind != TokenKind.Pipe)
                return first;
            var alts = new List<PatternNode> { first };
            while (Match(TokenKind.Pipe))
                alts.Add(ParseClosedPattern());
            return new OrPattern(first.Src, AstList.Freeze(alts));
        }

        private PatternNode ParseClosedPattern()
        {
            Token t = _cur;
            switch (t.Kind)
            {
                case TokenKind.Name:
                    if (t.Text == "_")
                    {
                        Take();
                        return new CapturePattern(Src(t), null);   // wildcard
                    }
                    Take();
                    if (_cur.Kind == TokenKind.Dot)
                    {
                        ExprNode dotted = new NameNode(Src(t), t.Text);
                        while (Match(TokenKind.Dot))
                            dotted = GuardDepth(new AttributeNode(Src(t), dotted, Expect(TokenKind.Name, "attribute name").Text));
                        if (_cur.Kind == TokenKind.LeftParen)
                            return ParseClassPattern(dotted);
                        return new ValuePattern(Src(t), dotted);
                    }
                    if (_cur.Kind == TokenKind.LeftParen)
                        return ParseClassPattern(new NameNode(Src(t), t.Text));
                    return new CapturePattern(Src(t), new NameNode(Src(t), t.Text));
                case TokenKind.LeftParen:
                    return ParseGroupOrSequencePattern();
                case TokenKind.LeftBracket:
                    return ParseBracketSequencePattern();
                case TokenKind.LeftBrace:
                    return ParseMappingPattern();
                default:
                    return new LiteralPattern(Src(t), ParseLiteralValue());
            }
        }

        // signed number | string(s) | None | True | False
        private ExprNode ParseLiteralValue()
        {
            Token t = _cur;
            if (t.Kind == TokenKind.Minus || t.Kind == TokenKind.Plus)
            {
                Take();
                Token num = _cur;
                if (num.Kind != TokenKind.IntLiteral && num.Kind != TokenKind.FloatLiteral)
                    throw Error("invalid pattern");
                Take();
                ExprNode lit = new LiteralNode(Src(num), num.Value, num.Kind == TokenKind.IntLiteral ? LiteralKind.Int : LiteralKind.Float);
                return new UnaryOpNode(Src(t), t.Kind == TokenKind.Minus ? UnaryOp.USub : UnaryOp.UAdd, lit);
            }
            switch (t.Kind)
            {
                case TokenKind.IntLiteral: Take(); return new LiteralNode(Src(t), t.Value, LiteralKind.Int);
                case TokenKind.FloatLiteral: Take(); return new LiteralNode(Src(t), t.Value, LiteralKind.Float);
                case TokenKind.KwNone: Take(); return new LiteralNode(Src(t), NoneMarker.Instance, LiteralKind.None);
                case TokenKind.KwTrue: Take(); return new LiteralNode(Src(t), true, LiteralKind.Bool);
                case TokenKind.KwFalse: Take(); return new LiteralNode(Src(t), false, LiteralKind.Bool);
                case TokenKind.StringLiteral:
                case TokenKind.FStringStart:
                    ExprNode s = ParseStringLike();
                    if (s is FStringNode)
                        throw Error("patterns may not contain f-strings");
                    return s;
                default:
                    throw Error("invalid pattern");
            }
        }

        private PatternNode ParseGroupOrSequencePattern()
        {
            Token open = Take();   // '('
            if (_cur.Kind == TokenKind.RightParen)
            {
                Take();
                return new SequencePattern(Src(open), AstList.Empty<PatternNode>(), -1);
            }
            PatternNode first = ParseMaybeStarPattern();
            if (_cur.Kind == TokenKind.Comma)
                return FinishSequence(open, TokenKind.RightParen, first);
            Expect(TokenKind.RightParen, "')'");
            return first;   // group: the parenthesized single pattern
        }

        private PatternNode ParseBracketSequencePattern()
        {
            Token open = Take();   // '['
            if (_cur.Kind == TokenKind.RightBracket)
            {
                Take();
                return new SequencePattern(Src(open), AstList.Empty<PatternNode>(), -1);
            }
            PatternNode first = ParseMaybeStarPattern();
            return FinishSequence(open, TokenKind.RightBracket, first);
        }

        private PatternNode FinishSequence(Token open, TokenKind close, PatternNode first)
        {
            var elts = new List<PatternNode> { first };
            int star = first is StarPattern ? 0 : -1;
            while (Match(TokenKind.Comma))
            {
                if (_cur.Kind == close)
                    break;
                PatternNode p = ParseMaybeStarPattern();
                if (p is StarPattern)
                {
                    if (star >= 0)
                        throw Error("multiple starred patterns in sequence pattern");
                    star = elts.Count;
                }
                elts.Add(p);
            }
            Expect(close, close == TokenKind.RightParen ? "')'" : "']'");
            return new SequencePattern(Src(open), AstList.Freeze(elts), star);
        }

        private PatternNode ParseMappingPattern()
        {
            Token open = Take();   // '{'
            var keys = new List<ExprNode>();
            var vals = new List<PatternNode>();
            NameNode rest = null;
            while (_cur.Kind != TokenKind.RightBrace)
            {
                if (_cur.Kind == TokenKind.DoubleStar)
                {
                    Take();
                    Token n = Expect(TokenKind.Name, "capture name after '**'");
                    if (n.Text == "_")
                        throw Error("cannot use '_' as a capture target");
                    rest = new NameNode(Src(n), n.Text);
                    Match(TokenKind.Comma);   // optional trailing comma; nothing may follow '**rest'
                    break;
                }
                keys.Add(ParseMappingKey());
                Expect(TokenKind.Colon, "':'");
                vals.Add(ParsePattern());
                if (!Match(TokenKind.Comma))
                    break;
            }
            Expect(TokenKind.RightBrace, "'}'");
            return new MappingPattern(Src(open), AstList.Freeze(keys), AstList.Freeze(vals), rest);
        }

        // ClassName '(' [pattern (',' pattern)*] [kw=pattern ...] ')' — positional then keyword sub-patterns.
        private PatternNode ParseClassPattern(ExprNode cls)
        {
            Take();   // '('
            var pos = new List<PatternNode>();
            var kwNames = new List<string>();
            var kwPats = new List<PatternNode>();
            bool sawKw = false;
            while (_cur.Kind != TokenKind.RightParen)
            {
                if (_cur.Kind == TokenKind.Name && Peek2().Kind == TokenKind.Assign)
                {
                    Token n = Take();
                    Take();   // '='
                    kwNames.Add(n.Text);
                    kwPats.Add(ParsePattern());
                    sawKw = true;
                }
                else
                {
                    if (sawKw)
                        throw Error("positional patterns follow keyword patterns");
                    pos.Add(ParsePattern());
                }
                if (!Match(TokenKind.Comma))
                    break;
            }
            Expect(TokenKind.RightParen, "')'");
            return new ClassPattern(cls.Src, cls, AstList.Freeze(pos), AstList.Freeze(kwNames), AstList.Freeze(kwPats));
        }

        // A mapping key is a literal or a dotted value pattern (a bare name is not allowed).
        private ExprNode ParseMappingKey()
        {
            Token t = _cur;
            if (t.Kind == TokenKind.Name)
            {
                Take();
                ExprNode e = new NameNode(Src(t), t.Text);
                if (_cur.Kind != TokenKind.Dot)
                    throw Error("mapping pattern keys must be literals or dotted names");
                while (Match(TokenKind.Dot))
                    e = GuardDepth(new AttributeNode(Src(t), e, Expect(TokenKind.Name, "attribute name").Text));
                return e;
            }
            return ParseLiteralValue();
        }
    }
}

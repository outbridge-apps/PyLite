using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class Parser
    {
        private ExprNode TryParseLambda() { return _cur.Kind == TokenKind.KwLambda ? ParseLambda() : null; }

        // atom { trailer }
        private ExprNode ParseAtomExpr()
        {
            ExprNode atom = ParseAtom();
            while (true)
            {
                switch (_cur.Kind)
                {
                    case TokenKind.LeftParen:
                        EnterExpr();
                        try
                        {
                            Take();
                            IReadOnlyList<Arg> args = ParseArgList();
                            Expect(TokenKind.RightParen, "')'");
                            atom = GuardDepth(new CallNode(atom.Src, atom, args));
                        }
                        finally
                        {
                            LeaveExpr();
                        }

                        break;
                    case TokenKind.LeftBracket:
                        EnterExpr();
                        try
                        {
                            Take();
                            ExprNode index = ParseSubscript();
                            Expect(TokenKind.RightBracket, "']'");
                            atom = GuardDepth(new IndexNode(atom.Src, atom, index));
                        }
                        finally
                        {
                            LeaveExpr();
                        }

                        break;
                    case TokenKind.Dot:
                        Take();
                        Token name = Expect(TokenKind.Name, "attribute name");
                        atom = GuardDepth(new AttributeNode(atom.Src, atom, name.Text));
                        break;
                    default:
                        return atom;
                }
            }
        }

        private ExprNode ParseAtom()
        {
            Token t = _cur;
            switch (t.Kind)
            {
                case TokenKind.Name: Take(); return new NameNode(Src(t), t.Text);
                case TokenKind.IntLiteral: Take(); return new LiteralNode(Src(t), t.Value, LiteralKind.Int);
                case TokenKind.FloatLiteral: Take(); return new LiteralNode(Src(t), t.Value, LiteralKind.Float);
                case TokenKind.StringLiteral:
                case TokenKind.FStringStart: return ParseStringLike();
                case TokenKind.BytesLiteral: return ParseBytesLike();
                case TokenKind.KwNone: Take(); return new LiteralNode(Src(t), NoneMarker.Instance, LiteralKind.None);
                case TokenKind.KwTrue: Take(); return new LiteralNode(Src(t), true, LiteralKind.Bool);
                case TokenKind.KwFalse: Take(); return new LiteralNode(Src(t), false, LiteralKind.Bool);
                case TokenKind.Ellipsis: Take(); return new LiteralNode(Src(t), EllipsisMarker.Instance, LiteralKind.Ellipsis);
                case TokenKind.LeftParen: return ParseParenAtom();
                case TokenKind.LeftBracket: return ParseListDisplay();
                case TokenKind.LeftBrace: return ParseBraceDisplay();
                case TokenKind.EndOfFile: throw Error("unexpected EOF while parsing");
                default: throw Error("invalid syntax");
            }
        }

        private ExprNode ParseParenAtom()
        {
            Token open = Take();   // '('
            if (_cur.Kind == TokenKind.RightParen)
            {
                Take();
                return new TupleNode(Src(open), AstList.Empty<ExprNode>());
            }
            ExprNode first = ParseTestOrStar();
            if (_cur.Kind == TokenKind.KwFor)
            {
                var comp = new ComprehensionNode(Src(open), CompKind.Generator, first, null, ParseCompClauses());
                Expect(TokenKind.RightParen, "')'");
                return comp;
            }
            if (_cur.Kind == TokenKind.Comma)
            {
                var elts = new List<ExprNode>
                {
                    first
                };
                while (Match(TokenKind.Comma))
                {
                    if (_cur.Kind == TokenKind.RightParen)
                        break;
                    elts.Add(ParseTestOrStar());
                }

                Expect(TokenKind.RightParen, "')'");
                return new TupleNode(Src(open), AstList.Freeze(elts));
            }
            if (first is StarredNode)
                throw Error("can't use starred expression here");
            Expect(TokenKind.RightParen, "')'");
            return first;
        }

        private ExprNode ParseListDisplay()
        {
            Token open = Take();   // '['
            if (_cur.Kind == TokenKind.RightBracket)
            {
                Take();
                return new ListNode(Src(open), AstList.Empty<ExprNode>());
            }
            ExprNode first = ParseTestOrStar();
            if (_cur.Kind == TokenKind.KwFor)
            {
                var comp = new ComprehensionNode(Src(open), CompKind.List, first, null, ParseCompClauses());
                Expect(TokenKind.RightBracket, "']'");
                return comp;
            }
            var elts = new List<ExprNode> { first };
            while (Match(TokenKind.Comma))
            {
                if (_cur.Kind == TokenKind.RightBracket)
                    break;
                elts.Add(ParseTestOrStar());
            }
            Expect(TokenKind.RightBracket, "']'");
            return new ListNode(Src(open), AstList.Freeze(elts));
        }

        private ExprNode ParseBraceDisplay()
        {
            Token open = Take();   // '{'
            if (_cur.Kind == TokenKind.RightBrace)
            {
                Take();
                return new DictNode(Src(open), AstList.Empty<ExprNode>(), AstList.Empty<ExprNode>());
            }

            // PEP 448: a leading '**' makes it a dict (with unpacking); a leading '*' makes it a set.
            if (_cur.Kind == TokenKind.DoubleStar)
                return ParseDictDisplay(open, null, null, true);
            if (_cur.Kind == TokenKind.Star)
                return ParseSetDisplay(open, null);

            ExprNode first = ParseTest();
            if (_cur.Kind == TokenKind.Colon)
            {
                Take();
                ExprNode firstVal = ParseTest();
                if (_cur.Kind == TokenKind.KwFor)
                {
                    var comp = new ComprehensionNode(Src(open), CompKind.Dict, first, firstVal, ParseCompClauses());
                    Expect(TokenKind.RightBrace, "'}'");
                    return comp;
                }

                return ParseDictDisplay(open, first, firstVal, false);
            }

            if (_cur.Kind == TokenKind.KwFor)
            {
                var comp = new ComprehensionNode(Src(open), CompKind.Set, first, null, ParseCompClauses());
                Expect(TokenKind.RightBrace, "'}'");
                return comp;
            }

            return ParseSetDisplay(open, first);
        }

        // The brace display is a dict. When unpackFirst, the first entry is '**expr'; else firstKey:firstVal.
        private ExprNode ParseDictDisplay(Token open, ExprNode firstKey, ExprNode firstVal, bool unpackFirst)
        {
            var keys = new List<ExprNode>();
            var vals = new List<ExprNode>();
            if (unpackFirst)
                AddDictUnpack(keys, vals);
            else
            {
                keys.Add(firstKey);
                vals.Add(firstVal);
            }
            while (Match(TokenKind.Comma))
            {
                if (_cur.Kind == TokenKind.RightBrace)
                    break;
                if (_cur.Kind == TokenKind.DoubleStar)
                {
                    AddDictUnpack(keys, vals);
                    continue;
                }
                keys.Add(ParseTest());
                Expect(TokenKind.Colon, "':'");
                vals.Add(ParseTest());
            }
            Expect(TokenKind.RightBrace, "'}'");
            return new DictNode(Src(open), AstList.Freeze(keys), AstList.Freeze(vals));
        }

        // PEP 448 '**mapping' entry: a null key marks its value as a mapping to merge (Python ast convention).
        private void AddDictUnpack(List<ExprNode> keys, List<ExprNode> vals)
        {
            Take();   // '**'
            keys.Add(null);
            vals.Add(ParseTest());
        }

        // The brace display is a set. 'first' is the already-parsed first element, or null for a leading '*expr'.
        private ExprNode ParseSetDisplay(Token open, ExprNode first)
        {
            var elts = new List<ExprNode> { first ?? ParseTestOrStar() };
            while (Match(TokenKind.Comma))
            {
                if (_cur.Kind == TokenKind.RightBrace)
                    break;
                elts.Add(ParseTestOrStar());
            }
            Expect(TokenKind.RightBrace, "'}'");
            return new SetNode(Src(open), AstList.Freeze(elts));
        }

        // subscript content: a single subscript element, or a comma-list -> Tuple index.
        private ExprNode ParseSubscript()
        {
            ExprNode first = ParseSubscriptElement();
            if (_cur.Kind != TokenKind.Comma)
                return first;
            var elts = new List<ExprNode> { first };
            while (Match(TokenKind.Comma))
            {
                if (_cur.Kind == TokenKind.RightBracket)
                    break;
                elts.Add(ParseSubscriptElement());
            }
            return new TupleNode(first.Src, AstList.Freeze(elts));
        }

        private ExprNode ParseSubscriptElement()
        {
            Token at = _cur;
            ExprNode lower = _cur.Kind == TokenKind.Colon ? null : ParseTest();
            if (_cur.Kind != TokenKind.Colon)
                return lower;   // simple index
            Take();   // ':'
            ExprNode upper = (_cur.Kind == TokenKind.Colon || _cur.Kind == TokenKind.RightBracket || _cur.Kind == TokenKind.Comma)
                ? null : ParseTest();
            ExprNode step = null;
            if (Match(TokenKind.Colon))
                step = (_cur.Kind == TokenKind.RightBracket || _cur.Kind == TokenKind.Comma) ? null : ParseTest();
            return new SliceNode(Src(at), lower, upper, step);
        }

        // Concatenation of adjacent string / f-string literals; any f-string makes the result an FStringNode.
        private ExprNode ParseStringLike()
        {
            Token first = _cur;
            var parts = new List<FStringPart>();
            bool anyF = false;
            while (_cur.Kind == TokenKind.StringLiteral || _cur.Kind == TokenKind.FStringStart)
            {
                if (_cur.Kind == TokenKind.StringLiteral)
                {
                    parts.Add(new FStringPart(Src(_cur), true, _cur.Text, null, '\0', null));
                    Take();
                }
                else
                {
                    anyF = true;
                    Take(); // FSTRING_START
                    parts.AddRange(ParseFStringParts(TokenKind.FStringEnd));
                    Expect(TokenKind.FStringEnd, "end of f-string");
                }
            }
            if (_cur.Kind == TokenKind.BytesLiteral)
                throw Error("cannot mix bytes and nonbytes literals");
            if (!anyF)
            {
                var sb = new StringBuilder();
                foreach (var p in parts)
                    sb.Append(p.Literal);
                return new LiteralNode(Src(first), sb.ToString(), LiteralKind.Str);
            }
            return new FStringNode(Src(first), AstList.Freeze(parts));
        }

        // Adjacent bytes literals concatenate; mixing bytes with str/f-string literals is an error.
        private ExprNode ParseBytesLike()
        {
            Token first = _cur;
            var buf = new List<byte>();
            while (_cur.Kind == TokenKind.BytesLiteral)
            {
                buf.AddRange((byte[])_cur.Value);
                Take();
            }
            if (_cur.Kind == TokenKind.StringLiteral || _cur.Kind == TokenKind.FStringStart)
                throw Error("cannot mix bytes and nonbytes literals");
            return new LiteralNode(Src(first), buf.ToArray(), LiteralKind.Bytes);
        }

        private List<FStringPart> ParseFStringParts(TokenKind end)
        {
            var parts = new List<FStringPart>();
            while (_cur.Kind != end)
            {
                if (_cur.Kind == TokenKind.FStringMiddle)
                {
                    parts.Add(new FStringPart(Src(_cur), true, _cur.Text, null, '\0', null));
                    Take();
                }
                else if (_cur.Kind == TokenKind.LeftBrace)
                {
                    Token brace = Take();
                    EnterExpr();
                    try
                    {
                        ExprNode expr = ParseTest();
                        string debugText = null;
                        if (_cur.Kind == TokenKind.FStringDebug)
                        {
                            debugText = _cur.Text;   // 'expr=' literal (PEP 501 self-documenting)
                            Take();
                        }
                        char conv = '\0';
                        if (Match(TokenKind.Bang))
                        {
                            Token c = Expect(TokenKind.Name, "conversion");
                            if (c.Text.Length != 1 || (c.Text != "s" && c.Text != "r" && c.Text != "a"))
                                throw ErrorAt(c, "f-string: invalid conversion character");
                            conv = c.Text[0];
                        }

                        IReadOnlyList<FStringPart> fmt = null;
                        if (Match(TokenKind.Colon))
                            fmt = AstList.Freeze(ParseFStringParts(TokenKind.RightBrace));
                        Expect(TokenKind.RightBrace, "'}' in f-string");
                        if (debugText != null)
                        {
                            parts.Add(new FStringPart(Src(brace), true, debugText, null, '\0', null));
                            if (conv == '\0' && fmt == null)
                                conv = 'r';   // 'x=' with no !conv/:spec defaults to repr
                        }
                        parts.Add(new FStringPart(Src(brace), false, null, expr, conv, fmt));
                    }
                    finally
                    {
                        LeaveExpr();
                    }
                }
                else
                    throw Error("invalid syntax in f-string");
            }
            return parts;
        }
    }
}

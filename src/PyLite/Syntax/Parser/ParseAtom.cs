using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class Parser
    {
        private ExprNode TryParseLambda() { return _pendingAtom == null && _cur.Kind == TokenKind.KwLambda ? ParseLambda() : null; }

        // super() in a method is super(<the defining class>, <first parameter>), CPython's __class__ cell: the
        // class reaches its methods through a hidden variable the class statement binds.
        private string ClassCell()
        {
            if (_superCell == null)
                _superCell = "$class" + (++_classConstSeq);
            return _superCell;
        }

        private IReadOnlyList<Arg> SuperArgs(SourceInfo s)
        {
            return AstList.Freeze(new List<Arg>
            {
                new Arg(s, ArgKind.Positional, null, new NameNode(s, ClassCell())),
                new Arg(s, ArgKind.Positional, null, new NameNode(s, _superSelf)),
            });
        }

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
                            ExpectClose(TokenKind.RightParen);
                            if (args.Count == 0 && _superSelf != null && atom is NameNode sn && sn.Id == "super")
                                args = SuperArgs(sn.Src);
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
                            ExpectClose(TokenKind.RightBracket);
                            atom = GuardDepth(new IndexNode(atom.Src, atom, index));
                        }
                        finally
                        {
                            LeaveExpr();
                        }

                        break;
                    case TokenKind.Dot:
                        Take();
                        Token name = Expect(TokenKind.Name);
                        atom = GuardDepth(new AttributeNode(atom.Src, atom, Mangle(name.Text)));
                        break;
                    default:
                        return atom;
                }
            }
        }

        private ExprNode ParseAtom()
        {
            if (_pendingAtom != null)
            {
                ExprNode pending = _pendingAtom;   // a `with (...)` group the expression continues from
                _pendingAtom = null;
                return pending;
            }
            Token t = _cur;
            switch (t.Kind)
            {
                case TokenKind.Name:
                {
                    Take();
                    string hiddenName;
                    if (_classRenames != null && _classSuspend == 0)
                    {
                        if (_classRenames.TryGetValue(Mangle(t.Text), out hiddenName))
                            return new NameNode(Src(t), hiddenName);   // an earlier constant of the class being parsed
                        if (_classReads != null)
                        {
                            var read = new NameNode(Src(t), t.Text);
                            _classReads.Add(read);
                            return read;
                        }
                    }
                    if (_inMethod && t.Text == "__class__")
                        return new NameNode(Src(t), ClassCell());   // the class whose method this is
                    return new NameNode(Src(t), t.Text);
                }
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
                case TokenKind.KwAwait: throw ForbiddenInFunction();
                case TokenKind.EndOfFile: throw Error("unexpected EOF while parsing");
                default: throw Unexpected(false);
            }
        }

        private ExprNode ParseParenAtom()
        {
            Token open = Take();   // '('
            if (_cur.Kind == TokenKind.KwYield)
                throw ForbiddenInFunction();   // where the grammar has a yield; elsewhere it is just bad syntax
            if (_cur.Kind == TokenKind.RightParen)
            {
                Take();
                return new TupleNode(Src(open), AstList.Empty<ExprNode>());
            }
            ExprNode first = ParseTestOrStar(true);
            if (_cur.Kind == TokenKind.KwFor)
            {
                var comp = new ComprehensionNode(Src(open), CompKind.Generator, CompElement(first), null, ParseCompClauses());
                ExpectClose(TokenKind.RightParen);
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

                ExpectClose(TokenKind.RightParen);
                return new TupleNode(Src(open), AstList.Freeze(elts));
            }
            if (first is StarredNode)
                throw ErrorAtSrc(first.Src, "cannot use starred expression here");
            ExpectClose(TokenKind.RightParen);
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
            ExprNode first = ParseTestOrStar(true);
            if (_cur.Kind == TokenKind.KwFor)
            {
                var comp = new ComprehensionNode(Src(open), CompKind.List, CompElement(first), null, ParseCompClauses());
                ExpectClose(TokenKind.RightBracket);
                return comp;
            }
            var elts = new List<ExprNode> { first };
            while (Match(TokenKind.Comma))
            {
                if (_cur.Kind == TokenKind.RightBracket)
                    break;
                elts.Add(ParseTestOrStar());
                UnparenthesizedTarget(first);
            }
            ExpectClose(TokenKind.RightBracket);
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
                ExprNode firstVal = ParseDictValue();
                if (_cur.Kind == TokenKind.KwFor)
                {
                    var comp = new ComprehensionNode(Src(open), CompKind.Dict, first, firstVal, ParseCompClauses());
                    ExpectClose(TokenKind.RightBrace);
                    return comp;
                }

                return ParseDictDisplay(open, first, firstVal, false);
            }

            if (_cur.Kind == TokenKind.KwFor)
            {
                var comp = new ComprehensionNode(Src(open), CompKind.Set, first, null, ParseCompClauses());
                ExpectClose(TokenKind.RightBrace);
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
                ExprNode key = ParseTest();
                if (_cur.Kind != TokenKind.Colon)
                    throw PySyntaxErrorException.Syntax("':' expected after dictionary key", key.Src.Line, _prevEndCol - 1);
                Take();
                keys.Add(key);
                vals.Add(ParseDictValue());
            }
            ExpectClose(TokenKind.RightBrace);
            return new DictNode(Src(open), AstList.Freeze(keys), AstList.Freeze(vals));
        }

        private ExprNode ParseDictValue()
        {
            if (_cur.Kind == TokenKind.Star)
                throw Error("cannot use a starred expression in a dictionary value");
            if (_cur.Kind == TokenKind.RightBrace || _cur.Kind == TokenKind.Comma)
                throw PySyntaxErrorException.Syntax("expression expected after dictionary key and ':'", _cur.Line, _prevEndCol - 1);
            return ParseTest();
        }

        // PEP 448 '**mapping' entry: a null key marks its value as a mapping to merge (Python ast convention).
        private void AddDictUnpack(List<ExprNode> keys, List<ExprNode> vals)
        {
            Token star = Take();   // '**'
            keys.Add(null);
            vals.Add(ParseTest());
            if (_cur.Kind == TokenKind.KwFor)
                throw ErrorAt(star, "dict unpacking cannot be used in dict comprehension");
        }

        // The brace display is a set. 'first' is the already-parsed first element, or null for a leading '*expr'.
        private ExprNode ParseSetDisplay(Token open, ExprNode first)
        {
            var elts = new List<ExprNode> { first ?? ParseTestOrStar(true) };
            while (Match(TokenKind.Comma))
            {
                if (_cur.Kind == TokenKind.RightBrace)
                    break;
                elts.Add(ParseTestOrStar());
                UnparenthesizedTarget(elts[0]);
            }
            ExpectClose(TokenKind.RightBrace);
            return new SetNode(Src(open), AstList.Freeze(elts));
        }

        // [x, y for x in z]: CPython's invalid_comprehension, at the display's first element
        private void UnparenthesizedTarget(ExprNode first)
        {
            if (_cur.Kind == TokenKind.KwFor)
                throw ErrorAtSrc(first.Src, "did you forget parentheses around the comprehension target?");
        }

        // subscript content: a single subscript element, or a comma-list -> Tuple index. A starred element
        // makes a tuple even alone (3.11: d[*a] is d[(*a,)]).
        private ExprNode ParseSubscript()
        {
            ExprNode first = ParseSubscriptElement();
            if (_cur.Kind != TokenKind.Comma)
                return first is StarredNode ? new TupleNode(first.Src, AstList.Freeze(new List<ExprNode> { first })) : first;
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
            if (Match(TokenKind.Star))
                return new StarredNode(Src(at), ParseTest());
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
            bool anyF = false, anyBytes = false;
            while (_cur.Kind == TokenKind.StringLiteral || _cur.Kind == TokenKind.FStringStart || _cur.Kind == TokenKind.BytesLiteral)
            {
                if (_cur.Kind == TokenKind.BytesLiteral)
                {
                    anyBytes = true;
                    Take();
                }
                else if (_cur.Kind == TokenKind.StringLiteral)
                {
                    parts.Add(new FStringPart(Src(_cur), true, _cur.Text, null, '\0', null));
                    Take();
                }
                else
                {
                    anyF = true;
                    Take(); // FSTRING_START
                    parts.AddRange(ParseFStringParts(TokenKind.FStringEnd));
                    Take(); // FSTRING_END
                }
            }
            if (anyBytes)
                throw Error("cannot mix bytes and nonbytes literals");   // CPython's place: after the last literal
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
            {
                ParseStringLike();
                throw Error("cannot mix bytes and nonbytes literals");
            }
            return new LiteralNode(Src(first), buf.ToArray(), LiteralKind.Bytes);
        }

        // a replacement field's expression: a bare tuple too (PEP 701), `f'{x, y}'`
        private ExprNode ParseFieldExpr()
        {
            ExprNode first = ParseTestOrStar();
            if (_cur.Kind != TokenKind.Comma)
                return NotLoneStar(first);
            var elts = new List<ExprNode> { first };
            while (Match(TokenKind.Comma))
            {
                if (IsFieldEnd(_cur.Kind) || _cur.Kind == TokenKind.FStringDebug)
                    break;
                elts.Add(ParseTestOrStar());
            }
            return new TupleNode(first.Src, AstList.Freeze(elts));
        }

        private static bool IsFieldEnd(TokenKind k)
        {
            return k == TokenKind.Bang || k == TokenKind.Colon || k == TokenKind.RightBrace;
        }

        // A field whose expression stopped at the field's end after an operator ("{1 +}", "{a if}", "{a.}", "{not}").
        private bool UnfinishedField(PySyntaxErrorException e, Token brace)
        {
            if (e.Message != "invalid syntax" || e.Line != _cur.Line || e.Col != _cur.Col || SameToken(_prevTok, brace))
                return false;
            if (!IsFieldEnd(_cur.Kind) && _cur.Kind != TokenKind.FStringDebug && _cur.Kind != TokenKind.FStringEnd)
                return false;
            switch (_prevTok.Kind)
            {
                case TokenKind.Name:
                case TokenKind.IntLiteral:
                case TokenKind.FloatLiteral:
                case TokenKind.StringLiteral:
                case TokenKind.BytesLiteral:
                case TokenKind.FStringEnd:
                case TokenKind.KwTrue:
                case TokenKind.KwFalse:
                case TokenKind.KwNone:
                case TokenKind.Ellipsis:
                case TokenKind.RightParen:
                case TokenKind.RightBracket:
                case TokenKind.RightBrace:
                    return false;
                default:
                    return true;
            }
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
                        // CPython's invalid_fstring_replacement_field messages
                        NoYieldHere();
                        Token fieldStart = _cur;
                        ExprNode expr;
                        try
                        {
                            expr = ParseFieldExpr();
                        }
                        catch (PySyntaxErrorException e) when (UnfinishedField(e, brace))
                        {
                            // CPython keeps the longest expression and fails on the token after it: f"{1 +}" at the '+'
                            throw SameToken(_prevTok, fieldStart)
                                ? ErrorAt(fieldStart, "f-string: expecting a valid expression after '{'")
                                : ErrorAt(_prevTok, "f-string: expecting '=', or '!', or ':', or '}'");
                        }
                        if (!IsFieldEnd(_cur.Kind) && _cur.Kind != TokenKind.FStringDebug)
                            throw SideBySide(true) ?? Error("f-string: expecting '=', or '!', or ':', or '}'");
                        string debugText = null;
                        if (_cur.Kind == TokenKind.FStringDebug)
                        {
                            debugText = _cur.Text;   // 'expr=' literal (PEP 501 self-documenting)
                            Take();
                            if (!IsFieldEnd(_cur.Kind))
                                throw Error("f-string: expecting '!', or ':', or '}'");
                        }
                        char conv = '\0';
                        if (Match(TokenKind.Bang))
                        {
                            if (_cur.Kind != TokenKind.Name)
                                throw Error("f-string: missing conversion character");
                            Token c = Take();
                            if (c.Text.Length != 1 || (c.Text != "s" && c.Text != "r" && c.Text != "a"))
                                throw ErrorAt(c, "f-string: invalid conversion character '" + c.Text + "': expected 's', 'r', or 'a'");
                            conv = c.Text[0];
                            if (_cur.Kind != TokenKind.Colon && _cur.Kind != TokenKind.RightBrace)
                                throw Error("f-string: expecting ':' or '}'");
                        }

                        IReadOnlyList<FStringPart> fmt = null;
                        if (Match(TokenKind.Colon))
                            fmt = AstList.Freeze(ParseFStringParts(TokenKind.RightBrace));
                        Expect(TokenKind.RightBrace, "'}'");
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
                else if (_cur.Kind == TokenKind.FStringEnd)
                    throw Error("f-string: expecting '}', or format specs");
                else
                    throw Error("invalid syntax in f-string");
            }
            return parts;
        }
    }
}

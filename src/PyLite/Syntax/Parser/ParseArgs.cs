using System.Collections.Generic;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class Parser
    {
        // Parses the content between a call's '(' ... ')' (the '(' already consumed; ')' consumed by the caller).
        private IReadOnlyList<Arg> ParseArgList()
        {
            if (_cur.Kind == TokenKind.RightParen)
                return AstList.Empty<Arg>();

            var args = new List<Arg>();
            bool sawKeyword = false, sawDoubleStar = false;
            var kwNames = new HashSet<string>(System.StringComparer.Ordinal);

            // PEP 448: multiple *iterable and **mapping unpackings are allowed and may interleave with
            // positional/keyword args; only a bare positional after a keyword/** and a *iterable after **
            // are rejected.
            while (_cur.Kind != TokenKind.RightParen)
            {
                Token at = _cur;
                if (_cur.Kind == TokenKind.DoubleStar)
                {
                    Take();
                    ExprNode v = ParseTest();
                    args.Add(new Arg(Src(at), ArgKind.DoubleStar, null, v));
                    sawDoubleStar = true;
                }
                else if (_cur.Kind == TokenKind.Star)
                {
                    if (sawDoubleStar)
                        throw Error("iterable argument unpacking follows keyword argument unpacking");
                    Take();
                    ExprNode v = ParseTest();
                    args.Add(new Arg(Src(at), ArgKind.Star, null, v));
                }
                else
                {
                    ExprNode e = ParseTest();
                    if (args.Count == 0 && _cur.Kind == TokenKind.KwFor)
                    {
                        ComprehensionNode gen = new ComprehensionNode(e.Src, CompKind.Generator, e, null, ParseCompClauses());
                        if (_cur.Kind == TokenKind.Comma)
                            throw Error("Generator expression must be parenthesized");
                        args.Add(new Arg(Src(at), ArgKind.Positional, null, gen));
                        break;
                    }

                    if (_cur.Kind == TokenKind.Assign)
                    {
                        Take();
                        NameNode nn = e as NameNode;
                        if (nn == null)
                            throw ErrorAt(at, "keyword can't be an expression");
                        if (!kwNames.Add(nn.Id))
                            throw ErrorAt(at, "keyword argument repeated");
                        ExprNode val = ParseTest();
                        args.Add(new Arg(Src(at), ArgKind.Keyword, nn.Id, val));
                        sawKeyword = true;
                    }
                    else
                    {
                        if (sawKeyword || sawDoubleStar)
                            throw ErrorAt(at, "positional argument follows keyword argument");
                        args.Add(new Arg(Src(at), ArgKind.Positional, null, e));
                    }
                }

                if (!Match(TokenKind.Comma))
                    break;
            }

            return AstList.Freeze(args);
        }
    }
}

using System.Collections.Generic;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class Parser
    {
        // Parses the content between a call's '(' ... ')' (the '(' already consumed; ')' consumed by the caller).
        private IReadOnlyList<Arg> ParseArgList(bool classBases = false)
        {
            if (_cur.Kind == TokenKind.RightParen)
                return AstList.Empty<Arg>();

            var args = new List<Arg>();
            bool sawKeyword = false, sawDoubleStar = false;
            var kwNames = new HashSet<string>(System.StringComparer.Ordinal);
            Token comma = _cur;

            // PEP 448: multiple *iterable and **mapping unpackings are allowed and may interleave with
            // positional/keyword args; only a bare positional after a keyword/** and a *iterable after **
            // are rejected. Messages and positions are CPython's invalid_arguments.
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
                        throw ErrorAt(comma, "iterable argument unpacking follows keyword argument unpacking");
                    Take();
                    if (_cur.Kind == TokenKind.RightParen || _cur.Kind == TokenKind.Comma)
                        throw Error("Invalid star expression");
                    ExprNode v = ParseTest();
                    if (_cur.Kind == TokenKind.KwFor)
                        throw ErrorAt(at, "iterable unpacking cannot be used in comprehension");
                    args.Add(new Arg(Src(at), ArgKind.Star, null, v));
                }
                else
                {
                    ExprNode e = ParseTest();
                    if (_cur.Kind == TokenKind.ColonEqual)   // an argument is `expression !':='`: no named-expression hint here
                        throw ErrorAt(_cur, "invalid syntax");
                    if (_cur.Kind == TokenKind.KwFor)
                    {
                        if (classBases)
                            throw ErrorAt(_cur, "invalid syntax");   // a bare generator is a call's, not a class header's
                        if (args.Count > 0)
                            throw ErrorAtSrc(e.Src, "Generator expression must be parenthesized");
                        ComprehensionNode gen = new ComprehensionNode(e.Src, CompKind.Generator, e, null, ParseCompClauses());
                        if (_cur.Kind == TokenKind.Comma)
                            throw ErrorAtSrc(e.Src, "Generator expression must be parenthesized");
                        args.Add(new Arg(Src(at), ArgKind.Positional, null, gen));
                        break;
                    }

                    if (_cur.Kind == TokenKind.Assign)
                    {
                        if ((at.Kind == TokenKind.KwTrue || at.Kind == TokenKind.KwFalse || at.Kind == TokenKind.KwNone) && _prevEndCol == at.EndCol)
                            throw ErrorAt(at, "cannot assign to " + (at.Kind == TokenKind.KwTrue ? "True" : at.Kind == TokenKind.KwFalse ? "False" : "None"));   // CPython's invalid_kwarg: f(None=1)
                        Take();
                        if (_cur.Kind == TokenKind.RightParen || _cur.Kind == TokenKind.Comma)
                            throw ErrorAt(at, "expected argument value expression");   // f(a=)
                        if (!(e is NameNode) || at.Kind != TokenKind.Name)   // f((b)=1): the keyword below would be the '(' token
                            throw ErrorAt(at, "expression cannot contain assignment, perhaps you meant \"==\"?");
                        string keyword = at.Text;   // the token, not the node's Id: a class-body constant may have renamed it
                        ForbidDebug(keyword, Src(at));
                        if (!kwNames.Add(keyword))
                            throw ErrorAt(at, "keyword argument repeated: " + keyword);
                        ExprNode val = ParseTest();
                        if (_cur.Kind == TokenKind.Assign)
                            throw ErrorAt(_cur, "invalid syntax");   // f(a=b=c): no named-expression hint on a keyword's value
                        args.Add(new Arg(Src(at), ArgKind.Keyword, keyword, val));
                        sawKeyword = true;
                    }
                    else
                    {
                        if (sawDoubleStar)
                            throw Error("positional argument follows keyword argument unpacking");
                        if (sawKeyword)
                            throw Error("positional argument follows keyword argument");
                        args.Add(new Arg(Src(at), ArgKind.Positional, null, e));
                    }
                }

                comma = _cur;
                if (!Match(TokenKind.Comma))
                    break;
            }

            return AstList.Freeze(args);
        }
    }
}

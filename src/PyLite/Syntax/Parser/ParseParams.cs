using System.Collections.Generic;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class Parser
    {
        private ExprNode ParseLambda()
        {
            Token lam = Take();   // 'lambda'
            ParamList prms = ParseParamList(TokenKind.Colon);
            Expect(TokenKind.Colon);
            if (_cur.Kind == TokenKind.FStringMiddle)
                throw ErrorAt(lam, "f-string: lambda expressions are not allowed without parentheses");
            string savedSelf = _superSelf;
            _superSelf = null;   // a lambda in a method is a function of its own: super() there has no arguments
            _funcDepth++;
            ExprNode body;
            try
            {
                body = ParseTest();
            }
            finally
            {
                _superSelf = savedSelf;
                _funcDepth--;
            }
            return new LambdaNode(Src(lam), prms, body);
        }

        // Explicit FSM (R-PAR-05). Terminator: ')' for def, ':' for lambda. Errors are CPython's invalid_parameters.
        private ParamList ParseParamList(TokenKind terminator)
        {
            var positional = new List<Param>();
            var kwonly = new List<Param>();
            Param starArg = null, kwArg = null;
            Token bareStar = default(Token);
            bool hasBareStar = false;
            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            SourceInfo listSrc = CurSrc();

            int phase = 0;   // 0=positional, 1=kwonly, 2=after **kwargs
            bool sawDefault = false;
            int posOnlyCount = 0;   // PEP 570: params before '/'

            while (_cur.Kind != terminator)
            {
                if (phase == 2 && (_cur.Kind == TokenKind.Name || _cur.Kind == TokenKind.Star || _cur.Kind == TokenKind.DoubleStar
                    || _cur.Kind == TokenKind.Slash))
                    throw Error("arguments cannot follow var-keyword argument");
                if (_cur.Kind == TokenKind.Slash)
                {
                    if (phase != 0)
                        throw Error("/ must be ahead of *");
                    if (posOnlyCount != 0)
                        throw Error("/ may appear only once");
                    if (positional.Count == 0)
                        throw Peek2().Kind == TokenKind.Comma ? Error("at least one argument must precede /") : Error("invalid syntax");
                    posOnlyCount = positional.Count;
                    Take();
                    if (_cur.Kind == TokenKind.Star)
                        throw Error("expected comma between / and *");
                }
                else if (_cur.Kind == TokenKind.DoubleStar)
                {
                    Take();
                    Token n = ExpectParamName(terminator);
                    if (_cur.Kind == TokenKind.Assign)
                        throw Error("var-keyword argument cannot have default value");
                    CheckDup(seen, n);
                    kwArg = new Param(Src(n), n.Text, null);
                    phase = 2;
                }
                else if (_cur.Kind == TokenKind.Star)
                {
                    if (phase != 0)
                        throw Error("* argument may appear only once");
                    Token star = Take();
                    if (_cur.Kind == TokenKind.Name)
                    {
                        Token n = ExpectParamName(terminator);
                        if (_cur.Kind == TokenKind.Assign)
                            throw Error("var-positional argument cannot have default value");
                        CheckDup(seen, n);
                        starArg = new Param(Src(n), n.Text, null);
                    }
                    else
                    {
                        hasBareStar = true;
                        bareStar = star;
                    }
                    phase = 1;
                }
                else if (_cur.Kind == TokenKind.Name)
                {
                    Token n = Take();
                    string ann = SkipAnnotation(terminator);
                    ExprNode def = null;
                    if (_cur.Kind == TokenKind.Assign)
                    {
                        Take();
                        if (_cur.Kind == TokenKind.Comma || _cur.Kind == TokenKind.RightParen)   // CPython's invalid_default
                            throw PySyntaxErrorException.Syntax("expected default value expression", _cur.Line, _prevEndCol - 1);
                        def = ParseTest();
                    }
                    CheckDup(seen, n);
                    var p = new Param(Src(n), n.Text, def, ann);
                    if (phase == 0)
                    {
                        if (def != null)
                            sawDefault = true;
                        else if (sawDefault)
                            throw ErrorAt(n, "parameter without a default follows parameter with a default");
                        positional.Add(p);
                    }
                    else
                        kwonly.Add(p); // keyword-only: defaults optional, no non-default rule
                }
                else if (_cur.Kind == TokenKind.LeftParen)
                {
                    // CPython's invalid_parameters: names only in the parentheses, or it is just bad syntax at them
                    Token open = Take();
                    bool names = _cur.Kind == TokenKind.Name;
                    while (_cur.Kind == TokenKind.Name)
                    {
                        Take();
                        if (!Match(TokenKind.Comma))
                            break;
                    }
                    throw ErrorAt(open, !names || _cur.Kind != TokenKind.RightParen ? "invalid syntax"
                        : terminator == TokenKind.Colon ? "Lambda expression parameters cannot be parenthesized"
                        : "Function parameters cannot be parenthesized");
                }
                else
                    throw Unexpected(false);
                if (!Match(TokenKind.Comma))
                    break;
            }

            if (hasBareStar && kwonly.Count == 0)
                throw terminator == TokenKind.Colon ? Error("named arguments must follow bare *") : ErrorAt(bareStar, "named arguments must follow bare *");

            return new ParamList(listSrc, AstList.Freeze(positional), starArg,
                AstList.Freeze(kwonly), kwArg, hasBareStar, posOnlyCount);
        }

        private Token ExpectParamName(TokenKind terminator)
        {
            Token n = Expect(TokenKind.Name);
            SkipAnnotation(terminator);
            return n;
        }

        // PEP 3107 parameter annotations are parsed, and only a plain NAME is kept (typing.get_type_hints
        // answers with names, the __future__ shape); anything else is discarded. A lambda's terminator is
        // ':' itself, so a ':' there is the body separator, never an annotation.
        private string SkipAnnotation(TokenKind terminator)
        {
            if (_cur.Kind == TokenKind.Colon && terminator != TokenKind.Colon)
            {
                Take();        // ':'
                return AnnotationName(ParseTest());
            }
            return null;
        }

        // The annotation as typing.get_type_hints will report it: a plain name, or None (a literal in
        // the grammar, a name to the reader); anything else - a subscript, a string - is dropped.
        internal static string AnnotationName(ExprNode ann)
        {
            NameNode name = ann as NameNode;
            if (name != null)
                return name.Id;
            LiteralNode lit = ann as LiteralNode;
            return lit != null && lit.Kind == LiteralKind.None ? "None" : null;
        }

        private void CheckDup(HashSet<string> seen, Token n)
        {
            ForbidDebug(n.Text, Src(n));
            if (!seen.Add(n.Text))
                throw ErrorAt(n, "duplicate argument '" + n.Text + "' in function definition");
        }
    }
}

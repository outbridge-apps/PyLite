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
            Expect(TokenKind.Colon, "':'");
            ExprNode body = ParseTest();
            return new LambdaNode(Src(lam), prms, body);
        }

        // Explicit FSM (R-PAR-05). Terminator: ')' for def, ':' for lambda. Annotations rejected in v1.
        private ParamList ParseParamList(TokenKind terminator)
        {
            var positional = new List<Param>();
            var kwonly = new List<Param>();
            Param starArg = null, kwArg = null;
            bool hasBareStar = false;
            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            SourceInfo listSrc = CurSrc();

            int phase = 0;   // 0=positional, 1=kwonly, 2=after **kwargs
            bool sawDefault = false;
            int posOnlyCount = 0;   // PEP 570: params before '/'

            while (_cur.Kind != terminator)
            {
                if (_cur.Kind == TokenKind.Slash)
                {
                    if (phase != 0 || posOnlyCount != 0)
                        throw Error("invalid syntax");
                    if (positional.Count == 0)
                        throw Error("at least one argument must precede /");
                    posOnlyCount = positional.Count;
                    Take();
                }
                else if (_cur.Kind == TokenKind.DoubleStar)
                {
                    Take();
                    Token n = ExpectParamName(terminator);
                    CheckDup(seen, n);
                    kwArg = new Param(Src(n), n.Text, null);
                    phase = 2;
                }
                else if (_cur.Kind == TokenKind.Star)
                {
                    if (phase != 0)
                        throw Error("invalid syntax");
                    Token star = Take();
                    if (_cur.Kind == TokenKind.Name)
                    {
                        Token n = ExpectParamName(terminator);
                        CheckDup(seen, n);
                        starArg = new Param(Src(n), n.Text, null);
                    }
                    else
                        hasBareStar = true;
                    phase = 1;
                }
                else if (_cur.Kind == TokenKind.Name)
                {
                    if (phase == 2)
                        throw Error("arguments cannot follow **kwargs");
                    Token n = Take();
                    string ann = SkipAnnotation(terminator);
                    ExprNode def = Match(TokenKind.Assign) ? ParseTest() : null;
                    CheckDup(seen, n);
                    var p = new Param(Src(n), n.Text, def, ann);
                    if (phase == 0)
                    {
                        if (def != null)
                            sawDefault = true;
                        else if (sawDefault)
                            throw Error("non-default argument follows default argument");
                        positional.Add(p);
                    }
                    else
                        kwonly.Add(p); // keyword-only: defaults optional, no non-default rule
                }
                else
                    throw Error("invalid syntax");
                if (!Match(TokenKind.Comma))
                    break;
            }

            if (hasBareStar && kwonly.Count == 0)
                throw Error("named arguments must follow bare *");

            return new ParamList(listSrc, AstList.Freeze(positional), starArg,
                AstList.Freeze(kwonly), kwArg, hasBareStar, posOnlyCount);
        }

        private Token ExpectParamName(TokenKind terminator)
        {
            Token n = Expect(TokenKind.Name, "parameter name");
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
            if (!seen.Add(n.Text))
                throw ErrorAt(n, "duplicate argument '" + n.Text + "' in function definition");
        }
    }
}

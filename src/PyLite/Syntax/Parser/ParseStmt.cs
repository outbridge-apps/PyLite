using System;
using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class Parser
    {
        private ModuleNode ParseModuleInternal()
        {
            var body = new List<StmtNode>();
            while (_cur.Kind != TokenKind.EndOfFile)
            {
                if (_cur.Kind == TokenKind.NewLine)
                {
                    Take();
                    continue;
                }

                ParseStatement(body);
            }
            if (_futureImports != null)
                CheckFutureImports(body);
            return new ModuleNode(new SourceInfo(1, 1), AstList.Freeze(body));
        }

        // The features CPython 3.14 knows; each is a no-op here (annotations are never evaluated).
        private static readonly HashSet<string> FutureFeatures = new HashSet<string>(StringComparer.Ordinal)
        {
            "nested_scopes", "generators", "division", "absolute_import", "with_statement", "print_function",
            "unicode_literals", "generator_stop", "annotations",
        };

        // CPython's future.c and compile.c: only a docstring and other future imports may come before one, anywhere
        // else it is refused; its names are checked in order.
        private void CheckFutureImports(List<StmtNode> body)
        {
            var prefix = new HashSet<FromImportNode>();
            int i = body.Count > 0 && IsDocstring(body[0]) ? 1 : 0;
            for (; i < body.Count; i++)
            {
                FromImportNode fi = body[i] as FromImportNode;
                if (fi == null || fi.Module != "__future__")
                    break;
                prefix.Add(fi);
                foreach (ImportAlias a in fi.Names)
                {
                    if (a.DottedName == "braces")
                        throw ErrorAtSrc(a.Src, "not a chance");
                    if (a.DottedName == "barry_as_FLUFL")
                        throw ErrorAtSrc(a.Src, "future feature barry_as_FLUFL is not supported in this dialect");
                    if (!FutureFeatures.Contains(a.DottedName))
                        throw ErrorAtSrc(a.Src, "future feature " + a.DottedName + " is not defined");
                }
            }
            foreach (FromImportNode fi in _futureImports)
                if (!prefix.Contains(fi))
                    throw ErrorAtSrc(fi.Src, "from __future__ imports must occur at the beginning of the file");
        }

        private static bool IsDocstring(StmtNode s)
        {
            ExprStmtNode e = s as ExprStmtNode;
            LiteralNode lit = e != null ? e.Value as LiteralNode : null;
            return lit != null && lit.Kind == LiteralKind.Str;
        }

        private void ParseStatement(List<StmtNode> into)
        {
            EnterStmt();
            try
            {
                switch (_cur.Kind)
                {
                    case TokenKind.KwIf:
                        into.Add(ParseIf());
                        return;
                    case TokenKind.KwWhile:
                        into.Add(ParseWhile());
                        return;
                    case TokenKind.KwFor:
                        into.Add(ParseFor());
                        return;
                    case TokenKind.KwTry:
                        into.Add(ParseTry());
                        return;
                    case TokenKind.KwDef:
                        into.Add(ParseFuncDef());
                        return;
                    case TokenKind.At:
                        ParseDecorated(into);
                        return;
                    case TokenKind.KwClass:
                        ParseClassDef(into, AstList.Empty<ExprNode>());
                        return;
                    case TokenKind.KwWith:
                        ParseWith(into);
                        return;
                    case TokenKind.KwAsync:
                        throw Forbidden("async");
                    default:
                        if (LooksLikeMatchStatement())
                        {
                            into.Add(ParseMatch());
                            return;
                        }
                        ParseSimpleStmt(into);
                        return;
                }
            }
            finally
            {
                LeaveStmt();
            }
        }

        private void ParseSimpleStmt(List<StmtNode> into)
        {
            into.Add(ParseSmallStmt());
            while (Match(TokenKind.Semicolon))
            {
                if (_cur.Kind == TokenKind.NewLine || _cur.Kind == TokenKind.EndOfFile)
                    break;
                into.Add(ParseSmallStmt());
            }
            if (_cur.Kind == TokenKind.NewLine)
                Take();
            else if (_cur.Kind != TokenKind.EndOfFile)
                throw Unexpected(false, false);
        }

        private StmtNode ParseSmallStmt()
        {
            switch (_cur.Kind)
            {
                case TokenKind.KwDel: return ParseDelStmt();
                case TokenKind.KwPass: return new PassNode(Src(Take()));
                case TokenKind.KwBreak: return new BreakNode(Src(Take()));
                case TokenKind.KwContinue: return new ContinueNode(Src(Take()));
                case TokenKind.KwReturn: return ParseReturn();
                case TokenKind.KwImport: return ParseImport();
                case TokenKind.KwFrom: return ParseFromImport();
                case TokenKind.KwGlobal: return ParseGlobal();
                case TokenKind.KwNonlocal: return ParseNonlocal();
                case TokenKind.KwAssert: return ParseAssert();
                case TokenKind.KwRaise: return ParseRaise();
                case TokenKind.KwYield:
                case TokenKind.KwAwait: throw ForbiddenInFunction();
                default: return ParseExprStmt();
            }
        }

        private static bool IsStmtEndToken(TokenKind k)
        {
            return k == TokenKind.NewLine || k == TokenKind.Semicolon || k == TokenKind.EndOfFile;
        }

        private StmtNode ParseReturn()
        {
            Token kw = Take();
            ExprNode val = IsStmtEndToken(_cur.Kind) ? null : NotLoneStar(ParseTestListStarExpr());
            return new ReturnNode(Src(kw), val);
        }

        private StmtNode ParseAssert()
        {
            Token kw = Take();
            ExprNode test = ParseTest();
            ExprNode msg = Match(TokenKind.Comma) ? ParseTest() : null;
            return new AssertNode(Src(kw), test, msg);
        }

        private StmtNode ParseRaise()
        {
            Token kw = Take();
            ExprNode exc = null, cause = null;
            if (!IsStmtEndToken(_cur.Kind))
            {
                exc = ParseTest();
                if (Match(TokenKind.KwFrom))
                    cause = ParseTest();
            }
            return new RaiseNode(Src(kw), exc, cause);
        }

        private StmtNode ParseGlobal()
        {
            Token kw = Take();
            var names = new List<string> { Expect(TokenKind.Name).Text };
            while (Match(TokenKind.Comma))
                names.Add(Expect(TokenKind.Name).Text);
            return new GlobalNode(Src(kw), AstList.Freeze(names));
        }

        private StmtNode ParseNonlocal()
        {
            Token kw = Take();
            var names = new List<string> { Expect(TokenKind.Name).Text };
            while (Match(TokenKind.Comma))
                names.Add(Expect(TokenKind.Name).Text);
            return new NonlocalNode(Src(kw), AstList.Freeze(names));
        }

        // CPython's invalid_import* messages
        private StmtNode ParseImport()
        {
            Token kw = Take();
            if (_cur.Kind == TokenKind.NewLine || _cur.Kind == TokenKind.EndOfFile)
                throw Error("Expected one or more names after 'import'");
            var aliases = new List<ImportAlias> { ParseDottedAsName() };
            while (Match(TokenKind.Comma))
                aliases.Add(ParseDottedAsName());
            if (_cur.Kind == TokenKind.KwFrom)
                throw ErrorAt(kw, "Did you mean to use 'from ... import ...' instead?");
            return new ImportNode(Src(kw), AstList.Freeze(aliases));
        }

        private ImportAlias ParseDottedAsName()
        {
            Token n0 = Expect(TokenKind.Name);
            var sb = new StringBuilder(n0.Text);
            while (Match(TokenKind.Dot))
                sb.Append('.').Append(Expect(TokenKind.Name).Text);
            string asName = ParseImportTarget();
            ForbidDebug(asName ?? n0.Text, Src(n0));
            return new ImportAlias(Src(n0), sb.ToString(), asName);
        }

        private string ParseImportTarget()
        {
            if (!Match(TokenKind.KwAs))
                return null;
            if (_cur.Kind == TokenKind.Name)
            {
                // CPython's invalid_dotted_as_name: a name the statement cannot go on from is an expression
                TokenKind next = Peek2().Kind;
                if (next == TokenKind.Comma || next == TokenKind.RightParen || next == TokenKind.NewLine || next == TokenKind.Semicolon
                    || next == TokenKind.EndOfFile)
                    return Take().Text;
            }
            if (!StartsExpression(_cur.Kind))
                throw Unexpected(false);
            ExprNode target = ParseTest();
            throw ErrorAtSrc(target.Src, "cannot use " + ExprName(target) + " as import target");
        }

        private StmtNode ParseFromImport()
        {
            Token kw = Take();
            if (_cur.Kind == TokenKind.Dot)
                throw Forbidden("relative");
            Token m0 = Expect(TokenKind.Name);
            var mod = new StringBuilder(m0.Text);
            while (Match(TokenKind.Dot))
                mod.Append('.').Append(Expect(TokenKind.Name).Text);
            Expect(TokenKind.KwImport);
            if (_cur.Kind == TokenKind.Star)
                throw Forbidden("import-star");
            if (_cur.Kind == TokenKind.NewLine || _cur.Kind == TokenKind.EndOfFile)
                throw Error("Expected one or more names after 'import'");

            var aliases = new List<ImportAlias>();
            bool paren = Match(TokenKind.LeftParen);
            aliases.Add(ParseImportAsName());
            while (Match(TokenKind.Comma))
            {
                if (paren && _cur.Kind == TokenKind.RightParen)
                    break;
                if (!paren && (_cur.Kind == TokenKind.NewLine || _cur.Kind == TokenKind.EndOfFile))
                    throw Error("trailing comma not allowed without surrounding parentheses");
                aliases.Add(ParseImportAsName());
            }
            if (paren)
                ExpectClose(TokenKind.RightParen);
            var node = new FromImportNode(Src(kw), mod.ToString(), AstList.Freeze(aliases), false);
            if (node.Module == "__future__")
                (_futureImports ?? (_futureImports = new List<FromImportNode>())).Add(node);
            return node;
        }

        private ImportAlias ParseImportAsName()
        {
            Token n = Expect(TokenKind.Name);
            string asName = ParseImportTarget();
            ForbidDebug(asName ?? n.Text, Src(n));
            return new ImportAlias(Src(n), n.Text, asName);
        }
    }
}

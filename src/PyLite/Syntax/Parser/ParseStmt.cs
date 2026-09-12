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
            return new ModuleNode(new SourceInfo(1, 1), AstList.Freeze(body));
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
                        into.Add(ParseDecorated());
                        return;
                    case TokenKind.KwClass:
                        into.Add(ParseClassDef());
                        return;
                    case TokenKind.KwWith:
                        throw Forbidden("with");
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
                throw Error("expected newline");
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
                case TokenKind.KwYield: throw Forbidden("yield");
                case TokenKind.KwAwait: throw Forbidden("await");
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
            ExprNode val = IsStmtEndToken(_cur.Kind) ? null : ParseTestList();
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
            var names = new List<string> { Expect(TokenKind.Name, "name").Text };
            while (Match(TokenKind.Comma))
                names.Add(Expect(TokenKind.Name, "name").Text);
            return new GlobalNode(Src(kw), AstList.Freeze(names));
        }

        private StmtNode ParseNonlocal()
        {
            Token kw = Take();
            var names = new List<string> { Expect(TokenKind.Name, "name").Text };
            while (Match(TokenKind.Comma))
                names.Add(Expect(TokenKind.Name, "name").Text);
            return new NonlocalNode(Src(kw), AstList.Freeze(names));
        }

        private StmtNode ParseImport()
        {
            Token kw = Take();
            var aliases = new List<ImportAlias> { ParseDottedAsName() };
            while (Match(TokenKind.Comma))
                aliases.Add(ParseDottedAsName());
            return new ImportNode(Src(kw), AstList.Freeze(aliases));
        }

        private ImportAlias ParseDottedAsName()
        {
            Token n0 = Expect(TokenKind.Name, "module name");
            var sb = new StringBuilder(n0.Text);
            while (Match(TokenKind.Dot))
                sb.Append('.').Append(Expect(TokenKind.Name, "name").Text);
            string asName = Match(TokenKind.KwAs) ? Expect(TokenKind.Name, "name").Text : null;
            return new ImportAlias(Src(n0), sb.ToString(), asName);
        }

        private StmtNode ParseFromImport()
        {
            Token kw = Take();
            if (_cur.Kind == TokenKind.Dot)
                throw Forbidden("relative");
            Token m0 = Expect(TokenKind.Name, "module name");
            var mod = new StringBuilder(m0.Text);
            while (Match(TokenKind.Dot))
                mod.Append('.').Append(Expect(TokenKind.Name, "name").Text);
            Expect(TokenKind.KwImport, "'import'");
            if (_cur.Kind == TokenKind.Star)
                throw Forbidden("import-star");

            var aliases = new List<ImportAlias>();
            bool paren = Match(TokenKind.LeftParen);
            aliases.Add(ParseImportAsName());
            while (Match(TokenKind.Comma))
            {
                if (paren && _cur.Kind == TokenKind.RightParen)
                    break;
                aliases.Add(ParseImportAsName());
            }
            if (paren)
                Expect(TokenKind.RightParen, "')'");
            return new FromImportNode(Src(kw), mod.ToString(), AstList.Freeze(aliases), false);
        }

        private ImportAlias ParseImportAsName()
        {
            Token n = Expect(TokenKind.Name, "name");
            string asName = Match(TokenKind.KwAs) ? Expect(TokenKind.Name, "name").Text : null;
            return new ImportAlias(Src(n), n.Text, asName);
        }
    }
}

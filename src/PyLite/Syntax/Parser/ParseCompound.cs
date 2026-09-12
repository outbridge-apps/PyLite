using System.Collections.Generic;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class Parser
    {
        private IReadOnlyList<StmtNode> ParseSuite()
        {
            if (_cur.Kind != TokenKind.NewLine)
            {
                var single = new List<StmtNode>();
                ParseSimpleStmt(single);
                return AstList.Freeze(single);
            }
            Take();   // NEWLINE
            Expect(TokenKind.Indent, "an indented block");
            var body = new List<StmtNode>();
            while (_cur.Kind != TokenKind.Dedent)
            {
                if (_cur.Kind == TokenKind.NewLine)
                {
                    Take();
                    continue;
                }

                if (_cur.Kind == TokenKind.EndOfFile)
                    throw Error("unexpected EOF while parsing");
                ParseStatement(body);
            }
            Expect(TokenKind.Dedent, "dedent");
            return AstList.Freeze(body);
        }

        private StmtNode ParseIf()
        {
            Token kw = Take();
            ExprNode test = ParseTest();
            Expect(TokenKind.Colon, "':'");
            IReadOnlyList<StmtNode> body = ParseSuite();

            var elifs = new List<KeyValuePair<Token, KeyValuePair<ExprNode, IReadOnlyList<StmtNode>>>>();
            while (_cur.Kind == TokenKind.KwElif)
            {
                Token e = Take();
                ExprNode t = ParseTest();
                Expect(TokenKind.Colon, "':'");
                IReadOnlyList<StmtNode> b = ParseSuite();
                elifs.Add(new KeyValuePair<Token, KeyValuePair<ExprNode, IReadOnlyList<StmtNode>>>(
                    e, new KeyValuePair<ExprNode, IReadOnlyList<StmtNode>>(t, b)));
            }

            IReadOnlyList<StmtNode> orelse = AstList.Empty<StmtNode>();
            if (_cur.Kind == TokenKind.KwElse)
            {
                Take();
                Expect(TokenKind.Colon, "':'");
                orelse = ParseSuite();
            }

            for (int i = elifs.Count - 1; i >= 0; i--)
            {
                var inner = new IfNode(Src(elifs[i].Key), elifs[i].Value.Key, elifs[i].Value.Value, orelse);
                orelse = AstList.Freeze(new List<StmtNode> { inner });
            }
            return new IfNode(Src(kw), test, body, orelse);
        }

        private StmtNode ParseWhile()
        {
            Token kw = Take();
            ExprNode test = ParseTest();
            Expect(TokenKind.Colon, "':'");
            IReadOnlyList<StmtNode> body = ParseSuite();
            IReadOnlyList<StmtNode> orelse = AstList.Empty<StmtNode>();
            if (_cur.Kind == TokenKind.KwElse)
            {
                Take();
                Expect(TokenKind.Colon, "':'");
                orelse = ParseSuite();
            }
            return new WhileNode(Src(kw), test, body, orelse);
        }

        private StmtNode ParseFor()
        {
            Token kw = Take();
            ExprNode target = ParseTargetList();
            ValidateAssignTarget(target);
            Expect(TokenKind.KwIn, "'in'");
            ExprNode iter = ParseTestList();
            Expect(TokenKind.Colon, "':'");
            IReadOnlyList<StmtNode> body = ParseSuite();
            IReadOnlyList<StmtNode> orelse = AstList.Empty<StmtNode>();
            if (_cur.Kind == TokenKind.KwElse)
            {
                Take();
                Expect(TokenKind.Colon, "':'");
                orelse = ParseSuite();
            }
            return new ForNode(Src(kw), target, iter, body, orelse);
        }

        private StmtNode ParseTry()
        {
            Token kw = Take();
            Expect(TokenKind.Colon, "':'");
            IReadOnlyList<StmtNode> body = ParseSuite();

            var handlers = new List<ExceptHandler>();
            IReadOnlyList<StmtNode> orelse = AstList.Empty<StmtNode>();
            IReadOnlyList<StmtNode> finallyBody = AstList.Empty<StmtNode>();
            bool sawExcept = false, sawBare = false, sawElse = false, sawFinally = false;

            while (true)
            {
                if (_cur.Kind == TokenKind.KwExcept)
                {
                    if (sawElse || sawFinally)
                        throw Error("invalid syntax");
                    if (sawBare)
                        throw Error("default 'except:' must be last");
                    Token e = Take();
                    ExprNode type = null;
                    string name = null;
                    if (_cur.Kind != TokenKind.Colon)
                    {
                        type = ParseTest();
                        if (Match(TokenKind.KwAs))
                            name = Expect(TokenKind.Name, "name").Text;
                    }
                    else
                        sawBare = true;
                    Expect(TokenKind.Colon, "':'");
                    IReadOnlyList<StmtNode> hb = ParseSuite();
                    handlers.Add(new ExceptHandler(Src(e), type, name, hb));
                    sawExcept = true;
                }
                else if (_cur.Kind == TokenKind.KwElse)
                {
                    if (!sawExcept)
                        throw Error("'else' without 'except'");
                    if (sawElse || sawFinally)
                        throw Error("invalid syntax");
                    Take();
                    Expect(TokenKind.Colon, "':'");
                    orelse = ParseSuite();
                    sawElse = true;
                }
                else if (_cur.Kind == TokenKind.KwFinally)
                {
                    if (sawFinally)
                        throw Error("invalid syntax");
                    Take();
                    Expect(TokenKind.Colon, "':'");
                    finallyBody = ParseSuite();
                    sawFinally = true;
                }
                else
                    break;
            }

            if (!sawExcept && finallyBody.Count == 0)
                throw Error("expected 'except' or 'finally' block");
            return new TryNode(Src(kw), body, AstList.Freeze(handlers), orelse, finallyBody);
        }

        private StmtNode ParseFuncDef() { return ParseFuncDef(AstList.Empty<ExprNode>()); }

        private StmtNode ParseFuncDef(IReadOnlyList<ExprNode> decorators)
        {
            Token kw = Take();
            Token name = Expect(TokenKind.Name, "function name");
            Expect(TokenKind.LeftParen, "'('");
            ParamList prms = ParseParamList(TokenKind.RightParen);
            Expect(TokenKind.RightParen, "')'");
            string returnAnn = null;
            if (Match(TokenKind.Arrow))
                returnAnn = AnnotationName(ParseTest());   // a plain name (or None) is kept, else dropped
            Expect(TokenKind.Colon, "':'");
            IReadOnlyList<StmtNode> body = ParseSuite();
            return new FuncDefNode(Src(kw), name.Text, prms, body, decorators, returnAnn);
        }

        // PEP 614: '@' expr NEWLINE, one per decorator line, then the decorated 'def'/'class'. Bottom-up.
        private StmtNode ParseDecorated()
        {
            var decorators = new List<ExprNode>();
            while (_cur.Kind == TokenKind.At)
            {
                Take();   // '@'
                decorators.Add(ParseTest());
                Expect(TokenKind.NewLine, "newline after decorator");
            }
            IReadOnlyList<ExprNode> frozen = AstList.Freeze(decorators);
            if (_cur.Kind == TokenKind.KwClass)
                return ParseClassDef(frozen);
            if (_cur.Kind != TokenKind.KwDef)
                throw Error("expected 'def' or 'class' after decorator");
            return ParseFuncDef(frozen);
        }

        // record class: 'class' NAME ['(' base ')'] ':' suite. Single inheritance only. The
        // body is restricted to def methods, class-body field annotations (@dataclass), and pass.
        private StmtNode ParseClassDef() { return ParseClassDef(AstList.Empty<ExprNode>()); }

        private StmtNode ParseClassDef(IReadOnlyList<ExprNode> decorators)
        {
            Token kw = Take();   // 'class'
            Token name = Expect(TokenKind.Name, "class name");
            ExprNode baseCls = null;
            if (Match(TokenKind.LeftParen))
            {
                if (_cur.Kind != TokenKind.RightParen)
                {
                    baseCls = ParseTest();
                    if (_cur.Kind == TokenKind.Comma)
                        throw Error("multiple inheritance is not supported in this dialect");
                }
                Expect(TokenKind.RightParen, "')'");
            }
            Expect(TokenKind.Colon, "':'");
            var methods = new List<FuncDefNode>();
            var fields = new List<ClassField>();
            ParseClassSuite(methods, fields);
            return new ClassDefNode(Src(kw), name.Text, baseCls, AstList.Freeze(methods), AstList.Freeze(fields), decorators);
        }

        private void ParseClassSuite(List<FuncDefNode> methods, List<ClassField> fields)
        {
            if (_cur.Kind != TokenKind.NewLine)
            {
                ParseClassStmt(methods, fields);
                return;
            }
            Take();   // NEWLINE
            Expect(TokenKind.Indent, "an indented block");
            while (_cur.Kind != TokenKind.Dedent)
            {
                if (_cur.Kind == TokenKind.NewLine)
                {
                    Take();
                    continue;
                }
                if (_cur.Kind == TokenKind.EndOfFile)
                    throw Error("unexpected EOF while parsing");
                ParseClassStmt(methods, fields);
            }
            Take();   // DEDENT
        }

        private void ParseClassStmt(List<FuncDefNode> methods, List<ClassField> fields)
        {
            if (_cur.Kind == TokenKind.KwDef)
            {
                methods.Add(TakeMethod(ParseFuncDef()));
                return;
            }
            if (_cur.Kind == TokenKind.KwPass)
            {
                Take();
                ExpectClassStmtEnd();
                return;
            }
            // class-body field annotation: NAME ':' annotation ['=' default]
            if (_cur.Kind == TokenKind.Name && Peek2().Kind == TokenKind.Colon)
            {
                Token n = Take();
                Take();          // ':'
                ExprNode ann = ParseTest();     // only a plain-name annotation is kept (dataclasses Field.type)
                ExprNode def = Match(TokenKind.Assign) ? ParseTest() : null;
                IndexNode subscripted = ann as IndexNode;     // ClassVar[...] / InitVar[...]: the head decides the kind
                NameNode head = subscripted == null ? null : subscripted.Value as NameNode;
                fields.Add(new ClassField(Src(n), n.Text, def, AnnotationName(ann), head == null ? null : head.Id));
                ExpectClassStmtEnd();
                return;
            }
            throw Error("class body may contain only method definitions and field annotations in this dialect");
        }

        // The protocol methods a record class may define. Everything else is refused here so that no
        // dunder is ever written and silently ignored: the arithmetic operators, the attribute hooks
        // (__getattr__/__setattr__/__delattr__), __new__/__del__, __enter__/__exit__ (no `with`),
        // __format__ and __class_getitem__ are all out.
        private static readonly System.Collections.Generic.HashSet<string> AllowedDunders =
            new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal)
            {
                "__init__", "__post_init__", "__repr__", "__str__",
                "__eq__", "__ne__", "__lt__", "__le__", "__gt__", "__ge__",
                "__hash__", "__bool__", "__len__", "__call__",
                "__contains__", "__getitem__", "__setitem__", "__delitem__",
                "__iter__", "__next__", "__reversed__",
            };

        private FuncDefNode TakeMethod(StmtNode def)
        {
            FuncDefNode fd = (FuncDefNode)def;
            if (IsDunder(fd.Name) && !AllowedDunders.Contains(fd.Name))
                throw ErrorAtSrc(fd.Src, "this dialect does not support '" + fd.Name + "' definitions");
            return fd;
        }

        private static bool IsDunder(string name)
        {
            return name.Length > 4 && name.StartsWith("__", System.StringComparison.Ordinal)
                && name.EndsWith("__", System.StringComparison.Ordinal);
        }

        private void ExpectClassStmtEnd()
        {
            if (_cur.Kind == TokenKind.NewLine)
                Take();
            else if (_cur.Kind != TokenKind.EndOfFile && _cur.Kind != TokenKind.Dedent)
                throw Error("expected newline");
        }
    }
}

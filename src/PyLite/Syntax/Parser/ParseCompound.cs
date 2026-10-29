using System.Collections.Generic;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class Parser
    {
        // `: suite`, the tail of every compound statement header.
        private IReadOnlyList<StmtNode> ParseColonSuite()
        {
            Expect(TokenKind.Colon, "':'");
            return ParseSuite();
        }

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
            IReadOnlyList<StmtNode> body = ParseColonSuite();

            var elifs = new List<KeyValuePair<Token, KeyValuePair<ExprNode, IReadOnlyList<StmtNode>>>>();
            while (_cur.Kind == TokenKind.KwElif)
            {
                Token e = Take();
                ExprNode t = ParseTest();
                IReadOnlyList<StmtNode> b = ParseColonSuite();
                elifs.Add(new KeyValuePair<Token, KeyValuePair<ExprNode, IReadOnlyList<StmtNode>>>(
                    e, new KeyValuePair<ExprNode, IReadOnlyList<StmtNode>>(t, b)));
            }

            IReadOnlyList<StmtNode> orelse = AstList.Empty<StmtNode>();
            if (_cur.Kind == TokenKind.KwElse)
            {
                Take();
                orelse = ParseColonSuite();
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
            IReadOnlyList<StmtNode> body = ParseColonSuite();
            IReadOnlyList<StmtNode> orelse = AstList.Empty<StmtNode>();
            if (_cur.Kind == TokenKind.KwElse)
            {
                Take();
                orelse = ParseColonSuite();
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
            IReadOnlyList<StmtNode> body = ParseColonSuite();
            IReadOnlyList<StmtNode> orelse = AstList.Empty<StmtNode>();
            if (_cur.Kind == TokenKind.KwElse)
            {
                Take();
                orelse = ParseColonSuite();
            }
            return new ForNode(Src(kw), target, iter, body, orelse);
        }

        private StmtNode ParseTry()
        {
            Token kw = Take();
            IReadOnlyList<StmtNode> body = ParseColonSuite();

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
                    IReadOnlyList<StmtNode> hb = ParseColonSuite();
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
                    orelse = ParseColonSuite();
                    sawElse = true;
                }
                else if (_cur.Kind == TokenKind.KwFinally)
                {
                    if (sawFinally)
                        throw Error("invalid syntax");
                    Take();
                    finallyBody = ParseColonSuite();
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

        // method: a def directly in a class body, whose super() calls name the class and the first parameter.
        private StmtNode ParseFuncDef(IReadOnlyList<ExprNode> decorators, bool method = false)
        {
            Token kw = Take();
            Token name = Expect(TokenKind.Name, "function name");
            Expect(TokenKind.LeftParen, "'('");
            ParamList prms = ParseParamList(TokenKind.RightParen);
            Expect(TokenKind.RightParen, "')'");
            string returnAnn = null;
            IReadOnlyList<StmtNode> body;
            string savedSelf = _superSelf;
            bool savedIn = _inMethod;
            _superSelf = method && prms.Positional.Count > 0 ? prms.Positional[0].Name : null;
            _inMethod |= method;
            _classSuspend++;   // a method body does not see the class body's names; its defaults above do
            try
            {
                if (Match(TokenKind.Arrow))
                    returnAnn = AnnotationName(ParseTest());   // a plain name (or None) is kept, else dropped
                body = ParseColonSuite();
            }
            finally
            {
                _classSuspend--;
                _superSelf = savedSelf;
                _inMethod = savedIn;
            }
            return new FuncDefNode(Src(kw), name.Text, prms, body, decorators, returnAnn);
        }

        // PEP 614: '@' expr NEWLINE, one per decorator line, then the decorated 'def'/'class'. Bottom-up.
        private void ParseDecorated(List<StmtNode> into)
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
            {
                ParseClassDef(into, frozen);
                return;
            }
            if (_cur.Kind != TokenKind.KwDef)
                throw Error("expected 'def' or 'class' after decorator");
            into.Add(ParseFuncDef(frozen));
        }

        // record class: 'class' NAME ['(' base ')'] ':' suite. Single inheritance only. The body holds
        // def methods, field annotations (@dataclass), constants (NAME = expr), docstrings and pass. A
        // constant's value goes to a hidden variable assigned just before the class statement, and a later
        // reference to it inside the class body (not inside a method body, which cannot see the class
        // scope in CPython either) is parsed as that variable.
        private void ParseClassDef(List<StmtNode> into, IReadOnlyList<ExprNode> decorators)
        {
            Dictionary<string, string> savedRenames = _classRenames;
            int savedSuspend = _classSuspend;
            string savedSelf = _superSelf, savedCell = _superCell;
            bool savedIn = _inMethod;
            _classRenames = new Dictionary<string, string>(System.StringComparer.Ordinal);
            _classSuspend = 0;
            _superSelf = null;
            _superCell = null;
            _inMethod = false;
            var hidden = new List<StmtNode>();
            ClassDefNode cls;
            try
            {
                cls = ParseClassDefCore(decorators, hidden);
            }
            finally
            {
                _classRenames = savedRenames;
                _classSuspend = savedSuspend;
                _superSelf = savedSelf;
                _superCell = savedCell;
                _inMethod = savedIn;
            }
            into.AddRange(hidden);
            into.Add(cls);
        }

        private ClassDefNode ParseClassDefCore(IReadOnlyList<ExprNode> decorators, List<StmtNode> hidden)
        {
            Token kw = Take();   // 'class'
            Token name = Expect(TokenKind.Name, "class name");
            ExprNode baseCls = null;
            string mixin = null;
            if (Match(TokenKind.LeftParen))
            {
                if (_cur.Kind != TokenKind.RightParen)
                {
                    baseCls = ParseTest();
                    if (_cur.Kind == TokenKind.Comma)
                    {
                        // the one form allowed: class X(str, Enum) / class X(int, Enum), the mixed-in type first
                        NameNode first = baseCls as NameNode;
                        if (first == null || (first.Id != "str" && first.Id != "int") || Peek2().Kind == TokenKind.RightParen)
                            throw Error("multiple inheritance is not supported in this dialect");
                        Take();   // ','
                        mixin = first.Id;
                        baseCls = ParseTest();
                        if (_cur.Kind == TokenKind.Comma)
                            throw Error("multiple inheritance is not supported in this dialect");
                    }
                }
                Expect(TokenKind.RightParen, "')'");
            }
            Expect(TokenKind.Colon, "':'");
            var methods = new List<FuncDefNode>();
            var fields = new List<ClassField>();
            var constants = new List<ClassConstant>();
            ParseClassSuite(methods, fields, constants, hidden);
            return new ClassDefNode(Src(kw), name.Text, baseCls, AstList.Freeze(methods), AstList.Freeze(fields), decorators,
                AstList.Freeze(constants), _superCell, mixin);
        }

        private void ParseClassSuite(List<FuncDefNode> methods, List<ClassField> fields, List<ClassConstant> constants,
            List<StmtNode> hidden)
        {
            if (_cur.Kind != TokenKind.NewLine)
            {
                ParseClassStmt(methods, fields, constants, hidden);
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
                ParseClassStmt(methods, fields, constants, hidden);
            }
            Take();   // DEDENT
        }

        private void ParseClassStmt(List<FuncDefNode> methods, List<ClassField> fields, List<ClassConstant> constants,
            List<StmtNode> hidden)
        {
            if (_cur.Kind == TokenKind.KwDef)
            {
                methods.Add(TakeMethod(ParseFuncDef(AstList.Empty<ExprNode>(), true)));
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
                _classSuspend++;   // an annotation is kept as the name written, never as a constant's variable
                ExprNode ann;
                try
                {
                    ann = ParseTest();     // only a plain-name annotation is kept (dataclasses Field.type)
                }
                finally
                {
                    _classSuspend--;
                }
                ExprNode def = Match(TokenKind.Assign) ? ParseTest() : null;
                IndexNode subscripted = ann as IndexNode;     // ClassVar[...] / InitVar[...]: the head decides the kind
                NameNode head = subscripted == null ? null : subscripted.Value as NameNode;
                fields.Add(new ClassField(Src(n), n.Text, def, AnnotationName(ann), head == null ? null : head.Id));
                ExpectClassStmtEnd();
                return;
            }
            // a constant: NAME = expr, evaluated once, in order, when the class is created
            if (_cur.Kind == TokenKind.Name && Peek2().Kind == TokenKind.Assign)
            {
                Token n = _cur;
                if (IsDunder(n.Text) && n.Text != "__match_args__" && n.Text != "__slots__" && n.Text != "__hash__")
                    throw Error("this dialect does not support assigning '" + n.Text + "' in a class body");
                Take();
                Take();          // '='
                ExprNode value = ParseTestList();   // earlier constants already read as their hidden variables
                if (_cur.Kind == TokenKind.Assign)
                    throw Error("a class body assignment takes one name in this dialect");
                string hiddenName = "$cls" + (++_classConstSeq) + "_" + n.Text;
                SourceInfo s = Src(n);
                hidden.Add(new AssignNode(s, AstList.Freeze(new List<ExprNode> { new NameNode(s, hiddenName) }), value));
                constants.Add(new ClassConstant(s, n.Text, new NameNode(s, hiddenName)));
                _classRenames[n.Text] = hiddenName;
                ExpectClassStmtEnd();
                return;
            }
            // a docstring (or any string literal statement) is accepted and ignored
            if (_cur.Kind == TokenKind.StringLiteral)
            {
                ParseStringLike();
                ExpectClassStmtEnd();
                return;
            }
            throw Error("class body may contain only methods, field annotations, constants (NAME = value) and docstrings in this dialect");
        }

        // The protocol methods a record class may define. Everything else is refused here so that no
        // dunder is ever written and silently ignored: the arithmetic operators, the attribute hooks
        // (__getattr__/__setattr__/__delattr__), __new__/__del__, __format__ and __class_getitem__ are all
        // out (__enter__/__exit__ are in, for `with`).
        private static readonly System.Collections.Generic.HashSet<string> AllowedDunders =
            new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal)
            {
                "__init__", "__post_init__", "__repr__", "__str__",
                "__eq__", "__ne__", "__lt__", "__le__", "__gt__", "__ge__",
                "__hash__", "__bool__", "__len__", "__call__",
                "__contains__", "__getitem__", "__setitem__", "__delitem__",
                "__iter__", "__next__", "__reversed__",
                "__enter__", "__exit__",
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

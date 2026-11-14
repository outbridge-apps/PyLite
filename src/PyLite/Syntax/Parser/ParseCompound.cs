using System.Collections.Generic;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    internal sealed partial class Parser
    {
        // `: suite`, the tail of every compound statement header. CPython forces the colon of def, else, try and
        // finally ("expected ':'"); the others say so only at the end of the line, else "invalid syntax".
        private IReadOnlyList<StmtNode> ParseColonSuite(Token kw, string header, bool forced = false)
        {
            ExpectHeaderColon(forced);
            return ParseSuite(kw, header);
        }

        private void ExpectHeaderColon(bool forced)
        {
            if (_cur.Kind != TokenKind.Colon)
                throw forced || _cur.Kind == TokenKind.NewLine ? Error("expected ':'") : Unexpected(false);
            Take();
        }

        private IReadOnlyList<StmtNode> ParseSuite(Token kw, string header)
        {
            if (_cur.Kind != TokenKind.NewLine)
            {
                var single = new List<StmtNode>();
                ParseSimpleStmt(single);
                return AstList.Freeze(single);
            }
            ExpectIndent(Take(), kw, header);
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
            Take();   // DEDENT
            return AstList.Freeze(body);
        }

        // IndentationError naming the header; at the end of the source it points at the header's line end.
        private void ExpectIndent(Token newline, Token kw, string header)
        {
            if (_cur.Kind == TokenKind.Indent)
            {
                Take();
                return;
            }
            int line = _cur.Line, col = _cur.Col;
            if (_cur.Kind == TokenKind.EndOfFile || (_cur.Kind == TokenKind.Dedent && _lexer.AtEnd))
                _lexer.EndOfSource(out line, out col);   // CPython: past the source's last line, blank or comment lines included
            throw PySyntaxErrorException.Indentation("expected an indented block after " + header + " on line "
                + kw.Line.ToString(System.Globalization.CultureInfo.InvariantCulture), line, col);
        }

        private IReadOnlyList<StmtNode> ParseElse()
        {
            Token kw = Take();
            return ParseColonSuite(kw, "'else' statement", true);
        }

        private StmtNode ParseIf()
        {
            Token kw = Take();
            ExprNode test = ParseTest();
            IReadOnlyList<StmtNode> body = ParseColonSuite(kw, "'if' statement");

            var elifs = new List<KeyValuePair<Token, KeyValuePair<ExprNode, IReadOnlyList<StmtNode>>>>();
            while (_cur.Kind == TokenKind.KwElif)
            {
                Token e = Take();
                ExprNode t = ParseTest();
                IReadOnlyList<StmtNode> b = ParseColonSuite(e, "'elif' statement");
                elifs.Add(new KeyValuePair<Token, KeyValuePair<ExprNode, IReadOnlyList<StmtNode>>>(
                    e, new KeyValuePair<ExprNode, IReadOnlyList<StmtNode>>(t, b)));
            }

            IReadOnlyList<StmtNode> orelse = AstList.Empty<StmtNode>();
            if (_cur.Kind == TokenKind.KwElse)
                orelse = ParseElse();

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
            IReadOnlyList<StmtNode> body = ParseColonSuite(kw, "'while' statement");
            IReadOnlyList<StmtNode> orelse = AstList.Empty<StmtNode>();
            if (_cur.Kind == TokenKind.KwElse)
                orelse = ParseElse();
            return new WhileNode(Src(kw), test, body, orelse);
        }

        private StmtNode ParseFor()
        {
            Token kw = Take();
            ExprNode target = ParseTargetList();
            ValidateAssignTarget(target);
            Expect(TokenKind.KwIn);
            ExprNode iter = NotLoneStar(ParseTestListStarExpr());
            IReadOnlyList<StmtNode> body = ParseColonSuite(kw, "'for' statement");
            IReadOnlyList<StmtNode> orelse = AstList.Empty<StmtNode>();
            if (_cur.Kind == TokenKind.KwElse)
                orelse = ParseElse();
            return new ForNode(Src(kw), target, iter, body, orelse);
        }

        private StmtNode ParseTry()
        {
            Token kw = Take();
            IReadOnlyList<StmtNode> body = ParseColonSuite(kw, "'try' statement", true);

            var handlers = new List<ExceptHandler>();
            IReadOnlyList<StmtNode> orelse = AstList.Empty<StmtNode>();
            IReadOnlyList<StmtNode> finallyBody = AstList.Empty<StmtNode>();
            bool sawExcept = false, sawBare = false, sawElse = false, sawFinally = false;
            Token bare = default(Token);

            while (true)
            {
                if (_cur.Kind == TokenKind.KwExcept)
                {
                    if (sawElse || sawFinally)
                        throw Unexpected(false);
                    if (sawBare)
                        throw ErrorAt(bare, "default 'except:' must be last");
                    Token e = Take();
                    ExprNode type = null;
                    string name = null;
                    if (_cur.Kind != TokenKind.Colon && _cur.Kind != TokenKind.NewLine)
                    {
                        type = ParseTest();
                        if (_cur.Kind == TokenKind.Comma)
                        {
                            // PEP 758: several types without parentheses, when there is no 'as'
                            var types = new List<ExprNode> { type };
                            while (Match(TokenKind.Comma))
                                types.Add(ParseTest());
                            if (_cur.Kind == TokenKind.KwAs)
                                throw ErrorAtSrc(type.Src, "multiple exception types must be parenthesized when using 'as'");
                            type = new TupleNode(type.Src, AstList.Freeze(types));
                        }
                        if (Match(TokenKind.KwAs))
                        {
                            // A bare NAME; CPython names any other expression only when a ':' follows it
                            Token first = _cur;
                            Token past = first.Kind == TokenKind.Name ? Peek2() : first;
                            bool bareName = first.Kind == TokenKind.Name && past.Kind == TokenKind.Colon;
                            ExprNode target = ParseTest();
                            if (!bareName)
                            {
                                if (_cur.Kind == TokenKind.Colon)
                                    throw ErrorAtSrc(target.Src, "cannot use except statement with " + ExprName(target));
                                throw ErrorAt(past, "invalid syntax");
                            }
                            name = ((NameNode)target).Id;
                            ForbidDebug(name, Src(e));
                        }
                    }
                    else if (_cur.Kind == TokenKind.Colon)
                    {
                        sawBare = true;
                        bare = e;
                    }
                    IReadOnlyList<StmtNode> hb = ParseColonSuite(e, "'except' statement");
                    handlers.Add(new ExceptHandler(Src(e), type, name, hb));
                    sawExcept = true;
                }
                else if (_cur.Kind == TokenKind.KwElse)
                {
                    if (sawElse || sawFinally)
                        throw Unexpected(false);
                    if (!sawExcept)
                        throw Error("expected 'except' or 'finally' block");
                    orelse = ParseElse();
                    sawElse = true;
                }
                else if (_cur.Kind == TokenKind.KwFinally)
                {
                    if (sawFinally)
                        throw Unexpected(false);
                    Token f = Take();
                    finallyBody = ParseColonSuite(f, "'finally' statement", true);
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
            Token name = Expect(TokenKind.Name);
            ForbidDebug(name.Text, Src(kw));
            Expect(TokenKind.LeftParen, "'('");
            ParamList prms = ParseParamList(TokenKind.RightParen);
            ExpectClose(TokenKind.RightParen);
            string returnAnn = null;
            IReadOnlyList<StmtNode> body;
            string savedSelf = _superSelf;
            bool savedIn = _inMethod;
            _superSelf = method && prms.Positional.Count > 0 ? prms.Positional[0].Name : null;
            _inMethod |= method;
            _classSuspend++;   // a method body does not see the class body's names; its defaults above do
            _funcDepth++;
            try
            {
                if (_cur.Kind == TokenKind.Arrow)
                {
                    Token arrow = Take();
                    if (!StartsOperand(_cur.Kind))
                        throw ErrorAt(arrow, "expected ':'");   // CPython's forced ':' after ')', where the annotation failed
                    returnAnn = AnnotationName(ParseTest());   // a plain name (or None) is kept, else dropped
                }
                body = ParseColonSuite(kw, "function definition", true);
            }
            finally
            {
                _classSuspend--;
                _funcDepth--;
                _superSelf = savedSelf;
                _inMethod = savedIn;
            }
            return new FuncDefNode(Src(kw), method ? Mangle(name.Text) : name.Text, prms, body, decorators, returnAnn);
        }

        // PEP 614: '@' expr NEWLINE, one per decorator line, then the decorated 'def'/'class'. Bottom-up.
        private void ParseDecorated(List<StmtNode> into)
        {
            var decorators = new List<ExprNode>();
            while (_cur.Kind == TokenKind.At)
            {
                Take();   // '@'
                decorators.Add(ParseTest());
                Expect(TokenKind.NewLine);
            }
            IReadOnlyList<ExprNode> frozen = AstList.Freeze(decorators);
            if (_cur.Kind == TokenKind.KwClass)
            {
                ParseClassDef(into, frozen);
                return;
            }
            if (_cur.Kind != TokenKind.KwDef)
                throw Unexpected(false);
            into.Add(ParseFuncDef(frozen));
        }

        // record class: 'class' NAME ['(' base ')'] ':' suite. Single inheritance only. The body holds
        // def methods, field annotations (@dataclass), constants (NAME = expr), docstrings and pass. Every
        // value the class statement computes (decorators, base, constants, field and method defaults) goes to
        // a hidden variable assigned just before it, in CPython's order: decorators, base, then the body top
        // to bottom. A later reference to a constant or field inside the class body (not inside a method
        // body, which cannot see the class scope in CPython either) is parsed as its variable.
        private void ParseClassDef(List<StmtNode> into, IReadOnlyList<ExprNode> decorators)
        {
            Dictionary<string, string> savedRenames = _classRenames;
            List<NameNode> savedReads = _classReads;
            int savedSuspend = _classSuspend;
            string savedSelf = _superSelf, savedCell = _superCell, savedMangle = _mangleClass;
            Dictionary<string, string> savedMangled = _mangled;
            bool savedIn = _inMethod;
            _classRenames = new Dictionary<string, string>(System.StringComparer.Ordinal);
            _classSuspend = 0;
            _superSelf = null;
            _superCell = null;
            _inMethod = false;
            _classReads = null;   // set for the body only: the base is not read in the class namespace
            var hidden = new List<StmtNode>();
            var hoisted = new List<ExprNode>(decorators.Count);
            foreach (ExprNode d in decorators)
                hoisted.Add(Hoist(hidden, d, "decorator", false));
            ClassDefNode cls;
            try
            {
                cls = ParseClassDefCore(AstList.Freeze(hoisted), hidden);
            }
            finally
            {
                _classRenames = savedRenames;
                _classReads = savedReads;
                _classSuspend = savedSuspend;
                _superSelf = savedSelf;
                _superCell = savedCell;
                _inMethod = savedIn;
                _mangleClass = savedMangle;
                _mangled = savedMangled;
            }
            into.AddRange(hidden);
            into.Add(cls);
        }

        private ClassDefNode ParseClassDefCore(IReadOnlyList<ExprNode> decorators, List<StmtNode> hidden)
        {
            Token kw = Take();   // 'class'
            Token name = Expect(TokenKind.Name);
            ForbidDebug(name.Text, Src(kw));
            ExprNode baseCls = null;
            string mixin = null;
            if (Match(TokenKind.LeftParen))
            {
                // CPython's arguments first, with their errors; the dialect's limits after them
                IReadOnlyList<Arg> bases = ParseArgList(true);
                ExpectClose(TokenKind.RightParen);
                foreach (Arg a in bases)
                {
                    if (a.Kind == ArgKind.Keyword)
                        throw ErrorAtSrc(a.Src, "class keyword arguments are not supported in this dialect");
                    if (a.Kind != ArgKind.Positional)
                        throw ErrorAtSrc(a.Src, "unpacking in class bases is not supported in this dialect");
                }
                if (bases.Count == 1)
                    baseCls = bases[0].Value;
                else if (bases.Count == 2)
                {
                    // the one form allowed: class X(str, Enum) / class X(int, Enum), the mixed-in type first
                    NameNode first = bases[0].Value as NameNode;
                    if (first == null || (first.Id != "str" && first.Id != "int"))
                        throw ErrorAtSrc(bases[1].Src, "multiple inheritance is not supported in this dialect");
                    mixin = first.Id;
                    baseCls = bases[1].Value;
                }
                else if (bases.Count > 2)
                    throw ErrorAtSrc(bases[2].Src, "multiple inheritance is not supported in this dialect");
            }
            if (baseCls != null)
                baseCls = Hoist(hidden, baseCls, "base", false);
            ExpectHeaderColon(false);
            _mangleClass = name.Text;   // the body's private names, not the base's
            _mangled = null;
            var methods = new List<FuncDefNode>();
            var fields = new List<ClassField>();
            var constants = new List<ClassConstant>();
            _classReads = new List<NameNode>();
            ParseClassSuite(kw, methods, fields, constants, hidden);
            foreach (NameNode read in _classReads)
            {
                if (_classRenames.ContainsKey(read.Id))
                    read.ReadsGlobalFirst = true;   // read before the class bound it
            }
            return new ClassDefNode(Src(kw), name.Text, baseCls, AstList.Freeze(methods), AstList.Freeze(fields), decorators,
                AstList.Freeze(constants), _superCell, mixin);
        }

        private void ParseClassSuite(Token kw, List<FuncDefNode> methods, List<ClassField> fields, List<ClassConstant> constants,
            List<StmtNode> hidden)
        {
            if (_cur.Kind != TokenKind.NewLine)
            {
                ParseClassStmt(methods, fields, constants, hidden);
                return;
            }
            ExpectIndent(Take(), kw, "class definition");
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
                methods.Add(HoistDefaults(TakeMethod(ParseFuncDef(AstList.Empty<ExprNode>(), true)), hidden));
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
                if (def != null)
                {
                    NameNode hiddenDef = Hoist(hidden, def, n.Text, true);
                    _classRenames[Mangle(n.Text)] = hiddenDef.Id;   // the field is in the class namespace from here
                    def = hiddenDef;
                }
                IndexNode subscripted = ann as IndexNode;     // ClassVar[...] / InitVar[...]: the head decides the kind
                NameNode head = subscripted == null ? null : subscripted.Value as NameNode;
                fields.Add(new ClassField(Src(n), Mangle(n.Text), def, AnnotationName(ann), head == null ? null : head.Id));
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
                NameNode hiddenVar = Hoist(hidden, value, n.Text, true);
                constants.Add(new ClassConstant(Src(n), Mangle(n.Text), hiddenVar));
                _classRenames[Mangle(n.Text)] = hiddenVar.Id;
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
            if (_cur.Kind == TokenKind.KwReturn || _cur.Kind == TokenKind.KwYield || _cur.Kind == TokenKind.KwAwait)
                throw Error("'" + (_cur.Kind == TokenKind.KwReturn ? "return" : _cur.Kind == TokenKind.KwYield ? "yield" : "await") + "' outside function");
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

        // inClassBody: CPython computes the value in the class namespace (decorators and the base are not).
        private NameNode Hoist(List<StmtNode> hidden, ExprNode value, string what, bool inClassBody)
        {
            string hiddenName = "$cls" + (++_classConstSeq) + "_" + what;
            SourceInfo s = value.Src;
            hidden.Add(new AssignNode(s, AstList.Freeze(new List<ExprNode> { new NameNode(s, hiddenName) }), value)
            {
                InClassBody = inClassBody,
            });
            return new NameNode(s, hiddenName);
        }

        // A method's defaults are computed when its def runs, between the class body's other statements.
        private FuncDefNode HoistDefaults(FuncDefNode fd, List<StmtNode> hidden)
        {
            ParamList p = fd.Params;
            bool any = false;
            foreach (Param pp in p.Positional)
                any |= pp.Default != null;
            foreach (Param pp in p.KwOnly)
                any |= pp.Default != null;
            if (!any)
                return fd;
            var pos = new List<Param>(p.Positional.Count);
            foreach (Param pp in p.Positional)
                pos.Add(pp.Default == null ? pp : new Param(pp.Src, pp.Name, Hoist(hidden, pp.Default, "default", true), pp.AnnotationName));
            var kwOnly = new List<Param>(p.KwOnly.Count);
            foreach (Param pp in p.KwOnly)
                kwOnly.Add(pp.Default == null ? pp : new Param(pp.Src, pp.Name, Hoist(hidden, pp.Default, "default", true), pp.AnnotationName));
            var hoistedParams = new ParamList(p.Src, AstList.Freeze(pos), p.StarArg, AstList.Freeze(kwOnly), p.KwArg,
                p.HasBareStar, p.PositionalOnlyCount);
            return new FuncDefNode(fd.Src, fd.Name, hoistedParams, fd.Body, fd.Decorators, fd.ReturnAnnotationName);
        }

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
                throw Unexpected(false, false);
        }
    }
}

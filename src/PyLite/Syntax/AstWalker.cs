using System.Collections.Generic;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    // The one traversal over every node kind, children in evaluation order. A pass overrides the hooks it
    // needs and calls base for the rest. The bodies of def, lambda and comprehension are other scopes, so
    // the defaults visit only what runs in the enclosing one (decorators, defaults, the first iterable).
    // Recursion follows the AST, whose depth the parser bounds; every node entry probes the stack.
    internal abstract class AstWalker<T>
    {
        internal void Stmts(IReadOnlyList<StmtNode> body, T c)
        {
            foreach (var st in body)
                Stmt(st, c);
        }

        // A name being read.
        protected virtual void Name(NameNode n, T c) { }

        // A name being bound without a NameNode: def/class, import, except ... as.
        protected virtual void Bind(string name, T c) { }

        // A NameNode being bound: an assignment target, a pattern capture, a walrus.
        protected virtual void BindName(NameNode n, T c) { Bind(n.Id, c); }

        // The C# stack guard; a compile-time pass reports a SyntaxError (same conversion as the parser).
        protected virtual void Probe(SourceInfo at)
        {
            try
            {
                System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
            }
            catch (System.InsufficientExecutionStackException)
            {
                throw new PySyntaxErrorException("SyntaxError", "input too complex to compile", at.Line, at.Col);
            }
        }

        protected virtual void Stmt(StmtNode st, T c)
        {
            Probe(st.Src);
            switch (st)
            {
                case ExprStmtNode e: Expr(e.Value, c); break;
                case AssignNode a:
                    Expr(a.Value, c);
                    foreach (var t in a.Targets)
                        Target(t, c);
                    break;
                case AugAssignNode aug: Expr(aug.Value, c); Target(aug.Target, c); break;
                case DelNode d:
                    foreach (var t in d.Targets)
                        Target(t, c);
                    break;
                case IfNode i: Expr(i.Test, c); Stmts(i.Body, c); Stmts(i.OrElse, c); break;
                case WhileNode w: Expr(w.Test, c); Stmts(w.Body, c); Stmts(w.OrElse, c); break;
                case ForNode f: Expr(f.Iter, c); Target(f.Target, c); Stmts(f.Body, c); Stmts(f.OrElse, c); break;
                case FuncDefNode fd: Bind(fd.Name, c); FuncDef(fd, c); break;
                case ClassDefNode cd: ClassDef(cd, c); break;
                case ReturnNode r:
                    if (r.Value != null)
                        Expr(r.Value, c);
                    break;
                case RaiseNode ra:
                    if (ra.Exc != null)
                        Expr(ra.Exc, c);
                    if (ra.Cause != null)
                        Expr(ra.Cause, c);
                    break;
                case AssertNode asrt:
                    Expr(asrt.Test, c);
                    if (asrt.Msg != null)
                        Expr(asrt.Msg, c);
                    break;
                case TryNode t:
                    Stmts(t.Body, c);
                    foreach (var h in t.Handlers)
                    {
                        if (h.Type != null)
                            Expr(h.Type, c);
                        if (h.Name != null)
                            Bind(h.Name, c);
                        Stmts(h.Body, c);
                    }
                    Stmts(t.OrElse, c);
                    Stmts(t.Finally, c);
                    break;
                case ImportNode im:
                    foreach (var al in im.Names)
                        Bind(ImportBoundName(al), c);
                    break;
                case FromImportNode fi:
                    foreach (var al in fi.Names)
                        Bind(al.AsName ?? al.DottedName, c);
                    break;
                case MatchNode m:
                    Expr(m.Subject, c);
                    foreach (var cs in m.Cases)
                    {
                        Pattern(cs.Pattern, c);
                        if (cs.Guard != null)
                            Expr(cs.Guard, c);
                        Stmts(cs.Body, c);
                    }
                    break;
                default: break;   // Pass/Break/Continue/Global/Nonlocal
            }
        }

        // `import a.b.c` binds `a`.
        internal static string ImportBoundName(ImportAlias al)
        {
            if (al.AsName != null)
                return al.AsName;
            int dot = al.DottedName.IndexOf('.');
            return dot < 0 ? al.DottedName : al.DottedName.Substring(0, dot);
        }

        // Decorators and defaults run in the enclosing scope; the body is the function's own.
        protected virtual void FuncDef(FuncDefNode fd, T c)
        {
            foreach (var d in fd.Decorators)
                Expr(d, c);
            Defaults(fd.Params, c);
        }

        // A class body is lowered to fields, constants and methods, all evaluated in the enclosing scope.
        protected virtual void ClassDef(ClassDefNode cd, T c)
        {
            Bind(cd.Name, c);
            if (cd.CellName != null)
                Bind(cd.CellName, c);
            foreach (var d in cd.Decorators)
                Expr(d, c);
            if (cd.Base != null)
                Expr(cd.Base, c);
            foreach (var f in cd.Fields)
                if (f.Default != null)
                    Expr(f.Default, c);
            foreach (var k in cd.Constants)
                Expr(k.Value, c);
            foreach (var meth in cd.Methods)
                Method(meth, c);
        }

        // A method is not bound in the enclosing scope (it is a class attribute).
        protected virtual void Method(FuncDefNode meth, T c) { FuncDef(meth, c); }

        protected virtual void Lambda(LambdaNode lam, T c) { Defaults(lam.Params, c); }

        // Only the first iterable runs in the enclosing scope.
        protected virtual void Comprehension(ComprehensionNode comp, T c) { Expr(comp.Clauses[0].Iter, c); }

        protected void Defaults(ParamList p, T c)
        {
            foreach (var pp in p.Positional)
                if (pp.Default != null)
                    Expr(pp.Default, c);
            foreach (var pp in p.KwOnly)
                if (pp.Default != null)
                    Expr(pp.Default, c);
        }

        protected virtual void Target(ExprNode e, T c)
        {
            switch (e)
            {
                case NameNode n: BindName(n, c); break;
                case TupleNode t:
                    foreach (var elt in t.Elts)
                        Target(elt, c);
                    break;
                case ListNode l:
                    foreach (var elt in l.Elts)
                        Target(elt, c);
                    break;
                case StarredNode st: Target(st.Value, c); break;
                case AttributeNode a: Expr(a.Value, c); break;
                case IndexNode idx: Expr(idx.Value, c); Expr(idx.Index, c); break;
                default: Expr(e, c); break;
            }
        }

        // Capture, star, rest and as targets bind; value, literal-key and class expressions are reads.
        protected virtual void Pattern(PatternNode p, T c)
        {
            switch (p)
            {
                case CapturePattern cp:
                    if (cp.Target != null)
                        BindName(cp.Target, c);
                    break;
                case StarPattern st:
                    if (st.Target != null)
                        BindName(st.Target, c);
                    break;
                case ValuePattern v: Expr(v.Value, c); break;
                case SequencePattern seq:
                    foreach (var e in seq.Elements)
                        Pattern(e, c);
                    break;
                case MappingPattern m:
                    foreach (var k in m.Keys)
                        Expr(k, c);
                    foreach (var vp in m.Values)
                        Pattern(vp, c);
                    if (m.Rest != null)
                        BindName(m.Rest, c);
                    break;
                case OrPattern o:
                    foreach (var alt in o.Alternatives)
                        Pattern(alt, c);
                    break;
                case AsPattern a: Pattern(a.Inner, c); BindName(a.Target, c); break;
                case ClassPattern cls:
                    Expr(cls.Cls, c);
                    foreach (var p2 in cls.Positional)
                        Pattern(p2, c);
                    foreach (var p2 in cls.KwPatterns)
                        Pattern(p2, c);
                    break;
                default: break;   // LiteralPattern
            }
        }

        protected virtual void Expr(ExprNode e, T c)
        {
            Probe(e.Src);
            switch (e)
            {
                case NameNode n: Name(n, c); break;
                case BinOpNode b: Expr(b.Left, c); Expr(b.Right, c); break;
                case UnaryOpNode u: Expr(u.Operand, c); break;
                case BoolOpNode bo:
                    foreach (var v in bo.Values)
                        Expr(v, c);
                    break;
                case CompareChainNode cc:
                    Expr(cc.Left, c);
                    foreach (var v in cc.Comparators)
                        Expr(v, c);
                    break;
                case IfExpNode ie: Expr(ie.Body, c); Expr(ie.Test, c); Expr(ie.OrElse, c); break;
                case CallNode call:
                    Expr(call.Func, c);
                    foreach (var arg in call.Args)
                        Expr(arg.Value, c);
                    break;
                case AttributeNode at: Expr(at.Value, c); break;
                case IndexNode idx: Expr(idx.Value, c); Expr(idx.Index, c); break;
                case SliceNode sl:
                    if (sl.Lower != null)
                        Expr(sl.Lower, c);
                    if (sl.Upper != null)
                        Expr(sl.Upper, c);
                    if (sl.Step != null)
                        Expr(sl.Step, c);
                    break;
                case TupleNode t:
                    foreach (var v in t.Elts)
                        Expr(v, c);
                    break;
                case ListNode l:
                    foreach (var v in l.Elts)
                        Expr(v, c);
                    break;
                case SetNode set:
                    foreach (var v in set.Elts)
                        Expr(v, c);
                    break;
                case DictNode d:
                    for (int i = 0; i < d.Keys.Count; i++)
                    {
                        if (d.Keys[i] != null)   // null key => '**' unpacking entry
                            Expr(d.Keys[i], c);
                        Expr(d.Values[i], c);
                    }
                    break;
                case StarredNode st: Expr(st.Value, c); break;
                case NamedExprNode ne: Expr(ne.Value, c); BindName(ne.Target, c); break;
                case LambdaNode lam: Lambda(lam, c); break;
                case ComprehensionNode comp: Comprehension(comp, c); break;
                case FStringNode fs:
                    foreach (var p in fs.Parts)
                    {
                        if (!p.IsLiteral)
                            Expr(p.Expr, c);
                        if (p.FormatSpec != null)
                            foreach (var fp in p.FormatSpec)
                                if (!fp.IsLiteral)
                                    Expr(fp.Expr, c);
                    }
                    break;
                default: break;   // LiteralNode
            }
        }
    }
}

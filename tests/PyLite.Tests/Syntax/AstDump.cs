using System.Text;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Tests.Syntax
{
    // Compact S-expression dumper for parser tests.
    internal static class AstDump
    {
        public static ExprNode Expr(string src)
        {
            var mod = Parser.ParseModule(new Lexer(src, SyntaxLimits.Default), SyntaxLimits.Default);
            return ((ExprStmtNode)mod.Body[0]).Value;
        }

        public static string Of(string src) { return D(Expr(src)); }

        public static StmtNode Stmt(string src)
        {
            return Parser.ParseModule(new Lexer(src, SyntaxLimits.Default), SyntaxLimits.Default).Body[0];
        }

        public static string OfStmt(string src) { return DS(Stmt(src)); }

        public static string DS(StmtNode s)
        {
            switch (s)
            {
                case ExprStmtNode es: return "(Expr " + D(es.Value) + ")";
                case AssignNode a:
                {
                    var sb = new StringBuilder("(Assign [");
                    for (int i = 0; i < a.Targets.Count; i++)
                    {
                        if (i > 0)
                            sb.Append(' ');
                        sb.Append(D(a.Targets[i]));
                    }
                    return sb.Append("] ").Append(D(a.Value)).Append(')').ToString();
                }
                case AugAssignNode aug: return "(Aug " + D(aug.Target) + " " + aug.Op + " " + D(aug.Value) + ")";
                case DelNode d:
                {
                    var sb = new StringBuilder("(Del");
                    foreach (var t in d.Targets)
                        sb.Append(' ').Append(D(t));
                    return sb.Append(')').ToString();
                }
                case PassNode _: return "(Pass)";
                case BreakNode _: return "(Break)";
                case ContinueNode _: return "(Continue)";
                case ReturnNode r: return "(Return " + (r.Value == null ? "_" : D(r.Value)) + ")";
                case IfNode i: return "(If " + D(i.Test) + " " + Body(i.Body) + " " + Body(i.OrElse) + ")";
                case WhileNode w: return "(While " + D(w.Test) + " " + Body(w.Body) + " " + Body(w.OrElse) + ")";
                case ForNode f: return "(For " + D(f.Target) + " " + D(f.Iter) + " " + Body(f.Body) + " " + Body(f.OrElse) + ")";
                case FuncDefNode fd: return "(Def " + fd.Name + Decos(fd.Decorators) + " " + DParams(fd.Params) + " " + Body(fd.Body) + ")";
                case TryNode t:
                {
                    var sb = new StringBuilder("(Try " + Body(t.Body) + " [");
                    for (int i = 0; i < t.Handlers.Count; i++)
                    {
                        if (i > 0)
                            sb.Append(' ');
                        var h = t.Handlers[i];
                        sb.Append("(except ").Append(h.Type == null ? "_" : D(h.Type)).Append(' ')
                          .Append(h.Name ?? "_").Append(' ').Append(Body(h.Body)).Append(')');
                    }
                    return sb.Append("] ").Append(Body(t.OrElse)).Append(' ').Append(Body(t.Finally)).Append(')').ToString();
                }
                case ImportNode im:
                {
                    var sb = new StringBuilder("(Import");
                    foreach (var a in im.Names)
                        sb.Append(' ').Append(a.DottedName).Append(a.AsName != null ? "=" + a.AsName : "");
                    return sb.Append(')').ToString();
                }
                case FromImportNode fi:
                {
                    var sb = new StringBuilder("(From " + fi.Module);
                    foreach (var a in fi.Names)
                        sb.Append(' ').Append(a.DottedName).Append(a.AsName != null ? "=" + a.AsName : "");
                    return sb.Append(')').ToString();
                }
                case RaiseNode ra: return "(Raise " + (ra.Exc == null ? "_" : D(ra.Exc)) + " " + (ra.Cause == null ? "_" : D(ra.Cause)) + ")";
                case AssertNode a: return "(Assert " + D(a.Test) + " " + (a.Msg == null ? "_" : D(a.Msg)) + ")";
                case GlobalNode g: return "(Global " + string.Join(",", g.Names) + ")";
                case NonlocalNode nl: return "(Nonlocal " + string.Join(",", nl.Names) + ")";
                case ClassDefNode cd:
                {
                    var sb = new StringBuilder("(Class " + cd.Name + Decos(cd.Decorators));
                    if (cd.Base != null)
                        sb.Append(" <").Append(D(cd.Base)).Append('>');
                    foreach (var f in cd.Fields)
                        sb.Append(" (field ").Append(f.Name).Append(f.Default != null ? "=" + D(f.Default) : "").Append(')');
                    foreach (var meth in cd.Methods)
                        sb.Append(' ').Append(DS(meth));
                    return sb.Append(')').ToString();
                }
                case MatchNode m:
                {
                    var sb = new StringBuilder("(Match " + D(m.Subject));
                    foreach (var cs in m.Cases)
                    {
                        sb.Append(" (case ").Append(DPat(cs.Pattern));
                        if (cs.Guard != null)
                            sb.Append(" if ").Append(D(cs.Guard));
                        sb.Append(' ').Append(Body(cs.Body)).Append(')');
                    }
                    return sb.Append(')').ToString();
                }
                default: return s.GetType().Name;
            }
        }

        private static string Body(System.Collections.Generic.IReadOnlyList<StmtNode> stmts)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < stmts.Count; i++)
            {
                if (i > 0)
                    sb.Append(' ');
                sb.Append(DS(stmts[i]));
            }
            return sb.Append(']').ToString();
        }

        public static ModuleNode Module(string src)
        {
            return Parser.ParseModule(new Lexer(src, SyntaxLimits.Default), SyntaxLimits.Default);
        }

        public static string OfModule(string src) { return Body(Module(src).Body); }

        public static string D(ExprNode e)
        {
            switch (e)
            {
                case LiteralNode lit:
                    if (lit.Kind == LiteralKind.None)
                        return "None";
                    if (lit.Kind == LiteralKind.Bool)
                        return (bool)lit.Value ? "True" : "False";
                    if (lit.Kind == LiteralKind.Str)
                        return "'" + (string)lit.Value + "'";
                    return lit.Value.ToString();
                case NameNode n: return n.Id;
                case BinOpNode b: return "(" + b.Op + " " + D(b.Left) + " " + D(b.Right) + ")";
                case UnaryOpNode u: return "(" + u.Op + " " + D(u.Operand) + ")";
                case BoolOpNode bo: return Seq("(" + bo.Op, bo.Values) + ")";
                case CompareChainNode c:
                {
                    var sb = new StringBuilder("(Cmp " + D(c.Left));
                    for (int i = 0; i < c.Ops.Count; i++)
                        sb.Append(' ').Append(c.Ops[i]).Append(' ').Append(D(c.Comparators[i]));
                    return sb.Append(')').ToString();
                }
                case IfExpNode ie: return "(IfExp " + D(ie.Body) + " " + D(ie.Test) + " " + D(ie.OrElse) + ")";
                case TupleNode t: return Seq("(Tuple", t.Elts) + ")";
                case ListNode l: return Seq("(List", l.Elts) + ")";
                case SetNode s: return Seq("(Set", s.Elts) + ")";
                case DictNode d:
                {
                    var sb = new StringBuilder("(Dict");
                    for (int i = 0; i < d.Keys.Count; i++)
                    {
                        if (d.Keys[i] == null)
                            sb.Append(" **").Append(D(d.Values[i]));   // '**' unpacking entry
                        else
                            sb.Append(' ').Append(D(d.Keys[i])).Append(' ').Append(D(d.Values[i]));
                    }
                    return sb.Append(')').ToString();
                }
                case StarredNode st: return "(Star " + D(st.Value) + ")";
                case NamedExprNode ne: return "(Named " + ne.Target.Id + " " + D(ne.Value) + ")";
                case CallNode call:
                {
                    var sb = new StringBuilder("(Call " + D(call.Func));
                    foreach (var a in call.Args)
                        sb.Append(' ').Append(DArg(a));
                    return sb.Append(')').ToString();
                }
                case AttributeNode at: return "(Attr " + D(at.Value) + " " + at.Attr + ")";
                case IndexNode idx: return "(Index " + D(idx.Value) + " " + D(idx.Index) + ")";
                case SliceNode sl: return "(Slice " + N(sl.Lower) + " " + N(sl.Upper) + " " + N(sl.Step) + ")";
                case ComprehensionNode comp:
                {
                    var sb = new StringBuilder("(Comp " + comp.Kind + " " + D(comp.Element));
                    if (comp.ValueElement != null)
                        sb.Append(' ').Append(D(comp.ValueElement));
                    foreach (var cl in comp.Clauses)
                        sb.Append(' ').Append(DClause(cl));
                    return sb.Append(')').ToString();
                }
                case FStringNode fs:
                {
                    var sb = new StringBuilder("(FStr");
                    foreach (var p in fs.Parts)
                        sb.Append(' ').Append(DPart(p));
                    return sb.Append(')').ToString();
                }
                case LambdaNode lam: return "(Lambda " + DParams(lam.Params) + " " + D(lam.Body) + ")";
                default: return e.GetType().Name;
            }
        }

        private static string N(ExprNode e) { return e == null ? "_" : D(e); }

        private static string Seq(string head, System.Collections.Generic.IReadOnlyList<ExprNode> items)
        {
            var sb = new StringBuilder(head);
            foreach (var v in items)
                sb.Append(' ').Append(D(v));
            return sb.ToString();
        }

        private static string DArg(Arg a)
        {
            switch (a.Kind)
            {
                case ArgKind.Keyword: return a.Keyword + "=" + D(a.Value);
                case ArgKind.Star: return "*" + D(a.Value);
                case ArgKind.DoubleStar: return "**" + D(a.Value);
                default: return D(a.Value);
            }
        }

        private static string DClause(CompClause c)
        {
            return c.IsFor ? "(for " + D(c.Target) + " " + D(c.Iter) + ")" : "(if " + D(c.Cond) + ")";
        }

        private static string DPart(FStringPart p)
        {
            if (p.IsLiteral)
                return "'" + p.Literal + "'";
            var sb = new StringBuilder("{" + D(p.Expr));
            if (p.Conversion != '\0')
                sb.Append('!').Append(p.Conversion);
            if (p.FormatSpec != null)
            {
                sb.Append(':');
                foreach (var fp in p.FormatSpec)
                    sb.Append(DPart(fp));
            }
            return sb.Append('}').ToString();
        }

        private static string DPat(PatternNode p)
        {
            switch (p)
            {
                case CapturePattern c: return c.Target == null ? "_" : c.Target.Id;
                case LiteralPattern l: return D(l.Value);
                case ValuePattern v: return D(v.Value);
                case StarPattern st: return "*" + (st.Target == null ? "_" : st.Target.Id);
                case SequencePattern seq:
                {
                    var sb = new StringBuilder("[");
                    for (int i = 0; i < seq.Elements.Count; i++)
                    {
                        if (i > 0)
                            sb.Append(' ');
                        sb.Append(DPat(seq.Elements[i]));
                    }
                    return sb.Append(']').ToString();
                }
                case MappingPattern m:
                {
                    var sb = new StringBuilder("{");
                    bool first = true;
                    for (int i = 0; i < m.Keys.Count; i++)
                    {
                        if (!first)
                            sb.Append(' ');
                        first = false;
                        sb.Append(D(m.Keys[i])).Append(": ").Append(DPat(m.Values[i]));
                    }
                    if (m.Rest != null)
                    {
                        if (!first)
                            sb.Append(' ');
                        sb.Append("**").Append(m.Rest.Id);
                    }
                    return sb.Append('}').ToString();
                }
                case OrPattern o:
                {
                    var sb = new StringBuilder("(|");
                    foreach (var a in o.Alternatives)
                        sb.Append(' ').Append(DPat(a));
                    return sb.Append(')').ToString();
                }
                case AsPattern ap: return "(as " + DPat(ap.Inner) + " " + ap.Target.Id + ")";
                case ClassPattern cp:
                {
                    var sb = new StringBuilder("(cls " + D(cp.Cls));
                    foreach (var pp in cp.Positional)
                        sb.Append(' ').Append(DPat(pp));
                    for (int i = 0; i < cp.KwNames.Count; i++)
                        sb.Append(' ').Append(cp.KwNames[i]).Append('=').Append(DPat(cp.KwPatterns[i]));
                    return sb.Append(')').ToString();
                }
                default: return p.GetType().Name;
            }
        }

        private static string Decos(System.Collections.Generic.IReadOnlyList<ExprNode> decorators)
        {
            if (decorators == null || decorators.Count == 0)
                return "";
            var sb = new StringBuilder();
            foreach (var d in decorators)
                sb.Append(" @").Append(D(d));
            return sb.ToString();
        }

        public static string DParams(ParamList p)
        {
            var sb = new StringBuilder("[");
            bool first = true;
            for (int i = 0; i < p.Positional.Count; i++)
            {
                var pp = p.Positional[i];
                Sep(sb, ref first);
                sb.Append(pp.Name);
                if (pp.Default != null)
                    sb.Append('=').Append(D(pp.Default));
                if (i + 1 == p.PositionalOnlyCount)
                {
                    Sep(sb, ref first);
                    sb.Append('/');
                }
            }
            if (p.StarArg != null)
            {
                Sep(sb, ref first);
                sb.Append('*').Append(p.StarArg.Name);
            }
            else if (p.HasBareStar)
            {
                Sep(sb, ref first);
                sb.Append('*');
            }
            foreach (var pp in p.KwOnly)
            {
                Sep(sb, ref first);
                sb.Append(pp.Name);
                if (pp.Default != null)
                    sb.Append('=').Append(D(pp.Default));
            }
            if (p.KwArg != null)
            {
                Sep(sb, ref first);
                sb.Append("**").Append(p.KwArg.Name);
            }
            return sb.Append(']').ToString();
        }

        private static void Sep(StringBuilder sb, ref bool first) { if (!first)
            sb.Append(','); first = false; }
    }
}

using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Syntax.Ast;

namespace Outbridge.PyLite.Syntax
{
    // Conservative literal constant folding. Bottom-up; only operations that are guaranteed
    // small, exact and cannot error at compile time. Never folds / ** div-by-0 str-* inf/nan BoolOp chains.
    internal static class ConstFolder
    {
        private const int MaxBits = 128;
        private const int MaxStrLen = 4096;
        private static readonly BigInteger BitCap = BigInteger.One << MaxBits;

        public static Dictionary<ExprNode, ExprNode> FoldAll(ModuleNode module)
        {
            var map = new Dictionary<ExprNode, ExprNode>();
            FoldStmts(module.Body, map);
            return map;
        }

        private static void FoldStmts(IReadOnlyList<StmtNode> stmts, Dictionary<ExprNode, ExprNode> map)
        {
            foreach (var st in stmts)
                FoldStmt(st, map);
        }

        private static void FoldStmt(StmtNode st, Dictionary<ExprNode, ExprNode> map)
        {
            switch (st)
            {
                case ExprStmtNode e: Fold(e.Value, map); break;
                case AssignNode a: Fold(a.Value, map); foreach (var t in a.Targets)
                    Fold(t, map); break;
                case AugAssignNode aug: Fold(aug.Value, map); Fold(aug.Target, map); break;
                case DelNode d: foreach (var t in d.Targets)
                    Fold(t, map); break;
                case IfNode i: Fold(i.Test, map); FoldStmts(i.Body, map); FoldStmts(i.OrElse, map); break;
                case WhileNode w: Fold(w.Test, map); FoldStmts(w.Body, map); FoldStmts(w.OrElse, map); break;
                case ForNode f: Fold(f.Target, map); Fold(f.Iter, map); FoldStmts(f.Body, map); FoldStmts(f.OrElse, map); break;
                case FuncDefNode fd: foreach (var d in fd.Decorators)
                    Fold(d, map); FoldDefaults(fd.Params, map); FoldStmts(fd.Body, map); break;
                case ClassDefNode cd:
                    foreach (var d in cd.Decorators)
                        Fold(d, map);
                    if (cd.Base != null)
                        Fold(cd.Base, map);
                    foreach (var f in cd.Fields)
                        if (f.Default != null)
                            Fold(f.Default, map);
                    foreach (var meth in cd.Methods)
                    {
                        FoldDefaults(meth.Params, map);
                        FoldStmts(meth.Body, map);
                    }
                    break;
                case ReturnNode r: if (r.Value != null)
                    Fold(r.Value, map); break;
                case RaiseNode ra: if (ra.Exc != null)
                    Fold(ra.Exc, map); if (ra.Cause != null)
                    Fold(ra.Cause, map); break;
                case AssertNode asrt: Fold(asrt.Test, map); if (asrt.Msg != null)
                    Fold(asrt.Msg, map); break;
                case TryNode t:
                    FoldStmts(t.Body, map);
                    foreach (var h in t.Handlers)
                    {
                        if (h.Type != null)
                            Fold(h.Type, map);
                        FoldStmts(h.Body, map);
                    }
                    FoldStmts(t.OrElse, map); FoldStmts(t.Finally, map);
                    break;
                case MatchNode m:
                    Fold(m.Subject, map);
                    foreach (var cs in m.Cases)
                    {
                        if (cs.Guard != null)
                            Fold(cs.Guard, map);
                        FoldStmts(cs.Body, map);   // pattern literal exprs are left unfolded (read raw at match time)
                    }
                    break;
                default: break;
            }
        }

        private static void FoldDefaults(ParamList p, Dictionary<ExprNode, ExprNode> map)
        {
            foreach (var pp in p.Positional)
                if (pp.Default != null)
                    Fold(pp.Default, map);
            foreach (var pp in p.KwOnly)
                if (pp.Default != null)
                    Fold(pp.Default, map);
        }

        // Same calling-thread stack probe as the resolver's walk: depth is parser-bounded, the caller's
        // remaining stack is not.
        private static void ProbeStack(ExprNode at)
        {
            try
            {
                System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
            }
            catch (System.InsufficientExecutionStackException)
            {
                throw new PySyntaxErrorException("SyntaxError", "input too complex to compile", at.Src.Line, at.Src.Col);
            }
        }

        // Returns the LiteralNode this expression folds to (null if it does not), registering folds bottom-up.
        private static LiteralNode Fold(ExprNode e, Dictionary<ExprNode, ExprNode> map)
        {
            ProbeStack(e);
            switch (e)
            {
                case LiteralNode lit: return lit;
                case UnaryOpNode u:
                {
                    LiteralNode operand = Fold(u.Operand, map);
                    if (operand != null)
                    {
                        LiteralNode r = TryUnary(u.Op, operand, e.Src);
                        if (r != null)
                        {
                            map[e] = new ConstFoldedNode(e, r);
                            return r;
                        }
                    }
                    return null;
                }
                case BinOpNode b:
                {
                    LiteralNode L = Fold(b.Left, map);
                    LiteralNode R = Fold(b.Right, map);
                    if (L != null && R != null)
                    {
                        LiteralNode r = TryBin(b.Op, L, R, e.Src);
                        if (r != null)
                        {
                            map[e] = new ConstFoldedNode(e, r);
                            return r;
                        }
                    }
                    return null;
                }
                case CompareChainNode c:
                {
                    LiteralNode L = Fold(c.Left, map);
                    LiteralNode last = null;
                    foreach (var cc in c.Comparators)
                        last = Fold(cc, map);
                    if (c.Ops.Count == 1 && L != null && last != null)
                    {
                        LiteralNode r = TryCompare(c.Ops[0], L, last, e.Src);
                        if (r != null)
                        {
                            map[e] = new ConstFoldedNode(e, r);
                            return r;
                        }
                    }
                    return null;
                }
                case BoolOpNode bo: foreach (var v in bo.Values)
                    Fold(v, map); return null;
                case IfExpNode ie: Fold(ie.Body, map); Fold(ie.Test, map); Fold(ie.OrElse, map); return null;
                case CallNode call: Fold(call.Func, map); foreach (var arg in call.Args)
                    Fold(arg.Value, map); return null;
                case AttributeNode at: Fold(at.Value, map); return null;
                case IndexNode idx: Fold(idx.Value, map); Fold(idx.Index, map); return null;
                case SliceNode sl:
                    if (sl.Lower != null)
                        Fold(sl.Lower, map);
                    if (sl.Upper != null)
                        Fold(sl.Upper, map);
                    if (sl.Step != null)
                        Fold(sl.Step, map);
                    return null;
                case TupleNode t: foreach (var v in t.Elts)
                    Fold(v, map); return null;
                case ListNode l: foreach (var v in l.Elts)
                    Fold(v, map); return null;
                case SetNode se: foreach (var v in se.Elts)
                    Fold(v, map); return null;
                case DictNode d: for (int i = 0; i < d.Keys.Count; i++)
                {
                    if (d.Keys[i] != null)   // null key => '**' unpacking entry
                        Fold(d.Keys[i], map);
                    Fold(d.Values[i], map);
                } return null;
                case StarredNode s: Fold(s.Value, map); return null;
                case NamedExprNode ne: Fold(ne.Value, map); return null;
                case LambdaNode lam: Fold(lam.Body, map); return null;
                case ComprehensionNode comp:
                    Fold(comp.Element, map);
                    if (comp.ValueElement != null)
                        Fold(comp.ValueElement, map);
                    foreach (var cl in comp.Clauses)
                    {
                        if (cl.IsFor)
                        {
                            Fold(cl.Target, map);
                            Fold(cl.Iter, map);
                        }
                        else
                            Fold(cl.Cond, map);
                    }
                    return null;
                case FStringNode fs:
                    foreach (var p in fs.Parts)
                    {
                        if (!p.IsLiteral)
                            Fold(p.Expr, map);
                        if (p.FormatSpec != null)
                            foreach (var fp in p.FormatSpec)
                                if (!fp.IsLiteral)
                                    Fold(fp.Expr, map);
                    }
                    return null;
                default: return null;
            }
        }

        private static LiteralNode TryUnary(UnaryOp op, LiteralNode o, SourceInfo src)
        {
            switch (op)
            {
                case UnaryOp.USub:
                    if (o.Kind == LiteralKind.Int)
                        return IntLit(-(BigInteger)o.Value, src);
                    if (o.Kind == LiteralKind.Float)
                        return FloatLit(-(double)o.Value, src);
                    return null;
                case UnaryOp.UAdd:
                    if (o.Kind == LiteralKind.Int || o.Kind == LiteralKind.Float)
                        return o;
                    return null;
                case UnaryOp.Invert:
                    if (o.Kind == LiteralKind.Int)
                        return IntLit(-(BigInteger)o.Value - 1, src);
                    return null;
                case UnaryOp.Not:
                    if (o.Kind == LiteralKind.Bool)
                        return BoolLit(!(bool)o.Value, src);
                    if (o.Kind == LiteralKind.None)
                        return BoolLit(true, src);
                    if (o.Kind == LiteralKind.Int)
                    {
                        BigInteger v = (BigInteger)o.Value;
                        if (v.IsZero || v == BigInteger.One)
                            return BoolLit(v.IsZero, src);
                    }
                    return null;
                default: return null;
            }
        }

        private static LiteralNode TryBin(BinOp op, LiteralNode L, LiteralNode R, SourceInfo src)
        {
            if (L.Kind == LiteralKind.Str && R.Kind == LiteralKind.Str)
            {
                if (op == BinOp.Add)
                {
                    string s = (string)L.Value + (string)R.Value;
                    return s.Length <= MaxStrLen ? StrLit(s, src) : null;
                }

                return null;
            }

            if (L.Kind == LiteralKind.Int && R.Kind == LiteralKind.Int)
            {
                BigInteger a = (BigInteger)L.Value, b = (BigInteger)R.Value;
                switch (op)
                {
                    case BinOp.Add:
                        return IntIfSmall(a + b, src);
                    case BinOp.Sub:
                        return IntIfSmall(a - b, src);
                    case BinOp.Mul:
                        return IntIfSmall(a * b, src);
                    case BinOp.FloorDiv:
                        return b.IsZero ? null : IntIfSmall(FloorDivInt(a, b), src);
                    case BinOp.Mod:
                        return b.IsZero ? null : IntIfSmall(ModInt(a, b), src);
                    case BinOp.LShift:
                        if (b.Sign < 0 || b > MaxBits)
                            return null;
                        if (a.IsZero)
                            return IntLit(BigInteger.Zero, src);
                        return IntIfSmall(a << (int)b, src);
                    case BinOp.RShift:
                        if (b.Sign < 0)
                            return null;
                        return IntLit(a >> (b > 4096 ? 4096 : (int)b), src);
                    case BinOp.BitAnd:
                        return IntLit(a & b, src);
                    case BinOp.BitOr:
                        return IntLit(a | b, src);
                    case BinOp.BitXor:
                        return IntLit(a ^ b, src);
                    default:
                        return null; // Div, Pow
                }
            }

            if (IsNum(L) && IsNum(R))
            {
                double a = ToDouble(L), b = ToDouble(R);
                switch (op)
                {
                    case BinOp.Add:
                        return FloatIfFinite(a + b, src);
                    case BinOp.Sub:
                        return FloatIfFinite(a - b, src);
                    case BinOp.Mul:
                        return FloatIfFinite(a * b, src);
                    default:
                        return null;
                }
            }
            return null;
        }

        private static LiteralNode TryCompare(CompareOp op, LiteralNode L, LiteralNode R, SourceInfo src)
        {
            int cmp;
            if (L.Kind == LiteralKind.Str && R.Kind == LiteralKind.Str)
                cmp = System.Math.Sign(string.CompareOrdinal((string)L.Value, (string)R.Value));
            else if (IsNum(L) && IsNum(R))
            {
                if (L.Kind == LiteralKind.Int && R.Kind == LiteralKind.Int)
                    cmp = BigInteger.Compare((BigInteger)L.Value, (BigInteger)R.Value);
                else
                    cmp = ToDouble(L).CompareTo(ToDouble(R));
            }
            else
                return null;

            switch (op)
            {
                case CompareOp.Lt: return BoolLit(cmp < 0, src);
                case CompareOp.Le: return BoolLit(cmp <= 0, src);
                case CompareOp.Gt: return BoolLit(cmp > 0, src);
                case CompareOp.Ge: return BoolLit(cmp >= 0, src);
                case CompareOp.Eq: return BoolLit(cmp == 0, src);
                case CompareOp.Ne: return BoolLit(cmp != 0, src);
                default: return null;   // In/NotIn/Is/IsNot
            }
        }

        private static bool IsNum(LiteralNode l) { return l.Kind == LiteralKind.Int || l.Kind == LiteralKind.Float || l.Kind == LiteralKind.Bool; }

        private static double ToDouble(LiteralNode l)
        {
            if (l.Kind == LiteralKind.Float)
                return (double)l.Value;
            if (l.Kind == LiteralKind.Bool)
                return (bool)l.Value ? 1.0 : 0.0;
            return (double)(BigInteger)l.Value;
        }

        private static BigInteger FloorDivInt(BigInteger a, BigInteger b)
        {
            BigInteger r;
            BigInteger q = BigInteger.DivRem(a, b, out r);
            if (!r.IsZero && r.Sign != b.Sign)
                q -= 1;
            return q;
        }

        private static BigInteger ModInt(BigInteger a, BigInteger b)
        {
            BigInteger r = a % b;
            if (!r.IsZero && r.Sign != b.Sign)
                r += b;
            return r;
        }

        private static LiteralNode IntIfSmall(BigInteger v, SourceInfo src)
        {
            return BigInteger.Abs(v) < BitCap ? IntLit(v, src) : null;
        }

        private static LiteralNode FloatIfFinite(double d, SourceInfo src)
        {
            return (double.IsInfinity(d) || double.IsNaN(d)) ? null : FloatLit(d, src);
        }

        private static LiteralNode IntLit(BigInteger v, SourceInfo src) { return new LiteralNode(src, v, LiteralKind.Int); }
        private static LiteralNode FloatLit(double v, SourceInfo src) { return new LiteralNode(src, v, LiteralKind.Float); }
        private static LiteralNode StrLit(string v, SourceInfo src) { return new LiteralNode(src, v, LiteralKind.Str); }
        private static LiteralNode BoolLit(bool v, SourceInfo src) { return new LiteralNode(src, v, LiteralKind.Bool); }
    }
}

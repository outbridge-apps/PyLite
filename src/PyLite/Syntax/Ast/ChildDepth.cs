using System.Collections.Generic;

namespace Outbridge.PyLite.Syntax.Ast
{
    // Max subtree depth over a node's children, so every ExprNode can record an EXACT
    // Depth = 1 + max(child depths) at construction (see ExprNode.Depth). Exactness matters: the parser's
    // fold/trailer loops check Depth to reject over-deep expressions, and a child that under-reports its
    // depth would let a deep subtree hide under a fold and defeat the guard.
    internal static class ChildDepth
    {
        public static int Of(ExprNode a) { return a == null ? 0 : a.Depth; }
        public static int Max(int a, int b) { return a > b ? a : b; }
        public static int Max(int a, int b, int c) { return Max(Max(a, b), c); }

        public static int OfList(IReadOnlyList<ExprNode> xs)
        {
            int m = 0;
            if (xs != null)
            {
                for (int i = 0; i < xs.Count; i++)
                {
                    ExprNode x = xs[i];
                    if (x != null && x.Depth > m)
                        m = x.Depth;
                }
            }
            return m;
        }

        public static int OfArgs(IReadOnlyList<Arg> args)
        {
            int m = 0;
            if (args != null)
            {
                for (int i = 0; i < args.Count; i++)
                {
                    Arg a = args[i];
                    if (a != null && a.Value != null && a.Value.Depth > m)
                        m = a.Value.Depth;
                }
            }
            return m;
        }

        // Param defaults are already-parsed expressions and can be arbitrarily deep on their own.
        public static int OfParamDefaults(ParamList p)
        {
            if (p == null)
                return 0;
            int m = OfDefaults(p.Positional);
            m = Max(m, OfDefaults(p.KwOnly));
            m = Max(m, DefaultOf(p.StarArg));
            m = Max(m, DefaultOf(p.KwArg));
            return m;
        }

        private static int DefaultOf(Param p) { return p != null ? Of(p.Default) : 0; }

        private static int OfDefaults(IReadOnlyList<Param> ps)
        {
            int m = 0;
            if (ps != null)
            {
                for (int i = 0; i < ps.Count; i++)
                {
                    int d = DefaultOf(ps[i]);
                    if (d > m)
                        m = d;
                }
            }
            return m;
        }

        public static int OfClauses(IReadOnlyList<CompClause> cs)
        {
            int m = 0;
            if (cs != null)
            {
                for (int i = 0; i < cs.Count; i++)
                {
                    CompClause c = cs[i];
                    if (c == null)
                        continue;
                    m = Max(m, Of(c.Target));
                    m = Max(m, Of(c.Iter));
                    m = Max(m, Of(c.Cond));
                }
            }
            return m;
        }

        // Recurses through nested format specs; the nesting itself is bounded by the lexer's f-string limit.
        public static int OfFStringParts(IReadOnlyList<FStringPart> parts)
        {
            int m = 0;
            if (parts != null)
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    FStringPart p = parts[i];
                    if (p == null)
                        continue;
                    m = Max(m, Of(p.Expr));
                    m = Max(m, OfFStringParts(p.FormatSpec));
                }
            }
            return m;
        }
    }
}

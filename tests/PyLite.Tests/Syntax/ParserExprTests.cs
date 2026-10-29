using System.Numerics;
using System.Text;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Syntax.Ast;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Compatibility")]
    public class ParserExprTests
    {
        internal static ExprNode Expr(string src)
        {
            var mod = Parser.ParseModule(new Lexer(src, SyntaxLimits.Default), SyntaxLimits.Default);
            return ((ExprStmtNode)mod.Body[0]).Value;
        }

        internal static string Dump(ExprNode e)
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
                case BinOpNode b: return "(" + b.Op + " " + Dump(b.Left) + " " + Dump(b.Right) + ")";
                case UnaryOpNode u: return "(" + u.Op + " " + Dump(u.Operand) + ")";
                case BoolOpNode bo:
                {
                    var sb = new StringBuilder("(" + bo.Op);
                    foreach (var v in bo.Values)
                        sb.Append(' ').Append(Dump(v));
                    return sb.Append(')').ToString();
                }
                case CompareChainNode c:
                {
                    var sb = new StringBuilder("(Cmp " + Dump(c.Left));
                    for (int i = 0; i < c.Ops.Count; i++)
                        sb.Append(' ').Append(c.Ops[i]).Append(' ').Append(Dump(c.Comparators[i]));
                    return sb.Append(')').ToString();
                }
                case IfExpNode ie: return "(IfExp " + Dump(ie.Body) + " " + Dump(ie.Test) + " " + Dump(ie.OrElse) + ")";
                case TupleNode t:
                {
                    var sb = new StringBuilder("(Tuple");
                    foreach (var v in t.Elts)
                        sb.Append(' ').Append(Dump(v));
                    return sb.Append(')').ToString();
                }
                default: return e.GetType().Name;
            }
        }

        [Theory]
        [InlineData("1 + 2 * 3", "(Add 1 (Mul 2 3))")]
        [InlineData("2 ** 3 ** 2", "(Pow 2 (Pow 3 2))")]
        [InlineData("-2 ** 2", "(USub (Pow 2 2))")]
        [InlineData("2 ** -2", "(Pow 2 (USub 2))")]
        [InlineData("~2 ** 2", "(Invert (Pow 2 2))")]
        [InlineData("a < b <= c", "(Cmp a Lt b Le c)")]
        [InlineData("a not in b", "(Cmp a NotIn b)")]
        [InlineData("a is not b", "(Cmp a IsNot b)")]
        [InlineData("a is b", "(Cmp a Is b)")]
        [InlineData("a or b and c", "(Or a (And b c))")]
        [InlineData("not a == b", "(Not (Cmp a Eq b))")]
        [InlineData("x if a else y if b else z", "(IfExp x a (IfExp y b z))")]
        [InlineData("1 << 2 + 3", "(LShift 1 (Add 2 3))")]
        [InlineData("1 | 2 & 3", "(BitOr 1 (BitAnd 2 3))")]
        [InlineData("a and not b", "(And a (Not b))")]
        [InlineData("- - 2", "(USub (USub 2))")]
        [InlineData("not not x", "(Not (Not x))")]
        [InlineData("a == b or c", "(Or (Cmp a Eq b) c)")]
        public void ExpressionShapes(string src, string expected) => Assert.Equal(expected, Dump(Expr(src)));

        [Fact]
        public void AdjacentStringConcat() => Assert.Equal("'abc'", Dump(Expr("'a' 'b' 'c'")));

        [Fact]
        public void ParenGrouping() => Assert.Equal("(Mul (Add 1 2) 3)", Dump(Expr("(1 + 2) * 3")));

        [Fact]
        public void TupleAndTrailingComma()
        {
            Assert.Equal("(Tuple 1 2)", Dump(Expr("(1, 2)")));
            Assert.Equal("(Tuple 1)", Dump(Expr("(1,)")));
            Assert.Equal("1", Dump(Expr("(1)")));
            Assert.Equal("(Tuple)", Dump(Expr("()")));
        }

        [Fact]
        public void DeepNestingIsSyntaxErrorNotOverflow()
        {
            var src = new string('(', 500) + "x" + new string(')', 500);
            var ex = Assert.Throws<PySyntaxErrorException>(() => Expr(src));
            Assert.Contains("expression too deeply nested", ex.Message);
        }

        [Fact]
        public void ModerateNestingOk()
        {
            var src = new string('(', 30) + "x" + new string(')', 30);
            Assert.Equal("x", Dump(Expr(src)));
        }

        // A comprehension iterable/element uses ParseOrTest, which sits below ParseTest's EnterExpr; without
        // its own guard a nested-comprehension chain ([x for x in [x for x in ...]]) once recursed the parser
        // into a StackOverflow. Each clause is now expr-depth guarded -> a clean SyntaxError.
        [Theory]
        [InlineData("iterable")]
        [InlineData("element")]
        public void DeepComprehensionNestingIsSyntaxErrorNotOverflow(string where)
        {
            var sb = new StringBuilder();
            if (where == "iterable")
            {
                for (int i = 0; i < 400; i++) sb.Append("[x for x in ");
                sb.Append("[1]");
                for (int i = 0; i < 400; i++) sb.Append(']');
            }
            else
            {
                for (int i = 0; i < 400; i++) sb.Append('[');
                sb.Append('1');
                for (int i = 0; i < 400; i++) sb.Append(" for x in [1]]");
            }
            var ex = Assert.Throws<PySyntaxErrorException>(() => Expr(sb.ToString()));
            Assert.Contains("too deeply nested", ex.Message);
        }

        [Fact]
        public void ModerateComprehensionNestingOk()
        {
            Assert.NotNull(Expr("[x for x in [y for y in [1, 2]]]"));
        }

        [Fact]
        public void ExpectErrorCarriesPosition()
        {
            var ex = Assert.Throws<PySyntaxErrorException>(() => Expr("(1"));
            Assert.Equal("SyntaxError", ex.PythonType);
        }

        // A flat left-associative chain (1+1+…, a[0][0]…, a.b.b…) grows the AST iteratively without EnterExpr,
        // so its tree depth equals its length. Unbounded, this overflowed the resolver's C# stack (uncatchable
        // StackOverflow) — now MaxAstDepth caps it as a SyntaxError.
        [Theory]
        [InlineData("+")]
        [InlineData("*")]
        [InlineData("|")]
        [InlineData("^")]
        [InlineData("&")]
        public void FlatOperatorChainTooDeepIsSyntaxError(string op)
        {
            var sb = new StringBuilder("1");
            for (int i = 0; i < 1500; i++)
                sb.Append(op).Append('1');
            var ex = Assert.Throws<PySyntaxErrorException>(() => Expr(sb.ToString()));
            Assert.Contains("expression too deeply nested", ex.Message);
        }

        [Theory]
        [InlineData("[0]")]
        [InlineData(".b")]
        [InlineData("()")]
        public void FlatTrailerChainTooDeepIsSyntaxError(string trailer)
        {
            var sb = new StringBuilder("a");
            for (int i = 0; i < 1500; i++)
                sb.Append(trailer);
            var ex = Assert.Throws<PySyntaxErrorException>(() => Expr(sb.ToString()));
            Assert.Contains("expression too deeply nested", ex.Message);
        }

        // The guard measures true tree depth (recorded on every node), so composition across precedence
        // levels / parens / a lambda default cannot slip a deep subtree under a shallow-looking chain.
        [Fact]
        public void ComposedDeepExpressionCannotEvadeGuard()
        {
            // Five parenthesised 300-deep '+' spines nested down the leftmost operand -> ~1500 tree depth,
            // while no single fold loop iterates past 300.
            string inner = "1";
            for (int lvl = 0; lvl < 5; lvl++)
            {
                var s = new StringBuilder("(").Append(inner);
                for (int i = 0; i < 300; i++)
                    s.Append("+1");
                inner = s.Append(')').ToString();
            }
            var ex = Assert.Throws<PySyntaxErrorException>(() => Expr(inner));
            Assert.Contains("expression too deeply nested", ex.Message);
        }

        [Fact]
        public void LambdaDefaultDepthCountsTowardGuard()
        {
            // The deep chain hides in a param default; Lambda.Depth includes it, so the enclosing fold is caught.
            var spine = new StringBuilder("1");
            for (int i = 0; i < 1200; i++)
                spine.Append("+1");
            var ex = Assert.Throws<PySyntaxErrorException>(() => Expr("(lambda x=" + spine + ": 0)"));
            Assert.Contains("expression too deeply nested", ex.Message);
        }

        [Fact]
        public void LongButUnderLimitFlatChainParses()
        {
            var sb = new StringBuilder("1");
            for (int i = 0; i < 900; i++)   // 901 terms, depth 901 < 1000
                sb.Append("+1");
            ExprNode e = Expr(sb.ToString());   // must not throw
            Assert.True(e.Depth <= SyntaxLimits.Default.MaxAstDepth);
        }

        [Fact]
        public void WideLiteralIsShallowAndParses()
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < 5000; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('1');
            }
            sb.Append(']');
            ExprNode e = Expr(sb.ToString());   // 5000-wide, depth 2 — must not throw
            Assert.Equal(2, e.Depth);
        }
    }
}

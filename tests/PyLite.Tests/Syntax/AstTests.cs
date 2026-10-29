using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Syntax.Ast;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Compatibility")]
    public class AstTests
    {
        private static readonly SourceInfo S = new SourceInfo(1, 1);

        [Fact]
        public void NameNodesAreDistinct()
        {
            var a = new NameNode(S, "x");
            var b = new NameNode(S, "x");
            Assert.False(ReferenceEquals(a, b));
        }

        [Fact]
        public void EmptyListIsSingleton()
        {
            Assert.Same(AstList.Empty<StmtNode>(), AstList.Empty<StmtNode>());
        }

        [Fact]
        public void FreezePreservesElements()
        {
            var n0 = new NameNode(S, "a");
            var n1 = new NameNode(S, "b");
            var n2 = new NameNode(S, "c");
            var frozen = AstList.Freeze(new List<ExprNode> { n0, n1, n2 });
            Assert.Equal(3, frozen.Count);
            Assert.Same(n0, frozen[0]);
            Assert.Same(n2, frozen[2]);
        }

        [Fact]
        public void NoneLiteralUsesMarker()
        {
            var lit = new LiteralNode(S, NoneMarker.Instance, LiteralKind.None);
            Assert.Same(NoneMarker.Instance, lit.Value);
            Assert.NotNull(lit.Value);
        }

        [Fact]
        public void EmptyTupleHasEmptyElts()
        {
            var t = new TupleNode(S, AstList.Empty<ExprNode>());
            Assert.Equal(0, t.Elts.Count);
            Assert.NotNull(t.Elts);
        }

        [Fact]
        public void ReferenceComparerUsesIdentity()
        {
            var cmp = (IEqualityComparer<object>)ReferenceEqualityComparer.Instance;
            string a = new string('x', 1);
            string b = new string('x', 1);
            Assert.False(cmp.Equals(a, b));
            Assert.True(cmp.Equals(a, a));
        }

        [Fact]
        public void SourceInfoFields()
        {
            var s = new SourceInfo(10, 4);
            Assert.Equal(10, s.Line);
            Assert.Equal(4, s.Col);
        }

        [Fact]
        public void ComprehensionKindValueInvariant()
        {
            var elem = new NameNode(S, "x");
            var val = new NameNode(S, "v");
            var clauses = AstList.Empty<CompClause>();
            // Dict requires a value element
            Assert.Throws<ArgumentException>(() => new ComprehensionNode(S, CompKind.Dict, elem, null, clauses));
            // List must NOT have a value element
            Assert.Throws<ArgumentException>(() => new ComprehensionNode(S, CompKind.List, elem, val, clauses));
            // Valid dict comprehension
            var ok = new ComprehensionNode(S, CompKind.Dict, elem, val, clauses);
            Assert.NotNull(ok.ValueElement);
        }

        [Fact]
        public void LiteralIntCarriesBigInteger()
        {
            var lit = new LiteralNode(S, (BigInteger)42, LiteralKind.Int);
            Assert.Equal((BigInteger)42, (BigInteger)lit.Value);
        }
    }
}

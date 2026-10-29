using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modern
{
    // User __eq__/__ne__ on record classes must be consulted by EVERY equality surface — ==/!=,
    // reflected (non-record on the left), membership/count/index, and nested containers — the way
    // CPython consults them (essence: no NotImplemented in the dialect, so the left operand's
    // dunder wins; only-right dunders dispatch reflected).
    [Trait("Category", "Compatibility")]
    public class RecordUserEqTests
    {
        private const string M =
            "class M:\n"
            + "    def __init__(self, v):\n"
            + "        self.v = v\n"
            + "    def __eq__(self, other):\n"
            + "        return self.v == other\n";

        [Theory]
        [InlineData("r = M(5) == 5", "True")]
        [InlineData("r = M(5) == 6", "False")]
        [InlineData("r = 5 == M(5)", "True")]        // reflected: int on the left
        [InlineData("r = M(5) != 5", "False")]       // default __ne__ inverts the user __eq__
        [InlineData("r = 6 != M(5)", "True")]
        [InlineData("r = M(None) == None", "True")]
        [InlineData("r = M('a') == 'a'", "True")]
        [InlineData("r = M(5) in [4, 5, 6]", "True")]
        [InlineData("r = M(7) in [4, 5, 6]", "False")]
        [InlineData("r = 5 in [M(5)]", "True")]      // reflected through membership
        [InlineData("r = [4, M(5), M(5)].count(5)", "2")]
        [InlineData("r = [M(4), M(5)].index(5)", "1")]
        [InlineData("r = [M(5)] == [5]", "True")]    // nested: SeqFrame leaf
        [InlineData("r = {1: M(5)} == {1: 5}", "True")]   // nested: DictFrame values
        [InlineData("r = (M(5), 1) == (5, 1)", "True")]
        public void UserEqConsultedEverywhere(string expr, string expected)
        {
            Assert.Equal(expected, Ev.Run(M + expr + "\n").Repr("r"));
        }

        // The __eq__ result coerces via Truth (documented essence — CPython would surface the raw
        // object from the == operator itself).
        [Fact]
        public void NonBoolEqResultCoercesViaTruth()
        {
            var r = Ev.Run(
                "class T:\n"
                + "    def __init__(self):\n        pass\n"
                + "    def __eq__(self, other):\n        return 1\n"
                + "class F:\n"
                + "    def __init__(self):\n        pass\n"
                + "    def __eq__(self, other):\n        return 0\n"
                + "r = (T() == 'x', F() == 'x')\n");
            Assert.Equal("(True, False)", r.Repr("r"));
        }

        // Without __ne__, != is the CPython-default inversion of __eq__ (which the != rows above prove).
        // An explicit __ne__ answers the OPERATOR only: membership and container comparison keep going
        // through __eq__, there as here. See RecordProtocolTests for the rest of the protocol methods.
        [Fact]
        public void ExplicitNeAnswersTheOperatorOnly()
        {
            const string src =
                "class W:\n"
                + "    def __init__(self, n):\n        self.n = n\n"
                + "    def __eq__(self, other):\n        return True\n"
                + "    def __ne__(self, other):\n        return True\n";
            var r = Ev.Run(src + "r = (W(1) != W(2), W(1) == W(2), W(1) in [W(2)], [W(1)] == [W(2)])\n");
            Assert.Equal("(True, True, True, True)", r.Repr("r"));
        }

        // Only the RIGHT record defines __eq__ (equal-to-anything marker): the left one has none,
        // so ONLY the reflected dunder can make this True.
        [Fact]
        public void ReflectedBetweenTwoRecordClasses()
        {
            var r = Ev.Run(
                "class Marker:\n"
                + "    def __init__(self):\n        pass\n"
                + "    def __eq__(self, other):\n        return True\n"
                + "class Plain:\n"
                + "    def __init__(self):\n        pass\n"
                + "r = (Plain() == Marker(), Marker() == Plain(), Plain() != Marker())\n");
            Assert.Equal("(True, True, False)", r.Repr("r"));
        }

        // A bare class without any user __eq__ compares by identity, as in CPython: never equal to a
        // non-record and never equal to a different instance of its own class.
        [Fact]
        public void NoUserEqIsIdentity()
        {
            var r = Ev.Run(
                "class P:\n"
                + "    def __init__(self, v):\n        self.v = v\n"
                + "p = P(5)\n"
                + "r = (P(5) == 5, 5 == P(5), P(5) == P(5), p == p, P(5) in [5], P(5) in [P(5)], p in [p])\n");
            Assert.Equal("(False, False, False, True, False, False, True)", r.Repr("r"));
        }

        // The engine's identity shortcut precedes the dunder: x == x is True without calling
        // __eq__ (documented deviation — CPython consults __eq__ even for the same object).
        [Fact]
        public void IdentityShortcutWinsOverUserEq_DocumentedDeviation()
        {
            var r = Ev.Run(
                "class N:\n"
                + "    def __init__(self):\n        pass\n"
                + "    def __eq__(self, other):\n        return False\n"
                + "n = N()\n"
                + "r = (n == n, n in [n])\n");
            Assert.Equal("(True, True)", r.Repr("r"));
        }

        // Errors raised inside __eq__ surface as the same typed script errors from every entry.
        [Theory]
        [InlineData("r = B() == 1")]
        [InlineData("r = 1 == B()")]
        [InlineData("r = B() in [1, 2]")]
        [InlineData("r = [B()] == [1]")]
        public void RaisingEqStaysTyped(string expr)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(
                "class B:\n"
                + "    def __init__(self):\n        pass\n"
                + "    def __eq__(self, other):\n        raise ValueError('boom')\n"
                + expr + "\n"));
            Assert.Equal("ValueError", ex.Value.ExcType.Name);
        }

        // A self-recursive __eq__ hits MaxCallDepth (the dunder call rides the full call prologue),
        // never the C# stack.
        [Fact]
        public void RecursiveEqHitsCallDepth()
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(
                "class R:\n"
                + "    def __init__(self):\n        pass\n"
                + "    def __eq__(self, other):\n        return self == other\n"
                + "r = R() == R()\n"));
            Assert.Equal(EngineAbortKind.Recursion, ex.Kind);
        }

        // A __eq__ that SHRINKS the lists being compared mid-drain: the SeqFrame re-validates live
        // counts — the compare resolves (unequal), never an out-of-range engine fault.
        [Fact]
        public void MutatingEqCannotFaultTheEngine()
        {
            var r = Ev.Run(
                "class Evil:\n"
                + "    def __init__(self):\n        pass\n"
                + "    def __eq__(self, other):\n"
                + "        del L1[1:]\n"
                + "        return True\n"
                + "L1 = [Evil(), 1, 2, 3]\n"
                + "L2 = [Evil(), 1, 2, 3]\n"
                + "r = (L1 == L2, len(L1))\n");
            Assert.Equal("(False, 1)", r.Repr("r"));
        }

        // The small-int membership fast path hands record elements to the full protocol (-1), so a
        // small-int needle still reaches the record's reflected __eq__.
        [Fact]
        public void SmallIntNeedleReachesRecordEq()
        {
            var r = Ev.Run(M + "r = (1 in [0, M(1)], [M(2), M(3)].count(3))\n");
            Assert.Equal("(True, 1)", r.Repr("r"));
        }
    }
}

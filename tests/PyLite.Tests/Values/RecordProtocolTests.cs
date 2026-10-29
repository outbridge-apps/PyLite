using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modern
{
    // User-defined protocol methods on record classes (RFC-001 §1, extended 2026-09-11). Every one
    // dispatches through CallHook, so script code reached from inside an engine walk is budget-charged
    // and stack-probed like any other call.
    [Trait("Category", "Compatibility")]
    public class RecordProtocolTests
    {
        // ---- __str__ ----

        private const string S =
            "class S:\n"
            + "    def __init__(self, v):\n"
            + "        self.v = v\n"
            + "    def __str__(self):\n"
            + "        return 'S<' + str(self.v) + '>'\n";

        [Theory]
        [InlineData("str(S(1))", "'S<1>'")]
        [InlineData("f'{S(2)}'", "'S<2>'")]
        [InlineData("'{}'.format(S(3))", "'S<3>'")]
        [InlineData("'%s' % S(4)", "'S<4>'")]
        [InlineData("repr(S(1))", "'S(v=1)'")]                 // repr stays the generated one
        [InlineData("str([S(1)])", "'[S(v=1)]'")]              // a container renders its items with repr
        [InlineData("f'{S(1)!r}'", "'S(v=1)'")]
        public void UserStr(string expr, string expected)
        {
            Assert.Equal(expected, Ev.Run(S + "r = (" + expr + ")\n").Repr("r"));
        }

        [Fact]
        public void StrMustReturnAString()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(
                "class B:\n    def __init__(self):\n        pass\n    def __str__(self):\n        return 1\nr = str(B())\n"));
            Assert.Equal("__str__ returned non-string (type int)", ex.Value.GetMessageText(Rt.Ctx()));
        }

        // ---- __hash__ ----

        private const string H =
            "class H:\n"
            + "    def __init__(self, k):\n"
            + "        self.k = k\n"
            + "    def __eq__(self, o):\n"
            + "        return self.k == o.k\n"
            + "    def __hash__(self):\n"
            + "        return hash(self.k)\n";

        [Theory]
        [InlineData("{H(1): 'x'}[H(1)]", "'x'")]
        [InlineData("len({H(1), H(1), H(2)})", "2")]
        [InlineData("H(1) in {H(1)}", "True")]
        [InlineData("hash(H(5)) == hash(5)", "True")]
        [InlineData("hash(H('s')) == hash('s')", "True")]
        [InlineData("{H(1): 1}.get(H(2), 'miss')", "'miss'")]
        [InlineData("sorted({H(1): 'a', H(2): 'b'}.values())", "['a', 'b']")]
        public void UserHashMakesARecordAKey(string expr, string expected)
        {
            Assert.Equal(expected, Ev.Run(H + "r = (" + expr + ")\n").Repr("r"));
        }

        [Fact]
        public void HashMustReturnAnInteger()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(
                "class B:\n    def __init__(self):\n        pass\n    def __hash__(self):\n        return 'no'\nr = hash(B())\n"));
            Assert.Equal("__hash__ method should return an integer", ex.Value.GetMessageText(Rt.Ctx()));
        }

        // Field equality over mutable slots cannot have a structural hash: the key would be lost on the
        // first assignment, which is why CPython's @dataclass(eq=True, frozen=False) sets __hash__ to
        // None. A bare class is not that case — it compares by identity and hashes by it.
        [Fact]
        public void OnlyAMutableFieldEqualityIsUnhashable()
        {
            Assert.Equal("1", Ev.Run("class P:\n    def __init__(self, v):\n        self.v = v\nr = len({P(1)})\n").Repr("r"));

            var ex = Assert.Throws<ScriptException>(() => Ev.Run(
                "from dataclasses import dataclass\n@dataclass\nclass P:\n    v: int\nr = {P(1)}\n"));
            Assert.Equal("unhashable type: 'P'", ex.Value.GetMessageText(Rt.Ctx()));

            // a bare class that WRITES __eq__ loses the hash, exactly as any CPython class does
            var wrote = Assert.Throws<ScriptException>(() => Ev.Run(
                "class Q:\n    def __init__(self, v):\n        self.v = v\n"
                + "    def __eq__(self, o):\n        return True\nr = {Q(1)}\n"));
            Assert.Equal("unhashable type: 'Q'", wrote.Value.GetMessageText(Rt.Ctx()));
        }

        [Theory]
        // an explicit __hash__ survives @dataclass, frozen or not, as it does in CPython
        [InlineData("@dataclass(frozen=True)\nclass D:\n    v: int\n    def __hash__(self):\n        return 999\nr = hash(D(1))\n", "999")]
        [InlineData("@dataclass\nclass D:\n    v: int\n    def __hash__(self):\n        return self.v\nr = {D(3): 'y'}[D(3)]\n", "'y'")]
        // without one, only frozen hashes, and it hashes by FIELD values
        [InlineData("@dataclass(frozen=True)\nclass D:\n    v: int\nr = len({D(1), D(1), D(2)})\n", "2")]
        // eq=False keeps identity on both sides, so the hash is the identity and two equal values are two
        // keys; it wins over frozen, which only decides whether a FIELD hash would be safe
        [InlineData("@dataclass(eq=False)\nclass D:\n    v: int\nd = D(1)\nr = ({d: 'x'}[d], D(1) == D(1))\n", "('x', False)")]
        [InlineData("@dataclass(eq=False)\nclass D:\n    v: int\nr = len({D(1), D(1)})\n", "2")]
        [InlineData("@dataclass(eq=False, frozen=True)\nclass D:\n    v: int\nr = len({D(1), D(1)})\n", "2")]
        [InlineData("@dataclass(eq=False)\nclass D:\n    v: int\nd = D(1)\nr = hash(d) == id(d)\n", "True")]
        // identity hashing means the key survives a field assignment, which is the whole point of it
        [InlineData("@dataclass(eq=False)\nclass D:\n    v: int\nd = D(1)\nm = {d: 'kept'}\nd.v = 99\nr = m[d]\n", "'kept'")]
        // a written __eq__ still costs the hash, eq=False or not: that is CPython's data-model rule
        [InlineData("@dataclass(eq=False)\nclass D:\n    v: int\n    def __eq__(self, o):\n        return True\nr = 'unhashable'\n", "'unhashable'")]
        public void DataclassHashInteraction(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run("from dataclasses import dataclass\n" + src).Repr("r"));
        }

        [Fact]
        public void EqFalseWithAWrittenEqIsStillUnhashable()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(
                "from dataclasses import dataclass\n@dataclass(eq=False)\nclass D:\n    v: int\n"
                + "    def __eq__(self, o):\n        return True\nr = {D(1)}\n"));
            Assert.Equal("unhashable type: 'D'", ex.Value.GetMessageText(Rt.Ctx()));
        }

        // ---- __bool__ / __len__ ----

        [Theory]
        [InlineData("class B:\n    def __init__(self, b):\n        self.b = b\n    def __bool__(self):\n        return self.b\nr = bool(B(False))\n", "False")]
        [InlineData("class B:\n    def __init__(self, b):\n        self.b = b\n    def __bool__(self):\n        return self.b\nr = 'y' if B(False) else 'n'\n", "'n'")]
        [InlineData("class B:\n    def __init__(self, b):\n        self.b = b\n    def __bool__(self):\n        return self.b\nr = not B(True)\n", "False")]
        [InlineData("class L:\n    def __init__(self, n):\n        self.n = n\n    def __len__(self):\n        return self.n\nr = len(L(3))\n", "3")]
        [InlineData("class L:\n    def __init__(self, n):\n        self.n = n\n    def __len__(self):\n        return self.n\nr = bool(L(0))\n", "False")]
        [InlineData("class L:\n    def __init__(self, n):\n        self.n = n\n    def __len__(self):\n        return self.n\nr = bool(L(2))\n", "True")]
        // neither method: always true, as any object is in CPython
        [InlineData("class P:\n    def __init__(self):\n        pass\nr = bool(P())\n", "True")]
        public void BoolAndLen(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        [Theory]
        [InlineData("        return -1\nr = len(L())\n", "ValueError", "__len__() should return >= 0")]
        [InlineData("        return 'x'\nr = len(L())\n", "TypeError", "'str' object cannot be interpreted as an integer")]
        [InlineData("        return 1\nr = bool(B())\n", "", "")]
        public void LenErrors(string tail, string exc, string message)
        {
            if (exc.Length == 0)
            {
                var bad = Assert.Throws<ScriptException>(() => Ev.Run(
                    "class B:\n    def __init__(self):\n        pass\n    def __bool__(self):\n" + tail));
                Assert.Equal("__bool__ should return bool, returned int", bad.Value.GetMessageText(Rt.Ctx()));
                return;
            }
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(
                "class L:\n    def __init__(self):\n        pass\n    def __len__(self):\n" + tail));
            Assert.Equal(exc, ex.Value.ExcType.Name);
            Assert.Equal(message, ex.Value.GetMessageText(Rt.Ctx()));
        }

        // ---- ordering ----

        private const string V =
            "class V:\n"
            + "    def __init__(self, n):\n"
            + "        self.n = n\n"
            + "    def __lt__(self, o):\n"
            + "        return self.n < o.n\n"
            + "    def __repr__(self):\n"
            + "        return 'V' + str(self.n)\n";

        [Theory]
        [InlineData("V(1) < V(2)", "True")]
        [InlineData("V(2) < V(1)", "False")]
        [InlineData("sorted([V(3), V(1), V(2)])", "[V1, V2, V3]")]
        [InlineData("min([V(3), V(1)])", "V1")]
        [InlineData("max([V(3), V(1)])", "V3")]
        // only __lt__ is defined, so > is answered by the reflected call V(2).__lt__(V(1))
        [InlineData("V(1) > V(2)", "False")]
        [InlineData("V(2) > V(1)", "True")]
        public void UserOrdering(string expr, string expected)
        {
            Assert.Equal(expected, Ev.Run(V + "r = (" + expr + ")\n").Repr("r"));
        }

        // No __le__ and no reflected __ge__: the dialect has no NotImplemented, so a missing method is
        // simply a missing comparison, and the engine's own message stands.
        [Fact]
        public void AMissingComparisonIsStillATypeError()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(V + "r = V(1) <= V(2)\n"));
            Assert.Equal("'<=' not supported between instances of 'V' and 'V'", ex.Value.GetMessageText(Rt.Ctx()));
        }

        // != consults __ne__; == and every container path keep going through __eq__, as in CPython.
        [Theory]
        [InlineData("class N:\n    def __init__(self, n):\n        self.n = n\n    def __ne__(self, o):\n        return 'yes'\nr = N(1) != N(1)\n", "True")]
        [InlineData("class N:\n    def __init__(self, n):\n        self.n = n\n    def __ne__(self, o):\n        return False\nr = N(1) != N(2)\n", "False")]
        // without __ne__ it stays the inversion of __eq__
        [InlineData("class N:\n    def __init__(self, n):\n        self.n = n\n    def __eq__(self, o):\n        return True\nr = N(1) != N(2)\n", "False")]
        public void UserNotEqual(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        // order=True generates all four comparisons, so CPython refuses to silently drop a written one.
        [Fact]
        public void DataclassOrderClashesWithAWrittenComparison()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(
                "from dataclasses import dataclass\n@dataclass(order=True)\nclass X:\n    v: int\n"
                + "    def __lt__(self, o):\n        return True\n"));
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
            Assert.Equal("Cannot overwrite attribute __lt__ in class X. Consider using functools.total_ordering",
                ex.Value.GetMessageText(Rt.Ctx()));
        }

        // ---- __call__ ----

        private const string C =
            "class C:\n"
            + "    def __init__(self, base):\n"
            + "        self.base = base\n"
            + "    def __call__(self, x, k=0):\n"
            + "        return self.base + x + k\n";

        [Theory]
        [InlineData("C(10)(5)", "15")]
        [InlineData("C(10)(5, 1)", "16")]
        [InlineData("C(10)(5, k=2)", "17")]
        [InlineData("C(10)(x=5)", "15")]
        [InlineData("callable(C(1))", "True")]
        [InlineData("list(map(C(100), [1, 2]))", "[101, 102]")]
        [InlineData("sorted([3, 1], key=C(0))", "[1, 3]")]
        public void UserCall(string expr, string expected)
        {
            Assert.Equal(expected, Ev.Run(C + "r = (" + expr + ")\n").Repr("r"));
        }

        [Fact]
        public void ARecordWithoutCallIsNotCallable()
        {
            Assert.Equal("False", Ev.Run("class P:\n    def __init__(self):\n        pass\nr = callable(P())\n").Repr("r"));
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("class P:\n    def __init__(self):\n        pass\nr = P()()\n"));
            Assert.Equal("'P' object is not callable", ex.Value.GetMessageText(Rt.Ctx()));
        }

        // ---- containers ----

        private const string Map =
            "class Map:\n"
            + "    def __init__(self):\n"
            + "        self.d = {}\n"
            + "    def __getitem__(self, k):\n"
            + "        return self.d[k]\n"
            + "    def __setitem__(self, k, v):\n"
            + "        self.d[k] = v\n"
            + "    def __delitem__(self, k):\n"
            + "        del self.d[k]\n"
            + "    def __contains__(self, k):\n"
            + "        return k in self.d\n"
            + "    def __len__(self):\n"
            + "        return len(self.d)\n";

        [Theory]
        [InlineData("m['a'] = 1\nr = m['a']\n", "1")]
        [InlineData("m['a'] = 1\nr = 'a' in m\n", "True")]
        [InlineData("r = 'a' in m\n", "False")]
        [InlineData("m['a'] = 1\nm['b'] = 2\nr = len(m)\n", "2")]
        [InlineData("m['a'] = 1\ndel m['a']\nr = len(m)\n", "0")]
        [InlineData("m['a'] = 1\nm['a'] = 2\nr = m['a']\n", "2")]
        [InlineData("m[(1, 2)] = 'tuple key'\nr = m[(1, 2)]\n", "'tuple key'")]
        public void UserSubscript(string body, string expected)
        {
            Assert.Equal(expected, Ev.Run(Map + "m = Map()\n" + body).Repr("r"));
        }

        [Fact]
        public void TheHookOwnErrorSurfaces()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Map + "m = Map()\nr = m['zz']\n"));
            Assert.Equal("KeyError", ex.Value.ExcType.Name);
        }

        // ---- iteration ----

        private const string Count =
            "class Count:\n"
            + "    def __init__(self, n):\n"
            + "        self.n = n\n"
            + "        self.i = 0\n"
            + "    def __iter__(self):\n"
            + "        self.i = 0\n"
            + "        return self\n"
            + "    def __next__(self):\n"
            + "        if self.i >= self.n:\n"
            + "            raise StopIteration\n"
            + "        self.i = self.i + 1\n"
            + "        return self.i - 1\n";

        [Theory]
        [InlineData("list(Count(3))", "[0, 1, 2]")]
        [InlineData("tuple(Count(2))", "(0, 1)")]
        [InlineData("[x * 2 for x in Count(3)]", "[0, 2, 4]")]
        [InlineData("sum(Count(4))", "6")]
        [InlineData("list(Count(0))", "[]")]
        [InlineData("max(Count(5))", "4")]
        [InlineData("2 in Count(5)", "True")]
        [InlineData("[(a, b) for a in Count(2) for b in Count(2)]", "[(0, 0), (0, 1), (1, 0), (1, 1)]")]
        [InlineData("list(enumerate(Count(2)))", "[(0, 0), (1, 1)]")]
        [InlineData("sorted(Count(3), reverse=True)", "[2, 1, 0]")]
        public void UserIteration(string expr, string expected)
        {
            Assert.Equal(expected, Ev.Run(Count + "r = (" + expr + ")\n").Repr("r"));
        }

        [Theory]
        // __iter__ may hand back any real iterator, which is the usual delegating shape
        [InlineData("class W:\n    def __init__(self, xs):\n        self.xs = xs\n    def __iter__(self):\n        return iter(self.xs)\nr = list(W([1, 2]))\n", "[1, 2]")]
        [InlineData("class W:\n    def __init__(self, xs):\n        self.xs = xs\n    def __reversed__(self):\n        return iter(self.xs[::-1])\nr = list(reversed(W([1, 2, 3])))\n", "[3, 2, 1]")]
        // only __getitem__: the legacy sequence protocol, indexed from 0 until IndexError
        [InlineData("class L:\n    def __init__(self, n):\n        self.n = n\n    def __getitem__(self, i):\n        if i >= self.n:\n            raise IndexError(i)\n        return i * 10\nr = list(L(3))\n", "[0, 10, 20]")]
        [InlineData("class L:\n    def __init__(self, n):\n        self.n = n\n    def __getitem__(self, i):\n        if i >= self.n:\n            raise IndexError(i)\n        return i * 10\nr = 20 in L(5)\n", "True")]
        public void IterationShapes(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        // Demanding a real iterator is CPython's rule, and it is also what stops two classes handing
        // each other back from recursing forever.
        [Fact]
        public void IterMustReturnAnIterator()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(
                "class B:\n    def __init__(self):\n        pass\n    def __iter__(self):\n        return [1, 2]\nr = list(B())\n"));
            Assert.Equal("iter() returned non-iterator of type 'list'", ex.Value.GetMessageText(Rt.Ctx()));

            var cyc = Assert.Throws<ScriptException>(() => Ev.Run(
                "class A:\n    def __init__(self):\n        pass\n    def __iter__(self):\n        return B()\n"
                + "class B:\n    def __init__(self):\n        pass\n    def __iter__(self):\n        return A()\n"
                + "r = list(A())\n"));
            Assert.Equal("iter() returned non-iterator of type 'B'", cyc.Value.GetMessageText(Rt.Ctx()));
        }

        // ---- id() ----

        [Theory]
        [InlineData("a = P(1)\nr = id(a) == id(a)\n", "True")]
        [InlineData("a = P(1)\nb = P(1)\nr = id(a) == id(b)\n", "False")]
        [InlineData("a = P(1)\nb = a\nr = id(a) == id(b)\n", "True")]
        [InlineData("a = P(1)\nb = P(1)\nr = (id(a) == id(b)) == (a is b)\n", "True")]
        [InlineData("r = id(None) == id(None)\n", "True")]
        [InlineData("r = id(True) == id(True)\n", "True")]
        [InlineData("a = P(1)\nr = type(id(a)).__name__\n", "'int'")]
        [InlineData("a = P(1)\nr = {id(a): 'x'}[id(a)]\n", "'x'")]
        [InlineData("a = [1]\nb = a\nr = id(a) == id(b)\n", "True")]
        [InlineData("r = id([1]) == id([1])\n", "False")]
        [InlineData("s = 'abc'\nr = id(s) == id(s)\n", "True")]
        public void IdIsIdentity(string body, string expected)
        {
            Assert.Equal(expected, Ev.Run("class P:\n    def __init__(self, v):\n        self.v = v\n" + body).Repr("r"));
        }

        [Fact]
        public void IdTakesExactlyOneArgument()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("r = id(1, 2)\n"));
            Assert.Equal("id() takes exactly one argument (2 given)", ex.Value.GetMessageText(Rt.Ctx()));
        }

        // ---- the sandbox still holds ----

        // Every protocol call goes through CallHook, so a method that never finishes stops on the budget
        // rather than hanging the host, and one that recurses hits the depth guard before the C# stack.
        [Fact]
        public void ProtocolCallsStayInsideTheBudget()
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(
                "class L:\n    def __init__(self):\n        pass\n    def __len__(self):\n        while True:\n            pass\n"
                + "r = len(L())\n",
                Outbridge.PyLite.Hosting.ResourceLimits.Default.With(maxSteps: 100_000)));
            Assert.Equal(EngineAbortKind.Steps, ex.Kind);
        }

        // An iterator that never raises StopIteration is the same story: bounded, not hung.
        [Fact]
        public void AnEndlessIteratorStopsOnTheBudget()
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(
                "class E:\n    def __init__(self):\n        pass\n    def __iter__(self):\n        return self\n"
                + "    def __next__(self):\n        return 1\nr = list(E())\n",
                Outbridge.PyLite.Hosting.ResourceLimits.Default.With(maxSteps: 100_000)));
            Assert.Equal(EngineAbortKind.Steps, ex.Kind);
        }
    }
}

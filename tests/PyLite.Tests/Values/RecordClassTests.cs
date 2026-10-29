using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Tests;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modern
{
    // RFC-001 §1 record classes (the prerequisite for RFC-003 §2.10 dataclasses / class patterns).
    [Trait("Category", "Compatibility")]
    public class RecordClassTests
    {
        private const string Point =
            "class Point:\n"
            + "    def __init__(self, x, y):\n"
            + "        self.x = x\n"
            + "        self.y = y\n"
            + "    def dist2(self):\n"
            + "        return self.x * self.x + self.y * self.y\n";

        // Slots come from every self.<name> target in __init__, nested ones and for targets included; a
        // nested def is another scope (D3).
        [Fact]
        public void SlotsFromEveryInitTarget()
        {
            string src = "class P:\n"
                + "    def __init__(self, n):\n"
                + "        for self.i in range(n):\n"
                + "            pass\n"
                + "        if n:\n"
                + "            self.b, (self.c, *self.d) = 1, (2, 3, 4)\n"
                + "        def inner(o):\n"
                + "            o.z = 1\n"
                + "p = P(3)\n";
            Assert.Equal("(2, 1, 2, [3, 4])", Ev.ReprOf(src, "p.i, p.b, p.c, p.d"));
            Assert.Equal("False", Ev.ReprOf(src, "hasattr(p, 'z')"));
        }

        [Fact]
        public void ConstructAndReadAttributes()
        {
            var r = Ev.Run(Point + "p = Point(3, 4)\na = p.x\nb = p.y\n");
            Assert.Equal("3", r.Repr("a"));
            Assert.Equal("4", r.Repr("b"));
        }

        [Fact]
        public void MethodCall()
        {
            Assert.Equal("25", Ev.Run(Point + "p = Point(3, 4)\nr = p.dist2()\n").Repr("r"));
        }

        [Fact]
        public void AutoRepr()
        {
            Assert.Equal("'Point(x=3, y=4)'", Ev.Run(Point + "p = Point(3, 4)\nr = repr(p)\n").Repr("r"));
        }

        // A self/mutual-referential record must render with a cycle marker, not recurse into an
        // uncatchable StackOverflow (auto-repr records descend through the ReprEngine's open set).
        [Fact]
        public void SelfReferentialAutoReprIsCycleSafe()
        {
            string a = "class A:\n def __init__(self):\n  self.x = None\na = A()\na.x = a\nr = repr(a)\n";
            Assert.Equal("'A(x=...)'", Ev.Run(a).Repr("r"));
        }

        [Fact]
        public void MutualReferentialAutoReprIsCycleSafe()
        {
            string src = "class A:\n def __init__(self):\n  self.x = None\na = A()\nb = A()\na.x = b\nb.x = a\nr = repr(a)\n";
            Assert.Equal("'A(x=A(x=...))'", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void RecordHoldingSelfReferentialContainerIsCycleSafe()
        {
            string src = "class A:\n def __init__(self):\n  self.x = None\nl = []\nl.append(l)\na = A()\na.x = l\nr = repr(a)\n";
            Assert.Equal("'A(x=[[...]])'", Ev.Run(src).Repr("r"));
        }

        // A bare class compares by IDENTITY, as in CPython. Field equality is what @dataclass(eq=True)
        // generates; see RecordProtocolTests and the dataclass tests for that side.
        [Fact]
        public void BareClassComparesByIdentity()
        {
            var r = Ev.Run(Point + "p = Point(1, 2)\na = p == p\nb = Point(1, 2) == Point(1, 2)\n"
                + "c = Point(1, 2) != Point(1, 2)\nd = p in [p]\ne = Point(1, 2) in [Point(1, 2)]\n");
            Assert.Equal("True", r.Repr("a"));
            Assert.Equal("False", r.Repr("b"));
            Assert.Equal("True", r.Repr("c"));
            Assert.Equal("True", r.Repr("d"));
            Assert.Equal("False", r.Repr("e"));
        }

        [Fact]
        public void DataclassStillComparesByFields()
        {
            var r = Ev.Run("from dataclasses import dataclass\n@dataclass\nclass P:\n    x: int\n"
                + "a = P(1) == P(1)\nb = P(1) == P(2)\n");
            Assert.Equal("True", r.Repr("a"));
            Assert.Equal("False", r.Repr("b"));
        }

        [Fact]
        public void MutableSlot()
        {
            Assert.Equal("100", Ev.Run(Point + "p = Point(1, 2)\np.x = 100\na = p.x\n").Repr("a"));
        }

        [Fact]
        public void AugAssignSlot()
        {
            Assert.Equal("6", Ev.Run(Point + "p = Point(1, 2)\np.x += 5\na = p.x\n").Repr("a"));
        }

        [Fact]
        public void AssignUndeclaredRaises()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Point + "p = Point(1, 2)\np.z = 5\n"));
            Assert.Equal("'Point' object has no attribute 'z'", ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]
        public void ReadUndeclaredRaises()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Point + "p = Point(1, 2)\nz = p.z\n"));
            Assert.Equal("'Point' object has no attribute 'z'", ex.Value.GetMessageText(Rt.Ctx()));
        }

        // Identity equality comes with the identity hash that agrees with it, as in CPython. Only
        // @dataclass(eq=True, frozen=False) is unhashable, and there CPython says so too.
        [Fact]
        public void BareClassHashesByIdentity()
        {
            var r = Ev.Run(Point + "p = Point(1, 2)\nd = {p: 'x'}\na = d[p]\nb = len({Point(1, 2), Point(1, 2)})\n"
                + "p.x = 99\nc = d[p]\ne = hash(p) == id(p)\n");
            Assert.Equal("'x'", r.Repr("a"));
            Assert.Equal("2", r.Repr("b"));
            Assert.Equal("'x'", r.Repr("c"));     // the key survives a field assignment
            Assert.Equal("True", r.Repr("e"));
        }

        [Fact]
        public void MutableDataclassIsUnhashable()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(
                "from dataclasses import dataclass\n@dataclass\nclass P:\n    x: int\nd = {P(1): 1}\n"));
            Assert.Equal("unhashable type: 'P'", ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]
        public void Isinstance()
        {
            var r = Ev.Run(Point + "p = Point(1, 2)\na = isinstance(p, Point)\nb = isinstance(5, Point)\n");
            Assert.Equal("True", r.Repr("a"));
            Assert.Equal("False", r.Repr("b"));
        }

        [Fact]
        public void InheritanceWithExplicitBaseCall()
        {
            var src = "class Animal:\n"
                + "    def __init__(self, name):\n        self.name = name\n"
                + "    def speak(self):\n        return self.name + ' makes a sound'\n"
                + "class Dog(Animal):\n"
                + "    def __init__(self, name, breed):\n"
                + "        Animal.__init__(self, name)\n"
                + "        self.breed = breed\n"
                + "    def speak(self):\n        return self.name + ' barks'\n"
                + "d = Dog('Rex', 'Lab')\n"
                + "s = d.speak()\nn = d.name\nb = d.breed\nia = isinstance(d, Animal)\n";
            var r = Ev.Run(src);
            Assert.Equal("'Rex barks'", r.Repr("s"));
            Assert.Equal("'Rex'", r.Repr("n"));
            Assert.Equal("'Lab'", r.Repr("b"));
            Assert.Equal("True", r.Repr("ia"));
        }

        [Fact]
        public void OverrideRepr()
        {
            var src = "class P:\n"
                + "    def __init__(self, v):\n        self.v = v\n"
                + "    def __repr__(self):\n        return 'P<' + str(self.v) + '>'\n"
                + "r = repr(P(7))\n";
            Assert.Equal("'P<7>'", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void OverrideEq()
        {
            var src = "class Ci:\n"
                + "    def __init__(self, s):\n        self.s = s\n"
                + "    def __eq__(self, other):\n        return self.s.lower() == other.s.lower()\n"
                + "a = Ci('Hi') == Ci('HI')\n";
            Assert.Equal("True", Ev.Run(src).Repr("a"));
        }

        [Fact]
        public void NoInitTakesNoArguments()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("class E:\n    def m(self):\n        return 1\nE(1)\n"));
            Assert.Equal("E() takes no arguments", ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]
        public void EmptyClassConstructs()
        {
            Assert.Equal("True", Ev.Run("class Empty:\n    pass\ne = Empty()\nt = isinstance(e, Empty)\n").Repr("t"));
        }

        // The protocol methods are allowed (RecordProtocolTests); what is refused is everything the
        // engine has no place to call — arithmetic, the attribute hooks, construction and `with`.
        [Theory]
        [InlineData("__add__")]
        [InlineData("__radd__")]
        [InlineData("__getattr__")]
        [InlineData("__setattr__")]
        [InlineData("__new__")]
        [InlineData("__aenter__")]
        [InlineData("__format__")]
        [InlineData("__class_getitem__")]
        public void UnsupportedDunderRejected(string name)
        {
            var ex = Assert.Throws<PySyntaxErrorException>(
                () => Ev.Run("class C:\n    def " + name + "(self):\n        return 1\n"));
            Assert.Equal("this dialect does not support '" + name + "' definitions", ex.Message);
        }

        [Fact]
        public void MultipleInheritanceRejected()
        {
            var ex = Assert.Throws<PySyntaxErrorException>(() => Ev.Run("class A:\n    pass\nclass B:\n    pass\nclass C(A, B):\n    pass\n"));
            Assert.Equal("multiple inheritance is not supported in this dialect", ex.Message);
        }

        [Fact]
        public void ClassBodyNonMethodRejected()
        {
            // NAME = value is a class constant since 1.1.0; any other statement is still refused
            Assert.Throws<PySyntaxErrorException>(() => Ev.Run("class C:\n    if True:\n        x = 5\n"));
            Assert.Throws<PySyntaxErrorException>(() => Ev.Run("class C:\n    x = y = 5\n"));
        }
    }
}

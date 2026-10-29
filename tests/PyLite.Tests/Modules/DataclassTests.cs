using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modern
{
    // RFC-003 §2.10 dataclasses + §2.9 class patterns (both ride on RFC-001 record classes).
    [Trait("Category", "Compatibility")]
    public class DataclassTests
    {
        private const string Imp = "from dataclasses import dataclass, field, fields, asdict, replace\n";
        private const string Pt = Imp + "@dataclass\nclass Point:\n    x: int\n    y: int\n";

        [Fact]
        public void GeneratedInitAndAttributes()
        {
            var r = Ev.Run(Pt + "p = Point(1, 2)\na = p.x\nb = p.y\n");
            Assert.Equal("1", r.Repr("a"));
            Assert.Equal("2", r.Repr("b"));
        }

        [Theory]
        [InlineData("r = astuple(Point(1, 2))", "(1, 2)")]
        [InlineData("@dataclass\nclass L:\n    a: Point\n    n: int\nr = astuple(L(Point(1, 2), 3))", "((1, 2), 3)")]   // nested, like asdict
        [InlineData("r = is_dataclass(Point)", "True")]
        [InlineData("r = is_dataclass(Point(1, 2))", "True")]
        [InlineData("class Plain:\n    x: int\nr = is_dataclass(Plain)", "False")]
        [InlineData("r = is_dataclass(3)", "False")]
        public void AstupleAndIsDataclass(string body, string expected)
        {
            Assert.Equal(expected, Ev.Run(Pt + "from dataclasses import astuple, is_dataclass\n" + body + "\n").Repr("r"));
        }

        private const string Iv = Imp + "from dataclasses import InitVar, astuple, make_dataclass\nfrom typing import ClassVar\n";

        // InitVar: an __init__ parameter handed to __post_init__, never stored, not a field.
        [Theory]
        [InlineData("@dataclass\nclass C:\n    a: int\n    scale: InitVar[int]\n    def __post_init__(self, scale):\n        self.a = self.a * scale\nr = C(2, 10).a", "20")]
        [InlineData("@dataclass\nclass C:\n    a: int\n    scale: InitVar[int] = 3\n    def __post_init__(self, scale):\n        self.a = self.a * scale\nr = C(2).a", "6")]
        [InlineData("@dataclass\nclass C:\n    a: int\n    scale: InitVar[int]\nr = [f.name for f in fields(C(1, 2))]", "['a']")]
        [InlineData("@dataclass\nclass C:\n    a: int\n    scale: InitVar[int]\nr = (repr(C(1, 2)), asdict(C(1, 2)), astuple(C(1, 2)))", "('C(a=1)', {'a': 1}, (1,))")]
        [InlineData("@dataclass\nclass C:\n    a: int\n    scale: InitVar[int]\nr = C(1, 2) == C(1, 3)", "True")]
        // __match_args__ is ('a', 'scale'): C(v, s) looks scale up on the instance, finds nothing, and fails
        [InlineData("@dataclass\nclass C:\n    a: int\n    scale: InitVar[int]\nr = 'no'\nmatch C(1, 2):\n    case C(v, s):\n        r = 'two'\n    case C(v):\n        r = v", "1")]
        [InlineData("@dataclass\nclass C:\n    a: int\n    scale: InitVar[int] = 1\nr = replace(C(1, 2), a=5).a", "5")]
        public void InitVarBehaviour(string body, string expected)
        {
            Assert.Equal(expected, Ev.Run(Iv + body + "\n").Repr("r"));
        }

        [Theory]
        [InlineData("@dataclass\nclass C:\n    a: int\n    scale: InitVar[int]\nC(1, 2).scale", "AttributeError")]
        [InlineData("@dataclass\nclass C:\n    a: int\n    scale: InitVar[int]\nreplace(C(1, 2), a=3)", "ValueError")]
        [InlineData("@dataclass\nclass C:\n    a: int\n    scale: InitVar[int]\nC(1)", "TypeError")]
        public void InitVarErrors(string body, string exc)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Iv + body + "\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
        }

        // ClassVar: a class attribute the machinery leaves alone; readable through the class and the instance.
        [Theory]
        [InlineData("@dataclass\nclass C:\n    a: int\n    limit: ClassVar[int] = 10\nr = (C(1).a, C(1).limit, C.limit)", "(1, 10, 10)")]
        [InlineData("@dataclass\nclass C:\n    a: int\n    limit: ClassVar[int] = 10\nr = [f.name for f in fields(C)]", "['a']")]
        [InlineData("@dataclass\nclass C:\n    a: int\n    limit: ClassVar[int] = 10\nr = (repr(C(1)), asdict(C(1)))", "('C(a=1)', {'a': 1})")]
        // __match_args__ is ('a',): a second positional sub-pattern is one too many, as in CPython
        [InlineData("@dataclass\nclass C:\n    a: int\n    limit: ClassVar[int] = 10\nr = 'no'\ntry:\n    match C(1):\n        case C(v, w):\n            r = 'two'\nexcept TypeError:\n    r = 'refused'", "'refused'")]
        [InlineData("class P:\n    n: int\n    limit: ClassVar[int] = 3\n    def __init__(self, n):\n        self.n = n\nr = (P(1).limit, P.limit)", "(3, 3)")]
        public void ClassVarBehaviour(string body, string expected)
        {
            Assert.Equal(expected, Ev.Run(Iv + body + "\n").Repr("r"));
        }

        [Fact]
        public void ClassVarIsNotAParameter()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Iv + "@dataclass\nclass C:\n    a: int\n    limit: ClassVar[int] = 10\nC(1, 2)\n"));
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
        }

        [Theory]
        [InlineData("C = make_dataclass('C', ['x', ('y', int), ('z', int, field(default=5))])\nr = (C(1, 2), C(1, 2).z, [f.name for f in fields(C)])", "(C(x=1, y=2, z=5), 5, ['x', 'y', 'z'])")]
        [InlineData("C = make_dataclass('C', [('y', int)])\nr = fields(C)[0].type", "'int'")]
        [InlineData("C = make_dataclass('C', ['x'], namespace={'double': lambda self: self.x * 2})\nr = C(4).double()", "8")]
        [InlineData("B = make_dataclass('B', ['a'])\nD = make_dataclass('D', ['b'], bases=(B,))\nr = (D(1, 2), D(1, 2) == D(1, 2))", "(D(a=1, b=2), True)")]
        [InlineData("C = make_dataclass('C', ['x'], frozen=True)\ntry:\n    C(1).x = 2\n    r = 'set'\nexcept AttributeError:\n    r = 'frozen'", "'frozen'")]
        [InlineData("C = make_dataclass('C', ['x', 'y'], order=True)\nr = C(1, 2) < C(1, 3)", "True")]
        public void MakeDataclass(string body, string expected)
        {
            Assert.Equal(expected, Ev.Run(Iv + body + "\n").Repr("r"));
        }

        [Theory]
        [InlineData("make_dataclass('C', ['x', 'x'])", "TypeError")]
        [InlineData("make_dataclass('C', [3])", "TypeError")]
        [InlineData("make_dataclass('C', ['x'], bogus=1)", "TypeError")]
        [InlineData("make_dataclass('C', ['x'], order=True, eq=False)", "ValueError")]
        public void MakeDataclassErrors(string expr, string exc)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Iv + "r = " + expr + "\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
        }

        [Fact]
        public void AstupleRefusesNonDataclass()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Imp + "from dataclasses import astuple\nclass P:\n    x: int\nastuple(3)\n"));
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void KeywordConstruction()
        {
            Assert.Equal("2", Ev.Run(Pt + "p = Point(y=2, x=1)\nb = p.y\n").Repr("b"));
        }

        [Fact]
        public void AutoReprAndEq()
        {
            var r = Ev.Run(Pt + "s = repr(Point(1, 2))\ne = Point(1, 2) == Point(1, 2)\nn = Point(1, 2) == Point(3, 3)\n");
            Assert.Equal("'Point(x=1, y=2)'", r.Repr("s"));
            Assert.Equal("True", r.Repr("e"));
            Assert.Equal("False", r.Repr("n"));
        }

        [Fact]
        public void FieldDefault()
        {
            var src = Imp + "@dataclass\nclass C:\n    a: int\n    b: int = 10\n"
                + "x = C(1).b\ny = C(1, 2).b\n";
            var r = Ev.Run(src);
            Assert.Equal("10", r.Repr("x"));
            Assert.Equal("2", r.Repr("y"));
        }

        [Fact]
        public void MissingRequiredArgRaises()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Imp + "@dataclass\nclass C:\n    a: int\nC()\n"));
            Assert.Contains("missing 1 required positional argument: a", ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]
        public void DefaultFactoryGivesFreshValue()
        {
            var src = Imp + "@dataclass\nclass Box:\n    items: list = field(default_factory=list)\n"
                + "b1 = Box()\nb2 = Box()\nb1.items.append(1)\nsep = b1.items is not b2.items\nn = len(b2.items)\n";
            var r = Ev.Run(src);
            Assert.Equal("True", r.Repr("sep"));
            Assert.Equal("0", r.Repr("n"));
        }

        [Fact]
        public void FrozenIsReadOnly()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Imp + "@dataclass(frozen=True)\nclass F:\n    v: int\nf = F(5)\nf.v = 6\n"));
            Assert.Equal("cannot assign to field 'v'", ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]
        public void NonDefaultAfterDefaultRejected()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Imp + "@dataclass\nclass C:\n    a: int = 1\n    b: int\n"));
            Assert.Equal("non-default argument 'b' follows default argument", ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]
        public void AsdictReturnsDict()
        {
            Assert.Equal("{'x': 1, 'y': 2}", Ev.Run(Pt + "d = asdict(Point(1, 2))\n").Repr("d"));
        }

        [Fact]
        public void FieldsReturnsFieldObjects()
        {
            Assert.Equal("('x', 'y')", Ev.Run(Pt + "f = tuple(fd.name for fd in fields(Point))\n").Repr("f"));
        }

        [Fact]
        public void DataclassInheritance()
        {
            var src = Imp + "@dataclass\nclass Base:\n    a: int\n"
                + "@dataclass\nclass Derived(Base):\n    b: int\n"
                + "d = Derived(1, 2)\nx = d.a\ny = d.b\n";
            var r = Ev.Run(src);
            Assert.Equal("1", r.Repr("x"));
            Assert.Equal("2", r.Repr("y"));
        }

        // ---- field(init=, repr=, compare=, kw_only=) / @dataclass(kw_only=) / replace() ----

        private const string Knobs = Imp + "@dataclass\nclass K:\n    a: int\n"
            + "    b: int = field(default=0, repr=False, compare=False)\n"
            + "    c: int = field(default=7, init=False)\n";

        [Theory]
        [InlineData(Knobs + "r = repr(K(1))\n", "'K(a=1, c=7)'")]                 // repr=False hides b
        [InlineData(Knobs + "r = K(1, 2) == K(1, 3)\n", "True")]                  // compare=False drops b
        [InlineData(Knobs + "r = K(1) == K(2)\n", "False")]
        [InlineData(Knobs + "r = K(1).c\n", "7")]                                 // init=False takes its default
        [InlineData(Knobs + "r = K(1, 5).b\n", "5")]
        [InlineData(Knobs + "r = [(f.name, f.init, f.repr, f.compare, f.kw_only) for f in fields(K)]\n",
            "[('a', True, True, True, False), ('b', True, False, False, False), ('c', False, True, True, False)]")]
        [InlineData(Knobs + "r = (fields(K)[0].hash, fields(K)[0].metadata)\n", "(None, {})")]
        [InlineData(Imp + "@dataclass(kw_only=True)\nclass Q:\n    a: int\n    b: int = 2\nr = Q(a=1)\n", "Q(a=1, b=2)")]
        [InlineData(Imp + "@dataclass\nclass M:\n    a: int = 1\n    b: int = field(kw_only=True)\nr = M(b=5)\n", "M(a=1, b=5)")]
        [InlineData(Pt + "r = replace(Point(1, 2), y=5)\n", "Point(x=1, y=5)")]
        [InlineData(Pt + "r = replace(Point(1, 2))\n", "Point(x=1, y=2)")]
        [InlineData(Imp + "@dataclass(frozen=True)\nclass F:\n    a: int\n    b: int = field(default=0, compare=False)\n"
            + "r = hash(F(1, 2)) == hash(F(1, 3))\n", "True")]
        [InlineData(Imp + "@dataclass(order=True)\nclass O:\n    a: int\n    b: int = field(default=0, compare=False)\n"
            + "r = (O(1, 5) < O(2, 0), O(1, 5) < O(1, 9))\n", "(True, False)")]
        public void FieldKnobs(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        [Theory]
        [InlineData(Knobs + "K(1, 2, 3)\n", "TypeError")]                          // c is not a parameter
        [InlineData(Knobs + "K(1, c=3)\n", "TypeError")]
        [InlineData(Imp + "@dataclass(kw_only=True)\nclass Q:\n    a: int\nQ(1)\n", "TypeError")]
        [InlineData(Imp + "field(nope=1)\n", "TypeError")]                         // refused, not ignored
        [InlineData(Imp + "dataclass(nope=1)\n", "TypeError")]
        [InlineData(Imp + "field(1)\n", "TypeError")]
        [InlineData(Pt + "replace(Point(1, 2), z=1)\n", "TypeError")]
        [InlineData(Knobs + "replace(K(1), c=2)\n", "ValueError")]                 // init=False cannot be replaced
        [InlineData(Imp + "replace(5, a=1)\n", "TypeError")]
        public void FieldKnobErrors(string src, string excType)
        {
            Assert.Equal(excType, Assert.Throws<ScriptException>(() => Ev.Run(src)).Value.ExcType.Name);
        }

        [Fact]
        public void KeywordOnlyMissingArgumentSaysKeywordOnly()
        {
            var ex = Assert.Throws<ScriptException>(
                () => Ev.Run(Imp + "@dataclass\nclass M:\n    a: int = 1\n    b: int = field(kw_only=True)\nM()\n"));
            Assert.Contains("missing 1 required keyword-only argument: b", ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]
        public void ReplaceRunsPostInitAndLeavesTheOriginal()
        {
            string src = Imp + "@dataclass\nclass P:\n    a: int\n    doubled: int = field(init=False, default=0)\n"
                + "    def __post_init__(self):\n        self.doubled = self.a * 2\n"
                + "orig = P(3)\nnew = replace(orig, a=5)\nr = (orig.doubled, new.doubled, orig is new)\n";
            Assert.Equal("(6, 10, False)", Ev.Run(src).Repr("r"));
        }

        // ---- §2.9 class patterns ----

        [Fact]
        public void ClassPatternPositionalAndKeyword()
        {
            var src = Pt + "def where(p):\n"
                + "    match p:\n"
                + "        case Point(0, 0):\n            return 'origin'\n"
                + "        case Point(x=0, y=b):\n            return ('yaxis', b)\n"
                + "        case Point(a, 0):\n            return ('xaxis', a)\n"
                + "        case Point(a, b):\n            return ('point', a, b)\n"
                + "        case _:\n            return 'other'\n"
                + "r1 = where(Point(0, 0))\nr2 = where(Point(0, 7))\nr3 = where(Point(5, 0))\nr4 = where(Point(3, 4))\nr5 = where(42)\n";
            var r = Ev.Run(src);
            Assert.Equal("'origin'", r.Repr("r1"));
            Assert.Equal("('yaxis', 7)", r.Repr("r2"));
            Assert.Equal("('xaxis', 5)", r.Repr("r3"));
            Assert.Equal("('point', 3, 4)", r.Repr("r4"));
            Assert.Equal("'other'", r.Repr("r5"));
        }

        [Fact]
        public void ClassPatternIsinstanceGate()
        {
            var src = Imp + "@dataclass\nclass A:\n    v: int\n"
                + "@dataclass\nclass B:\n    v: int\n"
                + "def f(o):\n    match o:\n        case A(v):\n            return ('a', v)\n        case B(v):\n            return ('b', v)\n"
                + "r1 = f(A(1))\nr2 = f(B(2))\n";
            var r = Ev.Run(src);
            Assert.Equal("('a', 1)", r.Repr("r1"));
            Assert.Equal("('b', 2)", r.Repr("r2"));
        }

        [Fact]
        public void ClassPatternWithNestedAndGuard()
        {
            var src = Pt + "def f(p):\n"
                + "    match p:\n"
                + "        case Point(x, y) if x == y:\n            return 'diagonal'\n"
                + "        case Point(x, y):\n            return 'off'\n"
                + "a = f(Point(3, 3))\nb = f(Point(3, 4))\n";
            var r = Ev.Run(src);
            Assert.Equal("'diagonal'", r.Repr("a"));
            Assert.Equal("'off'", r.Repr("b"));
        }

        // __match_args__ is readable, as CPython's dataclass writes it: the positional __init__ names.
        [Theory]
        [InlineData("r = Point.__match_args__", "('x', 'y')")]
        [InlineData("r = Point(1, 2).__match_args__", "('x', 'y')")]
        [InlineData("@dataclass\nclass C:\n    a: int\n    b: int = field(default=0, kw_only=True)\nr = C.__match_args__", "('a',)")]
        [InlineData("@dataclass(kw_only=True)\nclass C:\n    a: int\nr = C.__match_args__", "()")]
        [InlineData("@dataclass\nclass C:\n    a: int\n    scale: InitVar[int]\n    limit: ClassVar[int] = 10\n    b: int = field(init=False, default=0)\nr = C.__match_args__", "('a', 'scale')")]
        [InlineData("from dataclasses import make_dataclass\nQ = make_dataclass('Q', ['x', 'y'])\nr = Q.__match_args__", "('x', 'y')")]
        public void MatchArgsAttribute(string body, string expected)
        {
            Assert.Equal(expected, Ev.Run(Pt + Iv + body + "\n").Repr("r"));
        }

        [Theory]
        [InlineData("r = P.__match_args__")]
        [InlineData("r = P(1).__match_args__")]
        public void PlainClassHasNoMatchArgs(string body)
        {
            const string plain = "class P:\n    n: int\n    def __init__(self, n):\n        self.n = n\n";
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(plain + body + "\n"));
            Assert.Equal("AttributeError", ex.Value.ExcType.Name);
        }

        // A dataclass that refers to itself stops at the data-depth cap as a RecursionError the script can
        // catch (it used to end the run with an uncatchable stack abort, B16).
        [Theory]
        [InlineData("asdict")]
        [InlineData("astuple")]
        public void ACycleIsACatchableRecursionError(string fn)
        {
            string src = Iv + "@dataclass\nclass N:\n    v: int\n    nxt: object = None\nn = N(1)\nn.nxt = n\n"
                + "try:\n    " + fn + "(n)\n    r = 'no error'\nexcept RecursionError:\n    r = 'RecursionError'\n";
            Assert.Equal("'RecursionError'", Ev.Run(src).Repr("r"));
        }
    }
}

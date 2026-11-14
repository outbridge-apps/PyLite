using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // Review 6, T-1: fixes whose mutant (in t1's run) changed what a script sees and still passed every test. Each case
    // is t1's probe; the expected output is CPython 3.14.2's (Pyodide), equal to the engine's before the mutation.
    [Trait("Category", "Regression")]
    public class HeldFixesTests
    {
        private static string Out(string src)
        {
            RunResult r = TestEngines.WithCeiling(TestEngines.Unhurried).Run(src, new RunRequest());
            Assert.True(r.Succeeded, r.Succeeded ? null : r.Error.Message);
            return r.PrintOutput;
        }

        // A29: the fused comprehension path kept an IntEnum member, not its int
        [Fact]
        public void AnIntEnumThroughAConditionalComprehensionStaysAMember()
        {
            Assert.Equal("[<C.A: 1>, <C.B: 2>]\n[<C.A: 1>, <C.B: 2>]\n[<C.A: 1>, <C.B: 2>]\n[<C.A: 1>, <C.B: 2>]\n"
                + "{<C.A: 1>: <C.A: 1>, <C.B: 2>: <C.B: 2>}\n[<C.A: 1>, 5, <C.B: 2>]\n",
                Out("from enum import IntEnum\nclass C(IntEnum):\n    A = 1\n    B = 2\nprint([x for x in C])\n"
                + "print([x if x else 0 for x in C])\nprint([x if x > 1 else x for x in C])\nprint(list(x if x else 0 for x in C))\n"
                + "print({x: (x if x else 0) for x in C})\nprint([x if x else 0 for x in [C.A, 5, C.B]])\n"));
        }

        // A31: unary + of a big IntEnum member is an int
        [Fact]
        public void UnaryPlusOfABigIntEnumIsAnInt()
        {
            Assert.Equal("int int True\n", Out("from enum import IntEnum\nclass C(IntEnum):\n    A = 2 ** 70\n    S = 5\n"
                + "print(type(+C.A).__name__, type(+C.S).__name__, +C.A == 2 ** 70)\n"));
        }

        // A34: ordering reads every field, so an unset init=False field on the right is an AttributeError
        [Fact]
        public void DataclassOrderingReadsAnUnsetFieldOnTheRight()
        {
            Assert.Equal("AttributeError\n", Out("from dataclasses import dataclass, field\n@dataclass(order=True)\nclass P:\n"
                + "    a: int\n    b: int = field(init=False)\nx = P(1)\nx.b = 5\ny = P(2)\ntry:\n    print(x < y)\n"
                + "except AttributeError as e:\n    print('AttributeError')\n"));
        }

        // E07: super()'s fast path takes keyword and starred arguments as they are
        [Fact]
        public void SuperPassesKeywordAndStarredArguments()
        {
            Assert.Equal("(1, 5)\n(7, 2)\n", Out("class P:\n    def m(self, a=1, b=2):\n        return (a, b)\nclass C(P):\n"
                + "    def m(self, a=1, b=2):\n        return super().m(b=5)\nprint(C().m())\nclass D(P):\n"
                + "    def m(self, a=1, b=2):\n        return super().m(*[7])\nprint(D().m())\n"));
        }

        // E15: power-of-two bases carry their bits across a byte
        [Fact]
        public void PowerOfTwoBasesParseLongDigits()
        {
            Assert.Equal("0 40\n743431 966654 6246\n", Out("r = []\nfor base, digit in ((2, '1'), (4, '3'), (8, '7'), (16, 'f'), (32, 'v')):\n"
                + "    for n in (65, 100, 101, 257):\n        v = int(digit * n, base)\n        r.append(v == base ** n - 1)\n"
                + "        v2 = int('1' + '0' * n, base)\n        r.append(v2 == base ** n)\nprint(r.count(False), len(r))\n"
                + "print(int('1234567' * 20, 8) % 1000003, int('abcdef0123456789' * 9, 16) % 1000003, int('abcdefghijklmnopqrstuv' * 9, 32) % 1000003)\n"));
        }

        // E01: equal counts print in insertion order, past 16 keys too
        [Fact]
        public void CounterTiesKeepInsertionOrder()
        {
            Assert.Equal("Counter({'k02': 3, 'k05': 3, 'k08': 3, 'k11': 3, 'k14': 3, 'k17': 3, 'k20': 3, 'k23': 3, 'k26': 3, 'k29': 3, "
                + "'k32': 3, 'k35': 3, 'k38': 3, 'k41': 3, 'k44': 3, 'k47': 3, 'k50': 3, 'k53': 3, 'k56': 3, '\n"
                + "['k02', 'k05', 'k08', 'k11', 'k14', 'k17', 'k20', 'k23', 'k26', 'k29', 'k32', 'k35', 'k38', 'k41', 'k44', 'k47', 'k50', 'k53', 'k56', 'k59']\n",
                Out("from collections import Counter\nc = Counter()\nfor i in range(60):\n    c['k%02d' % i] = 1 + (i % 3)\n"
                + "print(repr(c)[:200])\nprint([k for k, v in c.most_common()][:20])\n"));
        }

        // E02: counts past 2**63 sort by value
        [Fact]
        public void CounterOrdersBigCountsByValue()
        {
            Assert.Equal("Counter({'c': 1208925819614629174706176, 'a': 1180591620717411303424, 'd': 7, 'b': 5, 'e': -73786976294838206464})\n"
                + "[('c', 1208925819614629174706176), ('a', 1180591620717411303424), ('d', 7)]\n",
                Out("from collections import Counter\nc = Counter()\nc['a'] = 2 ** 70\nc['b'] = 5\nc['c'] = 2 ** 80\nc['d'] = 7\n"
                + "c['e'] = -(2 ** 66)\nprint(repr(c))\nprint(c.most_common(3))\n"));
        }

        // R01: a class nested in a method mangles names with its own name
        [Fact]
        public void ANestedClassManglesWithItsOwnName()
        {
            Assert.Equal("True False\n", Out("class A:\n    def __init__(self):\n        self.__x = 0\n    def make(self):\n"
                + "        v = self.__x\n        class B:\n            def __init__(self):\n                self.__x = 1\n"
                + "        return B()\nb = A().make()\nprint(hasattr(b, '_B__x'), hasattr(b, '_A__x'))\n"));
        }

        // K09: the double-length product in sumprod, hypot and dist
        [Fact]
        public void SumprodHypotAndDistAreCorrectlyRounded()
        {
            Assert.Equal("458\n957f15fd04675dafd4124a7d1eb993ae\n", Out("import math\nout = []\n"
                + "vals = [1e16 + 2.0, 3.0000000000000004, 1.0000000000000002, 123456789.123456789, 0.1, 1e-300, 1e300, 1e290, 2.5e290, 3e153, 1e154, 7.0e-3, 134217729.0, 134217728.0, 9007199254740993.0]\n"
                + "for a in vals:\n    for b in vals:\n        out.append(repr(math.sumprod([a, 1.0], [b, -1.0])))\n        out.append(repr(math.hypot(a, b)))\n"
                + "out.append(repr(math.sumprod([1e200, 1e200, -1e200], [1e200, 1.0, 1e200])))\n"
                + "out.append(repr(math.sumprod([0.1] * 10, [0.1] * 10)))\nout.append(repr(math.dist([1e154, 1], [0, 1e154])))\n"
                + "out.append(repr(math.sumprod([1e308, 1e308], [10.0, -10.0])))\nout.append(repr(math.sumprod([3e-200, 1e-200], [3e-200, 5e-200])))\n"
                + "out.append(repr(math.hypot(3e-200, 4e-200)))\nout.append(repr(math.hypot(1e290, 1e290)))\n"
                + "out.append(repr(math.hypot(2.5e290, 1.5e290, 1e10)))\nprint(len(out))\nimport hashlib\n"
                + "print(hashlib.md5(' '.join(out).encode()).hexdigest())\n"));
        }

        // A15: a namespace tag is charged by its length each time it is written: 4,203 steps here, 2,928 without that
        // charge (51 tags of 100,000 chars). Steps are deterministic, so the line between the two holds.
        [Fact]
        public void ALongNamespaceTagIsChargedWhenWritten()
        {
            RunResult r = TestEngines.WithCeiling(TestEngines.Unhurried).Run("import xml.etree.ElementTree as ET\n"
                + "t = '{' + 'u' * 100000 + '}a'\nroot = ET.Element(t)\nfor _ in range(50):\n    ET.SubElement(root, t)\nET.tostring(root)\n",
                new RunRequest());
            Assert.True(r.Succeeded);
            Assert.True(r.Stats.Steps >= 3550, r.Stats.Steps.ToString());
        }
    }
}

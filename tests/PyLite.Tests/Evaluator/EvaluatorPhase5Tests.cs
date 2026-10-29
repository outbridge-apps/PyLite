using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests;
using Xunit;

namespace Outbridge.PyLite.Tests.Evaluator
{
    [Trait("Category", "Compatibility")]
    public class EvaluatorPhase5Tests
    {
        private static string S(EvResult r, string name)
        {
            return r.Get(name).Str(r.Ctx);
        }

        // ---------- starred / sequence unpack (T-EVL-08) ----------

        [Theory]
        [InlineData("a, b = 1, 2\nr = [a, b]\n", "[1, 2]")]
        [InlineData("a, b, c = [1, 2, 3]\nr = [a, b, c]\n", "[1, 2, 3]")]
        [InlineData("a, *b = [1, 2, 3, 4]\nr = [a, b]\n", "[1, [2, 3, 4]]")]
        [InlineData("a, *b, c = [1, 2, 3, 4]\nr = [a, b, c]\n", "[1, [2, 3], 4]")]
        [InlineData("*a, b = [1, 2, 3]\nr = [a, b]\n", "[[1, 2], 3]")]
        [InlineData("a, *b = [1]\nr = [a, b]\n", "[1, []]")]
        [InlineData("a, *b = (1, 2, 3)\nr = b\n", "[2, 3]")]
        [InlineData("(a, (b, c)) = (1, (2, 3))\nr = [a, b, c]\n", "[1, 2, 3]")]
        [InlineData("a, b, c = 'xyz'\nr = [a, b, c]\n", "['x', 'y', 'z']")]
        [InlineData("*a, = [1, 2]\nr = a\n", "[1, 2]")]
        public void Unpack(string src, string expectedRepr)
        {
            Assert.Equal(expectedRepr, Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void UnpackTooMany()
        {
            Assert.Equal("too many values to unpack (expected 2)", Ev.Err("a, b = [1, 2, 3]\n"));
        }

        [Fact]
        public void UnpackTooFew()
        {
            Assert.Equal("not enough values to unpack (expected 3, got 2)", Ev.Err("a, b, c = [1, 2]\n"));
        }

        [Fact]
        public void UnpackStarTooFew()
        {
            Assert.Equal("not enough values to unpack (expected at least 2, got 1)", Ev.Err("a, *b, c = [1]\n"));
        }

        [Fact]
        public void UnpackNonIterable()
        {
            Assert.Same(PyExceptionTypes.TypeError, Ev.ErrType("a, b = 5\n"));
        }

        // ---------- augmented assignment inplace (T-EVL-09) ----------

        [Fact]
        public void ListAugAddIsInplace()
        {
            var r = Ev.Run("a = [1, 2]\nb = a\na += [3]\nsame = a is b\n");
            Assert.Equal("[1, 2, 3]", r.Repr("a"));
            Assert.Equal("[1, 2, 3]", r.Repr("b"));
            Assert.Equal("True", r.Repr("same"));
        }

        [Fact]
        public void ListAugMulInplace()
        {
            Assert.Equal("[1, 2, 1, 2]", Ev.Run("a = [1, 2]\na *= 2\n").Repr("a"));
        }

        [Fact]
        public void IntAugAdd()
        {
            Assert.Equal("8", Ev.Run("i = 5\ni += 3\n").Repr("i"));
        }

        [Fact]
        public void AugmentedTypeErrorUsesAugmentedSymbol()
        {
            Assert.Equal("unsupported operand type(s) for -=: 'int' and 'str'", Ev.Err("x = 1\nx -= 'a'\n"));
        }

        // ---------- import (T-EVL-18) ----------

        [Theory]
        [InlineData("import math\nok = 3.14 < math.pi < 3.15\n")]
        [InlineData("import math as m\nok = 3.14 < m.pi < 3.15\n")]
        [InlineData("from math import pi\nok = 3.14 < pi < 3.15\n")]
        [InlineData("from math import pi as P\nok = 3.14 < P < 3.15\n")]
        [InlineData("from math import pi, e\nok = pi > 3 and e > 2\n")]
        public void Import(string src)
        {
            Assert.Equal("True", Ev.Run(src).Repr("ok"));
        }

        [Fact]
        public void ImportMissingModule()
        {
            Assert.Equal("No module named 'nope'", Ev.Err("import nope\n"));
        }

        [Fact]
        public void FromImportMissingName()
        {
            Assert.Equal("cannot import name 'nope'", Ev.Err("from math import nope\n"));
        }

        [Fact]
        public void FromImportMissingModule()
        {
            Assert.Equal("No module named 'nope'", Ev.Err("from nope import x\n"));
        }

        [Fact]
        public void DottedImportAs()
        {
            var r = Ev.Run("import xml.etree.ElementTree as ET\nr = ET\n");
            Assert.Equal("<module 'xml.etree.ElementTree' (built-in)>", r.Repr("r"));
        }

        // A dotted registry key implies packages: "xml.etree.ElementTree" gives "xml" and "xml.etree",
        // synthesized on demand so every CPython import form resolves.
        [Theory]
        [InlineData("from xml.etree import ElementTree as ET\nr = ET.fromstring('<a/>').tag\n", "'a'")]
        [InlineData("import xml.etree.ElementTree\nr = xml.etree.ElementTree.fromstring('<b/>').tag\n", "'b'")]
        [InlineData("from xml import etree\nr = etree.ElementTree.fromstring('<c/>').tag\n", "'c'")]
        [InlineData("import xml\nr = xml.etree.ElementTree.fromstring('<d/>').tag\n", "'d'")]
        [InlineData("import xml\nr = repr(xml)\n", "\"<module 'xml' (built-in)>\"")]
        [InlineData("from xml import etree\nimport xml.etree.ElementTree as ET\nr = etree.ElementTree is ET\n", "True")]
        public void PackageImportForms(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        [Theory]
        [InlineData("from xml.etree import nothing\n", "cannot import name 'nothing'")]
        [InlineData("import xml.nope\n", "No module named 'xml.nope'")]
        [InlineData("from xml import nope\n", "cannot import name 'nope'")]
        [InlineData("import nope.thing\n", "No module named 'nope.thing'")]
        public void PackageImportErrors(string src, string message)
        {
            Assert.Equal(message, Ev.Err(src));
        }

        [Fact]
        public void ImportInsideFunction()
        {
            Assert.Equal("True", Ev.Run("def f():\n    import math\n    return 3 < math.pi\nok = f()\n").Repr("ok"));
        }

        // ---------- f-strings (T-EVL-17) ----------

        [Theory]
        [InlineData("r = f''\n", "")]
        [InlineData("r = f'hello'\n", "hello")]
        [InlineData("r = f'{5}'\n", "5")]
        [InlineData("r = f'{1 + 2}'\n", "3")]
        [InlineData("x = 7\nr = f'a{x}b'\n", "a7b")]
        [InlineData("x = 'hi'\nr = f'{x!r}'\n", "'hi'")]
        [InlineData("x = 42\nr = f'{x!s}'\n", "42")]
        [InlineData("x = 'ab'\nr = f'{x:>5}'\n", "   ab")]
        [InlineData("x = 'ab'\nw = 5\nr = f'{x:{w}}'\n", "ab   ")]
        [InlineData("x = 3.14159\nw = 8\np = 2\nr = f'{x:{w}.{p}f}'\n", "    3.14")]
        [InlineData("r = f'{{literal}}'\n", "{literal}")]
        [InlineData("a = 1\nb = 2\nr = f'{a}{b}'\n", "12")]
        [InlineData("r = f'{3.14}'\n", "3.14")]
        [InlineData("x = 1\nr = f'{f\"{x}\"}'\n", "1")]
        public void FString(string src, string expected)
        {
            Assert.Equal(expected, S(Ev.Run(src), "r"));
        }

        [Fact]
        public void FStringBadFormatCode()
        {
            Assert.Equal("Unknown format code 'd' for object of type 'float'", Ev.Err("x = 1.5\nr = f'{x:d}'\n"));
        }
    }
}

using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Spec
{
    // Review 3, P3 (W1-W13): answers a script took for right and were not. Every expected value is CPython
    // 3.14's, checked on the oracle.
    [Trait("Category", "Regression")]
    public class SilentWrongAnswersTests
    {
        [Theory]
        [InlineData("sum([0.1] * 10)", "1.0")]
        [InlineData("sum([3.3, 1.1, 2.2])", "6.6")]
        [InlineData("sum([1, 2, 0.5, 3])", "6.5")]
        [InlineData("sum([-0.0], -0.0)", "-0.0")]
        [InlineData("sum([float('inf'), 1.0])", "inf")]
        [InlineData("sum([1e100, 1.0, -1e100, 1.0])", "2.0")]
        [InlineData("sum([0.1, 0.2, 2**70, 0.3])", "1.1805916207174113e+21")]
        [InlineData("sum([0.1, True, 0.2])", "1.3")]
        [InlineData("sum([2**62, 2**62, 2**62])", "13835058055282163712")]
        [InlineData("sum(x * 0.1 for x in range(100))", "495.0")]
        [InlineData("sum([0.1] * 3, 0.0)", "0.30000000000000004")]
        public void SumCompensatesFloats(string expr, string expected)   // W1
        {
            Assert.Equal(expected, Ev.ReprOf(expr));
        }

        [Theory]
        [InlineData("format(D('1E+2'), 'f')", "'100'")]
        [InlineData("format(D('1.23456789'), 'f')", "'1.23456789'")]
        [InlineData("format(D('100'), '.2')", "'1.0E+2'")]
        [InlineData("format(D('1234567.891'), ',.2f')", "'1,234,567.89'")]
        [InlineData("format(D('0.000001234'), 'g')", "'0.000001234'")]
        [InlineData("format(D('1.5E-7'), 'e')", "'1.5e-7'")]
        [InlineData("format(D('99.97'), '.3g')", "'100'")]
        [InlineData("format(D('0'), '.2e')", "'0.00e+2'")]
        [InlineData("format(D('-0.5'), '.1%')", "'-50.0%'")]
        [InlineData("format(D('1234.5'), '020_.3f')", "'0_000_000_001_234.500'")]
        [InlineData("format(D('1234.5'), '#.0f')", "'1234.'")]
        [InlineData("format(D('1234.5'), 'n')", "'1234.5'")]
        [InlineData("format(D('0E+3'), 'f')", "'0'")]
        public void DecimalFormatsAsDecimal(string expr, string expected)   // W2
        {
            Assert.Equal(expected, Ev.ReprOf("from decimal import Decimal as D\n", expr));
        }

        // A wide precision pads with zeros written as text, never as 10**n; Fraction's power is deadline-checked.
        [Fact]
        public void AWidePrecisionIsPaddedNotComputed()
        {
            EvResult d = Ev.Run("from decimal import Decimal\na = format(Decimal('1.5'), '.200000f')\nr = (len(a), a[:5], a[-3:])\n");
            Assert.Equal("(200002, '1.500', '000')", d.Repr("r"));
            Assert.True(d.Ctx.Budget.StepsUsed < 10_000, d.Ctx.Budget.StepsUsed.ToString());
            EvResult f = Ev.Run("from fractions import Fraction\nb = format(Fraction(1, 3), '.4000f')\nr = (len(b), b[-3:])\n");
            Assert.Equal("(4002, '333')", f.Repr("r"));
        }

        [Fact]
        public void DecimalRejectsANonDecimalCode()
        {
            Assert.Equal("invalid format string", Ev.Err("from decimal import Decimal\nformat(Decimal(1), 'x')\n"));
        }

        [Fact]
        public void DecimalRoundsInTheContextMode()   // W3
        {
            const string src = "from decimal import Decimal as D, localcontext, ROUND_HALF_UP, ROUND_DOWN\n"
                + "with localcontext() as c:\n"
                + "    c.rounding = ROUND_HALF_UP\n"
                + "    a = (f'{D(\"2.665\"):.2f}', format(D('2.5'), '.0f'), round(D('2.5')), round(D('3.5')), round(D('2.665'), 2))\n"
                + "    c.rounding = ROUND_DOWN\n"
                + "    b = (format(D('1.999'), '.2f'), format(D('1.999'), '.2e'), format(D('-1.999'), '.2g'))\n";
            EvResult r = Ev.Run(src);
            Assert.Equal("('2.67', '3', 2, 4, Decimal('2.67'))", r.Repr("a"));
            Assert.Equal("('1.99', '1.99e+0', '-1.9')", r.Repr("b"));
        }

        [Theory]
        [InlineData("'%.2f' % D('1.005')", "'1.00'")]
        [InlineData("'%d' % D('-7.9')", "'-7'")]
        [InlineData("'%i' % F(7, 2)", "'3'")]
        [InlineData("'%.3f' % F(1, 3)", "'0.333'")]
        [InlineData("'%g' % F(-1, 8)", "'-0.125'")]
        [InlineData("format(F(1, 3), '.2f')", "'0.33'")]
        [InlineData("format(F(-7, 2), '')", "'-7/2'")]
        [InlineData("format(F(2, 1), '#')", "'2/1'")]
        [InlineData("format(F(1234567, 3), '_.3f')", "'411_522.333'")]
        [InlineData("format(F(5, 2), '.0f')", "'2'")]
        [InlineData("format(F(1, 10**8), 'g')", "'1e-08'")]
        [InlineData("format(F(10**25, 3), '.3e')", "'3.333e+24'")]
        [InlineData("format(F(-1, 3), '012,.3f')", "'-000,000.333'")]
        [InlineData("format(F(1, 3), '*^14.4g')", "'****0.3333****'")]
        [InlineData("format(F(999, 1000), '.0g')", "'1'")]
        public void PercentAndFormatTakeDecimalAndFraction(string expr, string expected)   // W4
        {
            Assert.Equal(expected, Ev.ReprOf("from decimal import Decimal as D\nfrom fractions import Fraction as F\n", expr));
        }

        [Fact]
        public void PercentNamesTheRealNumberItWanted()
        {
            Assert.Equal("%d format: a real number is required, not str", Ev.Err("'%d' % 'x'\n"));
            Assert.Equal("must be real number, not str", Ev.Err("'%f' % 'x'\n"));
            Assert.Equal("Invalid format specifier '<05.2f' for object of type 'Fraction'",
                Ev.Err("from fractions import Fraction\nformat(Fraction(1, 3), '<05.2f')\n"));
        }

        [Theory]
        [InlineData("rows = [{'q': 1}, {'q': 7}]\nfound = None\nok = any((found := r)['q'] > 4 for r in rows)\nr = (ok, found)\n", "(True, {'q': 7})")]
        [InlineData("xs = [1, 2, 3]\ntotal = 0\nr = ([total := total + x for x in xs], total)\n", "([1, 3, 6], 6)")]
        [InlineData("def f():\n    tot = 0\n    vals = [tot := tot + v for v in [5, 6]]\n    return vals, tot\nr = f()\n", "([5, 11], 11)")]
        [InlineData("def g():\n    return [y for x in range(3) if (y := x * 2) > 1], y\nr = g()\n", "([2, 4], 4)")]
        [InlineData("r = ([[(w := j) for j in range(2)] for i in range(2)], w)\n", "([[0, 1], [0, 1]], 1)")]
        public void AWalrusInAComprehensionBindsOutside(string src, string expected)   // W5
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        [Theory]
        [InlineData("r = [x for x in (y := [1, 2])]\n", "assignment expression cannot be used in a comprehension iterable expression")]
        [InlineData("r = [(x := 1) for x in range(2)]\n", "assignment expression cannot rebind comprehension iteration variable 'x'")]
        [InlineData("r = [y for x in [1] if (y := 1) for y in [2]]\n", "comprehension inner loop cannot rebind assignment expression target 'y'")]
        [InlineData("class A:\n    r = [(z := 1) for _ in range(1)]\n", "assignment expression within a comprehension cannot be used in a class body")]
        public void AWalrusTheComprehensionCannotHaveIsASyntaxError(string src, string message)
        {
            var ex = Assert.Throws<PySyntaxErrorException>(() => Ev.Run(src));
            Assert.Contains(message, ex.Message);
        }

        [Fact]
        public void TypeOfAModuleValueIsTheModulesClass()   // W6
        {
            const string src = "import datetime, collections, io, uuid, zoneinfo, csv, decimal, fractions\n"
                + "import xml.etree.ElementTree as ET\n"
                + "r = [type(datetime.date(2020, 1, 1)) is datetime.date, type(datetime.datetime(2020, 1, 1)) is datetime.datetime,\n"
                + "     type(decimal.Decimal(1)) is decimal.Decimal, type(fractions.Fraction(1)) is fractions.Fraction,\n"
                + "     type(collections.Counter()) is collections.Counter, type(collections.deque()) is collections.deque,\n"
                + "     type(io.StringIO()) is io.StringIO, type(uuid.uuid4()) is uuid.UUID,\n"
                + "     type(zoneinfo.ZoneInfo('UTC')) is zoneinfo.ZoneInfo, type(csv.Sniffer()) is csv.Sniffer,\n"
                + "     isinstance(ET.Element('a'), ET.Element), type(ET.Element('a')) is ET.Element]\n";
            Assert.Equal("[True, True, True, True, True, True, True, True, True, True, True, True]", Ev.Run(src).Repr("r"));
        }

        [Theory]
        [InlineData("{*}a", "['{urn:d}a', '{urn:q}a']")]
        [InlineData("{urn:d}*", "['{urn:d}a', '{urn:d}b']")]
        [InlineData("{*}*", "['{urn:d}a', '{urn:q}a', '{urn:d}b']")]
        [InlineData(".//{*}a", "['{urn:d}a', '{urn:q}a', '{urn:d}a']")]
        [InlineData("{*}b[{*}a]", "['{urn:d}b']")]
        [InlineData("{}a", "[]")]
        public void PathWildcardsMatchNamespaces(string path, string expected)   // W7
        {
            string src = "import xml.etree.ElementTree as ET\n"
                + "root = ET.fromstring('<r xmlns=\"urn:d\" xmlns:q=\"urn:q\"><a>x</a><q:a>y</q:a><b><a>z</a></b></r>')\n"
                + "r = [e.tag for e in root.findall('" + path + "')]\n";
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        [Theory]
        [InlineData("ET.fromstring(b'\\xef\\xbb\\xbf<a>x</a>').text", "'x'")]
        [InlineData("ET.fromstring('<a>\\u00e9</a>'.encode('utf-16')).text", "'é'")]
        [InlineData("ET.fromstring('<a>\\u00e9</a>'.encode('utf-16-le')).text", "'é'")]
        [InlineData("ET.fromstring('<?xml version=\"1.0\" encoding=\"windows-1252\"?><a>\\u20ac</a>'.encode('cp1252')).text", "'€'")]
        [InlineData("ET.fromstring('\\ufeff<a>x</a>').text", "'x'")]
        [InlineData("json.loads(b'\\xef\\xbb\\xbf{\"a\": 1}')", "{'a': 1}")]
        [InlineData("json.loads('{\"a\": \"\\u00e9\"}'.encode('utf-16-be'))", "{'a': 'é'}")]
        [InlineData("json.loads(b'\\x00\\x31')", "1")]
        [InlineData("json.load(io.BytesIO(b'[3]'))", "[3]")]
        public void ByteDocumentsAreDecodedAsCPythonDoes(string expr, string expected)   // W8
        {
            Assert.Equal(expected, Ev.ReprOf("import io, json\nimport xml.etree.ElementTree as ET\n", expr));
        }

        [Theory]
        [InlineData("import xml.etree.ElementTree as ET\nET.fromstring(b'<a>\\n<b>\\xe9\\xe9</b></a>')\n", "not well-formed (invalid token): line 2, column 3")]
        [InlineData("import xml.etree.ElementTree as ET\nET.fromstring(b'<?xml version=\"1.0\" encoding=\"utf-16\"?><a/>')\n", "encoding specified in XML declaration is incorrect: line 1, column 30")]
        [InlineData("import json\njson.loads('\\ufeff[1]')\n", "Unexpected UTF-8 BOM (decode using utf-8-sig): line 1 column 1 (char 0)")]
        [InlineData("import json\njson.loads(5)\n", "the JSON object must be str, bytes or bytearray, not int")]
        public void ByteDocumentErrorsAsCPython(string src, string message)
        {
            Assert.Equal(message, Ev.Err(src));
        }

        [Theory]
        [InlineData("list(csv.reader(io.StringIO('1,\"L1\\r\\nL2\"')))", "[['1', 'L1\\r\\nL2']]")]
        [InlineData("list(csv.reader(['a,\"b', 'c\"']))", "[['a', 'bc']]")]
        [InlineData("list(csv.reader(['a,\"b' + chr(92), 'c\"'], escapechar=chr(92)))", "[['a', 'b\\nc']]")]
        [InlineData("list(csv.DictReader(io.StringIO('h1,h2\\r\\n1,\"x\\r\\ny\"\\r\\n')))", "[{'h1': '1', 'h2': 'x\\r\\ny'}]")]
        public void AQuotedFieldKeepsItsLineEndings(string expr, string expected)   // W9
        {
            Assert.Equal(expected, Ev.ReprOf("import csv, io\n", expr));
        }

        [Theory]
        [InlineData("'Europe/Moscow', 2012, 1, 1", "('4:00:00', '0:00:00', 'MSK')")]
        [InlineData("'America/Asuncion', 2020, 7, 1", "('-1 day, 20:00:00', '0:00:00', '-04')")]
        [InlineData("'America/Asuncion', 2020, 12, 1", "('-1 day, 21:00:00', '1:00:00', '-03')")]
        [InlineData("'America/Sao_Paulo', 2018, 12, 1", "('-1 day, 22:00:00', '1:00:00', '-02')")]
        [InlineData("'Europe/Berlin', 2020, 7, 1", "('2:00:00', '1:00:00', 'CEST')")]
        public void DstIsMeasuredFromTheStandardOffsetOfItsTime(string args, string expected)   // W10
        {
            string src = "import zoneinfo, datetime\n"
                + "def z(key, *a):\n"
                + "    d = datetime.datetime(*a, tzinfo=zoneinfo.ZoneInfo(key))\n"
                + "    return str(d.utcoffset()), str(d.dst()), d.tzname()\n"
                + "r = z(" + args + ")\n";
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        [Theory]
        [InlineData("[0, 10**200]", "7.071067811865475e+199")]
        [InlineData("[13, 43, 32, 48]", "15.513435037626795")]
        [InlineData("[1e-300, 3e-300]", "1.4142135623730952e-300")]
        [InlineData("[Decimal('1.5'), Decimal('2.5'), Decimal('4.75')]", "Decimal('1.664582029619848242981698527')")]
        public void StdevIsCorrectlyRounded(string data, string expected)   // W11
        {
            Assert.Equal(expected, Ev.ReprOf("import statistics\nfrom decimal import Decimal\n", "statistics.stdev(" + data + ")"));
        }

        [Fact]
        public void StdevNamesItselfAndRefusesNaN()
        {
            Assert.Equal("stdev requires at least two data points", Ev.Err("import statistics\nstatistics.stdev([1])\n"));
            Assert.Equal("pstdev requires at least one data point", Ev.Err("import statistics\nstatistics.pstdev([])\n"));
            Assert.Equal("inf or nan encountered in data", Ev.Err("import statistics\nstatistics.stdev([float('nan'), 1])\n"));
        }

        [Fact]
        public void AListComprehensionLetsStopIterationThrough()   // W12
        {
            const string src = "it = iter([1, 2])\ntry:\n    r = [next(it) for _ in range(3)]\nexcept StopIteration:\n    r = 'stop'\n";
            Assert.Equal("'stop'", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void AClassBodyReadsAsCPythonsDoes()   // W13
        {
            const string src = "x = 'mod'\n"
                + "def f():\n    x = 5\n    class A:\n        x = x\n    return A.x\n"
                + "class B:\n    y: int = 5\n    z = y\n"
                + "order = []\n"
                + "def note(v):\n    order.append(v)\n    return v\n"
                + "def deco(c):\n    order.append('apply')\n    return c\n"
                + "class Base: pass\n"
                + "@note(deco)\nclass C(note(Base)):\n    a = note(1)\n    def m(self, d=note(2)):\n        return d\n    b = note(3)\n    f1: int = note(4)\n"
                + "r = (f(), B.z, order[2:], C().m())\n";
            Assert.Equal("('mod', 5, [1, 2, 3, 4, 'apply'], 2)", Ev.Run(src).Repr("r"));
        }
    }
}

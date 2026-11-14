using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // Review 7 (crashes, limits, regressions against 1.1.0). Each answer is CPython 3.14's.
    [Trait("Category", "Regression")]
    public class Review7FixTests
    {
        private static RunResult Run(string source, int deadlineMs = 120_000, long? maxAllocBytes = null)
        {
            ResourceLimits limits = TestEngines.Unhurried.With(deadlineMs: deadlineMs, maxAllocBytes: maxAllocBytes);
            return new ScriptEngine(new EngineOptions { DefaultLimits = limits }).Run(source, new RunRequest());
        }

        private static string Out(string source, int deadlineMs = 120_000, long? maxAllocBytes = null)
        {
            RunResult r = Run(source, deadlineMs, maxAllocBytes);
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.LimitName + " " + r.Error.Message);
            return r.PrintOutput;
        }

        [Theory]
        [InlineData("import typing\ntry:\n    typing.Union[()]\nexcept TypeError as e:\n    print(e)\n", "Cannot take a Union of no types.\n")]
        [InlineData("print((int | None) == (None | int), (int | str) == (str | int), (int | str) == (int | str | float), hash(int | str) == hash(str | int))\n",
            "True True False True\n")]
        [InlineData("import re\ndef sp(m):\n    return None if m is None else m.span()\n"
            + "print(sp(re.compile(r'\\bab').match('xab', 1, 3)), sp(re.compile(r'\\bab').match('-ab', 1, 3)),\n"
            + "      sp(re.compile(r'(?<=x)ab$').search('xabab', 1, 3)), sp(re.compile(r'b\\Z').search('abc', 0, 2)),\n"
            + "      sp(re.compile(r'ab\\b').search('abab', 0, 2)), sp(re.compile(r'\\w+').fullmatch('abcdef', 1, 4)),\n"
            + "      re.compile(r'\\w').findall('abcdef', 1, 4), [m.span() for m in re.compile(r'\\w').finditer('abcdef', 2, 5)],\n"
            + "      sp(re.compile(r'^b', re.M).search('a\\nbc', 2, 3)), sp(re.compile(r'(?:ab)+').match('ababab', 0, 5)))\n",
            "None (1, 3) (1, 3) (1, 2) (0, 2) (1, 4) ['b', 'c', 'd'] [(2, 3), (3, 4), (4, 5)] (2, 3) (0, 4)\n")]
        [InlineData("import xml.etree.ElementTree as ET\nr = ET.fromstring('<r><c>a<d>b</d>c</c><c>ab</c><c>abcd</c><c>x</c><e><c>abc</c></e></r>')\n"
            + "print([ET.tostring(x) for x in r.findall(\"*[.='abc']\")], len(r.findall(\"c[.='ab']\")), len(r.findall(\"*[c='abc']\")), len(r.findall(\"*[.!='abc']\")))\n"
            + "r2 = ET.fromstring('<r><c>q<d/>z</c></r>')\nr2[0][0].tail = 5\ntry:\n    r2.findall(\"*[.='x']\")\nexcept TypeError as e:\n    print(e)\n",
            "[b'<c>a<d>b</d>c</c>', b'<e><c>abc</c></e>'] 1 1 3\nsequence item 1: expected str instance, int found\n")]
        [InlineData("import xml.etree.ElementTree as ET\nfor enc in ('ISO-8859-15', 'cp437', 'mac-roman', 'UTF-32', 'utf-32-le', '', ' utf-8', 'Shift_JIS', 'utf-16', 'latin2', 'ibm437', 'koi8_u', '850', 'foo', 'iso-8859-12'):\n"
            + "    doc = ('<?xml version=\"1.0\" encoding=\"%s\"?><a b=\"1\">text</a>' % enc).encode('ascii')\n"
            + "    try:\n        r = ET.fromstring(doc)\n        print(r.tag, r.text)\n    except Exception as e:\n        print(type(e).__name__, e)\n",
            "a text\na text\na text\nValueError multi-byte encodings are not supported\nValueError multi-byte encodings are not supported\n"
            + "ParseError XML declaration not well-formed: line 1, column 30\nParseError XML declaration not well-formed: line 1, column 30\n"
            + "ValueError multi-byte encodings are not supported\nParseError encoding specified in XML declaration is incorrect: line 1, column 30\n"
            + "a text\na text\na text\nParseError XML declaration not well-formed: line 1, column 30\n"
            + "LookupError unknown encoding: foo\nLookupError unknown encoding: iso-8859-12\n")]
        [InlineData("from collections import namedtuple\nimport typing\nfor i in range(3):\n    P = namedtuple('P', 'a b')\n    p = P(1, 2)\n"
            + "Q = typing.NamedTuple('Q', [('x', int)])\nprint(type(p) is P, isinstance(p, P), type(Q(1)) is Q, p)\n",
            "True True True P(a=1, b=2)\n")]
        // P3 rows
        [InlineData("import xml.etree.ElementTree as ET\nr = ET.fromstring('<r><c/><c/></r>')\n"
            + "for p in ('c[last()-2147483647]', 'c[last()-99999999999999999999]', 'c[99999999999999999999]', 'c[2147483648]', 'c[last()-1]', 'c[01]', 'c[last()-0]', 'c[00]'):\n"
            + "    try:\n        print(p, len(r.findall(p)))\n    except SyntaxError as e:\n        print(p, e)\n",
            "c[last()-2147483647] 0\nc[last()-99999999999999999999] 0\nc[99999999999999999999] 0\nc[2147483648] 0\nc[last()-1] 1\nc[01] 1\n"
            + "c[last()-0] XPath offset from last() must be negative\nc[00] XPath position >= 1 expected\n")]
        [InlineData("import time, datetime\nfor a, f in (('9999 52 7', '%G %V %u'), ('9999 366', '%Y %j'), ('9999 53 6', '%Y %U %w'), ('9998 53 7', '%G %V %u')):\n"
            + "    for fn in (time.strptime, datetime.datetime.strptime):\n        try:\n            r = fn(a, f)\n"
            + "            print(tuple(r[:3]) if fn is time.strptime else r)\n        except ValueError as e:\n            print(e)\n",
            "year must be in 1..9999, not 10000\nyear must be in 1..9999, not 10000\nyear must be in 1..9999, not 10000\nyear must be in 1..9999, not 10000\n"
            + "year must be in 1..9999, not 10000\nyear must be in 1..9999, not 10000\n(9999, 1, 3)\n9999-01-03 00:00:00\n")]
        [InlineData("from typing import Literal\nprint(Literal[1, 'a', b'x', None, True] == Literal[True, None, b'x', 'a', 1], Literal[1] == Literal[True],\n"
            + "      Literal[1, 2] == Literal[2, 1, 1], Literal[0] == Literal[False], Literal[1.0] == Literal[1.0], Literal[1, 2] == Literal[1, 3])\n",
            "True False True False True False\n")]
        [InlineData("from decimal import Decimal as D\nimport statistics, math, operator\n"
            + "print((D('123.456') // D('3'), D('123.456') % D('3'), divmod(D('-7.5'), D('2')), D('1.1') ** 10, D('123.456') ** 2, D('-1.1') ** 3,\n"
            + "       D('2.50') ** 2, D('-0.0') // D('3'), D('7') % D('-0.30')))\n"
            + "print((statistics.pstdev([1.0, 2.0]), statistics.pstdev([0.1, 0.2, 0.3]), math.isqrt(2**106 - 1), math.isqrt(10**31)))\n"
            + "try:\n    operator.attrgetter('a..b')(1)\nexcept AttributeError as e:\n    print(operator.attrgetter('real', 'imag.real')(3), e)\n",
            "(Decimal('41'), Decimal('0.456'), (Decimal('-3'), Decimal('-1.5')), Decimal('2.5937424601'), Decimal('15241.383936'), Decimal('-1.331'), "
            + "Decimal('6.2500'), Decimal('-0'), Decimal('0.10'))\n(0.5, 0.0816496580927726, 9007199254740991, 3162277660168379)\n"
            + "(3, 0) 'int' object has no attribute 'a'\n")]
        public void AnswersAsCPython(string source, string expected)
        {
            Assert.Equal(expected, Out(source));
        }

        // 20,000 values in reverse order: the member-by-member search ran out of a 3 s deadline.
        [Fact]
        public void LiteralEqualityIsLinear()
        {
            Assert.Equal("True False\n", Out("from typing import Literal\na = Literal[tuple(range(20000))]\nb = Literal[tuple(range(19999, -1, -1))]\n"
                + "print(a == b, a == Literal[tuple(range(19999))])\n", deadlineMs: 10_000));
        }

        // A getter costs what a def's function does: 600,000 of them built in a loop were charged 215 MB.
        [Fact]
        public void GettersInALoopAreChargedAsFunctions()
        {
            Assert.Equal("179999700000\n", Out("from operator import itemgetter\nn = 0\nfor i in range(600000):\n    n += itemgetter(1)((i, i))\nprint(n)\n",
                maxAllocBytes: 160L << 20));
        }

        // A class header is read as a call's arguments; the dialect's limits name what they refuse.
        [Theory]
        [InlineData("class C(metaclass=type):\n    pass\n", "class keyword arguments are not supported in this dialect")]
        [InlineData("bases = []\nclass C(*bases):\n    pass\n", "unpacking in class bases is not supported in this dialect")]
        [InlineData("class A:\n    pass\nclass B:\n    pass\nclass C(A, B):\n    pass\n", "multiple inheritance is not supported in this dialect")]
        public void ClassHeaderLimitsAreNamed(string source, string message)
        {
            RunResult r = Run(source);
            Assert.False(r.Succeeded);
            Assert.Equal(message, r.Error.Message);
        }

        [Fact]
        public void AClassWithATrailingCommaHasOneBase()
        {
            Assert.Equal("True\n", Out("class A:\n    pass\nclass C(A,):\n    pass\nprint(issubclass(C, A))\n"));
        }

        // A list's parts are sized once: the doubling's charge took ''.join of 5M strings past 80 MB.
        [Fact]
        public void JoinOfAListPaysNoDoubling()
        {
            Assert.Equal("5000000 5000000\n", Out("xs = ['a'] * 5000000\nprint(len(''.join(xs)), len(''.join(tuple(xs))))\n",
                maxAllocBytes: 120L << 20));
        }

        // 60K members: the member-by-member search took minutes past any deadline, uncharged.
        [Fact]
        public void UnionEqualityIsLinear()
        {
            Assert.Equal("True False\n", Out("ks = []\nfor i in range(60000):\n    class K:\n        pass\n    ks.append(K)\n"
                + "def build(lo, hi):\n    if hi - lo == 1:\n        return ks[lo]\n    mid = (lo + hi) // 2\n    return build(lo, mid) | build(mid, hi)\n"
                + "u1 = build(0, len(ks))\nks.reverse()\nu2 = build(0, len(ks))\nprint(u1 == u2, u1 == (u2 | int))\n",
                deadlineMs: 60_000, maxAllocBytes: 1L << 30));
        }

        // 600 elements share one 15M-char text: each candidate's text was joined per candidate, 47 s past a 1 s deadline.
        [Fact]
        public void TextPredicatesCopyNoSharedText()
        {
            Assert.Equal("0 600\n", Out("import xml.etree.ElementTree as ET\nbig = 'y' * 15000000\nroot = ET.Element('r')\n"
                + "for i in range(600):\n    ET.SubElement(root, 'c').text = big\n"
                + "print(len(root.findall(\"*[.='x']\")), len(root.findall(\"*[.!='x']\")))\n", deadlineMs: 30_000));
        }

        // An optional group keeps no stack that grows with the subject; an unbounded one still pays for it.
        [Fact]
        public void OnlyLongGroupLoopsPayForTheSubject()
        {
            Assert.Equal("999960 0\n", Out("import re\nline = '2026-11-10 12:00:01 INFO user=alice action=login ok\\n'\n"
                + "text = line * (1000000 // len(line))\nn = 0\nfor i in range(20):\n    n += len(re.findall(r'(?:INFO|WARN)?\\s+user=%d' % i, text))\n"
                + "print(len(text), n)\n", maxAllocBytes: 32L << 20));
            RunResult r = Run("import re\ntext = 'ab' * 500000\nfor i in range(40):\n    re.compile('(?:ab)+(?:%d)?' % i).search(text)\n",
                maxAllocBytes: 64L << 20);
            Assert.False(r.Succeeded);
            Assert.Equal("MaxAllocBytes", r.Error.LimitName);
        }

        // match(buf, pos, line_end) cut buf[:line_end] per call: 6000 lines of a 100K buffer were charged 2.4 GB.
        [Fact]
        public void EndposMakesNoCopy()
        {
            Assert.Equal("24000\n", Out("import re\nbuf = 'key = value  # c\\n' * 6000\ntok = re.compile(r'\\s*(\\w+|=|#.*)')\n"
                + "pos = 0\ncount = 0\nn = len(buf)\nwhile pos < n:\n    eol = buf.find('\\n', pos)\n    p = pos\n"
                + "    while True:\n        m = tok.match(buf, p, eol)\n        if not m:\n            break\n        count += 1\n        p = m.end()\n"
                + "    pos = eol + 1\nprint(count)\n", maxAllocBytes: 32L << 20));
            Assert.Equal("400\n", Out("import re\np = re.compile('a')\ns = 'a' * 1000000\nkept = [p.search(s, 0, len(s) - 1 - i) for i in range(400)]\n"
                + "print(len(kept))\n", maxAllocBytes: 32L << 20));
            Assert.Equal("40\n", Out("import re\np = re.compile('a')\ns = 'b' * 900000\nits = []\nfor i in range(40):\n"
                + "    it = p.finditer(s, 0, len(s) - 1 - i)\n    next(it, None)\n    its.append(it)\nprint(len(its))\n", maxAllocBytes: 32L << 20));
        }

        // A list or tuple passed by * is sized once: the doubling's charge refused what 1.1.0 ran.
        [Fact]
        public void StarListPaysNoDoubling()
        {
            Assert.Equal("4900000 3\n", Out("def f(*a):\n    return len(a)\nprint(f(*list(range(4900000))), f(*(1, 2), *[3]))\n",
                maxAllocBytes: 256L << 20));
        }
    }
}

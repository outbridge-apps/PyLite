using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // The paths the 2026-10 sandbox review made charged or linear must answer exactly as before: long str
    // compares in chunks, deque deletion from the nearer end, tee's minimum tracking, StringIO's own buffer,
    // keyword binding and dataclass fields by index, a long csv terminator, an HMAC key hashed once, the
    // astral property caches, the XML attribute cap.
    [Trait("Category", "Compatibility")]
    public class ChargedNativeWorkTests
    {
        [Fact]
        public void LongStrsCompareAndSortAsShortOnes()
        {
            string src = "a = 'x' * 10000 + 'a'\nb = 'x' * 10000 + 'b'\nc = 'x' * 10000\n"
                + "r = (a < b, a == b, a == a[:], c < a, a <= a[:], sorted([b, c, a]) == [c, a, b], (a,) == (a[:],),"
                + " 'xa' in a, a.find('xa'), a.rfind('x'), b.index('b'))\n";
            Assert.Equal("(True, False, True, True, True, True, True, True, 9999, 9999, 10000)", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void DequeDeletionKeepsTheOrder()
        {
            string src = "from collections import deque\nd = deque(range(10))\ndel d[2]\ndel d[-2]\ndel d[0]\n"
                + "d.appendleft(-1)\nd.remove(5)\ndel d[1]\nr = list(d)\n";
            Assert.Equal("[-1, 3, 4, 6, 7, 9]", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void TeeClonesReadEveryElementInAnyOrder()
        {
            string src = "import itertools\nts = itertools.tee(range(3000), 40)\nout = []\n"
                + "for k in range(3000):\n    out.append(next(ts[k % 7]))\n"
                + "r = (out[:8], all(list(t) == list(range(3000)) for t in ts[7:]), list(ts[0])[:3])\n";
            Assert.Equal("([0, 0, 0, 0, 0, 0, 0, 1], True, [429, 430, 431])", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void StringIOWritesReadsAndTruncates()
        {
            string src = "import io\nf = io.StringIO('abc\\ndef\\n')\nf.seek(2)\nf.write('XYZ')\nf.seek(0)\n"
                + "a = f.readline()\nb = f.readline()\nf.seek(10)\nf.write('!')\nf.truncate(5)\nf.seek(0, 2)\nf.write('end')\n"
                + "r = (a, b, f.getvalue())\n";
            Assert.Equal("('abXYZef\\n', '', 'abXYZend')", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void KeywordsBindByNameOnManyParameters()
        {
            var ps = new StringBuilder();
            for (int i = 0; i < 20; i++)
                ps.Append(i == 0 ? "" : ", ").Append("p").Append(i);
            string src = "def f(" + ps + ", /, q=0, *, k=1):\n    return p19 * 100 + q * 10 + k\n"
                + "args = list(range(20))\nr1 = f(*args, q=3, k=4)\n"
                + "try:\n    f(*args, p3=1)\nexcept TypeError as e:\n    r2 = str(e)\n"
                + "try:\n    f(*args, zz=1)\nexcept TypeError as e:\n    r3 = str(e)\n"
                + "def g(" + ps.ToString().Replace("p", "a") + ", **kw):\n    return a7 + len(kw)\n"
                + "r4 = g(**{'a%d' % i: i for i in range(20)}, x=1, y=2)\nr = (r1, r2, r3, r4)\n";
            Assert.Equal("(1934, \"f() got some positional-only arguments passed as keyword arguments: 'p3'\", "
                + "\"f() got an unexpected keyword argument 'zz'\", 9)", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void ADataclassWithManyFieldsBindsByName()
        {
            string src = "from dataclasses import make_dataclass, asdict, replace\n"
                + "C = make_dataclass('C', ['f%d' % i for i in range(30)])\nc = C(**{'f%d' % i: i for i in range(30)})\n"
                + "d = replace(c, f29=-1)\ntry:\n    C(**{'f%d' % i: i for i in range(30)}, nope=1)\nexcept TypeError as e:\n    err = str(e)\n"
                + "r = (c.f17, d.f29, asdict(d)['f0'], len(asdict(c)), err, c == C(*range(30)))\n";
            Assert.Equal("(17, -1, 0, 30, \"C.__init__() got an unexpected keyword argument 'nope'\", True)",
                Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void ALongCsvTerminatorQuotesItsCharacters()
        {
            string src = "import csv, io\nf = io.StringIO()\nw = csv.writer(f, lineterminator='abcdeabcde')\n"
                + "w.writerow(['xbx', 'y', 'q'])\nr = f.getvalue()\n";
            Assert.Equal("'\"xbx\",y,qabcdeabcde'", Ev.Run(src).Repr("r"));
        }

        [Theory]
        [InlineData("md5")]
        [InlineData("sha256")]
        [InlineData("sha512")]
        public void AnHmacKeyLongerThanTheBlockGivesTheSameMac(string algo)
        {
            byte[] key = Encoding.ASCII.GetBytes(new string('k', 300));
            byte[] msg = Encoding.ASCII.GetBytes("message");
            KeyedHashAlgorithm mac = algo == "md5" ? new HMACMD5(key) : algo == "sha256" ? (KeyedHashAlgorithm)new HMACSHA256(key) : new HMACSHA512(key);
            string expected = BitConverter.ToString(mac.ComputeHash(msg)).Replace("-", "").ToLowerInvariant();
            string src = "import hmac\nh = hmac.new(b'k' * 300, b'mess', '" + algo + "')\nh.update(b'age')\nr = h.hexdigest()\n";
            Assert.Equal("'" + expected + "'", Ev.Run(src).Repr("r"));
        }

        // The string classes answer from the Unicode 16.0.0 tables (CPython 3.14's), astral code points too.
        [Fact]
        public void AstralPredicatesAnswerAsTheTables()
        {
            var numeric = new StringBuilder();
            var digit = new StringBuilder();
            var alnum = new StringBuilder();
            for (int cp = 0x10000; cp < 0x30000; cp += 37)
            {
                numeric.Append(PyUnicodeClass.IsNumeric(cp) ? '1' : '0');
                digit.Append(PyUnicodeClass.IsDigit(cp) ? '1' : '0');
                alnum.Append(PyUnicodeClass.IsAlpha(cp) || PyUnicodeClass.IsNumeric(cp) ? '1' : '0');
            }
            string src = "cps = range(0x10000, 0x30000, 37)\n"
                + "n = ''.join('1' if chr(c).isnumeric() else '0' for c in cps)\n"
                + "d = ''.join('1' if chr(c).isdigit() else '0' for c in cps)\n"
                + "a = ''.join('1' if chr(c).isalnum() else '0' for c in cps)\n"
                + "r = (n, d, a)\n";
            Assert.Equal("('" + numeric + "', '" + digit + "', '" + alnum + "')", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void AnXmlTagPastTenThousandAttributesIsRefused()
        {
            string src = "import xml.etree.ElementTree as ET\n"
                + "ok = len(ET.fromstring('<r ' + ' '.join('a%d=\"1\"' % i for i in range(10000)) + '><!-- a=1 b=2 --></r>').attrib)\n"
                + "try:\n    ET.fromstring('<r ' + ' '.join('a%d=\"1\"' % i for i in range(10001)) + '/>')\n    r = None\n"
                + "except ET.ParseError as e:\n    r = (ok, str(e)[:40])\n";
            Assert.Equal("(10000, 'too many attributes in one element (limi')", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void EscapeAndCaseMappingsAreUnchanged()
        {
            string src = "import re, typing\nt = typing.TypeVar('T')\n"
                + "r = (re.escape('a.b*c(d) e#f'), 'ß\\ufb17Σx'.upper(), 'ΣΑΣ'.lower(), 'ǆa'.title(), 'aB'.swapcase(),"
                + " hash(t) == hash(t), isinstance(1, ((str,), (int, float))))\n";
            Assert.Equal("('a\\\\.b\\\\*c\\\\(d\\\\)\\\\ e\\\\#f', 'SSՄԽΣX', 'σας', 'ǅa', 'Ab', True, True)", Ev.Run(src).Repr("r"));
        }
    }
}

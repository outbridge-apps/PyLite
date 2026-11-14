using System;
using System.Security.Cryptography;
using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // Review 6, stage 1: the regressions against 1.1.0 and the two leaked runners. Each answer is CPython 3.14's.
    [Trait("Category", "Regression")]
    public class Review6FixTests
    {
        private static RunResult Run(string source, int deadlineMs = 120_000)
        {
            ResourceLimits limits = TestEngines.Unhurried.With(deadlineMs: deadlineMs);
            return new ScriptEngine(new EngineOptions { DefaultLimits = limits }).Run(source, new RunRequest());
        }

        private static string Out(string source)
        {
            RunResult r = Run(source);
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.Message);
            return r.PrintOutput;
        }

        [Theory]
        [InlineData("import functools\n@functools.lru_cache\ndef f(x):\n    return x\nprint(hash(f) == hash(f), {f: 1}[f], f in {f})\n",
            "True 1 True\n")]
        [InlineData("from decimal import Decimal as D\nprint(all(D(k) ** -1 == D(1) / D(k) for k in (19, 22, 347, 478)), D(19) ** -1)\n",
            "True 0.0526315789473684210526315789\n")]
        [InlineData("print(repr(sum([], True)), repr(sum([], False)), sum([True, True], True), sum([True], 0))\n",
            "True False 3 1\n")]
        [InlineData("import re\nprint(len(re.findall('[\\ud800-\\udbff]', 'a\\ud800b')))\ntry:\n    re.compile('[a\\ud800-z]')\nexcept re.error as e:\n    print(str(e).startswith('bad character range'))\n",
            "1\nTrue\n")]
        [InlineData("import datetime\nfor s, f in (('0000 01 1', '%G %V %u'), ('0000 100', '%Y %j'), ('0000 00 1', '%Y %W %w')):\n    try:\n        datetime.datetime.strptime(s, f)\n    except ValueError as e:\n        print(e)\n",
            "Year is out of range: 0\nyear 0 is out of range\nyear 0 is out of range\n")]
        [InlineData("import datetime\nprint(datetime.datetime.strptime('0001 00 1', '%Y %W %w'))\n",
            "0001-01-01 00:00:00\n")]
        public void AnswersAsCPython(string source, string expected)
        {
            Assert.Equal(expected, Out(source));
        }

        // The second batch: two regressions the first missed, and the old high rows of stage 1.
        [Theory]
        [InlineData("from dataclasses import dataclass, field\n@dataclass(order=True)\nclass A:\n    x: int\n    y: int = field(init=False)\n"
            + "a = A(1)\nb = A(1)\nb.y = 5\nfor f in (lambda: a == b, lambda: b == a, lambda: a < b, lambda: A(1) < A(2)):\n    try:\n        f()\n    except AttributeError as e:\n        print(e)\n",
            "'A' object has no attribute 'y'\n'A' object has no attribute 'y'\n'A' object has no attribute 'y'\n'A' object has no attribute 'y'\n")]
        [InlineData("import io, zipfile\nb = io.BytesIO()\nwith zipfile.ZipFile(b, 'w') as z:\n    z.writestr('a.txt', 'x')\n"
            + "    z.infolist()[0].filename = 'zzz'\n    print(z.namelist(), list(z.NameToInfo))\n",
            "['zzz'] ['a.txt']\n")]
        [InlineData("import calendar\nprint(repr(calendar.weekheader(-1)), repr(calendar.weekheader(0)), repr(calendar.weekheader(-3)))\n",
            "'Mo Tu We Th Fr Sa Su' '      ' '      '\n")]
        [InlineData("from enum import IntEnum\nclass C(IntEnum):\n    A = 1\n    B = 2\nprint([x for x in C], [x if x > 0 else 0 for x in C], list(x for x in C))\n"
            + "print({c.value: c for c in C}, [+x for x in C], +C.A, [x + 0 for x in C])\n",
            "[<C.A: 1>, <C.B: 2>] [<C.A: 1>, <C.B: 2>] [<C.A: 1>, <C.B: 2>]\n{1: <C.A: 1>, 2: <C.B: 2>} [1, 2] 1 [1, 2]\n")]
        [InlineData("import datetime\nfor s in ('2024 53 1', '2025 53 1', '2024 0 1', '2020 53 1', '2015 53 4', '2024 00 1'):\n    try:\n        print(datetime.datetime.strptime(s, '%G %V %u'))\n"
            + "    except ValueError as e:\n        print(e)\n",
            "Invalid week: 53\nInvalid week: 53\nInvalid week: 0\n2020-12-28 00:00:00\n2015-12-31 00:00:00\ntime data '2024 00 1' does not match format '%G %V %u'\n")]
        public void OldHighRowsAnswerAsCPython(string source, string expected)
        {
            Assert.Equal(expected, Out(source));
        }

        // Diff review: an IntEnum defined anywhere in the run switched fusion off for every generator expression.
        [Fact]
        public void AnEnumInTheRunKeepsFusionForPlainInts()
        {
            string body = "lst = list(range(100000))\nfor _ in range(5):\n    sum(x * x + 1 for x in lst)\n";
            long plain = ChargeHoldTests.Stats(body).AllocatedBytes;
            long withEnum = ChargeHoldTests.Stats("from enum import IntEnum\nclass C(IntEnum):\n    A = 1\n" + body).AllocatedBytes;
            Assert.True(withEnum < plain + plain / 5, plain + " " + withEnum);
        }

        // A limit hit while a finally runs in grace keeps the first abort's cause.
        [Fact]
        public void ASecondLimitInFinallyKeepsTheFirstCause()
        {
            ResourceLimits deadline = TestEngines.Unhurried.With(deadlineMs: 300);
            RunResult r = new ScriptEngine(new EngineOptions { DefaultLimits = deadline })
                .Run("try:\n    while True:\n        pass\nfinally:\n    x = 'a' * 20000000\n", new RunRequest());
            Assert.Equal("DeadlineMs", r.Error.LimitName);
            ResourceLimits steps = TestEngines.Unhurried.With(maxSteps: 5000);
            r = new ScriptEngine(new EngineOptions { DefaultLimits = steps })
                .Run("try:\n    while True:\n        pass\nfinally:\n    x = [0] * 6000000\n", new RunRequest());
            Assert.Equal("MaxSteps", r.Error.LimitName);
        }

        // An earlier fix charged the strict scan a step per character: 60 caught failures over 4M characters reached MaxSteps.
        // Each error also charged its e.object as a fresh 8 MB string, which reached MaxAllocBytes.
        [Fact]
        public void AStrictEncodeErrorChargesItsScanLinearly()
        {
            RunStatistics stats = ChargeHoldTests.Stats("s = 'a' + chr(233) * 4000000\nfor _ in range(60):\n    try:\n        s.encode('ascii')\n"
                + "    except UnicodeEncodeError as e:\n        assert e.object == s\n");
            Assert.True(stats.Steps < 1_000_000, stats.Steps.ToString());
            Assert.True(stats.AllocatedBytes < 64L << 20, stats.AllocatedBytes.ToString());
        }

        [Fact]
        public void ADecodeErrorSharesItsBytes()
        {
            RunStatistics stats = ChargeHoldTests.Stats("b = b'a' * 8000000 + b'\\xff'\nfor _ in range(60):\n    try:\n        b.decode('utf-8')\n"
                + "    except UnicodeDecodeError as e:\n        assert e.object == b and e.start == 8000000\n");
            Assert.True(stats.AllocatedBytes < 64L << 20, stats.AllocatedBytes.ToString());
        }

        [Fact]
        public void AnAstralCharacterIsOneCharacterInTheCodecMessage()
        {
            string source = "try:\n    'a\\U0001F600'.encode('ascii')\nexcept UnicodeEncodeError as e:\n"
                + "    print(e)\n    print(e.object[e.start:e.end] == '\\U0001F600')\n";
            Assert.Equal("'ascii' codec can't encode character '\\U0001f600' in position 1: ordinal not in range(128)\nTrue\n", Out(source));
        }

        [Fact]
        public void TheRunOfAnEncodeErrorSlicesOutOfTheObject()
        {
            string source = "try:\n    'x\\U0001F600\\xe9y'.encode('ascii')\nexcept UnicodeEncodeError as e:\n"
                + "    print(e.object[e.start:e.end] == '\\U0001F600\\xe9', e.object[e.end:])\n";
            Assert.Equal("True y\n", Out(source));
        }

        [Fact]
        public void AScriptBoolThatChangesTheChildrenDoesNotFaultAPredicate()
        {
            string source = "import xml.etree.ElementTree as ET\nclass W:\n    def __init__(self, p):\n        self.p = p\n"
                + "    def __bool__(self):\n        self.p.append(ET.Element('z'))\n        return False\n"
                + "root = ET.fromstring('<r><c><b/><b/></c></r>')\nc = root[0]\nc[0].text = W(c)\nc[1].text = 'x'\n"
                + "print(len(root.findall(\"*[b='x']\")), len(c))\n";
            Assert.Equal("1 3\n", Out(source));
        }

        // 1.2.0 drafts searched the rest of the path for every '{': 150K braces took 5.8 s and leaked the runner.
        [Fact]
        public void UnclosedBracesInAPathAreLinear()
        {
            RunResult r = Run("import xml.etree.ElementTree as ET\nroot = ET.fromstring('<r><a/></r>')\n"
                + "print(root.find('{' * 150000))\n", deadlineMs: 3000);
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.Message);
        }

        [Fact]
        public void ALongNamespaceTagIsChargedPerElement()
        {
            string build = "import xml.etree.ElementTree as ET\nt = '{' + 'u' * {0} + '}a'\nroot = ET.Element(t)\n"
                + "for _ in range(50):\n    ET.SubElement(root, t)\nET.tostring(root)\n";
            long longTag = ChargeHoldTests.Stats(build.Replace("{0}", "400000")).Steps;
            long shortTag = ChargeHoldTests.Stats(build.Replace("{0}", "4")).Steps;
            Assert.True(longTag - shortTag >= 10000, (longTag - shortTag).ToString());
        }

        // Diff review: a tag that opens a brace and never closes it was scanned uncharged per element.
        [Fact]
        public void AnUnclosedNamespaceTagIsChargedPerElement()
        {
            string build = "import xml.etree.ElementTree as ET\nt = '{' + 'u' * {0}\nroot = ET.Element(t)\n"
                + "for _ in range(50):\n    ET.SubElement(root, t)\ntry:\n    ET.tostring(root)\nexcept ValueError:\n    pass\n";   // CPython's rsplit fails here
            long longTag = ChargeHoldTests.Stats(build.Replace("{0}", "100000")).Steps;
            long shortTag = ChargeHoldTests.Stats(build.Replace("{0}", "4")).Steps;
            Assert.True(longTag - shortTag >= 3000, (longTag - shortTag).ToString());
        }

        // Diff review: the gathered tail is a real buffer per hash object, so it is charged as one.
        [Fact]
        public void AHashTailIsChargedForItsBuffer()
        {
            long bytes = ChargeHoldTests.Stats("import hashlib\nhs = []\nfor _ in range(20000):\n    h = hashlib.md5()\n"
                + "    h.update(b'a')\n    hs.append(h)\n").AllocatedBytes;
            Assert.True(bytes >= 20000L * 64, bytes.ToString());
        }

        [Fact]
        public void ABlankTailIsChargedByIndent()
        {
            string build = "import xml.etree.ElementTree as ET\nroot = ET.Element('r')\nfor _ in range(50):\n"
                + "    ET.SubElement(root, 'c').tail = {0} + ' ' * 400000\nET.indent(root)\n";
            long blank = ChargeHoldTests.Stats(build.Replace("{0}", "''")).Steps;
            long text = ChargeHoldTests.Stats(build.Replace("{0}", "'x'")).Steps;
            Assert.True(blank - text >= 4000, (blank - text).ToString());
        }

        // Small updates are gathered into one buffer and large ones kept whole: the digest and every copy still see
        // the bytes in the order they were fed.
        [Fact]
        public void GatheredUpdatesKeepTheDigestAndTheCopies()
        {
            int[] sizes = { 1, 4095, 4096, 70000, 3, 0, 5000, 1 };
            var all = new System.Collections.Generic.List<byte>();
            var script = new System.Text.StringBuilder("import hashlib\nh = hashlib.md5()\nparts = [");
            for (int i = 0; i < sizes.Length; i++)
            {
                script.Append("bytes([").Append(i + 1).Append("]) * ").Append(sizes[i]).Append(", ");
                for (int k = 0; k < sizes[i]; k++)
                    all.Add((byte)(i + 1));
            }
            script.Append("]\nfor p in parts:\n    h.update(p)\nfor _ in range(70000):\n    h.update(b'z')\n"
                + "c = h.copy()\nh.update(b'tail')\nprint(c.hexdigest())\nprint(h.hexdigest())\n");
            for (int k = 0; k < 70000; k++)
                all.Add((byte)'z');
            string before = Md5(all.ToArray());
            all.AddRange(System.Text.Encoding.ASCII.GetBytes("tail"));
            Assert.Equal(before + "\n" + Md5(all.ToArray()) + "\n", Out(script.ToString()));
        }

        // Round 4 review: [N] and [last()] rescanned the parent's children per candidate; 20,000 rows ran out of steps.
        // The answers, the mixed-tag ones and an iterfind that renames a tag between items are CPython 3.14.2's.
        [Fact]
        public void PositionalPredicatesAreLinear()
        {
            RunResult big = new ScriptEngine(new EngineOptions()).Run("import xml.etree.ElementTree as ET\n"
                + "root = ET.fromstring('<r>' + '<row><v>1</v></row>' * 20000 + '</r>')\n"
                + "print(len(root.findall('row[last()]')), len(root.findall('row[1]')), len(root.findall('*[2]')))\n",
                new RunRequest());
            Assert.True(big.Succeeded, big.Error == null ? "" : big.Error.Message);
            Assert.Equal("1 1 1\n", big.PrintOutput);
            Assert.Equal("['1', 'c', 'c'] ['c', '4', 'c'] ['3', '4', '5', '6'] ['2'] ['2']\n", Out("import xml.etree.ElementTree as ET\n"
                + "root = ET.fromstring('<r><a><b i=\"1\"/><c/><b i=\"2\"><b i=\"3\"/></b><c/><b i=\"4\"/></a><a><c/></a><b i=\"5\"><b i=\"6\"/></b></r>')\n"
                + "def ids(xs):\n    return [x.get('i') or x.tag for x in xs]\n"
                + "r5 = ET.fromstring('<r><p><x i=\"1\"/><x i=\"2\"/></p><p><x i=\"3\"/><x i=\"4\"/></p></r>')\n"
                + "out = []\nfor e in r5.iterfind('.//x[2]'):\n    out.append(e.get('i'))\n    r5[1][0].tag = 'y'\n"
                + "print(ids(root.findall('a/*[1]')), ids(root.findall('a/*[last()]')), ids(root.findall('.//b[last()]')),"
                + " ids(root.findall('a/b[2][@i=\"2\"]')), out)\n"));
            // the siblings seen for the first item are not reused after the script renamed one
            Assert.Equal("['a']\n", Out("import xml.etree.ElementTree as ET\nr = ET.fromstring('<r><a/><b/></r>')\nout = []\n"
                + "for e in r.iterfind('*[1]'):\n    out.append(e.tag)\n    r[0].tag = 'b'\nprint(out)\n"));
        }

        // Round 4 review: the exact \b is four copies of a 466-character word class; 14,400 distinct \b patterns were charged
        // 400 MB of translated text past a 256 MB budget, where 1.1.0 compiled them. An ASCII subject keeps .NET's own \b.
        [Fact]
        public void ManyWordBoundaryPatternsCompileWithinTheBudget()
        {
            Assert.Equal("119\n", Out("import re\ntext ='alpha beta gamma delta ' * 5 + ' '.join('w%d' % i for i in range(120))\n"
                + "words = ['w%d' % i for i in range(120)]\nn = 0\nfor a in words:\n    for b in words:\n"
                + "        if re.search(r'\\b%s\\b \\b%s\\b' % (a, b), text):\n            n += 1\nprint(n)\n"));
        }

        // Final review: finditer and sub chose their regex per step, so the same pattern run on a word inside the loop
        // made every step rescan the whole subject for WordRisk; 100,000 words ran out of time.
        [Fact]
        public void APatternUsedInsideItsOwnLoopStaysLinear()
        {
            Assert.Equal("100000 199999\n", Out("import re\nWORD = re.compile(r'\\w+')\n"
                + "text = ' '.join('w%d' % i for i in range(100000))\n"
                + "k = sum(1 for m in WORD.finditer(text) if WORD.fullmatch(m.group()))\n"
                + "s = WORD.sub(lambda m: WORD.sub('x', m.group()), text)\nprint(k, len(s))\n"));
        }

        private static string Md5(byte[] data)
        {
            using (MD5 md5 = MD5.Create())
            {
                return BitConverter.ToString(md5.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}

using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // Review 6, stage 2: what a script that fitted 1.1.0 meets on upgrade, the refusal a host must tell apart, and
    // arguments that were dropped silently. Each answer is CPython 3.14's.
    [Trait("Category", "Regression")]
    public class Review6Stage2Tests
    {
        private static string Out(string source)
        {
            ResourceLimits limits = TestEngines.Unhurried;
            RunResult r = new ScriptEngine(new EngineOptions { DefaultLimits = limits }).Run(source, new RunRequest());
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.Message);
            return r.PrintOutput;
        }

        // Formatting an int past 4300 digits is the catchable ValueError of str(), not a charge that ends the run.
        [Theory]
        [InlineData("f'{big:,}'")]
        [InlineData("'%d' % big")]
        [InlineData("format(big, 'd')")]
        [InlineData("'{:_}'.format(big)")]
        public void AHugeIntFormatsToTheValueError(string expr)
        {
            Assert.Equal("ValueError\n", Out("big = 3 ** 600000\ntry:\n    " + expr + "\nexcept ValueError:\n    print('ValueError')\n"));
        }

        // The count order of repr: no tuple and no negated int per key (a 1M-key Counter no longer fitted 256 MB).
        [Fact]
        public void ACounterReprCostsAboutWhatADictReprDoes()
        {
            long counter = ChargeHoldTests.Stats("from collections import Counter\nc = Counter(range(200000))\nrepr(c)\n").AllocatedBytes;
            long dict = ChargeHoldTests.Stats("from collections import Counter\nc = Counter(range(200000))\nrepr(dict(c))\n").AllocatedBytes;
            Assert.True(counter - dict < 200000L * 40, (counter - dict).ToString());
        }

        [Fact]
        public void ACounterReprKeepsTheCountOrder()
        {
            Assert.Equal("Counter({'a': 5, 'b': 2, 'r': 2, 'c': 1, 'd': 1})\nCounter({'x': 1.5, 'y': 1})\n",
                Out("from collections import Counter\nprint(repr(Counter('abracadabra')))\nprint(repr(Counter({'y': 1, 'x': 1.5})))\n"));
        }

        // super().m(...) makes neither the super object nor a bound method.
        [Fact]
        public void ASuperCallCostsWhatAClassCallDoes()
        {
            string classes = "class B:\n    def __init__(self, x):\n        self.x = x\nclass A(B):\n    def __init__(self, x):\n        {0}\n"
                + "for i in range(100000):\n    A(i)\n";
            long viaSuper = ChargeHoldTests.Stats(classes.Replace("{0}", "super().__init__(x)")).AllocatedBytes;
            long viaClass = ChargeHoldTests.Stats(classes.Replace("{0}", "B.__init__(self, x)")).AllocatedBytes;
            Assert.True(viaSuper - viaClass < 100000L * 16, (viaSuper - viaClass).ToString());
        }

        [Fact]
        public void SuperCallsAnswerAsBefore()
        {
            string source = "class B:\n    def __init__(self, x):\n        self.x = x\n    def m(self, a, b=2):\n        return a + b\n"
                + "    def n(self, *xs):\n        return len(xs)\n    k = 7\n"
                + "class A(B):\n    def __init__(self, x):\n        super().__init__(x)\n"
                + "    def m(self, a, b=2):\n        return super().m(a, b=b) * 10\n"
                + "    def n(self, *xs):\n        return super().n(*xs, 1)\n"
                + "    def k2(self):\n        return super().k\n"
                + "    def bad(self):\n        return super().nope()\n"
                + "a = A(3)\nprint(a.x, a.m(1), a.m(1, 5), a.n(1, 2), a.k2())\n"
                + "try:\n    a.bad()\nexcept AttributeError as e:\n    print(e)\n";
            Assert.Equal("3 30 60 3 7\n'super' object has no attribute 'nope'\n", Out(source));
        }

        [Theory]
        [InlineData("try:\n    b'abc' % b'x'\nexcept TypeError as e:\n    print(e)\nprint(repr('abc' % b'x'))\n",
            "not all arguments converted during bytes formatting\n'abc'\n")]
        [InlineData("import uuid\nu = uuid.uuid1()\nprint(u.node == uuid.uuid1().node, u.node & (1 << 40) != 0)\n", "True True\n")]
        public void AnswersAsCPython(string source, string expected)
        {
            Assert.Equal(expected, Out(source));
        }

        // A node shared by the process told one tenant's run that another's ran in the same process.
        [Fact]
        public void TheUuid1NodeIsOwnedByTheRun()
        {
            var engine = new ScriptEngine(new EngineOptions());
            string first = engine.Run("import uuid\nprint(uuid.uuid1().node)", new RunRequest()).PrintOutput;
            string second = engine.Run("import uuid\nprint(uuid.uuid1().node)", new RunRequest()).PrintOutput;
            Assert.NotEqual(first, second);
        }

        // A host reads a refusal through ScriptRunner: a HostError with the refusal's name, not a failing host function.
        [Fact]
        public void ScriptRunnerNamesARefusal()
        {
            var runner = new ScriptRunner(new ScriptEngine(new EngineOptions { FreeCommitReserveBytes = 1L << 62 }));
            runner.Run("print(1)");
            Assert.Equal("HostError", runner.GetErrorType());
            Assert.Equal("FreeMemory", runner.GetErrorLimit());
        }

        [Theory]
        [InlineData("from collections import deque\nd = deque([1, 2])\nd.pop(zzz=1)", "pop() takes no keyword arguments")]
        [InlineData("from collections import deque\ndeque([1]).popleft(1)", "popleft() takes no arguments (1 given)")]
        [InlineData("import itertools\nitertools.permutations([1, 2], 2, r=2)", "permutations() got multiple values for argument 'r'")]
        [InlineData("import html\nhtml.escape('<', True, quote=False)", "escape() got multiple values for argument 'quote'")]
        [InlineData("import textwrap\ntextwrap.dedent('x', text='y')", "dedent() got multiple values for argument 'text'")]
        [InlineData("import json\njson.dumps(1, 2)", "dumps() takes 1 positional argument but 2 were given")]
        [InlineData("import uuid\nuuid.uuid4(1)", "uuid4() takes no arguments (1 given)")]
        [InlineData("from decimal import Decimal\nDecimal(1).normalize(zzz=1)", "normalize() got an unexpected keyword argument 'zzz'")]
        [InlineData("from decimal import Decimal\nDecimal(1).as_tuple(5)", "as_tuple() takes no arguments (1 given)")]
        [InlineData("from collections import OrderedDict\nOrderedDict(a=1).move_to_end('a', zzz=1)", "move_to_end() got an unexpected keyword argument 'zzz'")]
        [InlineData("import xml.etree.ElementTree as ET\nET.Element('a').find('b', zzz=1)", "find() got an unexpected keyword argument 'zzz'")]
        [InlineData("(5).to_bytes(2, length=2)", "to_bytes() got multiple values for argument 'length'")]
        [InlineData("import datetime\ndatetime.date.fromordinal(1, 2)", "fromordinal() takes exactly one argument (2 given)")]
        [InlineData("ValueError(zzz=1)", "ValueError() takes no keyword arguments")]
        [InlineData("class E(Exception):\n    pass\nE(zzz=1)", "E() takes no keyword arguments")]
        public void ADroppedArgumentIsATypeError(string source, string message)
        {
            string wrapped = "try:\n" + string.Join("\n", System.Array.ConvertAll(source.Split('\n'), l => "    " + l))
                + "\nexcept TypeError as e:\n    print(e)\n";
            Assert.Equal(message + "\n", Out(wrapped));
        }

        // Diff review: super().m(a, b) with two or more arguments skipped the call prologue, so the traceback named the
        // callee's line for the caller.
        [Fact]
        public void ASuperCallWithArgumentsKeepsTheCallersLine()
        {
            string source = "class B:\n    def g(self, a, b):\n        raise ValueError('x')\nclass A(B):\n    def g(self, a, b):\n"
                + "        return super().g(a, b)\nA().g(1, 2)\n";
            RunResult r = new ScriptEngine(new EngineOptions()).Run(source, new RunRequest());
            Assert.Contains("line 6, in g", r.Error.ScriptTraceback);
            Assert.Contains("line 3, in g", r.Error.ScriptTraceback);
        }

        // Diff review: names the new signatures accepted that the bodies did not read.
        [Theory]
        [InlineData("import urllib.parse as p\nprint(p.quote_plus(string='a b'), p.unquote_plus(string='a+b'), p.parse_qsl(qs='a=1'), p.urlsplit(url='http://x/y').path)",
            "a+b a b [('a', '1')] /y")]
        [InlineData("import calendar\nprint(calendar.month(theyear=2024, themonth=1) == calendar.month(2024, 1))", "True")]
        [InlineData("import io\nimport xml.etree.ElementTree as ET\nb = io.BytesIO()\nET.ElementTree(ET.Element('a')).write(file_or_filename=b)\nprint(b.getvalue())", "b'<a />'")]
        [InlineData("import traceback\ntry:\n    try:\n        raise KeyError('k')\n    except KeyError:\n        raise ValueError('v')\nexcept ValueError as e:\n"
            + "    print(len(traceback.format_exception(e, e, None, None)) > len(traceback.format_exception(e, e, None, None, False)))", "True")]
        [InlineData("import io, traceback\nf = io.StringIO()\ntry:\n    1 / 0\nexcept ZeroDivisionError:\n    traceback.print_exc(file=f)\nprint(f.getvalue().splitlines()[-1])",
            "ZeroDivisionError: division by zero")]
        [InlineData("import uuid\nprint(uuid.UUID(None, bytes(range(16))))", "00010203-0405-0607-0809-0a0b0c0d0e0f")]
        [InlineData("from collections import ChainMap\ntry:\n    ChainMap({}).pop(key='z')\nexcept KeyError as e:\n    print('KeyError', e)", "KeyError \"Key not found in the first mapping: 'z'\"")]
        public void KeywordsTheSignaturesNameAreRead(string source, string expected)
        {
            Assert.Equal(expected + "\n", Out(source));
        }

        [Theory]
        [InlineData("import itertools\nitertools.repeat(times=2)", "repeat() missing required argument 'object' (pos 1)")]
        [InlineData("from collections import OrderedDict\nOrderedDict(a=1).move_to_end(last=False)", "move_to_end() missing required argument 'key' (pos 1)")]
        [InlineData("import csv\ncsv.Error('m', zz=1)", "Error() takes no keyword arguments")]
        [InlineData("from collections import ChainMap\nChainMap({}).pop('z', default=1)", "pop() got an unexpected keyword argument 'default'")]
        public void ARequiredArgumentLeftOutIsMissing(string source, string message)
        {
            string wrapped = "try:\n" + string.Join("\n", System.Array.ConvertAll(source.Split('\n'), l => "    " + l))
                + "\nexcept TypeError as e:\n    print(e)\n";
            Assert.Equal(message + "\n", Out(wrapped));
        }

        [Theory]
        [InlineData("import itertools\nprint(len(list(itertools.permutations([1, 2, 3], r=2))))", "6")]
        [InlineData("import re\nprint(re.error('m', pattern='p', pos=1))", "m at position 1")]
        [InlineData("import calendar\nprint(type(calendar.IllegalMonthError(month=13)).__name__)", "IllegalMonthError")]
        [InlineData("from collections import ChainMap\nprint(ChainMap({'a': 1}).pop('z', 5))", "5")]
        [InlineData("import io\nimport xml.etree.ElementTree as ET\nt = ET.ElementTree()\nt.parse(source=io.BytesIO(b'<r/>'))\nprint(t.getroot().tag)", "r")]
        [InlineData("from decimal import Decimal\nprint(Decimal(1).max(other=Decimal(2)))", "2")]
        [InlineData("from collections import OrderedDict\no = OrderedDict(a=1, b=2)\no.move_to_end('b', last=False)\nprint(list(o))", "['b', 'a']")]
        [InlineData("from collections import Counter\nprint(Counter('aab').most_common(n=1))", "[('a', 2)]")]
        [InlineData("import xml.etree.ElementTree as ET\nprint(ET.fromstring('<r><a/></r>').find(path='a') is not None)", "True")]
        [InlineData("print((5).to_bytes(length=2))", "b'\\x00\\x05'")]
        [InlineData("print(ImportError('m', name='x'))", "m")]
        public void TheKeywordsCPythonTakesStillWork(string source, string expected)
        {
            Assert.Equal(expected + "\n", Out(source));
        }
    }

    // About 2 s and 230 MB: not with the everyday tests.
    [Trait("Category", "MachineSensitive")]
    [Trait("Category", "Regression")]
    public class Review6CounterReprTests
    {
        // 1.1.0 printed a 1M-key Counter in the default limits; review 5's count order made it outgrow MaxAllocBytes.
        [Fact]
        public void AMillionKeyCounterReprFitsTheDefaultLimits()
        {
            RunResult r = new ScriptEngine(new EngineOptions { DefaultLimits = TestEngines.Unhurried })
                .Run("from collections import Counter\nprint(len(repr(Counter(range(10**6)))))\n", new RunRequest());
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.Message);
            Assert.Equal("10888899\n", r.PrintOutput);
        }
    }
}

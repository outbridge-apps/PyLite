using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // Review 6, part 4: the CPython 3.14 answers the backlog rows asked for (checked against CPython in Pyodide).
    [Trait("Category", "Regression")]
    public class Review6Part4Tests
    {
        private static string Out(string source)
        {
            RunResult r = new ScriptEngine(new EngineOptions { DefaultLimits = TestEngines.Unhurried }).Run(source, new RunRequest());
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.Message);
            return r.PrintOutput;
        }

        // print(expr), or the exception's type and text
        private static string Show(string setup, string expr)
        {
            return Out(setup + "try:\n    print(repr(" + expr + "))\nexcept Exception as e:\n    print(type(e).__name__ + ': ' + str(e))\n").TrimEnd('\n');
        }

        [Theory]
        [InlineData("copy.copy(itertools.chain([1]))", "TypeError: cannot pickle 'itertools.chain' object")]
        [InlineData("copy.deepcopy([(x for x in [1])])", "TypeError: cannot pickle 'generator' object")]
        [InlineData("copy.copy(hashlib.md5())", "TypeError: cannot pickle '_hashlib.HASH' object")]
        [InlineData("copy.copy(csv.reader(io.StringIO('a')))", "TypeError: cannot pickle '_csv.reader' object")]
        [InlineData("copy.copy(csv.writer(io.StringIO()))", "TypeError: cannot pickle '_csv.writer' object")]
        [InlineData("(lambda c: (c is ctx, c.prec))(copy.copy(ctx))", "(False, 7)")]
        [InlineData("(lambda c: (c is ctx, c.prec))(copy.deepcopy(ctx))", "(False, 7)")]
        [InlineData("(lambda i: (next(i), list(copy.copy(i)), list(i)))(iter('abc'))", "('a', ['b', 'c'], ['b', 'c'])")]
        [InlineData("(lambda i: (next(i), list(copy.deepcopy(i)), list(i)))(iter(range(3)))", "(0, [1, 2], [1, 2])")]
        [InlineData("type(copy.copy(itertools.tee([1])[0])).__name__", "'_tee'")]
        public void CopyAsCPythonDoes(string expr, string expected)
        {
            Assert.Equal(expected, Show("import copy, itertools, hashlib, csv, io, decimal\nctx = decimal.Context(prec=7)\n", expr));
        }

        [Theory]
        [InlineData("{}.fromkeys([1])", "{1: None}")]
        [InlineData("(5).from_bytes(b'\\x01', 'big')", "1")]
        [InlineData("'a'.maketrans('a', 'b')", "{97: 98}")]
        [InlineData("datetime.date(2000, 1, 1).min", "datetime.date(1, 1, 1)")]
        [InlineData("collections.OrderedDict().fromkeys('ab')", "OrderedDict({'a': None, 'b': None})")]
        public void AClassMethodIsReachableThroughAnInstance(string expr, string expected)
        {
            Assert.Equal(expected, Show("import datetime, collections\n", expr));
        }

        [Theory]
        [InlineData("len(range(10 ** 30))", "OverflowError: Python int too large to convert to C ssize_t")]
        [InlineData("len(range(2 ** 63 - 1))", "9223372036854775807")]
        [InlineData("struct.unpack_from('i', b'0000', 10 ** 30)", "OverflowError: Python int too large to convert to C ssize_t")]
        [InlineData("struct.pack('<e', 1.5)", "b'\\x00>'")]
        [InlineData("struct.pack('<e', 65519.99)", "b'\\xff{'")]
        [InlineData("struct.pack('<e', 65520.0)", "OverflowError: float too large to pack with e format")]
        [InlineData("struct.pack('<e', 10 ** 6)", "error: int too large to convert")]
        [InlineData("struct.pack('<e', 2.98e-08) + struct.pack('<e', 2.99e-08)", "b'\\x00\\x00\\x01\\x00'")]
        [InlineData("struct.pack('<ee', 2049.0, 2051.0)", "b'\\x00h\\x02h'")]
        [InlineData("struct.unpack('<e', b'\\x01\\x00')", "(5.960464477539063e-08,)")]
        [InlineData("struct.unpack('>e', b'\\xfc\\x00')", "(-inf,)")]
        public void RangesAndStructAsCPythonDo(string expr, string expected)
        {
            Assert.Equal(expected, Show("import struct\n", expr));
        }

        [Theory]
        [InlineData("format(float('nan'), '010')", "'0000000nan'")]
        [InlineData("format(float('-inf'), '010')", "'-000000inf'")]
        [InlineData("format(float('nan'), '<010')", "'nan0000000'")]
        [InlineData("'%010f' % float('nan')", "'0000000nan'")]
        [InlineData("isinstance(1, (int, 'a'))", "True")]
        [InlineData("issubclass(int, (int, 'a'))", "True")]
        [InlineData("isinstance(1, (str, 'a'))", "TypeError: isinstance() arg 2 must be a type, a tuple of types, or a union")]
        [InlineData("'a'.encode(errors=1)", "TypeError: encode() argument 'errors' must be str, not int")]
        [InlineData("'abc'.replace(old='a', new='x')", "TypeError: replace() takes at least 2 positional arguments (0 given)")]
        [InlineData("b'abc'.replace(b'a', new=b'x')", "TypeError: replace() takes at least 2 positional arguments (1 given)")]
        [InlineData("'aaa'.replace('a', 'b', count=1)", "'baa'")]
        [InlineData("bytes(3.0)", "TypeError: cannot convert 'float' object to bytes")]
        [InlineData("reversed(5)", "TypeError: 'int' object is not reversible")]
        [InlineData("sorted([1], 2)", "TypeError: sorted expected 1 argument, got 2")]
        public void TextsAndArgumentsAsCPythonHasThem(string expr, string expected)
        {
            Assert.Equal(expected, Show("", expr));
        }

        [Fact]
        public void SelfContainingContainersPrintCPythonsMarkers()
        {
            string src = "import collections\n"
                + "d = collections.deque(); d.append(d); print(repr(d))\n"
                + "cm = collections.ChainMap({}); cm.maps[0]['x'] = cm; print(repr(cm))\n"
                + "od = collections.OrderedDict(); od['a'] = od; print(repr(od))\n"
                + "dd = collections.defaultdict(list); dd['a'] = dd; print(repr(dd))\n"
                + "l = [1]; t = (l,); l.append(t); print(repr(t))\n"
                + "NT = collections.namedtuple('NT', 'a'); ll = []; nt = NT(ll); ll.append(nt); print(repr(nt))\n";
            Assert.Equal("deque([[...]])\nChainMap({'x': ...})\nOrderedDict({'a': ...})\n"
                + "defaultdict(<class 'list'>, {'a': defaultdict(<class 'list'>, {...})})\n([1, (...)],)\nNT(a=[NT(a=[...])])\n", Out(src));
        }

        [Theory]
        [InlineData("b'\\x00\\xd8\\x41\\x00'.decode('utf-16-le')", "UnicodeDecodeError: 'utf-16-le' codec can't decode bytes in position 0-1: illegal UTF-16 surrogate")]
        [InlineData("b'\\x00\\xd8\\x00'.decode('utf-16-le')", "UnicodeDecodeError: 'utf-16-le' codec can't decode bytes in position 0-2: unexpected end of data")]
        [InlineData("b'\\x00\\xdc\\x00\\x00'.decode('utf-16-le')", "UnicodeDecodeError: 'utf-16-le' codec can't decode bytes in position 0-1: illegal encoding")]
        [InlineData("b'\\x00'.decode('utf-16-le')", "UnicodeDecodeError: 'utf-16-le' codec can't decode byte 0x00 in position 0: truncated data")]
        [InlineData("gzip.decompress(gzip.compress(b'abc') + b'\\0' * 12)", "b'abc'")]
        [InlineData("gzip.decompress(gzip.compress(b'abc') + b'\\0\\0' + gzip.compress(b'de'))", "b'abcde'")]
        [InlineData("gzip.decompress(gzip.compress(b'abc') + b'xy')", "BadGzipFile: Not a gzipped file (b'xy')")]
        [InlineData("gzip.decompress(b'\\x1f')", "BadGzipFile: Not a gzipped file (b'\\x1f')")]
        [InlineData("json.loads('1\\x00')", "JSONDecodeError: Extra data: line 1 column 2 (char 1)")]
        [InlineData("json.loads('1\\x0c')", "JSONDecodeError: Extra data: line 1 column 2 (char 1)")]
        [InlineData("json.loads(' 1 \\t\\n\\r')", "1")]
        public void CodecsGzipAndJsonAsCPythonDo(string expr, string expected)
        {
            Assert.Equal(expected, Show("import gzip, json\n", expr));
        }

        [Theory]
        [InlineData("datetime.date(10 ** 5000, 1, 1)", "OverflowError: Python int too large to convert to C long")]
        [InlineData("datetime.date(2 ** 40, 1, 1)", "OverflowError: signed integer is greater than maximum")]
        [InlineData("datetime.date(2 ** 30, 1, 1)", "ValueError: year must be in 1..9999, not 1073741824")]
        [InlineData("datetime.date.fromordinal(2 ** 30)", "ValueError: year must be in 1..9999, not 2939806")]
        [InlineData("datetime.datetime.fromordinal(10 ** 5000)", "OverflowError: Python int too large to convert to C long")]
        public void DateFieldsOverflowAsCPythonDoes(string expr, string expected)
        {
            Assert.Equal(expected, Show("import datetime\n", expr));
        }

        [Theory]
        [InlineData("IOError is OSError and EnvironmentError is OSError", "True")]
        [InlineData("issubclass(FileNotFoundError, OSError) and issubclass(TimeoutError, OSError)", "True")]
        [InlineData("issubclass(ModuleNotFoundError, ImportError) and issubclass(UserWarning, Warning)", "True")]
        [InlineData("__debug__", "True")]
        [InlineData("(lambda e: (e.name, e.path, e.msg))(ImportError('m', name='n', path='p'))", "('n', 'p', 'm')")]
        [InlineData("(lambda e: (e.name, e.obj))(AttributeError('m'))", "(None, None)")]
        [InlineData("(lambda e: (e.msg, e.pattern, e.pos, e.lineno, e.colno, str(e)))(re.error('bad', 'a\\nb(', 3))", "('bad', 'a\\nb(', 3, 2, 2, 'bad at position 3 (line 2, column 2)')")]
        [InlineData("typing.TypeVar('T', zzz=1)", "TypeError: typevar() got an unexpected keyword argument 'zzz'")]
        [InlineData("functools.cache(abs, zzz=1)", "TypeError: cache() got an unexpected keyword argument 'zzz'")]
        [InlineData("[n for n in dir(csv.writer(io.StringIO())) if not n.startswith('_')]", "['dialect', 'writerow', 'writerows']")]
        public void BuiltinNamesAndExceptionAttributes(string expr, string expected)
        {
            Assert.Equal(expected, Show("import re, typing, functools, csv, io\n", expr));
        }

        [Fact]
        public void ExceptionArgsCanBeAssigned()
        {
            Assert.Equal("(1, 2) (1, 2)\n", Out("e = ValueError(1)\ne.args = [1, 2]\nprint(e.args, str(e))\n"));
        }

        [Fact]
        public void AMissingModuleIsModuleNotFoundError()
        {
            Assert.Equal("ModuleNotFoundError nope True\n",
                Out("try:\n    import nope\nexcept ImportError as e:\n    print(type(e).__name__, e.name, isinstance(e, ModuleNotFoundError))\n"));
        }

        [Theory]
        [InlineData("'<a'", "unclosed token: line 1, column 0 5")]
        [InlineData("'<a></b>'", "mismatched tag: line 1, column 5 7")]
        [InlineData("''", "no element found: line 1, column 0 3")]
        [InlineData("'<a>&bogus;</a>'", "undefined entity: line 1, column 3 11")]
        [InlineData("'<a b=\"1\" b=\"2\"/>'", "duplicate attribute: line 1, column 9 8")]
        [InlineData("'<a/><b/>'", "junk after document element: line 1, column 4 9")]
        [InlineData("'x'", "syntax error: line 1, column 0 2")]
        [InlineData("'<a>&#x110000;</a>'", "reference to invalid character number: line 1, column 3 14")]
        [InlineData("'<a>\\n<b></c>'", "mismatched tag: line 2, column 5 7")]
        public void XmlParseErrorsUseExpatsWordsAndCodes(string doc, string expected)
        {
            Assert.Equal(expected + "\n", Out("import xml.etree.ElementTree as ET\ntry:\n    ET.fromstring(" + doc + ")\nexcept ET.ParseError as e:\n    print(e, e.code)\n"));
        }

        [Theory]
        [InlineData("traceback.format_exception(ValueError, value=None, tb=None)", "['NoneType: None\\n']")]
        [InlineData("traceback.format_exception(ValueError, tb=None)", "ValueError: Both or neither of value and tb must be given")]
        [InlineData("json.loads(s='[1]')", "[1]")]
        [InlineData("json.dumps(obj=1, indent=None)", "'1'")]
        [InlineData("json.loads('1', s='2')", "TypeError: loads() got multiple values for argument 's'")]
        [InlineData("tuple(map(int, calendar.monthrange(year=2024, month=2)))", "(3, 29)")]
        [InlineData("calendar.isleap(year=2024)", "True")]
        [InlineData("int(calendar.weekday(year=2024, month=1, day=1))", "0")]
        public void KeywordsCPythonTakes(string expr, string expected)
        {
            Assert.Equal(expected, Show("import traceback, json, calendar\n", expr));
        }

        [Fact]
        public void BigAndSmallIntersectsInTheLeftOrder()
        {
            Assert.Equal("[5, 3, 1] True False\n",
                Out("big = set(range(100000))\nbig.discard(5); big.discard(3); big.discard(1)\nbig = {5, 3, 1} | big\nr = big & {1, 3, 5, -1}\nprint(list(r), {1, 2}.isdisjoint(big - {1, 2}), big.isdisjoint({7}))\n"));
        }

        [Fact]
        public void APowerOfTwoBaseParsesLongInputInLinearTime()
        {
            RunResult r = new ScriptEngine(new EngineOptions()).Run("x = int('f' * 238000, 16)\nr = x == 16 ** 238000 - 1\n",
                new RunRequest { Limits = ResourceLimits.Default.With(deadlineMs: 2000), ScalarOutputNames = new[] { "r" } });
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.Message);
            Assert.Equal("True", r.ScalarsOut["r"]);
        }

        [Fact]
        public void AModuleFrameNamesItsFileAndTheLineIsTheScripts()
        {
            RunResult r = new ScriptEngine(new EngineOptions()).Run("import string\nx = 1\nstring.Template('$y').substitute({})\n", new RunRequest());
            Assert.False(r.Succeeded);
            Assert.Equal(3, r.Error.Line);
            Assert.Contains("File \"string.py\", line", r.Error.ScriptTraceback);
            Assert.Contains("File \"<script>\", line 3, in <module>", r.Error.ScriptTraceback);
        }

        // Diff review of parts 3 and 4.
        [Fact]
        public void AnEqThatClearsTheBigSetMidIntersectionIsNoFault()
        {
            string src = "class K:\n    armed = False\n    def __init__(self, v):\n        self.v = v\n    def __hash__(self):\n        return 0\n"
                + "    def __eq__(self, o):\n        if K.armed:\n            big.clear()\n        return self.v == o.v\n"
                + "big = {K(i) for i in range(20)}\nsmall = {K(19), K(-1)}\nK.armed = True\nprint(len(big & small) >= 0)\n";
            Assert.Equal("True\n", Out(src));
        }

        [Fact]
        public void HugeCodecPositionsAssignedToArgsAreTheDigitLimit()
        {
            RunResult r = TestEngines.WithCeiling(TestEngines.Unhurried).Run(
                "n = 10 ** 300000\ne = UnicodeDecodeError('utf-8', b'x', 0, 1, 'r')\ne.args = ('utf-8', b'x', n, n + 2, 'r')\nstr(e)\n",
                new RunRequest());
            Assert.Equal("ValueError", r.Error.PythonType);
        }

        [Theory]
        [InlineData("(lambda c: (bytes(c), c.n))(Once())", "(b'C', 1)")]
        [InlineData("(E().name, E('m', name='n').name, I().path)", "('cls', 'cls', '/p')")]
        [InlineData("format(float('inf'), '010,') + format(float('nan'), '010_')", "'0000000inf0000000nan'")]
        [InlineData("(math.copysign(1.0, struct.unpack('>e', b'\\x7e\\x00')[0]), struct.pack('>e', struct.unpack('>e', b'\\x7c\\x01')[0]).hex())", "(1.0, '7c01')")]
        [InlineData("(lambda l: (l.append(collections.deque([l])), repr(l))[1])([1])", "'[1, deque([[...]])]'")]
        [InlineData("str(re.error('bad', b'ab\\ncd', 4)) + '|' + str(re.error('bad', 'abc', 10)) + '|' + str(re.error('bad', 'abc', True))", "'bad at position 4 (line 2, column 2)|bad at position 10|bad at position 1'")]
        [InlineData("(True.from_bytes(b'\\x01', 'big'), collections.defaultdict().fromkeys('a'))", "(True, defaultdict(None, {'a': None}))")]
        [InlineData("Miss()", "'foo'")]
        [InlineData("calendar.isleap(yr=1)", "TypeError: isleap() got an unexpected keyword argument 'yr'")]
        [InlineData("functools.cache(user_function=len)", "TypeError: cache() got some positional-only arguments passed as keyword arguments: 'user_function'")]
        [InlineData("json.dump(obj=1)", "TypeError: dump() missing 1 required positional argument: 'fp'")]
        [InlineData("json.dump()", "TypeError: dump() missing 2 required positional arguments: 'obj' and 'fp'")]
        [InlineData("datetime.date(2020, 1, 1).replace(year=2 ** 31)", "OverflowError: Python int too large to convert to C int")]
        [InlineData("copy.deepcopy(itertools.tee([1])[0])", "TypeError: cannot pickle 'itertools._tee' object")]
        [InlineData("(lambda i: (next(i), list(copy.copy(i)), list(i)))(iter({1: 0, 2: 0, 3: 0}))", "(1, [2, 3], [2, 3])")]
        [InlineData("gzip.decompress(gzip.compress(b'abc')[:5])", "EOFError: Compressed file ended before the end-of-stream marker was reached")]
        [InlineData("zlib.decompress(gzip.compress(b'abc') + b'junk', 31)", "b'abc'")]
        public void ReviewedCornersAnswerAsCPython(string expr, string expected)
        {
            string setup = "import math, struct, collections, re, calendar, functools, json, datetime, copy, itertools, gzip, zlib\n"
                + "class Once:\n    n = 0\n    def __iter__(self):\n        Once.n += 1\n        if Once.n > 1:\n            raise RuntimeError('twice')\n        return iter([67])\n"
                + "class E(AttributeError):\n    name = 'cls'\nclass I(ImportError):\n    path = '/p'\n"
                + "def Miss():\n    try:\n        (1).foo\n    except AttributeError as e:\n        return e.name\n";
            Assert.Equal(expected, Show(setup, expr));
        }

        [Theory]
        [InlineData("'<a>'", "no element found: line 1, column 3 3")]
        [InlineData("' '", "no element found: line 1, column 1 3")]
        [InlineData("'<?xml version=\"1.0\"?>'", "no element found: line 1, column 21 3")]
        [InlineData("'<a></a '", "unclosed token: line 1, column 3 5")]
        [InlineData("'<a>\\n</a>junk'", "junk after document element: line 2, column 4 9")]
        [InlineData("'<a>]]></a>'", "not well-formed (invalid token): line 1, column 5 4")]
        public void MoreXmlParseErrorsUseExpatsWords(string doc, string expected)
        {
            Assert.Equal(expected + "\n", Out("import xml.etree.ElementTree as ET\ntry:\n    ET.fromstring(" + doc + ")\nexcept ET.ParseError as e:\n    print(e, e.code)\n"));
        }

        [Fact]
        public void ATwoArgumentMethodCallMakesNoBoundMethod()
        {
            string loop = "class A:\n    def m(self, a, b):\n        return a\na = A()\nfor i in range(100000):\n    a.m(i, 2)\n";
            string bound = "class A:\n    def m(self, a, b):\n        return a\ng = A().m\nfor i in range(100000):\n    g(i, 2)\n";
            long viaSite = ChargeHoldTests.Stats(loop).AllocatedBytes;
            long viaBound = ChargeHoldTests.Stats(bound).AllocatedBytes;
            Assert.True(viaSite - viaBound < 100000L * 8, (viaSite - viaBound).ToString());
        }
    }
}

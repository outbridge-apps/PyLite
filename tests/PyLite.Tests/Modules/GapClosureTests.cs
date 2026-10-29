using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Tests;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modern
{
    // Probe-driven P1/P2 gap closure: exception surface (args/__cause__/custom classes), the __name__
    // family (+functools.wraps), the Ellipsis literal, iter(callable, sentinel), ascii()/!a, and the
    // str/int/float method pack. The format-spec fixes live in BuiltinsFormatTests.
    [Trait("Category", "Compatibility")]
    public class GapClosureTests
    {
        private static string R(string src)
        {
            return Ev.Run(src).Repr("r");
        }

        private static string S(string src)
        {
            var r = Ev.Run(src);
            return r.Get("r").Str(r.Ctx);
        }

        private static ExceptionValue Err(string src)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(src)).Value;
        }

        // ---- exception surface: args / __cause__ / __context__ ----

        [Theory]
        [InlineData("try:\n raise ValueError('a')\nexcept ValueError as e:\n r = e.args\n", "('a',)")]
        [InlineData("try:\n raise ValueError('a', 1)\nexcept ValueError as e:\n r = e.args\n", "('a', 1)")]
        [InlineData("try:\n raise ValueError\nexcept ValueError as e:\n r = e.args\n", "()")]
        [InlineData("e = KeyError('k')\nr = e.args\n", "('k',)")]
        public void ExceptionArgs(string src, string expected)
        {
            Assert.Equal(expected, R(src));
        }

        [Fact]
        public void CauseExposedAfterRaiseFrom()
        {
            string src = "try:\n try:\n  raise ValueError('a')\n except ValueError as e:\n  raise TypeError('b') from e\n"
                + "except TypeError as t:\n r = repr(t.__cause__)\n";
            Assert.Equal("ValueError('a')", S(src));
        }

        [Fact]
        public void CauseAndContextDefaultToNone()
        {
            Assert.Equal("(None, None)", R("e = ValueError('x')\nr = (e.__cause__, e.__context__)\n"));
        }

        [Fact]
        public void ImplicitContextChain()
        {
            string src = "try:\n try:\n  raise ValueError('a')\n except ValueError:\n  raise KeyError('b')\n"
                + "except KeyError as k:\n r = repr(k.__context__)\n";
            Assert.Equal("ValueError('a')", S(src));
        }

        // ---- custom exception classes ----

        [Fact]
        public void CustomExceptionRaiseAndCatch()
        {
            string src = "class MyErr(Exception):\n pass\ntry:\n raise MyErr('boom')\nexcept MyErr as e:\n r = (str(e), e.args)\n";
            Assert.Equal("('boom', ('boom',))", R(src));
        }

        [Fact]
        public void CustomExceptionCaughtByBase()
        {
            string src = "class MyErr(ValueError):\n pass\ntry:\n raise MyErr('x')\nexcept ValueError:\n r = 'value'\n";
            Assert.Equal("value", S(src));
            src = "class MyErr(ValueError):\n pass\ntry:\n raise MyErr('x')\nexcept Exception:\n r = 'exc'\n";
            Assert.Equal("exc", S(src));
        }

        [Fact]
        public void CustomExceptionNotCaughtBySibling()
        {
            string src = "class MyErr(ValueError):\n pass\ntry:\n raise MyErr('x')\nexcept TypeError:\n r = 'wrong'\n";
            ExceptionValue ev = Err(src);
            Assert.Equal("MyErr", ev.ExcType.Name);   // propagates with the custom type name
        }

        [Fact]
        public void CustomExceptionChainAndTuple()
        {
            string src = "class A(Exception):\n pass\nclass B(A):\n pass\ntry:\n raise B('x')\nexcept (TypeError, A) as e:\n r = type(e).__name__\n";
            Assert.Equal("B", S(src));
        }

        [Fact]
        public void CustomExceptionIsinstance()
        {
            string src = "class MyErr(ValueError):\n pass\ne = MyErr('x')\n"
                + "r = (isinstance(e, MyErr), isinstance(e, ValueError), isinstance(e, TypeError), issubclass(MyErr, Exception))\n";
            Assert.Equal("(True, True, False, True)", R(src));
        }

        [Fact]
        public void CustomExceptionReprAndTypeOnlyRaise()
        {
            Assert.Equal("MyErr('a', 1)", S("class MyErr(Exception):\n pass\nr = repr(MyErr('a', 1))\n"));
            Assert.Equal("caught", S("class MyErr(Exception):\n pass\ntry:\n raise MyErr\nexcept MyErr:\n r = 'caught'\n"));
        }

        [Fact]
        public void CustomExceptionBodyMayNotDefineMethodsOrFields()
        {
            ExceptionValue ev = Err("class MyErr(Exception):\n def __init__(self, m):\n  self.m = m\n");
            Assert.Equal("TypeError", ev.ExcType.Name);
            Assert.Contains("may not define methods or fields", ev.GetMessageText(Rt.Ctx()));
        }

        // ---- __name__ on callables / classes / types / modules ----

        [Theory]
        [InlineData("def foo():\n pass\nr = foo.__name__\n", "foo")]
        [InlineData("f = lambda: 1\nr = f.__name__\n", "<lambda>")]
        [InlineData("r = len.__name__\n", "len")]
        [InlineData("r = 'ab'.upper.__name__\n", "upper")]
        [InlineData("class A:\n pass\nr = A.__name__\n", "A")]
        [InlineData("class A:\n pass\nr = type(A()).__name__\n", "A")]
        [InlineData("r = type(5).__name__ + type('s').__name__\n", "intstr")]
        [InlineData("import math\nr = math.__name__\n", "math")]
        [InlineData("class MyErr(Exception):\n pass\nr = MyErr.__name__\n", "MyErr")]
        [InlineData("try:\n raise KeyError('k')\nexcept LookupError as e:\n r = type(e).__name__\n", "KeyError")]
        public void DunderName(string src, string expected)
        {
            Assert.Equal(expected, S(src));
        }

        [Fact]
        public void WrapsCopiesName()
        {
            string src = "from functools import wraps\ndef deco(f):\n @wraps(f)\n def w():\n  return f()\n return w\n"
                + "@deco\ndef hello():\n return 1\nr = (hello.__name__, hello())\n";
            Assert.Equal("('hello', 1)", R(src));
        }

        [Fact]
        public void UpdateWrapperCopiesName()
        {
            string src = "from functools import update_wrapper\ndef orig():\n return 1\ndef w():\n return 2\n"
                + "w2 = update_wrapper(w, orig)\nr = (w2.__name__, w2())\n";
            Assert.Equal("('orig', 2)", R(src));
        }

        // ---- Ellipsis literal ----

        [Theory]
        [InlineData("r = ...\n", "Ellipsis")]
        [InlineData("r = Ellipsis\n", "Ellipsis")]
        [InlineData("r = ... is Ellipsis\n", "True")]
        [InlineData("r = bool(...)\n", "True")]
        [InlineData("d = {...: 1}\nr = d[...]\n", "1")]
        [InlineData("def f(): ...\nr = f()\n", "None")]
        public void EllipsisLiteral(string src, string expected)
        {
            Assert.Equal(expected, R(src));
        }

        // ---- iter(callable, sentinel) ----

        [Fact]
        public void IterCallableSentinel()
        {
            string src = "vals = [1, 2, 0, 3]\nf = lambda: vals.pop(0)\nr = list(iter(f, 0))\n";
            Assert.Equal("[1, 2]", R(src));
        }

        [Fact]
        public void IterSentinelStaysExhausted()
        {
            string src = "vals = [0, 7]\nf = lambda: vals.pop(0)\nit = iter(f, 0)\nr = (list(it), list(it))\n";
            Assert.Equal("([], [])", R(src));
        }

        [Fact]
        public void IterSentinelNonCallable()
        {
            Assert.Equal("TypeError", Err("r = iter(5, 0)\n").ExcType.Name);
        }

        // ---- ascii() / !a ----

        [Theory]
        [InlineData("r = ascii('caf\\u00e9')\n", "'caf\\xe9'")]
        [InlineData("r = ascii('ok')\n", "'ok'")]
        [InlineData("r = ascii('\\u2022')\n", "'\\u2022'")]
        [InlineData("r = ascii('\\U0001F600')\n", "'\\U0001f600'")]
        [InlineData("r = ascii(['\\u00e9'])\n", "['\\xe9']")]
        [InlineData("s = 'caf\\u00e9'\nr = f'{s!a}'\n", "'caf\\xe9'")]
        [InlineData("r = '{0!a}'.format('caf\\u00e9')\n", "'caf\\xe9'")]
        public void AsciiFamily(string src, string expected)
        {
            Assert.Equal(expected, S(src));
        }

        // ---- \N{...} escapes resolve, and an unknown name is a compile-time error ----

        [Fact]
        public void NamedEscapeResolves()
        {
            Assert.Equal("'•'", Ev.ReprOf("'\\N{BULLET}'"));
            var ex = Assert.Throws<PySyntaxErrorException>(() => Ev.Run("s = '\\N{NO SUCH NAME}'\n"));
            Assert.Contains("unknown Unicode character name", ex.Message);
        }

        // ---- str method pack ----

        [Theory]
        [InlineData("r = '{a}-{b}'.format_map({'a': 1, 'b': 2})\n", "1-2")]
        [InlineData("r = 'abc'.translate(str.maketrans('ab', 'xy'))\n", "xyc")]
        [InlineData("r = 'abc'.translate(str.maketrans('a', 'x', 'c'))\n", "xb")]
        [InlineData("r = 'abc'.translate({97: 'AA', 98: None})\n", "AAc")]
        [InlineData("r = 'abc'.translate({97: 120})\n", "xbc")]
        public void TranslateAndFormatMap(string src, string expected)
        {
            Assert.Equal(expected, S(src));
        }

        [Theory]
        [InlineData("r = ('abc'.isascii(), 'caf\\u00e9'.isascii(), ''.isascii())\n", "(True, False, True)")]
        [InlineData("r = ('x1'.isidentifier(), 'a b'.isidentifier(), '1x'.isidentifier(), ''.isidentifier(), '_'.isidentifier())\n",
            "(True, False, False, False, True)")]
        [InlineData("r = ('ab c'.isprintable(), 'a\\x01'.isprintable(), ''.isprintable())\n", "(True, False, True)")]
        public void StrPredicates(string src, string expected)
        {
            Assert.Equal(expected, R(src));
        }

        [Theory]
        [InlineData("r = '{z}'.format_map({'a': 1})\n", "KeyError")]
        [InlineData("r = '{a}'.format_map([1])\n", "TypeError")]
        [InlineData("r = str.maketrans('ab', 'xyz')\n", "ValueError")]
        [InlineData("r = str.maketrans(5)\n", "TypeError")]
        [InlineData("r = str.maketrans({'ab': 'x'})\n", "ValueError")]
        [InlineData("r = 'a'.translate({97: 3.5})\n", "TypeError")]
        [InlineData("r = 'a'.translate({97: 1114112})\n", "ValueError")]
        public void StrMethodPackErrors(string src, string excType)
        {
            Assert.Equal(excType, Err(src).ExcType.Name);
        }

        // ---- dict.fromkeys ----

        [Theory]
        [InlineData("r = dict.fromkeys('ab', 0)\n", "{'a': 0, 'b': 0}")]
        [InlineData("r = dict.fromkeys([1, 2])\n", "{1: None, 2: None}")]
        [InlineData("d = dict.fromkeys('ab', [])\nd['a'].append(1)\nr = d['b']\n", "[1]")]
        public void DictFromKeys(string src, string expected)
        {
            Assert.Equal(expected, R(src));
        }

        // ---- int method pack ----

        [Theory]
        [InlineData("r = (255).bit_count()\n", "8")]
        [InlineData("r = (-255).bit_count()\n", "8")]
        [InlineData("r = (0).bit_count()\n", "0")]
        [InlineData("r = ((-2 ** 63).bit_count(), (-2 ** 64 + 1).bit_count(), True.bit_length(), False.bit_count())\n", "(1, 64, 1, 0)")]
        // a million bits in one pass each (a bit-at-a-time loop ran for minutes, past the deadline)
        [InlineData("x = 1 << 999999\nr = (x.bit_length(), (x - 1).bit_count(), (-x).bit_length())\n", "(1000000, 999999, 1000000)")]
        // to_bytes range by bit length: the edges of each width, signed and unsigned, and length 0
        [InlineData("def tb(v, n, s):\n    try:\n        return v.to_bytes(n, 'big', signed=s)\n    except OverflowError:\n        return 'OVF'\n"
            + "r = [tb(0, 0, True), tb(-1, 0, True), tb(128, 1, True), tb(-128, 1, True), tb(127, 1, True), tb(-129, 1, True), "
            + "tb(255, 1, False), tb(256, 1, False), tb(1, 0, False), tb(-1, 4, True)]\n",
            "[b'', 'OVF', 'OVF', b'\\x80', b'\\x7f', 'OVF', b'\\xff', 'OVF', 'OVF', b'\\xff\\xff\\xff\\xff']")]
        // from_bytes in one linear pass: both orders, signed and unsigned, empty, and a 100k-byte round trip
        [InlineData("r = (int.from_bytes(b'\\x80\\x00', 'big', signed=True), int.from_bytes(b'\\x00\\x80', 'little', signed=True), "
            + "int.from_bytes(b'\\xff\\xff'), int.from_bytes(b'', signed=True), int.from_bytes(b'\\x00\\x01', 'little'))\n",
            "(-32768, -32768, 65535, 0, 256)")]
        [InlineData("x = 7 ** 280000\nb = x.to_bytes((x.bit_length() + 7) // 8, 'little')\nr = (int.from_bytes(b, 'little') == x, "
            + "int.from_bytes((-x).to_bytes(len(b) + 1, 'big', signed=True), 'big', signed=True) == -x)\n", "(True, True)")]
        [InlineData("r = (5).as_integer_ratio()\n", "(5, 1)")]
        [InlineData("r = (-3).as_integer_ratio()\n", "(-3, 1)")]
        [InlineData("r = (1).to_bytes()\n", "b'\\x01'")]
        [InlineData("r = (255).to_bytes(2)\n", "b'\\x00\\xff'")]
        [InlineData("r = (255).to_bytes(length=2, byteorder='little')\n", "b'\\xff\\x00'")]
        [InlineData("r = int.from_bytes(b'\\x01\\x00')\n", "256")]
        [InlineData("r = int.from_bytes(b'\\x01\\x00', byteorder='little')\n", "1")]
        public void IntMethodPack(string src, string expected)
        {
            Assert.Equal(expected, R(src));
        }

        // The numeric tower: an int is its own real part and numerator, a float its own real part, and
        // neither has an imaginary one. Reachable through a format spec too, which is where they show up.
        [Theory]
        [InlineData("r = ((5).real, (5).imag, (5).numerator, (5).denominator)\n", "(5, 0, 5, 1)")]
        [InlineData("r = ((-7).numerator, (-7).denominator)\n", "(-7, 1)")]
        [InlineData("r = (5).conjugate()\n", "5")]
        [InlineData("r = ((1.5).real, (1.5).imag, (1.5).conjugate())\n", "(1.5, 0.0, 1.5)")]
        [InlineData("r = '{0.real}/{0.imag}'.format(5)\n", "'5/0'")]
        [InlineData("r = '{0.numerator}/{0.denominator}'.format(7)\n", "'7/1'")]
        public void NumericTower(string src, string expected)
        {
            Assert.Equal(expected, R(src));
        }

        // ---- float method pack ----

        [Theory]
        [InlineData("r = (2.0).is_integer()\n", "True")]
        [InlineData("r = (2.5).is_integer()\n", "False")]
        [InlineData("r = (0.5).as_integer_ratio()\n", "(1, 2)")]
        [InlineData("r = (2.5).as_integer_ratio()\n", "(5, 2)")]
        [InlineData("r = (-0.75).as_integer_ratio()\n", "(-3, 4)")]
        [InlineData("r = (0.0).as_integer_ratio()\n", "(0, 1)")]
        [InlineData("r = (10.0).as_integer_ratio()\n", "(10, 1)")]
        public void FloatMethodPack(string src, string expected)
        {
            Assert.Equal(expected, R(src));
        }

        [Theory]
        [InlineData("r = (3.0).hex()\n", "0x1.8000000000000p+1")]
        [InlineData("r = (0.0).hex()\n", "0x0.0p+0")]
        [InlineData("r = (-0.5).hex()\n", "-0x1.0000000000000p-1")]
        [InlineData("r = (5e-324).hex()\n", "0x0.0000000000001p-1022")]
        public void FloatHex(string src, string expected)
        {
            Assert.Equal(expected, S(src));
        }

        [Theory]
        [InlineData("r = float.fromhex('0x1.8p1')\n", "3.0")]
        [InlineData("r = float.fromhex('0x1.8p+1')\n", "3.0")]
        [InlineData("r = float.fromhex('-0x1.0p-1')\n", "-0.5")]
        [InlineData("r = float.fromhex('ff')\n", "255.0")]
        [InlineData("r = float.fromhex('  0X1p4  ')\n", "16.0")]
        [InlineData("r = float.fromhex('inf')\n", "inf")]
        [InlineData("r = float.fromhex('-InFinity')\n", "-inf")]
        [InlineData("r = float.fromhex((0.1).hex()) == 0.1\n", "True")]
        [InlineData("r = float.fromhex((5e-324).hex()) == 5e-324\n", "True")]
        [InlineData("r = float.fromhex('0x1p-1200')\n", "0.0")]
        public void FloatFromHex(string src, string expected)
        {
            Assert.Equal(expected, R(src));
        }

        [Theory]
        [InlineData("r = float('nan').as_integer_ratio()\n", "ValueError")]
        [InlineData("r = float('inf').as_integer_ratio()\n", "OverflowError")]
        [InlineData("r = float.fromhex('xyz')\n", "ValueError")]
        [InlineData("r = float.fromhex('0x1p1024')\n", "OverflowError")]
        [InlineData("r = float.fromhex('')\n", "ValueError")]
        public void FloatMethodPackErrors(string src, string excType)
        {
            Assert.Equal(excType, Err(src).ExcType.Name);
        }

        // ---- getattr / hasattr / setattr ----

        private const string Pt = "from dataclasses import dataclass\n@dataclass\nclass P:\n    x: int\n    y: int\np = P(1, 2)\n";

        [Theory]
        [InlineData(Pt + "r = getattr(p, 'x')\n", "1")]
        [InlineData(Pt + "r = getattr(p, 'missing', 5)\n", "5")]
        [InlineData(Pt + "r = getattr(p, 'missing', None)\n", "None")]
        [InlineData(Pt + "r = hasattr(p, 'y')\n", "True")]
        [InlineData(Pt + "r = hasattr(p, 'missing')\n", "False")]
        [InlineData(Pt + "setattr(p, 'x', 7)\nr = p.x\n", "7")]
        [InlineData(Pt + "r = setattr(p, 'x', 7)\n", "None")]
        [InlineData("r = getattr('abc', 'upper')()\n", "'ABC'")]
        [InlineData("r = hasattr('abc', 'upper')\n", "True")]
        [InlineData("r = hasattr(1, 'nope')\n", "False")]
        [InlineData("import math\nr = getattr(math, 'pi') == math.pi\n", "True")]
        [InlineData("import math\nr = hasattr(math, 'nope')\n", "False")]
        [InlineData("r = getattr(ValueError('x'), 'args')\n", "('x',)")]
        [InlineData("r = getattr(ValueError('x'), '__cause__') is None\n", "True")]
        public void AttrBuiltins(string src, string expected)
        {
            Assert.Equal(expected, R(src));
        }

        [Theory]
        [InlineData(Pt + "getattr(p, 'missing')\n", "AttributeError", "'P' object has no attribute 'missing'")]
        [InlineData(Pt + "getattr(p, 5)\n", "TypeError", "attribute name must be string, not 'int'")]
        [InlineData(Pt + "getattr(p)\n", "TypeError", "getattr expected at least 2 arguments, got 1")]
        [InlineData(Pt + "getattr(p, 'x', 1, 2)\n", "TypeError", "getattr expected at most 3 arguments, got 4")]
        [InlineData(Pt + "getattr(p, 'x', default=1)\n", "TypeError", "getattr() takes no keyword arguments")]
        [InlineData(Pt + "hasattr(p)\n", "TypeError", "hasattr expected 2 arguments, got 1")]
        [InlineData(Pt + "setattr(p, 'x')\n", "TypeError", "setattr expected 3 arguments, got 2")]
        [InlineData(Pt + "setattr(p, 'zz', 1)\n", "AttributeError", "'P' object has no attribute 'zz'")]
        [InlineData("setattr(1, 'x', 3)\n", "AttributeError", "'int' object has no attribute 'x'")]
        [InlineData("setattr('a', 'upper', 3)\n", "AttributeError", "attribute 'upper' of 'str' objects is not writable")]
        [InlineData("import math\nsetattr(math, 'pi', 3)\n", "AttributeError", "module 'math' is read-only in this environment")]
        [InlineData("from dataclasses import dataclass\n@dataclass(frozen=True)\nclass F:\n    v: int\nsetattr(F(1), 'v', 2)\n",
            "AttributeError", "cannot assign to field 'v'")]
        public void AttrBuiltinErrors(string src, string excType, string message)
        {
            ExceptionValue e = Err(src);
            Assert.Equal(excType, e.ExcType.Name);
            Assert.Equal(message, e.GetMessageText(Rt.Ctx()));
        }
    }
}

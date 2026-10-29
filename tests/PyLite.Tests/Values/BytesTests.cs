using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Tests;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modern
{
    // RFC-001 §2 binary stack — bytes type (Phase 1: literals + core protocols + bytes()).
    [Trait("Category", "Compatibility")]
    public class BytesTests
    {
        private static string R(string expr) => Ev.ReprOf(expr);

        [Theory]
        [InlineData("b'abc'", "b'abc'")]
        [InlineData("b'\\x41\\x42'", "b'AB'")]
        [InlineData("b'a\\tb'", "b'a\\tb'")]
        [InlineData("b'\\x00\\xff'", "b'\\x00\\xff'")]
        [InlineData("b'abc' b'de'", "b'abcde'")]     // adjacent concatenation
        [InlineData("len(b'hello')", "5")]
        [InlineData("b'abc'[0]", "97")]
        [InlineData("b'abc'[-1]", "99")]
        [InlineData("b'abcde'[1:3]", "b'bc'")]
        [InlineData("b'abcde'[::2]", "b'ace'")]
        [InlineData("b'ab' + b'cd'", "b'abcd'")]
        [InlineData("b'ab' * 3", "b'ababab'")]
        [InlineData("list(b'abc')", "[97, 98, 99]")]
        [InlineData("97 in b'abc'", "True")]
        [InlineData("b'bc' in b'abcd'", "True")]
        [InlineData("b'x' in b'abcd'", "False")]
        [InlineData("b'abc' == b'abc'", "True")]
        [InlineData("b'abc' == b'abd'", "False")]
        [InlineData("b'abc' < b'abd'", "True")]
        [InlineData("b'abc' < b'ab'", "False")]
        [InlineData("bytes()", "b''")]
        [InlineData("bytes(3)", "b'\\x00\\x00\\x00'")]
        [InlineData("bytes([104, 105])", "b'hi'")]
        [InlineData("bytes('hi', 'utf-8')", "b'hi'")]
        [InlineData("isinstance(b'x', bytes)", "True")]
        [InlineData("isinstance('x', bytes)", "False")]
        [InlineData("bool(b'')", "False")]
        [InlineData("bool(b'x')", "True")]
        public void BytesBehavior(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void RawBytesKeepsBackslash()
        {
            // rb'\n' is a 2-byte sequence: backslash, n
            Assert.Equal("b'\\\\n'", R("rb'\\n'"));
            Assert.Equal("2", R("len(rb'\\n')"));
        }

        [Fact]
        public void NonAsciiInBytesLiteralRejected()
        {
            Assert.Throws<PySyntaxErrorException>(() => Ev.Run("x = b'é'\n"));
        }

        [Fact]
        public void MixingBytesAndStrRejected()
        {
            Assert.Throws<PySyntaxErrorException>(() => Ev.Run("x = b'a' 'b'\n"));
        }

        [Fact]
        public void ByteOutOfRangeRejected()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("x = bytes([256])\n"));
            Assert.Equal("bytes must be in range(0, 256)", ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]
        public void StrWithoutEncodingRejected()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("x = bytes('hi')\n"));
            Assert.Equal("string argument without an encoding", ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]
        public void BytesAreHashable()
        {
            Assert.Equal("1", R("len({b'a': 1, b'a': 2})"));
        }

        // ---- methods ----

        [Theory]
        [InlineData("b'hi'.decode()", "'hi'")]
        [InlineData("b'hi'.decode('ascii')", "'hi'")]
        [InlineData("b'AB'.hex()", "'4142'")]
        [InlineData("b'\\x00\\xff'.hex()", "'00ff'")]
        [InlineData("b'hello'.find(b'll')", "2")]
        [InlineData("b'hello'.find(b'x')", "-1")]
        [InlineData("b'hello'.find(108)", "2")]
        [InlineData("b'hello'.rfind(b'l')", "3")]
        [InlineData("b'hello'.count(b'l')", "2")]
        [InlineData("b'hello'.startswith(b'he')", "True")]
        [InlineData("b'hello'.endswith(b'lo')", "True")]
        [InlineData("b'hello'.replace(b'l', b'L')", "b'heLLo'")]
        [InlineData("b'a,b,c'.split(b',')", "[b'a', b'b', b'c']")]
        [InlineData("b'  a b  '.split()", "[b'a', b'b']")]
        [InlineData("b'-'.join([b'a', b'b', b'c'])", "b'a-b-c'")]
        [InlineData("b'  hi  '.strip()", "b'hi'")]
        [InlineData("b'xxhixx'.strip(b'x')", "b'hi'")]
        [InlineData("b'abc'.upper()", "b'ABC'")]
        [InlineData("b'ABC'.lower()", "b'abc'")]
        [InlineData("'hi'.encode()", "b'hi'")]
        [InlineData("'hi'.encode('ascii')", "b'hi'")]
        [InlineData("'café'.encode('utf-8')", "b'caf\\xc3\\xa9'")]
        [InlineData("b'caf\\xc3\\xa9'.decode('utf-8')", "'café'")]
        public void BytesMethods(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void RoundTripEncodeDecode()
        {
            Assert.Equal("'héllo'", R("'héllo'.encode('utf-8').decode('utf-8')"));
        }

        [Fact]
        public void IndexRaisesWhenNotFound()
        {
            Assert.Throws<ScriptException>(() => Ev.Run("x = b'abc'.index(b'z')\n"));
        }

        // ---- cp866 / koi8-r / iso-8859-5: the other Cyrillic pages an integration meets (closed 2026-09-11) ----
        // Known positions from CPython's tables, as code points; all 256 positions are defined in each.

        [Theory]
        [InlineData("'\\u0416\\u0438\\u0440'.encode('cp866')", "b'\\x86\\xa8\\xe0'")]
        [InlineData("'\\u0416\\u0438\\u0440'.encode('koi8-r')", "b'\\xf6\\xc9\\xd2'")]
        [InlineData("'\\u0416\\u0438\\u0440'.encode('iso-8859-5')", "b'\\xb6\\xd8\\xe0'")]
        // cp866: 0x80 A, 0xAF p, 0xE0 r, 0xEF ya, 0xF0 YO, 0xF1 yo, 0xFF nbsp, 0xB0 light shade
        [InlineData("[hex(ord(c)) for c in b'\\x80\\xaf\\xe0\\xef\\xf0\\xf1\\xff\\xb0'.decode('cp866')]",
            "['0x410', '0x43f', '0x440', '0x44f', '0x401', '0x451', '0xa0', '0x2591']")]
        // koi8-r: 0xC0 yu, 0xC1 a, 0xE1 A, 0xA3 yo, 0xB3 YO, 0x9A nbsp
        [InlineData("[hex(ord(c)) for c in b'\\xc0\\xc1\\xe1\\xa3\\xb3\\x9a'.decode('koi8-r')]",
            "['0x44e', '0x430', '0x410', '0x451', '0x401', '0xa0']")]
        // iso-8859-5: 0xA1 YO, 0xB0 A, 0xD0 a, 0xF1 yo, 0xA0 nbsp, 0xFD section, 0xF0 numero
        [InlineData("[hex(ord(c)) for c in b'\\xa1\\xb0\\xd0\\xf1\\xa0\\xfd\\xf0'.decode('iso-8859-5')]",
            "['0x401', '0x410', '0x430', '0x451', '0xa0', '0xa7', '0x2116']")]
        [InlineData("bytes(range(256)).decode('cp866').encode('cp866') == bytes(range(256))", "True")]
        [InlineData("bytes(range(256)).decode('koi8-r').encode('koi8-r') == bytes(range(256))", "True")]
        [InlineData("bytes(range(256)).decode('iso-8859-5').encode('iso-8859-5') == bytes(range(256))", "True")]
        // aliases, spelled the ways CPython accepts
        [InlineData("[b'\\x80'.decode(a) for a in ('866', 'ibm866', 'CP866', 'IBM866')] == ['\\u0410'] * 4", "True")]
        [InlineData("[b'\\xc1'.decode(a) for a in ('koi8_r', 'KOI8-R', 'cskoi8r')] == ['\\u0430'] * 3", "True")]
        [InlineData("[b'\\xd0'.decode(a) for a in ('iso8859_5', 'ISO-8859-5', 'cyrillic')] == ['\\u0430'] * 3", "True")]
        [InlineData("'\\u20ac'.encode('cp866', 'replace')", "b'?'")]
        public void TheCyrillicPages(string expr, string expected)
        {
            Assert.Equal(expected, Ev.Run("r = " + expr + "\n").Repr("r"));
        }

        [Fact]
        public void TheCyrillicPagesRefuseWhatTheyDoNotCarry()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("x = '\\u20ac'.encode('cp866')\n"));
            Assert.Equal("UnicodeEncodeError", ex.Value.ExcType.Name);
            Assert.Equal("'cp866' codec can't encode character '\\u20ac' in position 0: character maps to <undefined>",
                ex.Value.GetMessageText(Rt.Ctx()));
        }

        // ---- cp1251 / cp1252 / utf-16 / utf-8-sig (non-ASCII spelled with escapes) ----

        [Theory]
        [InlineData("'\\u043f\\u0440\\u0438\\u0432\\u0435\\u0442'.encode('cp1251')", "b'\\xef\\xf0\\xe8\\xe2\\xe5\\xf2'")]
        [InlineData("b'\\xef\\xf0\\xe8\\xe2\\xe5\\xf2'.decode('cp1251') == '\\u043f\\u0440\\u0438\\u0432\\u0435\\u0442'", "True")]
        [InlineData("'\\u20ac'.encode('cp1252')", "b'\\x80'")]
        [InlineData("b'\\x80'.decode('cp1252') == '\\u20ac'", "True")]
        [InlineData("'\\u20ac'.encode('cp1251')", "b'\\x88'")]          // cp1251 has the euro at 0x88
        [InlineData("'\\u20ac'.encode('windows-1252')", "b'\\x80'")]
        [InlineData("'a'.encode('utf-16')", "b'\\xff\\xfea\\x00'")]
        [InlineData("b'\\xff\\xfea\\x00'.decode('utf-16')", "'a'")]
        [InlineData("b'\\xfe\\xff\\x00a'.decode('utf-16')", "'a'")]     // BOM picks the byte order
        [InlineData("b'a\\x00'.decode('utf-16')", "'a'")]               // no BOM: little-endian
        [InlineData("'a'.encode('utf-16-le')", "b'a\\x00'")]
        [InlineData("'a'.encode('utf-16-be')", "b'\\x00a'")]
        [InlineData("b'\\x00a'.decode('utf-16-be')", "'a'")]
        [InlineData("'abc'.encode('utf_16_le')", "b'a\\x00b\\x00c\\x00'")]
        [InlineData("'a'.encode('utf-8-sig')", "b'\\xef\\xbb\\xbfa'")]
        [InlineData("b'\\xef\\xbb\\xbfa'.decode('utf-8-sig')", "'a'")]
        [InlineData("b'a'.decode('utf-8-sig')", "'a'")]
        // errors= now applies to encode() too, and to the new codecs
        [InlineData("b'\\x98'.decode('cp1251', 'replace') == '\\ufffd'", "True")]
        [InlineData("b'\\x98'.decode('cp1251', 'ignore')", "''")]
        [InlineData("'\\u0448'.encode('cp1252', 'replace')", "b'?'")]
        [InlineData("'\\u0448'.encode('cp1252', 'ignore')", "b''")]
        [InlineData("'\\xe9x'.encode('ascii', 'replace')", "b'?x'")]
        [InlineData("'\\xe9x'.encode('ascii', 'ignore')", "b'x'")]
        public void ExtraCodecs(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        // the code pages' undefined positions have no mapping, as in CPython (the framework's tables
        // round-trip them to the same-numbered C1 control)
        [InlineData("x = b'ab\\x98'.decode('cp1251')", "UnicodeDecodeError",
            "'cp1251' codec can't decode byte 0x98 in position 2: character maps to <undefined>")]
        [InlineData("x = b'\\x81'.decode('cp1252')", "UnicodeDecodeError",
            "'cp1252' codec can't decode byte 0x81 in position 0: character maps to <undefined>")]
        [InlineData("x = '\\u0448'.encode('cp1252')", "UnicodeEncodeError",
            "'cp1252' codec can't encode character '\\u0448' in position 0: character maps to <undefined>")]
        [InlineData("x = b'abc'.decode('utf-16')", "UnicodeDecodeError",
            "'utf-16' codec can't decode byte 0x63 in position 2: truncated data")]
        public void ExtraCodecErrors(string src, string excType, string message)
        {
            ExceptionValue e = Assert.Throws<ScriptException>(() => Ev.Run(src + "\n")).Value;
            Assert.Equal(excType, e.ExcType.Name);
            Assert.Equal(message, e.GetMessageText(Rt.Ctx()));
        }

        // ---- backslashreplace / xmlcharrefreplace / surrogateescape ----

        [Theory]
        [InlineData("'x\\xfc'.encode('ascii', 'backslashreplace')", "b'x\\\\xfc'")]
        [InlineData("'x\\u0448'.encode('ascii', 'backslashreplace')", "b'x\\\\u0448'")]
        [InlineData("'\\U0001f600'.encode('ascii', 'backslashreplace')", "b'\\\\U0001f600'")]   // one code point, not two units
        [InlineData("'\\u0448'.encode('cp1252', 'backslashreplace')", "b'\\\\u0448'")]
        [InlineData("'\\xfc'.encode('ascii', 'xmlcharrefreplace')", "b'&#252;'")]
        [InlineData("'\\U0001f600'.encode('ascii', 'xmlcharrefreplace')", "b'&#128512;'")]
        [InlineData("'x\\u20ac'.encode('ascii', 'namereplace')", "b'x\\\\N{EURO SIGN}'")]
        [InlineData("'\\U0001f600'.encode('ascii', 'namereplace')", "b'\\\\N{GRINNING FACE}'")]
        [InlineData("'\\u01a2'.encode('ascii', 'namereplace')", "b'\\\\N{LATIN CAPITAL LETTER OI}'")]     // its name, not its alias
        [InlineData("'\\uac01'.encode('ascii', 'namereplace')", "b'\\\\N{HANGUL SYLLABLE GAG}'")]         // composed
        [InlineData("'\\u4e00'.encode('ascii', 'namereplace')", "b'\\\\N{CJK UNIFIED IDEOGRAPH-4E00}'")]   // by rule
        [InlineData("'\\uf900'.encode('ascii', 'namereplace')", "b'\\\\N{CJK COMPATIBILITY IDEOGRAPH-F900}'")]
        [InlineData("'\\ue000'.encode('ascii', 'namereplace')", "b'\\\\ue000'")]                           // private use: no name
        [InlineData("'\\x80'.encode('ascii', 'namereplace')", "b'\\\\x80'")]                                // a control: no name
        [InlineData("'\\u0448'.encode('cp1252', 'namereplace')", "b'\\\\N{CYRILLIC SMALL LETTER SHA}'")]
        [InlineData("b'a\\xff\\xfeb'.decode('utf-8', 'backslashreplace')", "'a\\\\xff\\\\xfeb'")]
        [InlineData("b'\\x98'.decode('cp1251', 'backslashreplace')", "'\\\\x98'")]
        [InlineData("b'a\\xffb'.decode('utf-8', 'surrogateescape') == 'a\\udcffb'", "True")]
        [InlineData("b'a\\xff\\x80b'.decode('utf-8', 'surrogateescape').encode('utf-8', 'surrogateescape')",
            "b'a\\xff\\x80b'")]                                                                  // the round trip is the point
        [InlineData("b'\\xe9'.decode('ascii', 'surrogateescape').encode('ascii', 'surrogateescape')", "b'\\xe9'")]
        [InlineData("b'\\xef\\xbb\\xbf\\xff'.decode('utf-8-sig', 'surrogateescape') == '\\udcff'", "True")]
        [InlineData("b'\\xff\\xff'.decode('utf-8', 'replace') == '\\ufffd\\ufffd'", "True")]      // one per malformed run
        [InlineData("'abc'.encode('ascii', 'bogus')", "b'abc'")]                                  // handler resolved lazily
        [InlineData("b'abc'.decode('utf-8', 'bogus')", "'abc'")]
        public void ErrorHandlers(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        // a lone surrogate has no utf-8/utf-16 encoding; .NET would silently emit U+FFFD
        [InlineData("x = '\\udc80'.encode('utf-8')", "UnicodeEncodeError",
            "'utf-8' codec can't encode character '\\udc80' in position 0: surrogates not allowed")]
        [InlineData("x = '\\ud800'.encode('utf-16-le')", "UnicodeEncodeError",
            "'utf-16-le' codec can't encode character '\\ud800' in position 0: surrogates not allowed")]
        [InlineData("x = b'\\x00\\xd8'.decode('utf-16-le')", "UnicodeDecodeError",
            "'utf-16-le' codec can't decode bytes in position 0-1: illegal encoding")]
        // surrogateescape only round-trips what it could have escaped
        [InlineData("x = '\\xfc'.encode('ascii', 'surrogateescape')", "UnicodeEncodeError",
            "'ascii' codec can't encode character '\\xfc' in position 0: ordinal not in range(128)")]
        // an unknown handler is a LookupError, not a silent 'replace'
        [InlineData("x = '\\xfc'.encode('ascii', 'bogus')", "LookupError", "unknown error handler name 'bogus'")]
        [InlineData("x = b'\\xff'.decode('utf-8', 'bogus')", "LookupError", "unknown error handler name 'bogus'")]
        [InlineData("x = b'\\xff'.decode('utf-8', 'xmlcharrefreplace')", "TypeError",
            "don't know how to handle UnicodeDecodeError in error callback")]
        [InlineData("x = b'\\xff'.decode('utf-8', 'namereplace')", "TypeError",
            "don't know how to handle UnicodeDecodeError in error callback")]
        public void ErrorHandlerErrors(string src, string excType, string message)
        {
            ExceptionValue e = Assert.Throws<ScriptException>(() => Ev.Run(src + "\n")).Value;
            Assert.Equal(excType, e.ExcType.Name);
            Assert.Equal(message, e.GetMessageText(Rt.Ctx()));
        }

        // ---- classmethods + int<->bytes ----

        [Theory]
        [InlineData("bytes.fromhex('4142')", "b'AB'")]
        [InlineData("bytes.fromhex('41 42 43')", "b'ABC'")]     // spaces allowed
        [InlineData("bytes.fromhex('')", "b''")]
        [InlineData("(255).to_bytes(2, 'big')", "b'\\x00\\xff'")]
        [InlineData("(255).to_bytes(2, 'little')", "b'\\xff\\x00'")]
        [InlineData("(1024).to_bytes(2, 'big')", "b'\\x04\\x00'")]
        [InlineData("(-2).to_bytes(2, 'big', signed=True)", "b'\\xff\\xfe'")]
        [InlineData("int.from_bytes(b'\\x00\\xff', 'big')", "255")]
        [InlineData("int.from_bytes(b'\\xff\\x00', 'little')", "255")]
        [InlineData("int.from_bytes(b'\\xff\\xfe', 'big', signed=True)", "-2")]
        [InlineData("int.from_bytes(b'\\x04\\x00', 'big')", "1024")]
        [InlineData("(5).bit_length()", "3")]
        public void IntBytesInterop(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        // bool carries int's method surface, as CPython's int subclass does. Every answer is an int,
        // never the bool back: True.real is 1, not True.
        [Theory]
        [InlineData("True.bit_length()", "1")]
        [InlineData("False.bit_length()", "0")]
        [InlineData("True.bit_count()", "1")]
        [InlineData("True.to_bytes(1, 'big')", "b'\\x01'")]
        [InlineData("False.to_bytes(2, 'big')", "b'\\x00\\x00'")]
        [InlineData("True.real", "1")]
        [InlineData("True.imag", "0")]
        [InlineData("True.numerator", "1")]
        [InlineData("True.denominator", "1")]
        [InlineData("True.conjugate()", "1")]
        [InlineData("True.as_integer_ratio()", "(1, 1)")]
        [InlineData("bool.from_bytes(b'\\x01', 'big')", "1")]
        [InlineData("type(True.real).__name__", "'int'")]
        [InlineData("type(True.conjugate()).__name__", "'int'")]
        [InlineData("type(True).__name__", "'bool'")]        // still its own type
        [InlineData("repr(True)", "'True'")]
        [InlineData("isinstance(True, int)", "True")]
        [InlineData("hash(True) == hash(1)", "True")]
        [InlineData("'bit_length' in dir(True)", "True")]
        public void BoolCarriesIntMethods(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void ToBytesRoundTrip()
        {
            Assert.Equal("12345", R("int.from_bytes((12345).to_bytes(4, 'big'), 'big')"));
        }

        [Fact]
        public void ToBytesOverflowRaises()
        {
            Assert.Throws<ScriptException>(() => Ev.Run("x = (256).to_bytes(1, 'big')\n"));
        }

        [Fact]
        public void NegativeUnsignedToBytesRaises()
        {
            Assert.Throws<ScriptException>(() => Ev.Run("x = (-1).to_bytes(2, 'big')\n"));
        }

        [Fact]
        public void FromHexOddLengthRaises()
        {
            Assert.Throws<ScriptException>(() => Ev.Run("x = bytes.fromhex('abc')\n"));
        }

        // The text-shaped methods. They read bytes as ASCII, as CPython does: a byte over 127 is never
        // a letter, a space or half of a case pair.
        [Theory]
        [InlineData("b'a=b=c'.partition(b'=')", "(b'a', b'=', b'b=c')")]
        [InlineData("b'a=b=c'.rpartition(b'=')", "(b'a=b', b'=', b'c')")]
        [InlineData("b'abc'.partition(b'=')", "(b'abc', b'', b'')")]
        [InlineData("b'abc'.rpartition(b'=')", "(b'', b'', b'abc')")]   // a miss puts it LAST
        [InlineData("b'a\\nb\\r\\nc'.splitlines()", "[b'a', b'b', b'c']")]
        [InlineData("b'a\\nb'.splitlines(True)", "[b'a\\n', b'b']")]
        [InlineData("b'a\\x0bb'.splitlines()", "[b'a\\x0bb']")]         // bytes breaks on \n and \r only
        [InlineData("b'a\\n'.splitlines()", "[b'a']")]
        [InlineData("b''.splitlines()", "[]")]
        [InlineData("b'ab'.center(6, b'-')", "b'--ab--'")]
        [InlineData("b'ab'.center(5, b'-')", "b'--ab-'")]               // the odd byte goes CPython's way
        [InlineData("b'ab'.center(4)", "b' ab '")]
        [InlineData("b'ab'.ljust(4, b'.')", "b'ab..'")]
        [InlineData("b'ab'.rjust(4, b'.')", "b'..ab'")]
        [InlineData("b'abcd'.ljust(2)", "b'abcd'")]
        [InlineData("b'1'.zfill(3)", "b'001'")]
        [InlineData("b'-1'.zfill(3)", "b'-01'")]
        [InlineData("b'+1'.zfill(4)", "b'+001'")]
        [InlineData("b'a\\tb'.expandtabs(4)", "b'a   b'")]
        [InlineData("b'ab\\tc'.expandtabs()", "b'ab      c'")]
        [InlineData("b'abcdef'.removeprefix(b'abc')", "b'def'")]
        [InlineData("b'abcdef'.removeprefix(b'x')", "b'abcdef'")]
        [InlineData("b'abcdef'.removesuffix(b'def')", "b'abc'")]
        [InlineData("b'abc'.translate(None, b'b')", "b'ac'")]
        [InlineData("b'abc'.translate(bytes.maketrans(b'a', b'x'))", "b'xbc'")]
        [InlineData("b'abc'.translate(bytes.maketrans(b'a', b'x'), b'c')", "b'xb'")]
        [InlineData("len(bytes.maketrans(b'ab', b'cd'))", "256")]
        [InlineData("b'aBC'.capitalize()", "b'Abc'")]
        [InlineData("b'hello world'.title()", "b'Hello World'")]
        [InlineData("b'aBc'.swapcase()", "b'AbC'")]
        [InlineData("(b'abc'.isascii(), b''.isascii(), bytes([255]).isascii())", "(True, True, False)")]
        [InlineData("(b'abc'.isalpha(), b''.isalpha(), b'ab1'.isalpha())", "(True, False, False)")]
        [InlineData("(b'123'.isdigit(), b'a1'.isalnum(), b' \\t'.isspace())", "(True, True, True)")]
        [InlineData("(b'abc'.islower(), b'aBc'.islower(), b'123'.islower())", "(True, False, False)")]
        [InlineData("(b'ABC'.isupper(), b'Hello World'.istitle(), b'Hello world'.istitle())", "(True, True, False)")]
        public void TextMethods(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("b'abc'.partition(b'')", "ValueError")]
        [InlineData("b'abc'.partition('=')", "TypeError")]
        [InlineData("b'abc'.center(5, b'ab')", "TypeError")]
        [InlineData("b'abc'.translate(b'short')", "ValueError")]
        [InlineData("bytes.maketrans(b'ab', b'c')", "ValueError")]
        public void TextMethodErrors(string expr, string exc)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("x = " + expr + "\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
        }

        // As str does: an empty needle matches at every position, and a start past the end finds nothing.
        [Theory]
        [InlineData("b'abc'.replace(b'', b'-')", "b'-a-b-c-'")]
        [InlineData("b'abc'.replace(b'', b'-', 2)", "b'-a-bc'")]
        [InlineData("b''.replace(b'', b'x')", "b'x'")]
        [InlineData("b'aXbXc'.replace(b'X', b'--', 1)", "b'a--bXc'")]
        [InlineData("b'abc'.replace(b'z', b'y')", "b'abc'")]
        [InlineData("b'abc'.find(b'', 4)", "-1")]
        [InlineData("b'abc'.find(b'', 3)", "3")]
        [InlineData("b'abc'.rfind(b'', 4)", "-1")]
        [InlineData("b'abc'.count(b'', 5)", "0")]
        [InlineData("b'abc'.count(b'')", "4")]
        [InlineData("b'abc'.startswith(b'', 4)", "False")]
        [InlineData("b'-'.join([b'a', b'', b'b'])", "b'a--b'")]
        [InlineData("b'-'.join([])", "b''")]
        public void EmptyNeedlesAndWindowsMatchStr(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void JoinNamesTheBadItem()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("x = b'-'.join([b'a', 'b'])\n"));
            Assert.Equal("sequence item 1: expected a bytes-like object, str found", ex.Value.GetMessageText(Rt.Ctx()));
        }
    }
}

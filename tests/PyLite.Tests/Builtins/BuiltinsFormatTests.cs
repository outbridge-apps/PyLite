using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Builtins
{
    // impl/06 T-BLT-21/22/23/24: format spec, numeric format, str.format markup, % operator.
    [Trait("Category", "Compatibility")]
    public class BuiltinsFormatTests
    {
        private static string Val(string expr)
        {
            var r = Ev.Run("__v = (" + expr + ")\n");
            return r.Get("__v").Str(r.Ctx);
        }

        // ---- format() string path (T-BLT-21) ----

        [Theory]
        [InlineData("format('abc', '')", "abc")]
        [InlineData("format('abc', '>5')", "  abc")]
        [InlineData("format('abc', '*^7')", "**abc**")]
        [InlineData("format('abcdef', '.3')", "abc")]
        [InlineData("format('abc', '5.2')", "ab   ")]
        // '0' flag zero-fills: an explicit align keeps it, a bare '0' means the str default (left) (3.10+)
        [InlineData("format('ab', '>08')", "000000ab")]
        [InlineData("format('ab', '<08')", "ab000000")]
        [InlineData("format('ab', '08')", "ab000000")]
        [InlineData("format(None, '')", "None")]
        [InlineData("format(True, '')", "True")]
        [InlineData("format(True, 'd')", "1")]
        public void FormatString(string expr, string expected)
        {
            Assert.Equal(expected, Val(expr));
        }

        [Theory]
        [InlineData("format('abc', '+5')", "Sign not allowed in string format specifier")]
        [InlineData("format('abc', '=5')", "'=' alignment not allowed in string format specifier")]
        [InlineData("format('abc', ',')", "Cannot specify ',' with 's'.")]
        [InlineData("format('abc', '#5')", "Alternate form (#) not allowed in string format specifier")]
        [InlineData("format('abc', 'z')", "Unknown format code 'z' for object of type 'str'")]
        [InlineData("format('abc', '.')", "Format specifier missing precision")]
        [InlineData("format('abc', '5x.2')", "Invalid format specifier")]
        [InlineData("format([1], '5')", "non-empty format string passed to object.__format__")]
        public void FormatStringErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err("x = " + expr + "\n"));
        }

        // ---- numeric format (T-BLT-22) ----

        [Theory]
        [InlineData("format(255, 'x')", "ff")]
        [InlineData("format(255, '#010x')", "0x000000ff")]
        [InlineData("format(-26, 'X')", "-1A")]
        [InlineData("format(-26, '#o')", "-0o32")]
        [InlineData("format(1234567, ',d')", "1,234,567")]
        [InlineData("format(1234, '08,d')", "0,001,234")]
        [InlineData("format(-1234, '08,d')", "-001,234")]
        [InlineData("format(65, 'c')", "A")]
        [InlineData("format(3.14159, '.2f')", "3.14")]
        [InlineData("format(3.14159, '10.2f')", "      3.14")]
        [InlineData("format(3.14159, '010.2f')", "0000003.14")]
        [InlineData("format(-3.14, '010.2f')", "-000003.14")]
        [InlineData("format(1234.5678, ',.2f')", "1,234.57")]
        [InlineData("format(12345.678, '.3e')", "1.235e+04")]
        [InlineData("format(12345.678, '.3E')", "1.235E+04")]
        [InlineData("format(0.00001234, 'g')", "1.234e-05")]
        [InlineData("format(123456789.0, 'g')", "1.23457e+08")]
        [InlineData("format(100.0, 'g')", "100")]
        [InlineData("format(100.0, '#g')", "100.000")]
        [InlineData("format(0.25, '.1%')", "25.0%")]
        [InlineData("format(0.25, '%')", "25.000000%")]
        [InlineData("format(2.0, '')", "2.0")]
        [InlineData("format(42, '.1f')", "42.0")]
        [InlineData("format(1234.5, ',')", "1,234.5")]
        // underscore grouping (PEP 515): decimal/float group /3, base types group /4
        [InlineData("format(1234567, '_')", "1_234_567")]
        [InlineData("format(1234567, '_d')", "1_234_567")]
        [InlineData("format(1234567.5, '_.1f')", "1_234_567.5")]
        [InlineData("format(0xFFFFF, '_x')", "f_ffff")]
        [InlineData("format(0xFFFFF, '#_x')", "0xf_ffff")]
        [InlineData("format(255, '_b')", "1111_1111")]
        [InlineData("format(0o777777, '_o')", "77_7777")]
        [InlineData("format(65535, '_x')", "ffff")]
        [InlineData("format(65536, '_X')", "1_0000")]
        [InlineData("format(255, '#X')", "0XFF")]
        [InlineData("format(5, '+#x')", "+0x5")]
        [InlineData("format(0, '_b')", "0")]
        [InlineData("format(123, ',')", "123")]
        [InlineData("format(-1234567.891, ',.2f')", "-1,234,567.89")]
        [InlineData("format(5, '08_')", "0_000_005")]
        [InlineData("format(5, '08,')", "0,000,005")]
        // '=' alignment pads between the sign and the digits (explicit form, any fill)
        [InlineData("format(42, '=8')", "      42")]
        [InlineData("format(-42, '=8')", "-     42")]
        [InlineData("format(-42, '0=8')", "-0000042")]
        [InlineData("format(42, '=+8')", "+     42")]
        [InlineData("format(-42, '*=8')", "-*****42")]
        [InlineData("format(-1234, '0=10,')", "-0,001,234")]
        [InlineData("format(1, '015,')", "000,000,000,001")]
        [InlineData("format(-1, '015,')", "-00,000,000,001")]
        [InlineData("format(-3.5, '0=9.1f')", "-000003.5")]
        // '0' flag with an EXPLICIT align: zero fill, alignment wins (CPython 3.10+)
        [InlineData("format(42, '>08')", "00000042")]
        [InlineData("format(-42, '>08')", "00000-42")]
        [InlineData("format(42, '<08')", "42000000")]
        public void FormatNumeric(string expr, string expected)
        {
            Assert.Equal(expected, Val(expr));
        }

        // Grouping is linear: a 200,000-bit binary used to take ~24 s with no deadline check.
        [Fact]
        public void GroupingAHugeIntIsLinear()
        {
            var r = Ev.Run("r = len(format(2 ** 200000, '_b'))\n", ResourceLimits.Default.With(deadlineMs: 5000));
            Assert.Equal("250001", r.Repr("r"));
        }

        [Fact]
        public void FormatInfNan()
        {
            Assert.Equal("+inf", Val("format(float('inf'), '+f')"));
            Assert.Equal("NAN", Val("format(float('nan'), 'F')"));
        }

        [Theory]
        [InlineData("format(42, '.1d')", "Precision not allowed in integer format specifier")]
        [InlineData("format(255, ',x')", "Cannot specify ',' with 'x'.")]
        [InlineData("format('abc', '_')", "Cannot specify '_' with 's'.")]
        [InlineData("format(65, '+c')", "Sign not allowed with integer format specifier 'c'")]
        public void FormatNumericErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err("x = " + expr + "\n"));
        }

        // ---- str.format markup (T-BLT-23) ----

        [Theory]
        [InlineData("'{} {}'.format('a', 1)", "a 1")]
        [InlineData("'{1} {0}'.format('a', 'b')", "b a")]
        [InlineData("'{x:>5}'.format(x='ab')", "   ab")]
        [InlineData("'{:{}}'.format(5, 3)", "  5")]
        [InlineData("'{:>{w}.{p}f}'.format(3.14159, w=8, p=2)", "    3.14")]
        [InlineData("'{!r:>6}'.format('a')", "   'a'")]
        [InlineData("'{{}}'.format()", "{}")]
        public void StrFormat(string expr, string expected)
        {
            Assert.Equal(expected, Val(expr));
        }

        [Fact]
        public void FStringWithSpec()
        {
            Assert.Equal("002", Val("f'{1 + 1:03d}'"));
            Assert.Equal("ab   ", Val("f'{\"ab\":<5}'"));
        }

        [Theory]
        [InlineData("'{} {1}'.format(1, 2)", "cannot switch from automatic field numbering to manual field specification")]
        [InlineData("'{0} {}'.format(1, 2)", "cannot switch from manual field specification to automatic field numbering")]
        [InlineData("'{!x}'.format(1)", "Unknown conversion specifier x")]
        [InlineData("'{5}'.format(1)", "tuple index out of range")]
        [InlineData("'{y}'.format(x=1)", "'y'")]
        [InlineData("'{'.format()", "Single '{' encountered in format string")]
        [InlineData("'}'.format()", "Single '}' encountered in format string")]
        [InlineData("'{0.attr}'.format(5)", "'int' object has no attribute 'attr'")]
        [InlineData("'{0.}'.format(5)", "Empty attribute in format string")]
        [InlineData("'{0[a}'.format({})", "Missing ']' in format string")]
        [InlineData("'{0[a]}'.format(5)", "'int' object is not subscriptable")]
        [InlineData("'{:d}'.format('x')", "Unknown format code 'd' for object of type 'str'")]
        public void StrFormatErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err("x = " + expr + "\n"));
        }

        // field_name ::= arg_name ("." attribute_name | "[" element_index "]")* -- the accessors reach
        // exactly what the dot operator and [] reach, so the sandbox surface is unchanged.
        [Theory]
        [InlineData("'{0[a]}'.format({'a': 1})", "1")]
        [InlineData("'{0[k k]}'.format({'k k': 2})", "2")]              // the key is unquoted and may hold spaces
        [InlineData("'{0[1]}'.format([10, 20])", "20")]                 // all digits -> an integer index
        [InlineData("'{0[5]}'.format({5: 'int', '5': 'str'})", "int")]
        [InlineData("'{0[0]}'.format('xyz')", "x")]
        [InlineData("'{0[a][0][b]}'.format({'a': [{'b': 7}]})", "7")]   // chains compose
        [InlineData("'{0[a]!r}'.format({'a': 'x'})", "'x'")]
        [InlineData("'{0[a]:{1}}'.format({'a': 3}, '>4')", "   3")]     // still one nesting level
        public void StrFormatFieldAccess(string expr, string expected)
        {
            Assert.Equal(expected, Val(expr));
        }

        [Theory]
        [InlineData("'{0.x}'.format(p)", "3")]
        [InlineData("'{0.name}'.format(p)", "bob")]
        [InlineData("'{q.x}'.format(q=p)", "3")]
        [InlineData("'{.x}'.format(p)", "3")]                           // auto-numbered arg_name may be empty
        [InlineData("'{0.x:>4}'.format(p)", "   3")]
        [InlineData("'{0.name!r}'.format(p)", "'bob'")]
        [InlineData("'{0.x}{0.name}'.format(p)", "3bob")]               // manual numbering reuses the arg
        [InlineData("'{k.x}'.format_map({'k': p})", "3")]
        public void StrFormatAttrAccess(string expr, string expected)
        {
            var r = Ev.Run("import dataclasses\n@dataclasses.dataclass\nclass P:\n    x: int\n    name: str\n"
                + "p = P(3, 'bob')\n__v = (" + expr + ")\n");
            Assert.Equal(expected, r.Get("__v").Str(r.Ctx));
        }

        // ---- % operator (T-BLT-24) ----

        [Theory]
        [InlineData("'%s %r' % ('a', 'a')", "a 'a'")]
        [InlineData("'%d' % 3.7", "3")]
        [InlineData("'%d' % -3.7", "-3")]
        [InlineData("'%5d|' % 42", "   42|")]
        [InlineData("'%-5d|' % 42", "42   |")]
        [InlineData("'%05.1f' % -1.5", "-01.5")]
        [InlineData("'%x %#x %X' % (255, 255, 255)", "ff 0xff FF")]
        [InlineData("'%.3e' % 12345.678", "1.235e+04")]
        [InlineData("'%g' % 0.00001234", "1.234e-05")]
        [InlineData("'%.2s' % 'abcd'", "ab")]
        [InlineData("'%*d' % (5, 1)", "    1")]
        [InlineData("'%%' % ()", "%")]
        [InlineData("'%05.3d' % 7", "  007")]
        [InlineData("'100%% %s' % 'ok'", "100% ok")]
        // %(name)s mapping substitution
        [InlineData("'%(name)s' % {'name': 'Bo'}", "Bo")]
        [InlineData("'%(n)s=%(v)d' % {'n': 'x', 'v': 5}", "x=5")]
        [InlineData("'%(x)08.2f' % {'x': 3.14159}", "00003.14")]
        [InlineData("'%(a)s' % {'a': 1, 'b': 2}", "1")]
        [InlineData("'%(a)s %(a)s' % {'a': 'hi'}", "hi hi")]
        [InlineData("'%s' % {'a': 1}", "{'a': 1}")]
        // %c (int code point or single char) and %a (ascii repr)
        [InlineData("'%c' % 65", "A")]
        [InlineData("'%c' % 'x'", "x")]
        [InlineData("'%c%c' % (66, 'x')", "Bx")]
        [InlineData("'%3c|' % 65", "  A|")]
        [InlineData("'%a' % 'caf\\u00e9'", "'caf\\xe9'")]
        [InlineData("'%a' % 'ok'", "'ok'")]
        public void PercentFormat(string expr, string expected)
        {
            Assert.Equal(expected, Val(expr));
        }

        [Theory]
        [InlineData("'%s' % (1, 2)", "not all arguments converted during string formatting")]
        [InlineData("'%s %s' % (1,)", "not enough arguments for format string")]
        [InlineData("'%z' % 1", "unsupported format character 'z' (0x7a) at index 1")]
        [InlineData("'%d' % 'x'", "%d format: a number is required, not str")]
        [InlineData("'%x' % 1.5", "%x format: an integer is required, not float")]
        [InlineData("'%*d' % ('a', 1)", "* wants int")]
        [InlineData("'abc%' % ()", "incomplete format")]
        [InlineData("'%(z)s' % {'a': 1}", "'z'")]
        [InlineData("'%(a)s' % (1, 2)", "format requires a mapping")]
        [InlineData("'%(a)s' % '{a}'", "format requires a mapping")]
        [InlineData("'%(a' % {'a': 1}", "incomplete format key")]
        [InlineData("'%c' % 'ab'", "%c requires int or char")]
        [InlineData("'%c' % 1114112", "%c arg not in range(0x110000)")]
        [InlineData("'%c' % 1.5", "%c requires int or char")]
        public void PercentFormatErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err("x = " + expr + "\n"));
        }

        // ---- bytes % args (CPython 3.5+): the same parser, bytes-specific conversions ----

        [Theory]
        [InlineData("b'%d' % 5", "b'5'")]
        [InlineData("b'%s' % b'x'", "b'x'")]
        [InlineData("b'%b' % b'x'", "b'x'")]                            // %b is the primary spelling
        [InlineData("b'%s=%d' % (b'a', 1)", "b'a=1'")]
        [InlineData("b'%(k)s-%(n)d' % {'k': b'v', 'n': 2}", "b'v-2'")]
        [InlineData("b'%a' % 'x'", "b\"'x'\"")]
        [InlineData("b'%a' % '\\u0448'", "b\"'\\\\u0448'\"")]           // %a stays ASCII
        [InlineData("b'%r' % b'x'", "b\"b'x'\"")]
        [InlineData("b'%c' % 65", "b'A'")]
        [InlineData("b'%c' % b'A'", "b'A'")]
        [InlineData("b'%x' % 255", "b'ff'")]
        [InlineData("b'%#x' % 255", "b'0xff'")]
        [InlineData("b'%.2f' % 1.5", "b'1.50'")]
        [InlineData("b'%5d|' % 42", "b'   42|'")]
        [InlineData("b'%-5d|' % 42", "b'42   |'")]
        [InlineData("b'%*d' % (5, 42)", "b'   42'")]
        [InlineData("b'100%%' % ()", "b'100%'")]
        [InlineData("b'%.2s' % b'abcd'", "b'ab'")]
        // the format and the operand are byte-transparent: arbitrary bytes pass through
        [InlineData("b'\\xff%d\\xfe' % 7", "b'\\xff7\\xfe'")]
        [InlineData("b'%s' % b'\\xff\\x00\\xfe'", "b'\\xff\\x00\\xfe'")]
        public void BytesPercentFormat(string expr, string expected)
        {
            Assert.Equal(expected, Ev.ReprOf(expr));
        }

        [Theory]
        [InlineData("b'%s' % 'x'", "%b requires a bytes-like object, or an object that implements __bytes__, not 'str'")]
        [InlineData("b'%b' % 5", "%b requires a bytes-like object, or an object that implements __bytes__, not 'int'")]
        [InlineData("b'%c' % 300", "%c requires an integer in range(256) or a single byte")]
        [InlineData("b'%c' % 'A'", "%c requires an integer in range(256) or a single byte")]
        [InlineData("b'%s %s' % (b'a',)", "not enough arguments for format string")]
        [InlineData("b'%s' % (b'a', b'b')", "not all arguments converted during bytes formatting")]
        [InlineData("b'%q' % 1", "unsupported format character 'q' (0x71) at index 1")]
        [InlineData("'%b' % 1", "unsupported format character 'b' (0x62) at index 1")]   // str has no %b
        [InlineData("5 % b'x'", "unsupported operand type(s) for %: 'int' and 'bytes'")]
        public void BytesPercentErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err("x = " + expr + "\n"));
        }
    }
}

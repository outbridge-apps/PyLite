using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // v2 C10: struct pack/unpack/unpack_from/calcsize.
    [Trait("Category", "Compatibility")]
    public class StructTests
    {
        private const string P = "import struct\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        // ---- calcsize ----
        [InlineData("r = struct.calcsize('i')", "4")]
        [InlineData("r = struct.calcsize('<2h4b')", "8")]
        [InlineData("r = struct.calcsize('10s')", "10")]
        [InlineData("r = struct.calcsize('!bxxi')", "7")]
        [InlineData("r = struct.calcsize('qQd')", "24")]
        // ---- pack: endianness ----
        [InlineData("r = struct.pack('<H', 1)", "b'\\x01\\x00'")]
        [InlineData("r = struct.pack('>H', 1)", "b'\\x00\\x01'")]
        [InlineData("r = struct.pack('!I', 0xDEADBEEF)", "b'\\xde\\xad\\xbe\\xef'")]
        [InlineData("r = struct.pack('<i', -1)", "b'\\xff\\xff\\xff\\xff'")]
        [InlineData("r = struct.pack('>h', -2)", "b'\\xff\\xfe'")]
        // ---- pack: codes ----
        [InlineData("r = struct.pack('b', -128)", "b'\\x80'")]
        [InlineData("r = struct.pack('B', 255)", "b'\\xff'")]
        [InlineData("r = struct.pack('?', True)", "b'\\x01'")]
        [InlineData("r = struct.pack('c', b'A')", "b'A'")]
        [InlineData("r = struct.pack('3s', b'ab')", "b'ab\\x00'")]
        [InlineData("r = struct.pack('3s', b'abcdef')", "b'abc'")]
        [InlineData("r = struct.pack('xx')", "b'\\x00\\x00'")]
        [InlineData("r = struct.pack('<2h', 1, 2)", "b'\\x01\\x00\\x02\\x00'")]
        // ---- round trips ----
        [InlineData("r = struct.unpack('<hHiIqQ', struct.pack('<hHiIqQ', -5, 5, -100000, 100000, -2**40, 2**40))",
            "(-5, 5, -100000, 100000, -1099511627776, 1099511627776)")]
        [InlineData("r = struct.unpack('>d', struct.pack('>d', 3.5))", "(3.5,)")]
        [InlineData("r = struct.unpack('<f', struct.pack('<f', 1.5))", "(1.5,)")]
        [InlineData("r = struct.unpack('?c3s', struct.pack('?c3s', False, b'Z', b'hey'))", "(False, b'Z', b'hey')")]
        [InlineData("r = struct.unpack('<bxh', struct.pack('<bxh', 7, 300))", "(7, 300)")]
        // ---- unpack literal ----
        [InlineData("r = struct.unpack('>I', b'\\x00\\x00\\x01\\x00')", "(256,)")]
        [InlineData("r = struct.unpack('<Q', b'\\x01\\x00\\x00\\x00\\x00\\x00\\x00\\x00')", "(1,)")]
        // ---- unpack_from ----
        [InlineData("r = struct.unpack_from('<h', b'\\xff\\x01\\x00\\xff', 1)", "(1,)")]
        [InlineData("r = struct.unpack_from('<h', struct.pack('<ih', 9, 3), offset=4)", "(3,)")]
        public void Struct(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("x = struct.pack('b', 200)", "error")]
        [InlineData("x = struct.pack('B', -1)", "error")]
        [InlineData("x = struct.pack('H', 65536)", "error")]
        [InlineData("x = struct.pack('i', 1, 2)", "error")]
        [InlineData("x = struct.pack('z', 1)", "error")]
        [InlineData("x = struct.unpack('i', b'\\x00')", "error")]
        [InlineData("x = struct.unpack_from('i', b'\\x00\\x00', 0)", "error")]
        [InlineData("x = struct.pack('c', b'AB')", "error")]
        [InlineData("x = struct.pack('i', 'text')", "error")]
        public void Errors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Fact]
        public void ErrorIsCatchable()
        {
            string src = P + "caught = ''\ntry:\n    struct.pack('b', 999)\nexcept struct.error as e:\n    caught = str(e)\nr = caught\n";
            Assert.Equal("'byte format requires -128 <= number <= 127'", Ev.Run(src).Repr("r"));
        }

        // iter_unpack: one tuple per record, and a buffer that is not a whole number of records is an error.
        [Theory]
        [InlineData("r = list(struct.iter_unpack('>h', struct.pack('>3h', 1, 2, 3)))", "[(1,), (2,), (3,)]")]
        [InlineData("r = list(struct.iter_unpack('<hh', struct.pack('<4h', 1, 2, 3, 4)))", "[(1, 2), (3, 4)]")]
        [InlineData("r = list(struct.iter_unpack('>h', b''))", "[]")]
        [InlineData("r = [t[0] for t in struct.iter_unpack('>B', b'abc')]", "[97, 98, 99]")]
        public void IterUnpack(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("r = list(struct.iter_unpack('>h', b'abc'))")]
        [InlineData("r = list(struct.iter_unpack('', b''))")]
        public void IterUnpackErrors(string body)
        {
            Assert.Equal("error", Raises(body).Value.ExcType.Name);
        }

        // Lazy as CPython's: reading the first few records of a 4M-record buffer costs those records.
        // An eager implementation would build four million tuples here and die on the budget.
        [Fact]
        public void IterUnpackIsLazy()
        {
            string body = "it = struct.iter_unpack('>b', bytes(4000000))\n"
                + "r = [next(it)[0], next(it)[0], next(it)[0]]\n";
            Assert.Equal("[0, 0, 0]", Body(body));
        }

        // The integer codes decode through a long rather than a BigInteger, so sign extension and the
        // unsigned 8-byte edge are pinned from both ends and in both byte orders.
        [Theory]
        [InlineData("r = struct.unpack('>h', bytes([255, 254]))[0]", "-2")]
        [InlineData("r = struct.unpack('<h', bytes([254, 255]))[0]", "-2")]
        [InlineData("r = struct.unpack('>H', bytes([255, 254]))[0]", "65534")]
        [InlineData("r = struct.unpack('>b', bytes([128]))[0]", "-128")]
        [InlineData("r = struct.unpack('>B', bytes([128]))[0]", "128")]
        [InlineData("r = struct.unpack('>i', bytes([255, 255, 255, 255]))[0]", "-1")]
        [InlineData("r = struct.unpack('>I', bytes([255, 255, 255, 255]))[0]", "4294967295")]
        [InlineData("r = struct.unpack('>q', bytes([128, 0, 0, 0, 0, 0, 0, 0]))[0]", "-9223372036854775808")]
        [InlineData("r = struct.unpack('>q', bytes([255] * 8))[0]", "-1")]
        [InlineData("r = struct.unpack('>Q', bytes([255] * 8))[0]", "18446744073709551615")]
        [InlineData("r = struct.unpack('<Q', bytes([255] * 8))[0]", "18446744073709551615")]
        [InlineData("r = struct.pack('>Q', 18446744073709551615)", "b'\\xff\\xff\\xff\\xff\\xff\\xff\\xff\\xff'")]
        [InlineData("r = struct.pack('>q', -9223372036854775808)", "b'\\x80\\x00\\x00\\x00\\x00\\x00\\x00\\x00'")]
        [InlineData("r = struct.unpack('>q', struct.pack('>q', -9223372036854775808))[0]", "-9223372036854775808")]
        [InlineData("r = struct.unpack('>Q', struct.pack('>Q', 18446744073709551615))[0]", "18446744073709551615")]
        [InlineData("r = struct.unpack('<d', struct.pack('<d', -2.5))[0]", "-2.5")]
        [InlineData("r = (struct.pack('<f', 1.5) == struct.pack('>f', 1.5)[::-1])", "True")]
        [InlineData("r = (struct.pack('<d', 1.5) == struct.pack('>d', 1.5)[::-1])", "True")]
        public void IntegerEdges(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("r = struct.pack('>q', 9223372036854775808)")]
        [InlineData("r = struct.pack('>Q', -1)")]
        [InlineData("r = struct.pack('>Q', 10 ** 40)")]
        [InlineData("r = struct.pack('>b', 128)")]
        [InlineData("r = struct.pack('>B', 256)")]
        [InlineData("r = struct.pack('>h', -32769)")]
        public void RangeErrors(string body)
        {
            Assert.Equal("error", Raises(body).Value.ExcType.Name);
        }

        [Fact]
        public void NativeAtSignActsAsStandard()
        {
            // Documented deviation: '@' == '=' (standard sizes, no alignment padding).
            Assert.Equal("(1, 2)", Body("r = struct.unpack('@bh', struct.pack('@bh', 1, 2))"));
        }
    }
}

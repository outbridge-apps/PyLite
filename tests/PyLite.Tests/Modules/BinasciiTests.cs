using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // v2 C9: binascii.
    [Trait("Category", "Compatibility")]
    public class BinasciiTests
    {
        private const string P = "import binascii\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        [InlineData("r = binascii.hexlify(b'ABC')", "b'414243'")]
        [InlineData("r = binascii.hexlify(b'')", "b''")]
        [InlineData("r = binascii.b2a_hex(b'\\xff\\x00')", "b'ff00'")]
        [InlineData("r = binascii.unhexlify('414243')", "b'ABC'")]
        [InlineData("r = binascii.unhexlify(b'414243')", "b'ABC'")]
        [InlineData("r = binascii.a2b_hex('FF00')", "b'\\xff\\x00'")]
        [InlineData("r = binascii.unhexlify(binascii.hexlify(b'round-trip'))", "b'round-trip'")]
        [InlineData("r = binascii.b2a_base64(b'ABC')", "b'QUJD\\n'")]
        [InlineData("r = binascii.b2a_base64(b'ABC', newline=False)", "b'QUJD'")]
        [InlineData("r = binascii.a2b_base64('QUJD')", "b'ABC'")]
        [InlineData("r = binascii.a2b_base64(b'QUJD\\n')", "b'ABC'")]
        [InlineData("r = binascii.crc32(b'hello world')", "222957957")]
        [InlineData("r = binascii.crc32(b'')", "0")]
        [InlineData("r = binascii.crc32(b' world', binascii.crc32(b'hello'))", "222957957")]
        public void Binascii(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("x = binascii.unhexlify('abc')", "Error")]
        [InlineData("x = binascii.unhexlify('zz')", "Error")]
        [InlineData("x = binascii.a2b_base64('!not-base64!')", "Error")]
        [InlineData("x = binascii.hexlify('str not bytes')", "TypeError")]
        public void Errors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Fact]
        public void ErrorIsValueErrorSubclass()
        {
            string src = P + "caught = ''\ntry:\n    binascii.unhexlify('abc')\nexcept ValueError:\n    caught = 'v'\nr = caught\n";
            Assert.Equal("'v'", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void OddLengthMessage()
        {
            string src = P + "msg = ''\ntry:\n    binascii.unhexlify('abc')\nexcept binascii.Error as e:\n    msg = str(e)\n";
            Assert.Equal("'Odd-length string'", Ev.Run(src).Repr("msg"));
        }
    }
}

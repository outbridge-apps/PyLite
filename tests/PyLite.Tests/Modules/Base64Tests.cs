using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modern
{
    // RFC-001 §2 base64 module.
    [Trait("Category", "Compatibility")]
    public class Base64Tests
    {
        private const string Imp = "import base64\n";
        private static string Run(string body, string var) => Ev.Run(Imp + body).Repr(var);

        [Theory]
        [InlineData("r = base64.b64encode(b'hello')", "b'aGVsbG8='")]
        [InlineData("r = base64.b64decode(b'aGVsbG8=')", "b'hello'")]
        [InlineData("r = base64.b64decode('aGVsbG8=')", "b'hello'")]      // accepts ASCII str
        [InlineData("r = base64.b64encode(b'')", "b''")]
        [InlineData("r = base64.standard_b64encode(b'foobar')", "b'Zm9vYmFy'")]
        [InlineData("r = base64.b16encode(b'AB')", "b'4142'")]
        [InlineData("r = base64.b16decode(b'4142')", "b'AB'")]
        [InlineData("r = base64.b32encode(b'foobar')", "b'MZXW6YTBOI======'")]
        [InlineData("r = base64.b32decode(b'MZXW6YTBOI======')", "b'foobar'")]
        public void Base64Ops(string body, string expected)
        {
            Assert.Equal(expected, Run(body, "r"));
        }

        [Fact]
        public void RoundTripStandard()
        {
            Assert.Equal("b'the quick brown fox'",
                Run("r = base64.b64decode(base64.b64encode(b'the quick brown fox'))", "r"));
        }

        [Fact]
        public void UrlsafeUsesDashUnderscore()
        {
            // bytes chosen so standard base64 contains + and /
            var r = Ev.Run(Imp + "s = base64.b64encode(b'\\xfb\\xff\\xbf')\nu = base64.urlsafe_b64encode(b'\\xfb\\xff\\xbf')\n");
            Assert.Equal("b'+/+/'", r.Repr("s"));
            Assert.Equal("b'-_-_'", r.Repr("u"));
        }

        [Fact]
        public void UrlsafeRoundTrip()
        {
            Assert.Equal("b'\\xfb\\xff\\xbf'",
                Run("r = base64.urlsafe_b64decode(base64.urlsafe_b64encode(b'\\xfb\\xff\\xbf'))", "r"));
        }

        [Fact]
        public void InvalidBase64Raises()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Imp + "x = base64.b64decode(b'abc')\n"));   // bad padding
            Assert.Equal("Error", ex.Value.ExcType.Name);   // binascii.Error, a ValueError
            ex = Assert.Throws<ScriptException>(() => Ev.Run(Imp + "x = base64.b64decode(b'YW*Jj', validate=True)\n"));
            Assert.Equal("Error", ex.Value.ExcType.Name);
        }

        // As CPython: without validate, characters outside the alphabet are discarded; altchars and validate
        // bind by name (both used to be dropped silently, B10).
        [Theory]
        [InlineData("r = base64.b64decode(b'!!!!')", "b''")]
        [InlineData("r = base64.b64decode(b'YW*Jj')", "b'abc'")]
        [InlineData("r = base64.b64encode(b'\\xfb\\xff', altchars=b'-_')", "b'-_8='")]
        [InlineData("r = base64.b64encode(b'\\xfb\\xff', b'-_')", "b'-_8='")]
        [InlineData("r = base64.b64decode(b'-_8=', altchars=b'-_')", "b'\\xfb\\xff'")]
        [InlineData("r = base64.b64decode(s=b'YWI=', validate=True)", "b'ab'")]
        [InlineData("import binascii\nr = binascii.hexlify(b'abc', sep='-')", "b'61-62-63'")]
        [InlineData("import binascii\nr = binascii.b2a_hex(b'abcd', '-', 2)", "b'6162-6364'")]
        public void KeywordsBindAsInCPython(string body, string expected)
        {
            Assert.Equal(expected, Run(body + "\n", "r"));
        }
    }
}

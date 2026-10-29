using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/11 T-MFD-24..27: uuid.UUID, uuid1/3/4/5, NAMESPACE_* + variant constants.
    [Trait("Category", "Compatibility")]
    public class UuidTests
    {
        private const string P = "import uuid\nfrom uuid import UUID, uuid1, uuid3, uuid4, uuid5, NAMESPACE_DNS\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        // ---- construction / string forms ----
        [InlineData("r = str(UUID('6fa459ea-ee8a-3ca4-894e-db77e160355e'))", "'6fa459ea-ee8a-3ca4-894e-db77e160355e'")]
        [InlineData("r = str(UUID('{6fa459ea-ee8a-3ca4-894e-db77e160355e}'))", "'6fa459ea-ee8a-3ca4-894e-db77e160355e'")]
        [InlineData("r = str(UUID('urn:uuid:6fa459ea-ee8a-3ca4-894e-db77e160355e'))", "'6fa459ea-ee8a-3ca4-894e-db77e160355e'")]
        [InlineData("r = str(UUID('6fa459eaee8a3ca4894edb77e160355e'))", "'6fa459ea-ee8a-3ca4-894e-db77e160355e'")]
        [InlineData("r = UUID(int=0).int", "0")]
        [InlineData("r = str(UUID(int=0))", "'00000000-0000-0000-0000-000000000000'")]
        [InlineData("r = repr(UUID(int=0))", "\"UUID('00000000-0000-0000-0000-000000000000')\"")]
        // ---- fields ----
        [InlineData("u = UUID('12345678-1234-5678-1234-567812345678')\nr = u.hex", "'12345678123456781234567812345678'")]
        [InlineData("u = UUID('12345678-1234-5678-1234-567812345678')\nr = u.int", "24197857161011715162171839636988778104")]
        [InlineData("u = UUID('12345678-1234-5678-1234-567812345678')\nr = u.urn", "'urn:uuid:12345678-1234-5678-1234-567812345678'")]
        [InlineData("u = UUID('12345678-1234-5678-1234-567812345678')\nr = u.time_low", "305419896")]
        [InlineData("u = UUID('12345678-1234-5678-1234-567812345678')\nr = u.time_mid", "4660")]
        [InlineData("u = UUID('12345678-1234-5678-1234-567812345678')\nr = u.time_hi_version", "22136")]
        [InlineData("u = UUID('12345678-1234-5678-1234-567812345678')\nr = u.node", "95073701484152")]
        [InlineData("u = UUID('12345678-1234-5678-1234-567812345678')\nr = u.fields", "(305419896, 4660, 22136, 18, 52, 95073701484152)")]
        [InlineData("r = UUID('6fa459ea-ee8a-3ca4-894e-db77e160355e').version", "3")]
        [InlineData("r = UUID('6fa459ea-ee8a-3ca4-894e-db77e160355e').variant", "'specified in RFC 4122'")]
        [InlineData("r = UUID(int=0).variant", "'reserved for NCS compatibility'")]
        // ---- comparisons / hash ----
        [InlineData("r = (UUID(int=1) < UUID(int=2))", "True")]
        [InlineData("r = (UUID(int=1) == UUID(int=1))", "True")]
        [InlineData("r = (hash(UUID(int=5)) == hash(UUID(int=5)))", "True")]
        // ---- version kwarg ----
        [InlineData("r = UUID(int=0, version=4).version", "4")]
        [InlineData("r = UUID(int=0, version=4).variant", "'specified in RFC 4122'")]
        // ---- uuid3/5 goldens ----
        [InlineData("r = str(uuid3(NAMESPACE_DNS, 'python.org'))", "'6fa459ea-ee8a-3ca4-894e-db77e160355e'")]
        [InlineData("r = str(uuid5(NAMESPACE_DNS, 'python.org'))", "'886313e1-3b8a-5372-9b90-0c9aee199e5d'")]
        [InlineData("r = uuid3(NAMESPACE_DNS, 'python.org').version", "3")]
        [InlineData("r = uuid5(NAMESPACE_DNS, 'python.org').version", "5")]
        [InlineData("r = str(NAMESPACE_DNS)", "'6ba7b810-9dad-11d1-80b4-00c04fd430c8'")]
        [InlineData("r = uuid.RFC_4122", "'specified in RFC 4122'")]
        // ---- uuid1 / uuid4 ----
        [InlineData("r = uuid1().version", "1")]
        [InlineData("r = uuid1().variant", "'specified in RFC 4122'")]
        [InlineData("r = uuid1(node=0x123456789abc).node", "20015998343868")]
        [InlineData("r = uuid1(clock_seq=100).clock_seq", "100")]
        [InlineData("r = ((uuid1().node & 0x010000000000) != 0)", "True")]
        [InlineData("r = (uuid1().node == uuid1().node)", "True")]
        [InlineData("r = uuid4().version", "4")]
        [InlineData("r = uuid4().variant", "'specified in RFC 4122'")]
        public void Uuids(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("UUID(int=2**128)", "ValueError")]
        [InlineData("UUID(hex='not-hex-at-all-xxxx')", "ValueError")]
        [InlineData("UUID('12345')", "ValueError")]
        [InlineData("UUID(hex='6fa459eaee8a3ca4894edb77e160355e', int=1)", "TypeError")]
        [InlineData("UUID()", "TypeError")]
        [InlineData("UUID(int=0, version=7)", "ValueError")]
        public void Errors(string body, string excType)
        {
            Assert.Equal(excType, Raises("x = " + body).Value.ExcType.Name);
        }

        [Fact]
        public void Uuid1TwiceDistinct()
        {
            Assert.Equal("True", Body("r = (uuid1() != uuid1())"));
        }

        [Fact]
        public void Uuid4ReproducibleWithSeed()
        {
            string src = "import random, uuid\nrandom.seed(42)\na = uuid.uuid4()\nrandom.seed(42)\nb = uuid.uuid4()\nr = (a == b)\n";
            Assert.Equal("True", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void Uuid3LoneSurrogateRaises()
        {
            ScriptException ex = Raises("uuid3(NAMESPACE_DNS, '\\ud800')");
            Assert.Equal("ValueError", ex.Value.ExcType.Name);
        }
    }
}

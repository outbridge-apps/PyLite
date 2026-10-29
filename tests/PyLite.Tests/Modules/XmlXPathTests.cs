using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/11 T-MFD-31/32: mini-XPath find/findall/findtext/iter + register_namespace.
    [Trait("Category", "Compatibility")]
    public class XmlXPathTests
    {
        private const string P = "import xml.etree.ElementTree as ET\nfrom xml.etree.ElementTree import fromstring, Element, SubElement\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        [Theory]
        [InlineData("r = fromstring('<a><b/><c/></a>').find('b').tag", "'b'")]
        [InlineData("r = fromstring('<a><b/></a>').find('z')", "None")]
        [InlineData("r = [e.tag for e in fromstring('<a><b/><c/></a>').findall('*')]", "['b', 'c']")]
        [InlineData("r = [e.tag for e in fromstring('<a><b><d/></b><c/></a>').findall('.//d')]", "['d']")]
        [InlineData("r = fromstring('<a><b>hi</b></a>').findtext('b')", "'hi'")]
        [InlineData("r = fromstring('<a><b/></a>').findtext('b')", "''")]
        [InlineData("r = fromstring('<a/>').findtext('z', 'def')", "'def'")]
        [InlineData("r = fromstring('<a><b x=\"1\"/><b x=\"2\"/></a>').find(\"b[@x='2']\").get('x')", "'2'")]
        [InlineData("r = fromstring('<a><b><c/></b></a>').find('b/c').tag", "'c'")]
        [InlineData("r = [e.tag for e in fromstring('<a><b><c/></b></a>').iter()]", "['a', 'b', 'c']")]
        [InlineData("r = [e.tag for e in fromstring('<a><b/><b/></a>').iter('b')]", "['b', 'b']")]
        [InlineData("r = fromstring('<a><b/></a>').find('.').tag", "'a'")]
        [InlineData("r = fromstring('<a><x a=\"1\"/></a>').find('x[@a]').get('a')", "'1'")]
        [InlineData("r = fromstring('<a><b><c/></b></a>').find('a[b]')", "None")]
        [InlineData("r = fromstring('<r><a><b/></a></r>').find('a[b]').tag", "'a'")]
        [InlineData("r = [e.tag for e in fromstring('<a><b/><c><d/></c></a>').findall('.//*')]", "['b', 'c', 'd']")]
        [InlineData("r = len(fromstring('<a>x</a>').findall('z'))", "0")]
        public void XPath(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Fact]
        public void RegisterNamespaceAffectsTostring()
        {
            string body = "ET.register_namespace('foo', 'http://x')\n"
                + "s = ET.tostring(fromstring('<ns:a xmlns:ns=\"http://x\"/>'), 'unicode')\n"
                + "r = ('foo:a' in s and 'xmlns:foo=\"http://x\"' in s)\n";
            Assert.Equal("True", Body(body));
        }

        [Fact]
        public void RegisterNamespaceReservedPrefixRaises()
        {
            var ex = Assert.Throws<Outbridge.PyLite.Runtime.Errors.ScriptException>(
                () => Ev.Run(P + "ET.register_namespace('xmlbad', 'uri')\n"));
            Assert.Equal("ValueError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void IterIsLazyPreOrder()
        {
            string body = "e = fromstring('<a><b><c/></b><d/></a>')\nr = [x.tag for x in e.iter()]\n";
            Assert.Equal("['a', 'b', 'c', 'd']", Body(body));
        }

        [Fact]
        public void FindTextFoundButEmpty()
        {
            Assert.Equal("''", Body("r = fromstring('<a><b></b></a>').findtext('b')"));
        }
    }
}

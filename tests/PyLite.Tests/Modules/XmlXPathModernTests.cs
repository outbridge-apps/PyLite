using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // v2 E18: XPath positional predicates [N]/[last()]/[last()-N] and the namespaces= prefix map.
    [Trait("Category", "Compatibility")]
    public class XmlXPathModernTests
    {
        private const string P = "from xml.etree.ElementTree import fromstring\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        // ---- positional predicates ----
        [InlineData("e = fromstring('<a><b i=\"1\"/><b i=\"2\"/><b i=\"3\"/></a>')\nr = e.find('b[1]').get('i')", "'1'")]
        [InlineData("e = fromstring('<a><b i=\"1\"/><b i=\"2\"/><b i=\"3\"/></a>')\nr = e.find('b[2]').get('i')", "'2'")]
        [InlineData("e = fromstring('<a><b i=\"1\"/><b i=\"2\"/><b i=\"3\"/></a>')\nr = e.find('b[last()]').get('i')", "'3'")]
        [InlineData("e = fromstring('<a><b i=\"1\"/><b i=\"2\"/><b i=\"3\"/></a>')\nr = e.find('b[last()-1]').get('i')", "'2'")]
        [InlineData("e = fromstring('<a><b/><b/></a>')\nr = e.find('b[5]')", "None")]
        // position counts only tag-matched siblings, skipping others
        [InlineData("e = fromstring('<a><c/><b i=\"1\"/><c/><b i=\"2\"/></a>')\nr = e.find('b[2]').get('i')", "'2'")]
        // per-parent grouping: b[1] of EACH matched subtree node
        [InlineData("e = fromstring('<r><a><b i=\"1\"/><b i=\"2\"/></a><a><b i=\"3\"/></a></r>')\nr = [x.get('i') for x in e.findall('a/b[1]')]", "['1', '3']")]
        // combined with attribute predicate: filter first, then position
        [InlineData("e = fromstring('<a><b x=\"n\"/><b x=\"y\" i=\"1\"/><b x=\"y\" i=\"2\"/></a>')\nr = e.find(\"b[@x='y'][2]\").get('i')", "'2'")]
        // ---- namespaces= ----
        [InlineData("e = fromstring('<ns:a xmlns:ns=\"http://x\"><ns:b>v</ns:b></ns:a>')\nr = e.findtext('p:b', None, {'p': 'http://x'})", "'v'")]
        [InlineData("e = fromstring('<ns:a xmlns:ns=\"http://x\"><ns:b/></ns:a>')\nr = e.find('p:b', {'p': 'http://x'}).tag", "'{http://x}b'")]
        [InlineData("e = fromstring('<ns:a xmlns:ns=\"http://x\"><ns:b/><ns:b/></ns:a>')\nr = len(e.findall('p:b', namespaces={'p': 'http://x'}))", "2")]
        // default namespace ('' key) applies to bare tags
        [InlineData("e = fromstring('<a xmlns=\"http://d\"><b/></a>')\nr = e.find('b', {'': 'http://d'}).tag", "'{http://d}b'")]
        [InlineData("e = fromstring('<a><b/></a>')\nr = e.find('b', None).tag", "'b'")]
        public void ModernXPath(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("e = fromstring('<a><b/></a>')\nx = e.find('p:b', {'q': 'http://x'})", "KeyError")]
        [InlineData("e = fromstring('<a><b/></a>')\nx = e.find('.//b[1]')", "ValueError")]
        [InlineData("e = fromstring('<a><b/></a>')\nx = e.find('b', [1, 2])", "TypeError")]
        public void Errors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Fact]
        public void PlainPathsStillWork()
        {
            // Regression guard for the matcher rework.
            string body = "e = fromstring('<a><b x=\"1\"/><c><d/></c></a>')\n"
                + "r = (e.find('b').get('x'), [t.tag for t in e.findall('.//d')], e.find('.').tag)\n";
            Assert.Equal("('1', ['d'], 'a')", Body(body));
        }
    }
}

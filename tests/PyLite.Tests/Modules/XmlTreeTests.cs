using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/11 T-MFD-28/29/30: Element/SubElement/iselement, fromstring/XML, tostring, ParseError.
    [Trait("Category", "Compatibility")]
    public class XmlTreeTests
    {
        private const string P = "import xml.etree.ElementTree as ET\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        // ---- Element ----
        // tostring(xml_declaration=), indent, iterfind, ElementTree
        [InlineData("r = ET.tostring(ET.Element('r'), encoding='utf-8', xml_declaration=True)", "b\"<?xml version='1.0' encoding='utf-8'?>\\n<r />\"")]
        [InlineData("r = ET.tostring(ET.Element('r'), encoding='unicode', xml_declaration=True)", "\"<?xml version='1.0' encoding='utf-8'?>\\n<r />\"")]
        [InlineData("r = ET.tostring(ET.Element('r'), encoding='latin-1', xml_declaration=False)", "b'<r />'")]
        [InlineData("r = ET.tostring(ET.Element('r'), xml_declaration=None)", "b'<r />'")]
        [InlineData("e = ET.fromstring('<a><b><c/></b><d>t</d></a>')\nET.indent(e)\nr = ET.tostring(e, encoding='unicode')", "'<a>\\n  <b>\\n    <c />\\n  </b>\\n  <d>t</d>\\n</a>'")]
        [InlineData("e = ET.fromstring('<a><b/></a>')\nET.indent(e, space='\\t', level=1)\nr = ET.tostring(e, encoding='unicode')", "'<a>\\n\\t\\t<b />\\n\\t</a>'")]
        [InlineData("e = ET.fromstring('<a>keep<b/>tail</a>')\nET.indent(e)\nr = ET.tostring(e, encoding='unicode')", "'<a>keep<b />tail</a>'")]
        [InlineData("e = ET.Element('a')\nr = ET.indent(e)", "None")]
        [InlineData("r = [e.text for e in ET.fromstring('<a><b>1</b><c><b>2</b></c></a>').iterfind('.//b')]", "['1', '2']")]
        [InlineData("r = type(ET.fromstring('<a><b/></a>').iterfind('b')).__name__ != 'list'", "True")]
        [InlineData("t = ET.ElementTree(ET.fromstring('<a><b>x</b></a>'))\nr = (t.getroot().tag, t.find('b').text, t.findtext('b'), [e.tag for e in t.iter()], [e.text for e in t.iterfind('b')], len(t.findall('b')))", "('a', 'x', 'x', ['a', 'b'], ['x'], 1)")]
        [InlineData("import io\nt = ET.ElementTree(ET.fromstring('<a><b>x</b></a>'))\nout = io.StringIO()\nt.write(out, encoding='unicode', xml_declaration=True)\nr = out.getvalue()", "\"<?xml version='1.0' encoding='utf-8'?>\\n<a><b>x</b></a>\"")]
        [InlineData("r = (ET.ElementTree().getroot(), isinstance(ET.ElementTree(ET.Element('x')), ET.ElementTree))", "(None, True)")]
        [InlineData("t = ET.ElementTree()\nt._setroot(ET.Element('z'))\nr = t.getroot().tag", "'z'")]
        [InlineData("r = ET.Element('a').tag", "'a'")]
        [InlineData("r = ET.Element('a').text", "None")]
        [InlineData("r = bool(ET.Element('a'))", "False")]
        [InlineData("e = ET.Element('a')\ne.append(ET.Element('b'))\nr = bool(e)", "True")]
        [InlineData("r = ET.Element('a', {'x':'1'}).get('x')", "'1'")]
        [InlineData("r = ET.Element('a').get('z', 'd')", "'d'")]
        [InlineData("e = ET.Element('a')\ne.set('k','v')\nr = e.get('k')", "'v'")]
        [InlineData("e = ET.Element('a')\ne.attrib['m'] = 'n'\nr = e.get('m')", "'n'")]
        [InlineData("r = len(ET.Element('a'))", "0")]
        [InlineData("r = ET.iselement(ET.Element('a'))", "True")]
        [InlineData("r = ET.iselement('x')", "False")]
        [InlineData("p = ET.Element('p')\nc = ET.SubElement(p, 'c')\nr = (len(p), p[0] is c)", "(1, True)")]
        [InlineData("r = sorted(ET.Element('a', {'b':'2','a':'1'}).keys())", "['a', 'b']")]
        // ---- fromstring ----
        [InlineData("r = ET.fromstring('<a>hi</a>').text", "'hi'")]
        [InlineData("r = ET.fromstring('<a/>').text", "None")]
        [InlineData("r = ET.fromstring('<a><b/></a>')[0].tag", "'b'")]
        [InlineData("e = ET.fromstring('<a>x<b/>y</a>')\nr = (e.text, e[0].tail)", "('x', 'y')")]
        [InlineData("r = ET.fromstring('<a><![CDATA[x]]>y</a>').text", "'xy'")]
        [InlineData("e = ET.fromstring('<a>x<b/>y<c/>z</a>')\nr = (e.text, e[0].tail, e[1].tail)", "('x', 'y', 'z')")]
        [InlineData("r = ET.fromstring('<a b=\"1\" c=\"2\"/>').get('b')", "'1'")]
        [InlineData("r = ET.fromstring('<ns:a xmlns:ns=\"http://x\"/>').tag", "'{http://x}a'")]
        [InlineData("r = ET.fromstring('<a>&amp;</a>').text", "'&'")]
        [InlineData("r = len(ET.fromstring('<a><b/><c/><d/></a>'))", "3")]
        // ---- tostring ----
        [InlineData("r = ET.tostring(ET.Element('a'), 'unicode')", "'<a />'")]
        [InlineData("e = ET.Element('a')\ne.text = 'hi'\nr = ET.tostring(e, 'unicode')", "'<a>hi</a>'")]
        [InlineData("r = ET.tostring(ET.Element('a', {'b':'2','a':'1'}), 'unicode')", "'<a b=\"2\" a=\"1\" />'")]
        [InlineData("e = ET.Element('a')\ne.text = 'x&y'\nr = ET.tostring(e, 'unicode')", "'<a>x&amp;y</a>'")]
        [InlineData("e = ET.Element('a')\ne.text = '<b>'\nr = ET.tostring(e, 'unicode')", "'<a>&lt;b&gt;</a>'")]
        [InlineData("r = ET.tostring(ET.Element('a', {'x':'a\"b'}), 'unicode')", "'<a x=\"a&quot;b\" />'")]
        [InlineData("p = ET.Element('p')\nc = ET.SubElement(p, 'c')\nc.text = 'y'\nr = ET.tostring(p, 'unicode')", "'<p><c>y</c></p>'")]
        [InlineData("p = ET.Element('p')\nc = ET.SubElement(p, 'c')\nc.tail = 'T'\nr = ET.tostring(p, 'unicode')", "'<p><c />T</p>'")]
        [InlineData("r = ET.tostring(ET.fromstring('<a><b/><c/></a>'), 'unicode')", "'<a><b /><c /></a>'")]
        [InlineData("r = ET.tostring(ET.fromstring('<a>x<b/>y</a>'), 'unicode')", "'<a>x<b />y</a>'")]
        public void Tree(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("ET.fromstring('<!DOCTYPE a><a/>')")]
        [InlineData("ET.fromstring('<a>&nbsp;</a>')")]
        [InlineData("ET.fromstring('<a>')")]
        [InlineData("ET.fromstring('<a></b>')")]
        public void ParseErrors(string body)
        {
            Assert.Equal("ParseError", Raises("x = " + body).Value.ExcType.Name);
        }

        // The default encoding is us-ascii and yields BYTES, as in CPython; non-ASCII becomes
        // character references. No XML declaration for us-ascii or utf-8 (CPython omits it for both).
        [Theory]
        [InlineData("r = ET.tostring(ET.Element('a'))", "b'<a />'")]
        [InlineData("r = ET.tostring(ET.Element('a'), 'us-ascii')", "b'<a />'")]
        [InlineData("r = ET.tostring(ET.Element('a'), 'utf-8')", "b'<a />'")]
        [InlineData("r = ET.tostring(ET.Element('a'), encoding='unicode')", "'<a />'")]
        [InlineData("e = ET.Element('u')\ne.text = '\\xe9\\u263a'\nr = ET.tostring(e)", "b'<u>&#233;&#9786;</u>'")]
        [InlineData("e = ET.Element('u')\ne.text = '\\xe9'\nr = ET.tostring(e, 'utf-8')", "b'<u>\\xc3\\xa9</u>'")]
        [InlineData("e = ET.Element('u')\ne.text = '\\xe9'\nr = ET.tostring(e, 'unicode') == '<u>\\xe9</u>'", "True")]
        [InlineData("r = ET.fromstring(ET.tostring(ET.fromstring('<a x=\"1\">t</a>'))).get('x')", "'1'")]
        public void TostringDefaultsToBytes(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        // Any other codec yields bytes WITH the XML declaration CPython writes for it, and a character
        // the codec cannot hold becomes a character reference rather than an error.
        [Theory]
        [InlineData("r = ET.tostring(ET.Element('a'), encoding='latin-1')", "b\"<?xml version='1.0' encoding='latin-1'?>\\n<a />\"")]
        [InlineData("e = ET.Element('u')\ne.text = '\\xe9'\nr = ET.tostring(e, 'cp1251') == b\"<?xml version='1.0' encoding='cp1251'?>\\n<u>&#233;</u>\"", "True")]
        [InlineData("e = ET.Element('u')\ne.text = '\\u0448'\nr = ET.tostring(e, 'cp1251')[-8:] == b'<u>\\xf8</u>'", "True")]
        public void TostringOtherCodecsDeclare(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Fact]
        public void TostringUnknownCodecRaises()
        {
            Assert.Equal("LookupError", Raises("x = ET.tostring(ET.Element('a'), encoding='no-such')").Value.ExcType.Name);
        }

        [Fact]
        public void RemoveByIdentity()
        {
            // Two equal-by-content but distinct b's: remove(b1) drops exactly the passed instance.
            string body = "a = ET.Element('a')\nb1 = ET.SubElement(a, 'b')\nb2 = ET.SubElement(a, 'b')\n"
                + "a.remove(b1)\nr = (len(a), a[0] is b2)\n";
            Assert.Equal("(1, True)", Body(body));
        }

        [Fact]
        public void ParseErrorIsCatchable()
        {
            string body = "caught = False\ntry:\n    ET.fromstring('<a>')\nexcept ET.ParseError:\n    caught = True\nr = caught\n";
            Assert.Equal("True", Body(body));
        }

        // itertext(): this element's text, then every descendant's text and tail, in document order.
        [Theory]
        [InlineData("x = ET.fromstring('<a>t<b>u</b>v<c>w</c>x</a>')\nr = list(x.itertext())", "['t', 'u', 'v', 'w', 'x']")]
        [InlineData("x = ET.fromstring('<a>1<b>2<c>3</c>4</b>5</a>')\nr = ''.join(x.itertext())", "'12345'")]
        [InlineData("x = ET.fromstring('<a>t<b>u</b>v</a>')\nr = list(x.find('b').itertext())", "['u']")]
        [InlineData("r = list(ET.Element('n').itertext())", "[]")]
        [InlineData("x = ET.fromstring('<a><b/></a>')\nr = list(x.itertext())", "[]")]
        public void IterText(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        // A comment or a processing instruction is an Element whose TAG is the factory itself, which is
        // how CPython marks them: they never match a tag filter and their text is written verbatim.
        private const string Nodes = "r = ET.Element('r')\n"
            + "r.append(ET.Comment(' note '))\n"
            + "r.append(ET.ProcessingInstruction('php', 'echo'))\n"
            + "ET.SubElement(r, 'x').text = 'v'\n";

        [Theory]
        [InlineData(Nodes + "r = ET.tostring(r, encoding='unicode')", "'<r><!-- note --><?php echo?><x>v</x></r>'")]
        [InlineData(Nodes + "r = (r[0].tag is ET.Comment, r[1].tag is ET.ProcessingInstruction)", "(True, True)")]
        [InlineData(Nodes + "r = [e.tag for e in r.findall('x')]", "['x']")]
        [InlineData(Nodes + "r = len(list(r.iter()))", "4")]
        [InlineData(Nodes + "r = ''.join(r.itertext())", "'v'")]
        [InlineData("c = ET.Comment('t')\nr = (c.text, len(c), c.attrib)", "('t', 0, {})")]
        [InlineData("r = ET.tostring(ET.Comment('a<b'), encoding='unicode')", "'<!--a<b-->'")]
        [InlineData("r = ET.tostring(ET.ProcessingInstruction('t'), encoding='unicode')", "'<?t?>'")]
        [InlineData("x = ET.Element('a')\nr = ''.join(ET.tostringlist(x, encoding='unicode'))", "'<a />'")]
        public void CommentAndProcessingInstruction(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Fact]
        public void DumpWritesToThePrintSink()
        {
            var run = Ev.Run(P + "ET.dump(ET.fromstring('<a><b/></a>'))\n");
            Assert.Equal("<a><b /></a>\n", run.PrintOutput);
        }

        [Fact]
        public void NamespacedRoundTripCanonic()
        {
            // ns prefix generation: round-trip preserves the Clark tag.
            string body = "x = ET.fromstring('<ns:a xmlns:ns=\"http://x\"><ns:b/></ns:a>')\n"
                + "s = ET.tostring(x)\ny = ET.fromstring(s)\nr = (y.tag, y[0].tag)\n";
            Assert.Equal("('{http://x}a', '{http://x}b')", Body(body));
        }
    }
}

using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // The XPath predicates, the serializer methods and the parse-error surface (closed 2026-09-11).
    // Three of these were not gaps but wrong answers: tostring accepted method= and
    // short_empty_elements= and dropped them, and an unreadable path answered with an empty match set.
    [Trait("Category", "Regression")]
    public class XmlDepthTests
    {
        private const string P = "import xml.etree.ElementTree as ET\nfrom io import StringIO\n"
            + "DOC = '<r><i k=\"a\"><n>x</n></i><i k=\"b\"><n>y</n></i><i><n>z</n></i></r>'\n"
            + "r = ET.fromstring(DOC)\n";

        private static string R(string expr)
        {
            return Ev.ReprOf(P, expr);
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        // ---- XPath ----

        [Theory]
        // '..' resolves through a parent map built from the element the search started at
        [InlineData("r.find('.//n/..').get('k')", "'a'")]
        [InlineData("[e.get('k') for e in r.findall('.//n/..')]", "['a', 'b', None]")]
        [InlineData("r.find('..')", "None")]                            // above the start: not there
        [InlineData("r.find('i/..').tag", "'r'")]
        // [@a!='v'] is elem.get(a) != v, so an element WITHOUT the attribute is selected
        [InlineData("[e.get('k') for e in r.findall(\"i[@k!='a']\")]", "['b', None]")]
        [InlineData("[e.get('k') for e in r.findall(\"i[@k!='zz']\")]", "['a', 'b', None]")]
        // [tag!='text'] needs such a child to exist, which is CPython's asymmetry with the form above
        [InlineData("[e.get('k') for e in r.findall(\"i[n!='x']\")]", "['b', None]")]
        [InlineData("[e.get('k') for e in r.findall(\"i[n!='zz']\")]", "['a', 'b', None]")]
        [InlineData("r.findall(\"i[q!='x']\")", "[]")]                   // no such child: nothing selected
        // the text compared is the COMPLETE content, as CPython's ''.join(e.itertext()) is
        [InlineData("ET.fromstring('<r><i><n>a<b>c</b></n></i></r>').findall(\"i[n='ac']\") != []", "True")]
        [InlineData("ET.fromstring('<r><i><n>a<b>c</b></n></i></r>').findall(\"i[n='a']\")", "[]")]
        [InlineData("ET.fromstring('<a>x<b>y</b>z</a>').findall(\".[.='xyz']\") != []", "True")]
        // the rest of the subset is unchanged
        [InlineData("[e.get('k') for e in r.findall('i')]", "['a', 'b', None]")]
        [InlineData("[e.get('k') for e in r.findall(\"i[@k]\")]", "['a', 'b']")]
        [InlineData("[e.get('k') for e in r.findall(\"i[@k='b']\")]", "['b']")]
        [InlineData("[e.get('k') for e in r.findall('i[1]')]", "['a']")]
        [InlineData("[e.get('k') for e in r.findall('i[last()]')]", "[None]")]
        [InlineData("len(r.findall('.//n'))", "3")]
        public void XPathPredicates(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        // A path the grammar has no reading for raises, where an empty match set would read as
        // "nothing found" and hide the typo.
        [Theory]
        [InlineData("res = r.findall('i[@]')", "invalid predicate: [@]")]
        [InlineData("res = r.findall('i[]')", "invalid predicate: []")]
        [InlineData("res = r.findall(\"i[='x']\")", "invalid predicate: [='x']")]
        [InlineData("res = r.findall('i/text()')", "unsupported path syntax: text()")]
        [InlineData("res = r.findall('i[position()=1]')", "unsupported predicate: [position()=1]")]
        public void AnUnreadablePathRaises(string body, string message)
        {
            var ex = Raises(body);
            Assert.Equal("SyntaxError", ex.Value.ExcType.Name);
            Assert.Equal(message, ex.Value.GetMessageText(Rt.Ctx()));
        }

        // ---- tostring: method and short_empty_elements ----

        [Theory]
        // method='text' writes no markup: the complete text content plus the element's own tail
        [InlineData("ET.tostring(ET.fromstring('<i><n>first</n><q>10</q></i>'), encoding='unicode', method='text')", "'first10'")]
        [InlineData("ET.tostring(ET.fromstring('<a>x<b>y</b>z</a>'), encoding='unicode', method='text')", "'xyz'")]
        [InlineData("ET.tostring(ET.fromstring('<a><b/></a>'), encoding='unicode', method='text')", "''")]
        [InlineData("ET.tostring(ET.fromstring('<i><n>first</n></i>'), method='text')", "b'first'")]
        // method='html': void elements open and never close, nothing self-closes, script/style stay raw
        [InlineData("ET.tostring(ET.Element('br'), encoding='unicode', method='html')", "'<br>'")]
        [InlineData("ET.tostring(ET.Element('img'), encoding='unicode', method='html')", "'<img>'")]
        [InlineData("ET.tostring(ET.Element('div'), encoding='unicode', method='html')", "'<div></div>'")]
        [InlineData("ET.tostring(ET.fromstring('<script>if (a&lt;b) {}</script>'), encoding='unicode', method='html')", "'<script>if (a<b) {}</script>'")]
        [InlineData("ET.tostring(ET.fromstring('<style>a &gt; b {}</style>'), encoding='unicode', method='html')", "'<style>a > b {}</style>'")]
        [InlineData("ET.tostring(ET.fromstring('<p>a&lt;b</p>'), encoding='unicode', method='html')", "'<p>a&lt;b</p>'")]
        [InlineData("ET.tostring(ET.fromstring('<p>x<br/></p>'), encoding='unicode', method='html')", "'<p>x<br></p>'")]
        // method='xml' is the default and unchanged
        [InlineData("ET.tostring(ET.Element('br'), encoding='unicode')", "'<br />'")]
        [InlineData("ET.tostring(ET.Element('br'), encoding='unicode', method='xml')", "'<br />'")]
        // short_empty_elements
        [InlineData("ET.tostring(ET.Element('a'), encoding='unicode', short_empty_elements=False)", "'<a></a>'")]
        [InlineData("ET.tostring(ET.Element('a'), encoding='unicode', short_empty_elements=True)", "'<a />'")]
        [InlineData("ET.tostring(ET.fromstring('<a><b/><c/></a>'), encoding='unicode', short_empty_elements=False)", "'<a><b></b><c></c></a>'")]
        // write() takes the same two
        [InlineData("(lambda o: (ET.ElementTree(ET.fromstring('<a>t</a>')).write(o, encoding='unicode', method='text'), o.getvalue())[1])(StringIO())", "'t'")]
        [InlineData("(lambda o: (ET.ElementTree(ET.Element('a')).write(o, encoding='unicode', short_empty_elements=False), o.getvalue())[1])(StringIO())", "'<a></a>'")]
        public void SerializeMethods(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        // An option that is accepted and dropped is a wrong document with no signal, so every keyword
        // is read and an unknown one is refused.
        [Theory]
        [InlineData("res = ET.tostring(r, nosuch=1)", "TypeError")]
        [InlineData("res = ET.tostring(r, method='nope')", "ValueError")]
        [InlineData("res = ET.tostring(r, default_namespace='urn:x')", "TypeError")]
        public void UnknownSerializerOptionsAreRefused(string body, string exc)
        {
            Assert.Equal(exc, Raises(body).Value.ExcType.Name);
        }

        // ---- ParseError, tree.parse, XMLID ----

        [Fact]
        public void ParseErrorCarriesItsPosition()
        {
            var res = Ev.Run(P
                + "def err(s):\n    try:\n        ET.fromstring(s)\n    except ET.ParseError as e:\n        return e\n"
                + "res = (err('<a>').position, err('<!DOCTYPE a [<!ENTITY e \"x\">]><a>&e;</a>').position)\n");
            Assert.Equal("((1, 0), (1, 0))", res.Repr("res"));
        }

        // The reader's own sentence advises setting an XmlReaderSettings property, which is addressed to
        // whoever embeds the engine rather than to the script.
        [Fact]
        public void TheDtdRefusalReadsForAScriptAuthor()
        {
            var ex = Raises("res = ET.fromstring('<!DOCTYPE a [<!ENTITY e \"x\">]><a>&e;</a>')");
            Assert.Equal("ParseError", ex.Value.ExcType.Name);
            Assert.Equal("DTD is prohibited in this environment: line 1, column 0", ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Theory]
        [InlineData("ET.ElementTree().parse(StringIO('<a><b/></a>')).tag", "'a'")]
        [InlineData("(lambda t: (t.parse(StringIO('<a/>')), t.getroot().tag)[1])(ET.ElementTree())", "'a'")]
        [InlineData("(lambda r2: (r2[0].tag, sorted(r2[1])))(ET.XMLID('<a><b id=\"q\"/><c id=\"w\"/></a>'))", "('a', ['q', 'w'])")]
        [InlineData("ET.XMLID('<a><b id=\"q\"/></a>')[1]['q'].tag", "'b'")]
        [InlineData("ET.XMLID('<a><b/></a>')[1]", "{}")]
        [InlineData("ET.XMLID('<a id=\"top\"><b id=\"q\"/></a>')[1]['top'].tag", "'a'")]
        public void TreeParseAndXmlId(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }
    }
}

using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // Three absences integration code kept running into, closed 2026-09-11: Decimal in statistics, the
    // csv dialect registry and field limit, and the list/source-shaped entry points of xml.etree.
    [Trait("Category", "Compatibility")]
    public class IntegrationGapTests
    {
        private static string R(string prelude, string expr)
        {
            return Ev.ReprOf(prelude, expr);
        }

        private static ScriptException Raises(string prelude, string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(prelude + body + "\n"));
        }

        // ---- statistics over Decimal ----
        // A Decimal IS a rational, so the exact-Fraction summation carries it without losing a digit and
        // the answer converts back with T(numerator)/T(denominator), as CPython's _convert does.

        private const string S = "import statistics\nfrom decimal import Decimal as D\nfrom fractions import Fraction\n";

        [Theory]
        [InlineData("statistics.mean([D(1), D(2)])", "Decimal('1.5')")]
        [InlineData("statistics.mean([D('19.99'), D('20.01')])", "Decimal('20')")]
        [InlineData("statistics.mean([D('0.1'), D('0.2'), D('0.3')])", "Decimal('0.2')")]
        [InlineData("statistics.mean([D(1), 2])", "Decimal('1.5')")]                       // int mixes in
        [InlineData("statistics.mean([D(1), D(3)])", "Decimal('2')")]
        [InlineData("statistics.median([D('1.5'), D('2.5'), D('3.5')])", "Decimal('2.5')")]
        [InlineData("statistics.median([D(1), D(2)])", "Decimal('1.5')")]
        [InlineData("statistics.median_low([D(1), D(2)])", "Decimal('1')")]
        [InlineData("statistics.mode([D(1), D(1), D(2)])", "Decimal('1')")]
        [InlineData("statistics.variance([D(1), D(2), D(3)])", "Decimal('1')")]
        [InlineData("statistics.stdev([D(1), D(2), D(3)])", "Decimal('1')")]
        [InlineData("type(statistics.stdev([D(1), D(2), D(3)])).__name__", "'Decimal'")]
        [InlineData("statistics.quantiles([D(1), D(2), D(3), D(4)])", "[Decimal('1.25'), Decimal('2.5'), Decimal('3.75')]")]
        [InlineData("statistics.fmean([D(1), D(2)])", "1.5")]                               // float, as in CPython
        // the exact summation is the point: a float mean would lose the 1 between the two 1e10s
        [InlineData("statistics.mean([D('1e10'), D('1'), D('-1e10')]) * 3", "Decimal('0.9999999999999999999999999999')")]
        // the other input types are untouched
        [InlineData("statistics.mean([1, 2, 3])", "2")]
        [InlineData("statistics.mean([1.0, 2.0])", "1.5")]
        [InlineData("statistics.mean([Fraction(1, 2), Fraction(1, 2)])", "Fraction(1, 2)")]
        public void StatisticsTakesDecimal(string expr, string expected)
        {
            Assert.Equal(expected, R(S, expr));
        }

        // CPython's _coerce: int mixes with anything, Fraction with float, Decimal with neither.
        [Theory]
        [InlineData("statistics.mean([D(1), 2.0])", "don't know how to coerce float and Decimal")]
        [InlineData("statistics.mean([2.0, D(1)])", "don't know how to coerce float and Decimal")]
        [InlineData("statistics.mean([D(1), Fraction(1, 2)])", "don't know how to coerce Fraction and Decimal")]
        public void DecimalDoesNotMixWithFloatOrFraction(string expr, string message)
        {
            var ex = Raises(S, "r = " + expr);
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
            Assert.Equal(message, ex.Value.GetMessageText(Rt.Ctx()));
        }

        // ---- csv: the registry and the field limit, both per RUN ----

        private const string C = "import csv\nfrom io import StringIO\n";

        [Theory]
        [InlineData("csv.field_size_limit()", "131072")]
        [InlineData("(csv.field_size_limit(20), csv.field_size_limit())", "(131072, 20)")]   // returns the OLD one
        [InlineData("(csv.register_dialect('semi', delimiter=';'), list(csv.reader(['a;b'], 'semi')))[1]", "[['a', 'b']]")]
        [InlineData("(csv.register_dialect('semi', delimiter=';'), list(csv.reader(['a;b'], dialect='semi')))[1]", "[['a', 'b']]")]
        [InlineData("(csv.register_dialect('semi', delimiter=';'), 'semi' in csv.list_dialects())[1]", "True")]
        [InlineData("(csv.register_dialect('semi', delimiter=';'), csv.get_dialect('semi').delimiter)[1]", "';'")]
        // the base may be a name or a dialect object, and fmtparams override it
        [InlineData("(csv.register_dialect('t', 'excel', delimiter='\\t'), list(csv.reader(['a\\tb'], 't')))[1]", "[['a', 'b']]")]
        [InlineData("(csv.register_dialect('a', delimiter=';'), csv.register_dialect('b', csv.get_dialect('a'), delimiter=':'), list(csv.reader(['x:y'], 'b')))[2]", "[['x', 'y']]")]
        [InlineData("(csv.register_dialect('s', delimiter=';'), list(csv.reader(['a|b'], 's', delimiter='|')))[1]", "[['a', 'b']]")]
        // registering over a preset shadows it, as it does in CPython's single dict
        [InlineData("(csv.register_dialect('excel', delimiter='#'), list(csv.reader(['a#b'], 'excel')))[1]", "[['a', 'b']]")]
        [InlineData("(csv.register_dialect('s', delimiter=';'), csv.unregister_dialect('s'), 's' in csv.list_dialects())[2]", "False")]
        [InlineData("sorted(n for n in csv.list_dialects() if n in ('excel', 'excel-tab', 'unix'))", "['excel', 'excel-tab', 'unix']")]
        [InlineData("(csv.register_dialect('q', delimiter=';', quoting=csv.QUOTE_ALL), (lambda o: (csv.writer(o, 'q').writerow(['x', 'y']), o.getvalue())[1])(StringIO()))[1]", "'\"x\";\"y\"\\r\\n'")]
        [InlineData("(csv.register_dialect('d', delimiter=';'), list(csv.DictReader(['a;b', '1;2'], dialect='d')))[1]", "[{'a': '1', 'b': '2'}]")]
        public void CsvRegistryAndLimit(string expr, string expected)
        {
            Assert.Equal(expected, R(C, expr));
        }

        [Fact]
        public void FieldLimitStopsARunawayField()
        {
            var ex = Raises(C, "csv.field_size_limit(20)\nr = list(csv.reader(['a' * 50 + ',b']))");
            Assert.Equal("Error", ex.Value.ExcType.Name);
            Assert.Equal("field larger than field limit (20)", ex.Value.GetMessageText(Rt.Ctx()));
            // and the same document reads fine at the default
            Assert.Equal("'b'", R(C, "list(csv.reader(['a' * 50 + ',b']))[0][1]"));
        }

        [Theory]
        [InlineData("r = csv.get_dialect('nope')", "Error")]
        [InlineData("r = csv.unregister_dialect('nope')", "Error")]
        [InlineData("r = list(csv.reader(['a;b'], 'never-registered'))", "Error")]
        [InlineData("r = csv.register_dialect('x', nosuch=1)", "TypeError")]
        [InlineData("r = csv.register_dialect(1)", "TypeError")]
        [InlineData("r = csv.field_size_limit('x')", "TypeError")]
        public void CsvRegistryErrors(string body, string exc)
        {
            Assert.Equal(exc, Raises(C, body).Value.ExcType.Name);
        }

        // The registry is per RUN: the module dictionary is frozen and shared, so a dialect one script
        // registers must not be visible to the next.
        [Fact]
        public void TheRegistryDoesNotLeakBetweenRuns()
        {
            Assert.Equal("[['a', 'b']]",
                Ev.Run(C + "csv.register_dialect('leaky', delimiter=';')\nr = list(csv.reader(['a;b'], 'leaky'))\n").Repr("r"));
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(C + "r = list(csv.reader(['a;b'], 'leaky'))\n"));
            Assert.Equal("unknown dialect", ex.Value.GetMessageText(Rt.Ctx()));
            Assert.Equal("131072", Ev.Run(C + "r = csv.field_size_limit()\n").Repr("r"));
        }

        // ---- xml.etree: QName, makeelement, fromstringlist, parse, iterparse ----

        private const string X = "import xml.etree.ElementTree as ET\nfrom io import StringIO\n"
            + "DOC = '<order id=\"7\"><line sku=\"A\">10</line><line sku=\"B\">20</line></order>'\n";

        [Theory]
        [InlineData("str(ET.QName('tag'))", "'tag'")]
        [InlineData("str(ET.QName('urn:x', 'tag'))", "'{urn:x}tag'")]
        [InlineData("ET.QName('urn:x', 'tag').text", "'{urn:x}tag'")]
        [InlineData("repr(ET.QName('urn:x', 't'))", "\"<QName '{urn:x}t'>\"")]
        // it carries the text, so it compares, hashes and orders as that text does
        [InlineData("ET.QName('a') == 'a'", "True")]
        [InlineData("'a' == ET.QName('a')", "True")]
        [InlineData("ET.QName('a') == ET.QName('a')", "True")]
        [InlineData("ET.QName('a') == ET.QName('b')", "False")]
        [InlineData("{ET.QName('a'): 1}['a']", "1")]
        [InlineData("ET.QName('a') in {'a'}", "True")]
        [InlineData("ET.QName('a') < ET.QName('b')", "True")]
        // used as a tag or an attribute name it is stored as its text
        [InlineData("ET.Element(ET.QName('urn:x', 't')).tag", "'{urn:x}t'")]
        [InlineData("ET.tostring(ET.Element(ET.QName('urn:x', 't')), encoding='unicode')", "'<ns0:t xmlns:ns0=\"urn:x\" />'")]
        public void XmlQName(string expr, string expected)
        {
            Assert.Equal(expected, R(X, expr));
        }

        [Theory]
        [InlineData("ET.tostring(ET.fromstringlist(['<a>', '<b/>', '</a>']), encoding='unicode')", "'<a><b /></a>'")]
        [InlineData("ET.fromstringlist(x for x in ['<a/>']).tag", "'a'")]
        [InlineData("ET.parse(StringIO(DOC)).getroot().get('id')", "'7'")]
        [InlineData("type(ET.parse(StringIO(DOC))).__name__", "'ElementTree'")]
        [InlineData("ET.parse(StringIO(DOC)).getroot().find('line').get('sku')", "'A'")]
        [InlineData("ET.parse(StringIO(DOC)).findall('line')[1].text", "'20'")]
        [InlineData("ET.Element('x').makeelement('y', {'k': 'v'}).get('k')", "'v'")]
        [InlineData("len(ET.Element('x').makeelement('y', {}))", "0")]                  // not attached
        public void XmlListAndSourceEntryPoints(string expr, string expected)
        {
            Assert.Equal(expected, R(X, expr));
        }

        [Theory]
        [InlineData("[(e, el.tag) for e, el in ET.iterparse(StringIO(DOC))]", "[('end', 'line'), ('end', 'line'), ('end', 'order')]")]
        [InlineData("[(e, el.tag) for e, el in ET.iterparse(StringIO(DOC), ['start'])]", "[('start', 'order'), ('start', 'line'), ('start', 'line')]")]
        [InlineData("[e for e, el in ET.iterparse(StringIO(DOC), events=['start', 'end'])]", "['start', 'start', 'end', 'start', 'end', 'end']")]
        [InlineData("[el.get('sku') for e, el in ET.iterparse(StringIO(DOC)) if el.tag == 'line']", "['A', 'B']")]
        [InlineData("len(list(ET.iterparse(StringIO('<a>' + '<b>' * 50 + '</b>' * 50 + '</a>'))))", "51")]
        public void XmlIterParse(string expr, string expected)
        {
            Assert.Equal(expected, R(X, expr));
        }

        // ---- json: dump/load, object_hook, parse_constant, skipkeys ----

        private const string J = "import json\nfrom io import StringIO\n";

        [Theory]
        // dump/load over any object with write()/read(); there are no files, io.StringIO is the sink
        [InlineData("(lambda o: (json.dump({'a': [1, 2]}, o), o.getvalue())[1])(StringIO())", "'{\"a\": [1, 2]}'")]
        [InlineData("(lambda o: (json.dump({'a': 1}, o, indent=2), o.getvalue())[1])(StringIO())", "'{\\n  \"a\": 1\\n}'")]
        [InlineData("(lambda o: (json.dump({'b': 1, 'a': 2}, o, sort_keys=True), o.getvalue())[1])(StringIO())", "'{\"a\": 2, \"b\": 1}'")]
        [InlineData("json.dump(1, StringIO())", "None")]
        [InlineData("json.load(StringIO('{\"a\": [1, 2]}'))", "{'a': [1, 2]}")]
        [InlineData("json.load(StringIO('{\"a\":1}'), object_hook=lambda d: list(d.items()))", "[('a', 1)]")]
        [InlineData("json.load(StringIO('1.5'), parse_float=str)", "'1.5'")]
        // object_hook replaces each finished dict, innermost first, and never sees an array
        [InlineData("json.loads('{\"a\":1}', object_hook=lambda d: list(d.items()))", "[('a', 1)]")]
        [InlineData("json.loads('{\"a\":{\"b\":1}}', object_hook=lambda d: ('H', sorted(d.items())))", "('H', [('a', ('H', [('b', 1)]))])")]
        [InlineData("json.loads('[{\"a\":1},{\"b\":2}]', object_hook=lambda d: len(d))", "[1, 1]")]
        [InlineData("json.loads('{\"a\":[1,2]}', object_hook=lambda d: d['a'])", "[1, 2]")]
        [InlineData("json.loads('{\"z\":1,\"a\":2}', object_hook=lambda d: list(d))", "['z', 'a']")]   // insertion order
        [InlineData("json.loads('{\"x\":{\"y\":{\"z\":1}}}', object_hook=dict)", "{'x': {'y': {'z': 1}}}")]
        [InlineData("json.loads('{\"a\":{\"b\":1}}')", "{'a': {'b': 1}}")]                              // unchanged without it
        // parse_constant sees the three literals by their CPython names, and nothing else
        [InlineData("json.loads('NaN', parse_constant=str)", "'NaN'")]
        [InlineData("json.loads('[Infinity, -Infinity]', parse_constant=str)", "['Infinity', '-Infinity']")]
        [InlineData("json.loads('[1.5, NaN]', parse_constant=lambda n: 'C')", "[1.5, 'C']")]
        [InlineData("str(json.loads('[NaN, Infinity]'))", "'[nan, inf]'")]                              // default stands
        // skipkeys drops a key JSON cannot spell instead of raising
        [InlineData("json.dumps({(1, 2): 'v', 'ok': 1}, skipkeys=True)", "'{\"ok\": 1}'")]
        [InlineData("json.dumps({(1, 2): 'v'}, skipkeys=True)", "'{}'")]
        [InlineData("json.dumps({'a': {(1,): 1, 'b': 2}}, skipkeys=True)", "'{\"a\": {\"b\": 2}}'")]
        [InlineData("json.dumps({(1,): 0, 'b': 1, 'a': 2}, skipkeys=True, sort_keys=True)", "'{\"a\": 2, \"b\": 1}'")]
        [InlineData("json.dumps({1: 'a', None: 'b', 2.5: 'd'}, skipkeys=True)", "'{\"1\": \"a\", \"null\": \"b\", \"2.5\": \"d\"}'")]
        public void JsonFileHooksAndSkipKeys(string expr, string expected)
        {
            Assert.Equal(expected, R(J, expr));
        }

        [Theory]
        [InlineData("r = json.dumps({(1, 2): 'v'})", "TypeError")]              // skipkeys is still off by default
        [InlineData("r = json.load(StringIO('{,}'))", "JSONDecodeError")]
        [InlineData("r = json.load(StringIO('1'), 2)", "TypeError")]
        [InlineData("r = json.dump(1)", "TypeError")]
        [InlineData("r = json.load(1)", "AttributeError")]                      // no read()
        [InlineData("r = json.dump(1, 2)", "AttributeError")]                   // no write()
        [InlineData("r = json.loads('{}', object_pairs_hook=list)", "TypeError")]   // still absent
        [InlineData("r = json.load(StringIO('{}'), cls=1)", "TypeError")]
        public void JsonFileAndHookErrors(string body, string exc)
        {
            Assert.Equal(exc, Raises(J, body).Value.ExcType.Name);
        }

        // The hook's own error surfaces instead of being swallowed.
        [Fact]
        public void AHookErrorSurfaces()
        {
            Assert.Equal("ZeroDivisionError", Raises(J, "r = json.loads('[NaN]', parse_constant=lambda n: 1 / 0)").Value.ExcType.Name);
            Assert.Equal("KeyError", Raises(J, "r = json.loads('{\"a\":1}', object_hook=lambda d: d['zz'])").Value.ExcType.Name);
        }

        // CPython's signature is fromstring(text, parser=None): passing None explicitly was a TypeError
        // here while fromstringlist and parse took it, which was an inconsistency and not a rule.
        [Theory]
        [InlineData("ET.fromstring('<a/>').tag", "'a'")]
        [InlineData("ET.fromstring('<a/>', None).tag", "'a'")]
        [InlineData("ET.fromstring('<a/>', parser=None).tag", "'a'")]
        public void FromStringTakesTheParserSlot(string expr, string expected)
        {
            Assert.Equal(expected, R(X, expr));
        }

        [Theory]
        // there is no filesystem, so a filename says so rather than being parsed as a document
        [InlineData("r = ET.parse('file.xml')", "TypeError")]
        [InlineData("r = ET.fromstring('<a/>', 1)", "TypeError")]               // a real parser is still refused
        [InlineData("r = ET.fromstring('<a/>', None, None)", "TypeError")]
        [InlineData("r = ET.parse(StringIO(DOC), 1)", "TypeError")]
        [InlineData("r = ET.fromstringlist([1])", "TypeError")]
        [InlineData("r = list(ET.iterparse(StringIO(DOC), ['nope']))", "ValueError")]
        [InlineData("r = list(ET.iterparse(StringIO(DOC), ['start-ns']))", "ValueError")]
        [InlineData("r = ET.QName(1)", "TypeError")]
        public void XmlSourceErrors(string body, string exc)
        {
            Assert.Equal(exc, Raises(X, body).Value.ExcType.Name);
        }
    }
}

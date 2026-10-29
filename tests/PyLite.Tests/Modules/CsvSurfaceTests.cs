using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // The reader/writer object surface: what a script can read back off one, and the two return values
    // CPython added in 3.5 and 3.8 (closed 2026-09-11).
    [Trait("Category", "Compatibility")]
    public class CsvSurfaceTests
    {
        private const string P = "import csv\nfrom io import StringIO\n";

        private static string R(string expr)
        {
            return Ev.ReprOf(P, expr);
        }

        // writerow returns whatever the sink's write() returned, which is how a caller counts what it
        // wrote; writerows stays None, as it is in CPython.
        [Theory]
        [InlineData("(lambda b: csv.writer(b).writerow(['a']))(StringIO())", "3")]                  // 'a\r\n'
        [InlineData("(lambda b: csv.writer(b).writerow(['ab']))(StringIO())", "4")]
        [InlineData("(lambda b: csv.writer(b, lineterminator='\\n').writerow(['a']))(StringIO())", "2")]
        [InlineData("(lambda b: csv.writer(b).writerows([['a'], ['b']]))(StringIO())", "None")]
        [InlineData("(lambda b: csv.DictWriter(b, ['a']).writeheader())(StringIO())", "3")]
        [InlineData("(lambda b: csv.DictWriter(b, ['a']).writerow({'a': 1}))(StringIO())", "3")]
        public void WriterowReturnsTheSinksAnswer(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("type(csv.DictWriter(StringIO(), ['a'])).__name__", "'DictWriter'")]
        [InlineData("type(csv.writer(StringIO())).__name__", "'writer'")]
        [InlineData("type(csv.reader(['a'])).__name__", "'reader'")]
        [InlineData("type(csv.DictReader(['a'])).__name__", "'DictReader'")]
        public void EachObjectReportsItsOwnType(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        // What a script can read back off a reader or a writer.
        [Theory]
        [InlineData("csv.writer(StringIO(), delimiter=';').dialect.delimiter", "';'")]
        [InlineData("csv.reader(['a'], delimiter=';').dialect.delimiter", "';'")]
        [InlineData("csv.DictWriter(StringIO(), ['a', 'b']).fieldnames", "['a', 'b']")]
        [InlineData("csv.DictWriter(StringIO(), ['a']).restval", "''")]
        [InlineData("csv.DictWriter(StringIO(), ['a'], restval='?').restval", "'?'")]
        [InlineData("csv.DictWriter(StringIO(), ['a']).extrasaction", "'raise'")]
        [InlineData("csv.DictWriter(StringIO(), ['a'], extrasaction='ignore').extrasaction", "'ignore'")]
        [InlineData("csv.DictReader(['a'], restkey='x').restkey", "'x'")]
        [InlineData("csv.DictReader(['a']).restkey", "None")]
        [InlineData("csv.DictReader(['a'], restval='?').restval", "'?'")]
        [InlineData("csv.DictReader(['a']).restval", "None")]
        [InlineData("csv.DictReader(['a'], delimiter=';').dialect.delimiter", "';'")]
        [InlineData("csv.DictReader(['a,b', '1,2']).fieldnames", "['a', 'b']")]
        [InlineData("type(csv.DictReader(['a']).reader).__name__", "'reader'")]
        [InlineData("csv.__version__", "'1.0'")]
        // line_num counts SOURCE lines, on the dict reader as on the plain one
        [InlineData("(lambda r2: ([x for x in r2], r2.line_num)[1])(csv.DictReader(['a', '1', '2']))", "3")]
        [InlineData("(lambda r2: ([x for x in r2], r2.line_num)[1])(csv.reader(['a', 'b']))", "2")]
        [InlineData("csv.DictReader(['a']).line_num", "0")]
        public void ObjectAttributes(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        // A plain writer has none of the three a DictWriter carries, as in CPython.
        [Theory]
        [InlineData("fieldnames")]
        [InlineData("restval")]
        [InlineData("extrasaction")]
        public void APlainWriterHasNoDictAttributes(string name)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(P + "r = csv.writer(StringIO())." + name + "\n"));
            Assert.Equal("AttributeError", ex.Value.ExcType.Name);
        }

        // Two guesses of mine that the probe refuted, kept as tests so they are not "fixed" again:
        // the Sniffer picks a delimiter from a one-character sample exactly as CPython's frequency
        // analysis does, and QUOTE_NONNUMERIC quotes None, which is why 3.12 added QUOTE_NOTNULL.
        [Theory]
        [InlineData("csv.Sniffer().sniff('zzzz').delimiter", "'z'")]
        [InlineData("(lambda b: (csv.writer(b, quoting=csv.QUOTE_NONNUMERIC, lineterminator='\\n').writerow(['a', 1, 2.5, None]), b.getvalue())[1])(StringIO())", "'\"a\",1,2.5,\"\"\\n'")]
        [InlineData("(lambda b: (csv.writer(b, lineterminator='\\n').writerow([1, None]), b.getvalue())[1])(StringIO())", "'1,\\n'")]
        [InlineData("(lambda b: (csv.writer(b, quoting=csv.QUOTE_ALL, lineterminator='\\n').writerow([1, None]), b.getvalue())[1])(StringIO())", "'\"1\",\"\"\\n'")]
        public void TheSnifferAndNoneQuotingAreParity(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        // ---- QUOTE_STRINGS / QUOTE_NOTNULL (3.12) ----
        // Writing: STRINGS quotes only real strings, NOTNULL quotes everything but None, and both write
        // a None as an empty UNQUOTED field. Reading: both read an empty unquoted field back as None,
        // and past that STRINGS behaves as NONNUMERIC while NOTNULL behaves as ALL.

        private const string Row = "['a', 1, 2.5, None, '']";

        [Theory]
        [InlineData("QUOTE_MINIMAL", "'a,1,2.5,,\\n'")]
        [InlineData("QUOTE_ALL", "'\"a\",\"1\",\"2.5\",\"\",\"\"\\n'")]
        [InlineData("QUOTE_NONNUMERIC", "'\"a\",1,2.5,\"\",\"\"\\n'")]
        [InlineData("QUOTE_STRINGS", "'\"a\",1,2.5,,\"\"\\n'")]
        [InlineData("QUOTE_NOTNULL", "'\"a\",\"1\",\"2.5\",,\"\"\\n'")]
        public void QuotingModesOnWrite(string mode, string expected)
        {
            Assert.Equal(expected, R("(lambda b: (csv.writer(b, lineterminator='\\n', quoting=csv."
                + mode + ").writerow(" + Row + "), b.getvalue())[1])(StringIO())"));
        }

        [Theory]
        [InlineData("QUOTE_MINIMAL", "[['a', '1', '', '2.5']]")]
        [InlineData("QUOTE_NONNUMERIC", "[['a', 1.0, '', 2.5]]")]
        [InlineData("QUOTE_STRINGS", "[['a', 1.0, None, 2.5]]")]
        [InlineData("QUOTE_NOTNULL", "[['a', '1', None, '2.5']]")]
        public void QuotingModesOnRead(string mode, string expected)
        {
            Assert.Equal(expected, R("list(csv.reader(['\"a\",1,,2.5'], quoting=csv." + mode + "))"));
        }

        [Theory]
        [InlineData("csv.QUOTE_STRINGS", "4")]
        [InlineData("csv.QUOTE_NOTNULL", "5")]
        // an empty QUOTED field is a string, not a None: only an unquoted one becomes None
        [InlineData("list(csv.reader(['\"\",a'], quoting=csv.QUOTE_NOTNULL))", "[['', 'a']]")]
        public void TheNewConstants(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        // The value is validated now: outside the range it was silently QUOTE_MINIMAL.
        [Theory]
        [InlineData("list(csv.reader(['a'], quoting=99))")]
        [InlineData("list(csv.reader(['a'], quoting=-1))")]
        [InlineData("list(csv.reader(['a'], quoting=6))")]
        [InlineData("csv.writer(StringIO(), quoting=99)")]
        public void ABadQuotingValueIsRefused(string body)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(P + "r = " + body + "\n"));
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
            Assert.Equal("bad \"quoting\" value", ex.Value.GetMessageText(Rt.Ctx()));
        }

        // ---- a dialect is anything shaped like one ----
        // CPython's dialect_init reads the eight attributes off whatever object it is handed and clears a
        // missing one, so csv.Dialect there is a carrier rather than a requirement. A record class with
        // the fields set in __init__ is therefore a dialect here, without any subclassing.

        private const string Mine =
            "class Mine:\n"
            + "    def __init__(self):\n"
            + "        self.delimiter = ';'\n"
            + "        self.quoting = csv.QUOTE_ALL\n"
            + "        self.lineterminator = '\\n'\n"
            + "class Partial:\n"
            + "    def __init__(self):\n"
            + "        self.delimiter = '|'\n";

        [Theory]
        [InlineData("list(csv.reader(['a;b'], Mine()))", "[['a', 'b']]")]
        [InlineData("(lambda b: (csv.writer(b, Mine()).writerow(['x', 'y']), b.getvalue())[1])(StringIO())", "'\"x\";\"y\"\\n'")]
        [InlineData("list(csv.reader(['a|b'], Partial()))", "[['a', 'b']]")]                  // the rest default
        [InlineData("list(csv.reader(['a:b'], Mine(), delimiter=':'))", "[['a', 'b']]")]      // fmtparams still win
        [InlineData("list(csv.DictReader(['a;b', '1;2'], dialect=Mine()))", "[{'a': '1', 'b': '2'}]")]
        [InlineData("(csv.register_dialect('m', Mine()), csv.get_dialect('m').delimiter)[1]", "';'")]
        [InlineData("(csv.register_dialect('m', Mine()), list(csv.reader(['a;b'], 'm')))[1]", "[['a', 'b']]")]
        // an object carrying none of the eight is the default dialect, which is CPython's answer too
        [InlineData("list(csv.reader(['a,b'], 42))", "[['a', 'b']]")]
        public void AnyObjectShapedLikeADialectServes(string expr, string expected)
        {
            Assert.Equal(expected, Ev.Run(P + Mine + "r = (" + expr + ")\n").Repr("r"));
        }

        [Fact]
        public void ADuckDialectStillValidatesWhatItCarries()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(P
                + "class Bad:\n    def __init__(self):\n        self.quoting = 42\n"
                + "r = list(csv.reader(['a'], Bad()))\n"));
            Assert.Equal("bad \"quoting\" value", ex.Value.GetMessageText(Rt.Ctx()));
            // a NAME is still looked up in the registry, so a typo is an unknown dialect, not a duck
            var miss = Assert.Throws<ScriptException>(() => Ev.Run(P + "r = list(csv.reader(['a'], 'nope'))\n"));
            Assert.Equal("unknown dialect", miss.Value.GetMessageText(Rt.Ctx()));
        }

        [Theory]
        [InlineData("''")]
        [InlineData("'\\n\\n'")]
        public void AnEmptySampleCannotBeSniffed(string sample)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(P + "r = csv.Sniffer().sniff(" + sample + ")\n"));
            Assert.Equal("Error", ex.Value.ExcType.Name);
            Assert.Equal("Could not determine delimiter", ex.Value.GetMessageText(Rt.Ctx()));
        }
    }
}

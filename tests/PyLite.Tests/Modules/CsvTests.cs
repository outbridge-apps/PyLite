using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // v2 C11: io.StringIO + csv reader/writer/DictReader/DictWriter.
    [Trait("Category", "Compatibility")]
    public class CsvTests
    {
        private const string P = "import csv\nfrom io import StringIO\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        // ---- StringIO ----
        [InlineData("s = StringIO()\ns.write('ab')\ns.write('cd')\nr = s.getvalue()", "'abcd'")]
        [InlineData("s = StringIO('hello')\nr = s.read()", "'hello'")]
        [InlineData("s = StringIO('hello')\nr = (s.read(2), s.read())", "('he', 'llo')")]
        [InlineData("s = StringIO('a\\nb\\nc')\nr = (s.readline(), s.readline())", "('a\\n', 'b\\n')")]
        [InlineData("s = StringIO('a\\nb\\n')\nr = s.readlines()", "['a\\n', 'b\\n']")]
        [InlineData("s = StringIO('xyz')\ns.read()\ns.seek(0)\nr = s.read(1)", "'x'")]
        [InlineData("s = StringIO('abc')\ns.seek(1)\ns.write('X')\nr = s.getvalue()", "'aXc'")]
        [InlineData("s = StringIO('a\\nb')\nr = [line for line in s]", "['a\\n', 'b']")]
        [InlineData("s = StringIO('abc')\nr = (s.tell(), s.read(2), s.tell())", "(0, 'ab', 2)")]
        // ---- csv.reader ----
        [InlineData("r = list(csv.reader(['a,b,c']))", "[['a', 'b', 'c']]")]
        [InlineData("r = list(csv.reader(['a,b', 'c,d']))", "[['a', 'b'], ['c', 'd']]")]
        [InlineData("r = list(csv.reader(['\"a,x\",b']))", "[['a,x', 'b']]")]
        [InlineData("r = list(csv.reader(['\"say \"\"hi\"\"\",b']))", "[['say \"hi\"', 'b']]")]
        [InlineData("r = list(csv.reader(['a;b'], delimiter=';'))", "[['a', 'b']]")]
        [InlineData("r = list(csv.reader(['a,,c']))", "[['a', '', 'c']]")]
        [InlineData("r = list(csv.reader(['a,b,']))", "[['a', 'b', '']]")]
        [InlineData("r = list(csv.reader([',']))", "[['', '']]")]
        [InlineData("r = list(csv.reader(['']))", "[[]]")]
        [InlineData("r = list(csv.reader(['\"multi', 'line\",b']))", "[['multi\\nline', 'b']]")]
        [InlineData("r = list(csv.reader(StringIO('a,b\\nc,d\\n')))", "[['a', 'b'], ['c', 'd']]")]
        // ---- csv.writer ----
        [InlineData("s = StringIO()\nw = csv.writer(s)\nw.writerow(['a', 'b'])\nr = s.getvalue()", "'a,b\\r\\n'")]
        [InlineData("s = StringIO()\nw = csv.writer(s, lineterminator='\\n')\nw.writerows([[1, 2], [3, 4]])\nr = s.getvalue()", "'1,2\\n3,4\\n'")]
        [InlineData("s = StringIO()\nw = csv.writer(s, lineterminator='\\n')\nw.writerow(['a,x', 'b'])\nr = s.getvalue()", "'\"a,x\",b\\n'")]
        [InlineData("s = StringIO()\nw = csv.writer(s, lineterminator='\\n')\nw.writerow(['say \"hi\"'])\nr = s.getvalue()", "'\"say \"\"hi\"\"\"\\n'")]
        [InlineData("s = StringIO()\nw = csv.writer(s, lineterminator='\\n', quoting=csv.QUOTE_ALL)\nw.writerow(['a', 1])\nr = s.getvalue()", "'\"a\",\"1\"\\n'")]
        [InlineData("s = StringIO()\nw = csv.writer(s, lineterminator='\\n', quoting=csv.QUOTE_NONNUMERIC)\nw.writerow(['a', 1, 2.5])\nr = s.getvalue()", "'\"a\",1,2.5\\n'")]
        [InlineData("s = StringIO()\nw = csv.writer(s, lineterminator='\\n')\nw.writerow(['x', None])\nr = s.getvalue()", "'x,\\n'")]
        // ---- round trip ----
        [InlineData("s = StringIO()\nw = csv.writer(s, lineterminator='\\n')\nw.writerow(['a,1', 'say \"x\"', 'plain'])\nr = list(csv.reader(StringIO(s.getvalue())))", "[['a,1', 'say \"x\"', 'plain']]")]
        // ---- DictReader / DictWriter ----
        [InlineData("rows = list(csv.DictReader(['name,age', 'bob,30', 'eve,25']))\nr = (rows[0]['name'], rows[1]['age'])", "('bob', '25')")]
        [InlineData("rows = list(csv.DictReader(['bob,30'], fieldnames=['name', 'age']))\nr = rows[0]['name']", "'bob'")]
        [InlineData("rows = list(csv.DictReader(['a,b', '1']))\nr = rows[0]['b']", "None")]
        [InlineData("s = StringIO()\nw = csv.DictWriter(s, ['a', 'b'], lineterminator='\\n')\nw.writeheader()\nw.writerow({'a': 1, 'b': 2})\nr = s.getvalue()", "'a,b\\n1,2\\n'")]
        [InlineData("s = StringIO()\nw = csv.DictWriter(s, ['a', 'b'], lineterminator='\\n')\nw.writerow({'a': 1})\nr = s.getvalue()", "'1,\\n'")]
        public void CsvAndStringIO(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("x = list(csv.reader([1, 2]))", "Error")]
        [InlineData("x = list(csv.reader(['\"unterminated']))", "Error")]
        [InlineData("s = StringIO()\nw = csv.writer(s, quoting=csv.QUOTE_NONE)\nw.writerow(['a,b'])", "Error")]
        [InlineData("s = StringIO()\nw = csv.DictWriter(s, ['a'])\nw.writerow({'a': 1, 'zzz': 2})", "ValueError")]
        public void Errors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Fact]
        public void ReaderErrorIsCatchable()
        {
            string src = P + "caught = False\ntry:\n    list(csv.reader(['\"open']))\nexcept csv.Error:\n    caught = True\nr = caught\n";
            Assert.Equal("True", Ev.Run(src).Repr("r"));
        }

        // ---- the reader/writer options that used to be accepted and dropped ----

        [Theory]
        [InlineData("r = list(csv.reader(['a, b,  c'], skipinitialspace=True))", "[['a', 'b', 'c']]")]
        [InlineData("r = list(csv.reader(['a, b']))", "[['a', ' b']]")]
        [InlineData("r = list(csv.reader(['a\\\\,b,c'], escapechar='\\\\'))", "[['a,b', 'c']]")]
        [InlineData("r = list(csv.reader(['\"a\\\\\"b\",c'], escapechar='\\\\'))", "[['a\"b', 'c']]")]
        [InlineData("r = list(csv.reader(['\"a\"\"b\"']))", "[['a\"b']]")]
        [InlineData("r = list(csv.reader(['\"a\\\\\"b\"'], doublequote=False, escapechar='\\\\'))", "[['a\"b']]")]
        [InlineData("r = list(csv.reader(['1,\"x\",2.5'], quoting=csv.QUOTE_NONNUMERIC))", "[[1.0, 'x', 2.5]]")]
        [InlineData("r = list(csv.reader(['-0.0,1_0'], quoting=csv.QUOTE_NONNUMERIC))", "[[-0.0, 10.0]]")]   // float()'s grammar and sign
        [InlineData("r = list(csv.reader([',1'], quoting=csv.QUOTE_NONNUMERIC))", "[['', 1.0]]")]
        [InlineData("r = list(csv.reader(['\"a\"b']))", "[['ab']]")]                     // lenient by default
        [InlineData("r = list(csv.reader(['a\"b'], strict=True))", "[['a\"b']]")]        // literal mid-field quote
        [InlineData("r = list(csv.reader(['\"a\",b'], quoting=csv.QUOTE_NONE))", "[['\"a\"', 'b']]")]
        [InlineData("r = list(csv.reader(['\"a', 'b\"']))", "[['a\\nb']]")]              // still spans lines
        public void ReaderOptions(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("r = list(csv.DictReader(['a,b', '1,2,3'], restkey='rest'))", "[{'a': '1', 'b': '2', 'rest': ['3']}]")]
        [InlineData("r = list(csv.DictReader(['a,b', '1,2,3']))", "[{'a': '1', 'b': '2', None: ['3']}]")]
        [InlineData("r = list(csv.DictReader(['a,b', '1'], restval='-'))", "[{'a': '1', 'b': '-'}]")]
        [InlineData("r = list(csv.DictReader(['a,b', '1']))", "[{'a': '1', 'b': None}]")]
        [InlineData("r = csv.DictReader(['a,b', '1,2']).fieldnames", "['a', 'b']")]
        [InlineData("r = csv.DictReader([]).fieldnames", "None")]
        [InlineData("r = list(csv.DictReader(['1,2'], fieldnames=['x', 'y']))", "[{'x': '1', 'y': '2'}]")]
        public void DictReaderOptions(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("s = StringIO()\ncsv.writer(s, quoting=csv.QUOTE_NONE, escapechar='\\\\').writerow(['a,b'])\nr = s.getvalue()",
            "'a\\\\,b\\r\\n'")]
        [InlineData("s = StringIO()\ncsv.writer(s, doublequote=False, escapechar='\\\\').writerow(['a\"b'])\nr = s.getvalue()",
            "'\"a\\\\\"b\"\\r\\n'")]
        [InlineData("s = StringIO()\ncsv.DictWriter(s, ['a'], extrasaction='ignore').writerow({'a': 1, 'b': 2})\nr = s.getvalue()",
            "'1\\r\\n'")]
        [InlineData("s = StringIO()\ncsv.DictWriter(s, ['a', 'b'], restval='-').writerow({'a': 1})\nr = s.getvalue()",
            "'1,-\\r\\n'")]
        [InlineData("s = StringIO()\ncsv.DictWriter(s, fieldnames=['a', 'b']).writeheader()\nr = s.getvalue()",
            "'a,b\\r\\n'")]
        public void WriterOptions(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("x = list(csv.reader(['\"a\"b'], strict=True))", "Error")]
        [InlineData("x = list(csv.reader(['abc'], quoting=csv.QUOTE_NONNUMERIC))", "ValueError")]
        [InlineData("x = list(csv.reader(['a'], nope=1))", "TypeError")]                  // refused, not ignored
        [InlineData("s = StringIO()\nx = csv.DictWriter(s, ['a'], extrasaction='nope')", "ValueError")]
        [InlineData("s = StringIO()\nx = csv.DictWriter(s)", "TypeError")]
        [InlineData("s = StringIO()\nx = csv.writer(s, escapechar='xx')", "TypeError")]
        public void OptionErrors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Theory]
        [InlineData("r = list(csv.reader(['a,b'], dialect='excel'))", "[['a', 'b']]")]
        [InlineData("r = list(csv.reader(['a,b'], 'excel'))", "[['a', 'b']]")]              // dialect is positional too
        [InlineData("r = list(csv.reader(['a\\tb'], dialect='excel-tab'))", "[['a', 'b']]")]
        [InlineData("r = list(csv.reader(['a;b'], dialect='excel', delimiter=';'))", "[['a', 'b']]")]   // fmtparam wins
        [InlineData("r = list(csv.DictReader(['a,b', '1,2'], dialect='unix'))", "[{'a': '1', 'b': '2'}]")]
        [InlineData("s = StringIO()\ncsv.writer(s, dialect='unix').writerow(['a', 'b'])\nr = s.getvalue()",
            "'\"a\",\"b\"\\n'")]                                                            // unix = QUOTE_ALL + LF
        [InlineData("s = StringIO()\ncsv.writer(s, 'excel-tab').writerow(['a', 'b'])\nr = s.getvalue()",
            "'a\\tb\\r\\n'")]
        [InlineData("s = StringIO()\ncsv.writer(s, dialect='unix', quoting=csv.QUOTE_MINIMAL).writerow(['a'])\nr = s.getvalue()",
            "'a\\n'")]                                                                      // preset kept, quoting overridden
        public void NamedDialects(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("x = list(csv.reader(['a'], dialect='nope'))", "Error")]
        [InlineData("x = list(csv.reader(['a'], 'excel', dialect='unix'))", "TypeError")]
        // dialect=1 is no longer refused: any object is read for the eight attributes and an object with
        // none of them is the default dialect, which is what CPython's dialect_init answers too. See
        // CsvSurfaceTests.AnyObjectShapedLikeADialectServes.
        [InlineData("x = list(csv.reader(['a'], 'excel', 'unix'))", "TypeError")]
        public void DialectErrors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        // reader.line_num counts SOURCE lines, so a row with an embedded newline advances it by more
        // than one, and reader.dialect is the dialect the reader settled on.
        [Theory]
        [InlineData("rd = csv.reader(StringIO('a,b\\nc,d\\n'))\nnext(rd)\nr = rd.line_num", "1")]
        [InlineData("rd = csv.reader(StringIO('a,b\\nc,d\\n'))\nnext(rd)\nnext(rd)\nr = rd.line_num", "2")]
        [InlineData("rd = csv.reader(StringIO('\"x\\ny\",2\\nc,d\\n'))\nnext(rd)\nr = rd.line_num", "2")]
        [InlineData("rd = csv.reader(StringIO('a\\n'))\nr = rd.line_num", "0")]
        [InlineData("rd = csv.reader(StringIO('a\\n'))\nr = rd.dialect.delimiter", "','")]
        [InlineData("rd = csv.reader(StringIO('a;b\\n'), delimiter=';')\nr = rd.dialect.delimiter", "';'")]
        public void ReaderLineNumAndDialect(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }
    }
}

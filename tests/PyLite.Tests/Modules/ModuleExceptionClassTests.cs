using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // A module's exception class is one object for the engine's lifetime, recorded on its exception type
    // (Support/ModuleKit): type(e) is the class, a script can construct and raise it, and every import
    // hands out the same class. Before D14, nine modules made a new class per import and type(e) was
    // never the class; zoneinfo's could not be constructed.
    [Trait("Category", "Compatibility")]
    public class ModuleExceptionClassTests
    {
        [Theory]
        [InlineData("re", "re.error", "re.compile('(')")]
        [InlineData("struct", "struct.error", "struct.pack('x', 1)")]
        [InlineData("zlib", "zlib.error", "zlib.decompress(b'xx')")]
        [InlineData("json", "json.JSONDecodeError", "json.loads('{')")]
        [InlineData("statistics", "statistics.StatisticsError", "statistics.mean([])")]
        [InlineData("binascii", "binascii.Error", "binascii.a2b_base64(b'a')")]
        [InlineData("base64, binascii", "binascii.Error", "base64.b16decode(b'0')")]
        [InlineData("gzip", "gzip.BadGzipFile", "gzip.decompress(b'xxxxxxxxxxxx')")]
        [InlineData("decimal", "decimal.InvalidOperation", "decimal.Decimal('x')")]
        [InlineData("zoneinfo", "zoneinfo.ZoneInfoNotFoundError", "zoneinfo.ZoneInfo('Nope/Nope')")]
        [InlineData("xml.etree.ElementTree as ET", "ET.ParseError", "ET.fromstring('<a')")]
        [InlineData("csv", "csv.Error", "next(csv.reader(['\"a'], strict=True))")]
        [InlineData("zipfile, io", "zipfile.BadZipFile", "zipfile.ZipFile(io.BytesIO(b'x'))")]
        [InlineData("calendar", "calendar.IllegalMonthError", "calendar.monthrange(2024, 13)")]
        public void TypeOfTheErrorIsTheModuleClass(string imports, string cls, string failing)
        {
            string src = "import " + imports + "\ntry:\n    " + failing + "\n    r = 'no error'\nexcept Exception as e:\n"
                + "    r = (type(e) is " + cls + ", isinstance(e, " + cls + "))\n";
            Assert.Equal("(True, True)", Outbridge.PyLite.Tests.Evaluator.Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void TheClassIsStableAndConstructible()
        {
            string src = "import re, zoneinfo\nimport re as again\n"
                + "e = zoneinfo.ZoneInfoNotFoundError('k')\n"
                + "r = (again.error is re.error, type(e) is zoneinfo.ZoneInfoNotFoundError, isinstance(e, KeyError))\n";
            Assert.Equal("(True, True, True)", Outbridge.PyLite.Tests.Evaluator.Ev.Run(src).Repr("r"));
        }
    }
}

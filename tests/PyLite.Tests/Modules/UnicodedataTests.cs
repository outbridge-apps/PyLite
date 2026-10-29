using System;
using System.Text;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // unicodedata: normalization forms, the name table both ways, and the character properties.
    // Results with non-ASCII text are compared inside the script so the reprs stay ASCII.
    [Trait("Category", "Compatibility")]
    public class UnicodedataTests
    {
        private const string Imp = "import unicodedata\nfrom unicodedata import normalize, is_normalized, name, lookup, category, numeric, decimal, digit, east_asian_width, combining, bidirectional, mirrored, decomposition\n";

        [Theory]
        [InlineData("normalize('NFD', '\\u00e9') == 'e\\u0301'", "True")]
        [InlineData("normalize('NFC', 'e\\u0301') == '\\u00e9'", "True")]
        [InlineData("normalize('NFKD', '\\ufb01')", "'fi'")]
        [InlineData("normalize('NFKC', '\\u2460')", "'1'")]
        [InlineData("normalize('NFC', 'plain ascii')", "'plain ascii'")]
        [InlineData("''.join([c for c in normalize('NFD', 'Cr\\u00e8me Br\\u00fbl\\u00e9e') if category(c) != 'Mn'])", "'Creme Brulee'")]
        [InlineData("normalize('NFC', 'a\\ud83db') == 'a\\ud83db'", "True")]
        [InlineData("normalize('NFD', '\\u00e9\\ud83d\\u00e9') == 'e\\u0301\\ud83de\\u0301'", "True")]
        [InlineData("(is_normalized('NFC', '\\u00e9'), is_normalized('NFD', '\\u00e9'), is_normalized('NFD', 'e\\u0301'))", "(True, False, True)")]
        [InlineData("name('\\u20ac')", "'EURO SIGN'")]
        [InlineData("name('\\U0001F600')", "'GRINNING FACE'")]
        [InlineData("name('\\uac00')", "'HANGUL SYLLABLE GA'")]
        [InlineData("name('\\x00', 'none')", "'none'")]
        [InlineData("lookup('EURO SIGN') == '\\u20ac'", "True")]
        [InlineData("lookup('latin small letter a')", "'a'")]
        [InlineData("lookup('GRINNING FACE') == '\\U0001F600'", "True")]
        [InlineData("[category(c) for c in 'aA1 $']", "['Ll', 'Lu', 'Nd', 'Zs', 'Sc']")]
        [InlineData("(category('\\u0301'), category('\\U0001F600'), category('\\ud83d'))", "('Mn', 'So', 'Cs')")]
        [InlineData("(numeric('\\u00bd'), numeric('\\u2460'), numeric('a', -1))", "(0.5, 1.0, -1)")]
        [InlineData("(decimal('7'), decimal('\\u0663'), decimal('a', None))", "(7, 3, None)")]
        [InlineData("(digit('\\u00b2'), digit('7'), digit('\\u00bd', -1))", "(2, 7, -1)")]
        [InlineData("unicodedata.unidata_version", "'16.0.0'")]
        [InlineData("(east_asian_width('a'), east_asian_width('\\u00c0'), east_asian_width('\\u00a1'), east_asian_width('\\u3042'), east_asian_width('\\uff21'), east_asian_width('\\uff61'), east_asian_width('\\U0001F600'))", "('Na', 'N', 'A', 'W', 'F', 'H', 'W')")]
        [InlineData("(east_asian_width('\\ud83d'), east_asian_width('\\U000E0100'), east_asian_width('\\U0010FFFD'), east_asian_width('\\U0010FFFF'))", "('N', 'A', 'A', 'N')")]
        [InlineData("sum([2 if east_asian_width(c) in ('W', 'F') else 1 for c in 'ab\\u3042\\u3044'])", "6")]
        [InlineData("(combining('a'), combining('\\u0301'), combining('\\u0327'), combining('\\u05b0'), combining('\\u3099'), combining('\\ud83d'))", "(0, 230, 202, 10, 8, 0)")]
        [InlineData("''.join([c for c in normalize('NFD', 'Cr\\u00e8me') if not combining(c)])", "'Creme'")]
        [InlineData("[bidirectional(c) for c in 'a\\u05d0\\u06271+$\\u0660,\\u0301\\u200b\\n\\t !']", "['L', 'R', 'AL', 'EN', 'ES', 'ET', 'AN', 'CS', 'NSM', 'BN', 'B', 'S', 'WS', 'ON']")]
        [InlineData("(bidirectional('\\u202a'), bidirectional('\\u2066'), bidirectional('\\U0001F600'), bidirectional('\\u4e00'), bidirectional('\\uac00'))", "('LRE', 'LRI', 'ON', 'L', 'L')")]
        [InlineData("(bidirectional('\\ud800'), bidirectional('\\U000E0000'), bidirectional('\\U0010FFFF'))", "('L', '', '')")]
        [InlineData("[mirrored(c) for c in '([<a\\u2208)\\ud83d']", "[1, 1, 1, 0, 1, 1, 0]")]
        [InlineData("(decomposition('\\u00c0'), decomposition('\\u00bd'), decomposition('\\ufb01'), decomposition('\\u2460'))", "('0041 0300', '<fraction> 0031 2044 0032', '<compat> 0066 0069', '<circle> 0031')")]
        [InlineData("(decomposition('a'), decomposition('\\uac00'), decomposition('\\ud83d'), decomposition('\\U0001F600'))", "('', '', '', '')")]
        [InlineData("decomposition('\\U0001D400')", "'<font> 0041'")]
        [InlineData("decomposition('\\ufffd')", "''")]
        public void Surface(string expr, string expected)
        {
            Assert.Equal(expected, Ev.Run(Imp + "r = (" + expr + ")\n").Repr("r"));
        }

        [Theory]
        [InlineData("normalize('NFX', 'a')", "ValueError")]
        [InlineData("normalize('NFC', 1)", "TypeError")]
        [InlineData("normalize(form='NFC', unistr='a')", "TypeError")]
        [InlineData("normalize('NFC')", "TypeError")]
        [InlineData("name('\\x00')", "ValueError")]
        [InlineData("name('ab')", "TypeError")]
        [InlineData("name(1)", "TypeError")]
        [InlineData("lookup('NO SUCH NAME')", "KeyError")]
        [InlineData("numeric('a')", "ValueError")]
        [InlineData("decimal('\\u00b2')", "ValueError")]
        [InlineData("digit('\\u00bd')", "ValueError")]
        [InlineData("east_asian_width('ab')", "TypeError")]
        [InlineData("east_asian_width()", "TypeError")]
        [InlineData("combining(1)", "TypeError")]
        [InlineData("bidirectional('ab')", "TypeError")]
        [InlineData("mirrored('')", "TypeError")]
        [InlineData("decomposition(b'a')", "TypeError")]
        public void Errors(string expr, string exc)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Imp + "r = " + expr + "\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
        }

        // A string longer than 4096 characters is normalized in chunks (one Normalize() call is quadratic
        // in its own length). The cut may only fall where it cannot change the answer, so each case puts a
        // composition right at the boundary and compares against a single call over the whole string.
        [Theory]
        [InlineData(4090, "09C7 09BE")]        // Bengali: the second half of the pair is a starter
        [InlineData(4095, "09C7 09BE")]        // ...and here the naive cut would land between the two
        [InlineData(4096, "09C7 09BE")]
        [InlineData(4095, "0DD9 0DCF")]        // Sinhala, same shape
        [InlineData(4095, "1100 1161 11A8")]   // Hangul jamo compose algorithmically
        [InlineData(4095, "0061 0301 0302")]   // a base and its marks stay together
        [InlineData(4095, "FB01 FDFA")]        // compatibility expansions
        [InlineData(4095, "D83D DE00")]        // an astral character: never split the surrogate pair
        public void ChunkedNormalizeMatchesOneCall(int filler, string hexCodes)
        {
            var middle = new StringBuilder();
            var expr = new StringBuilder("'x' * " + filler);
            foreach (string h in hexCodes.Split(' '))
            {
                middle.Append((char)Convert.ToInt32(h, 16));
                expr.Append(" + chr(0x" + h + ")");
            }
            expr.Append(" + 'y' * 5000");
            string whole = new string('x', filler) + middle + new string('y', 5000);
            foreach (string form in new[] { "NFC", "NFD", "NFKC", "NFKD" })
            {
                string got = ((StrValue)Ev.Run(Imp + "r = normalize('" + form + "', " + expr + ")\n").Get("r")).Value;
                Assert.Equal(whole.Normalize(FormOf(form)), got);
            }
        }

        private static NormalizationForm FormOf(string name)
        {
            switch (name)
            {
                case "NFC": return NormalizationForm.FormC;
                case "NFD": return NormalizationForm.FormD;
                case "NFKC": return NormalizationForm.FormKC;
                default: return NormalizationForm.FormKD;
            }
        }

        // Marks never offer a break point, and reordering thousands of them is quadratic in the BCL, so a
        // run that long is refused instead of run (a divergence: CPython normalizes it).
        [Fact]
        public void CombiningRunTooLongToBreakIsRefused()
        {
            var ex = Assert.Throws<ScriptException>(
                () => Ev.Run(Imp + "r = normalize('NFC', 'a' + chr(0x301) * 20000)\n"));
            Assert.Equal("ValueError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void CombiningRunWithinOneChunkStillNormalizes()
        {
            string got = ((StrValue)Ev.Run(Imp + "r = normalize('NFC', 'a' + chr(0x301) * 4000)\n").Get("r")).Value;
            Assert.Equal(("a" + new string(Convert.ToChar(0x301), 4000)).Normalize(NormalizationForm.FormC), got);
        }
    }
}

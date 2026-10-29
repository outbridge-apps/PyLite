using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    // The name table is read through a lazily built index: a page directory over the sorted code points,
    // the owning block per record, and the packed bytes unpacked once. A wrong page bound or block start
    // would answer with a NEIGHBOUR's name rather than fail, so the guard is a round trip over the whole
    // table: every code point that has a name resolves back to itself through lookup().
    [Trait("Category", "Compatibility")]
    public class UnicodeNameTableTests
    {
        [Fact]
        public void EveryNamedCodePointRoundTrips()
        {
            int named = 0;
            for (int cp = 0; cp <= 0x10FFFF; cp++)
            {
                if (cp >= 0xD800 && cp <= 0xDFFF)
                    continue;
                string name;
                if (!PyUnicodeNames.TryName(cp, out name))
                    continue;
                named++;
                string value;
                Assert.True(PyUnicodeNames.TryLookup(name, out value), name);
                Assert.Equal(char.ConvertFromUtf32(cp), value);
            }
            Assert.True(named > 140000, "expected the rule-named families too, got " + named);
        }

        [Theory]
        [InlineData(0x0041, "LATIN CAPITAL LETTER A")]
        [InlineData(0x00E9, "LATIN SMALL LETTER E WITH ACUTE")]
        [InlineData(0x20AC, "EURO SIGN")]
        [InlineData(0x0416, "CYRILLIC CAPITAL LETTER ZHE")]
        [InlineData(0x1F600, "GRINNING FACE")]
        [InlineData(0xAC00, "HANGUL SYLLABLE GA")]
        [InlineData(0x4E2D, "CJK UNIFIED IDEOGRAPH-4E2D")]
        [InlineData(0x10FFFD, null)]
        public void KnownNames(int cp, string expected)
        {
            string name;
            bool ok = PyUnicodeNames.TryName(cp, out name);
            Assert.Equal(expected != null, ok);
            if (expected != null)
                Assert.Equal(expected, name);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0x110000)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void OutOfRangeIsNotNamed(int cp)
        {
            string name;
            Assert.False(PyUnicodeNames.TryName(cp, out name));
        }

        // The first and last code point of a page exercise the directory's bounds from both sides.
        [Theory]
        [InlineData(0x0100)]
        [InlineData(0x01FF)]
        [InlineData(0x2000)]
        [InlineData(0x20C0)]
        [InlineData(0xFF01)]
        [InlineData(0xFFFD)]
        public void PageEdgesResolve(int cp)
        {
            string name;
            Assert.True(PyUnicodeNames.TryName(cp, out name));
            string value;
            Assert.True(PyUnicodeNames.TryLookup(name, out value));
            Assert.Equal(char.ConvertFromUtf32(cp), value);
        }
    }
}

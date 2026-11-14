using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // The second sandbox review (2026-10), memory: outputs built in full before their size was checked (quote,
    // re.escape, XML escaping, normalize, bytes.hex, encode, base64, a csv field), parts collected uncharged
    // (textwrap's chunks), children added past the item cap, and walks that built every
    // match first (ElementTree find/iterfind/itertext over shared subtrees). Each now stops on the limit it
    // would cross, or no longer builds the set, as CPython's generators do not.
    [Trait("Category", "ResourceLimits")]
    public class BoundedOutputsTests
    {
        private static readonly ResourceLimits SmallStr = TestEngines.Unhurried.With(maxStrChars: 1_000_000);
        private static readonly ResourceLimits SmallAlloc = TestEngines.Unhurried.With(maxAllocBytes: 16L << 20);

        [Theory]
        [InlineData("import urllib.parse\nurllib.parse.quote('\\x00' * 400000)\n")]
        [InlineData("import re\nre.escape('.' * 600000)\n")]
        [InlineData("import xml.etree.ElementTree as ET\nET.tostring(ET.Element('a', b='\"' * 200000))\n")]
        [InlineData("import xml.etree.ElementTree as ET\ne = ET.Element('a')\ne.text = '&' * 250000\nET.tostring(e)\n")]
        [InlineData("import unicodedata\nunicodedata.normalize('NFKD', '\\ufdfa' * 100000)\n")]
        [InlineData("(b'a' * 400000).hex(':')\n")]
        [InlineData("('a' * 600000).encode('utf-16')\n")]
        [InlineData("('\\u20ac' * 400000).encode('utf-8')\n")]
        [InlineData("('a' * 500000).replace('a', 'bbb')\n")]
        [InlineData("import binascii\nbinascii.hexlify(b'a' * 600000)\n")]
        [InlineData("import binascii\nbinascii.b2a_base64(b'a' * 800000)\n")]
        [InlineData("import base64\nbase64.b64encode(b'a' * 800000)\n")]
        [InlineData("import base64\nbase64.b32encode(b'a' * 700000)\n")]
        [InlineData("import base64\nbase64.b16encode(b'a' * 600000)\n")]
        [InlineData("import csv\ncsv.field_size_limit(10**9)\nnext(csv.reader(['\"'] + ['x' * 400000] * 4))\n")]
        public void AnOutputPastTheStrCapIsRefused(string src)
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(src, SmallStr));
            Assert.Equal("MaxStrChars", ex.LimitName);
        }

        // The pieces cut from one string were held uncharged until the result was made, or never charged
        // (parse_qs and capwords no longer cut them at all: they stream into the result).
        [Theory]
        [InlineData("import textwrap\ntextwrap.shorten('a ' * 1000000, 10)\n")]
        [InlineData("import textwrap\ntextwrap.wrap('a ' * 1000000, 10, break_on_hyphens=False)\n")]
        public void PartsAreChargedAsTheyAreCut(string src)
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(src, SmallAlloc));
            Assert.Equal("MaxAllocBytes", ex.LimitName);
        }

        [Theory]
        [InlineData("import xml.etree.ElementTree as ET\nr = ET.Element('r')\nfor i in range(1100):\n    ET.SubElement(r, 'a')\n")]
        [InlineData("import xml.etree.ElementTree as ET\nET.fromstring('<r>' + '<a/>' * 1100 + '</r>')\n")]
        public void ChildrenStopAtTheItemCap(string src)
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(src, ResourceLimits.Default.With(maxCollectionItems: 1000)));
            Assert.Equal("MaxCollectionItems", ex.LimitName);
        }

        // find('*/*') over 3000 shared copies of a 3000-child element built all 9M pairs before the first.
        [Theory]
        [InlineData("x = R.find('*/*').tag\n")]
        [InlineData("x = next(R.iterfind('*/*')).tag\n")]
        [InlineData("x = R.findtext('*/*')\n")]
        [InlineData("x = next(R.itertext())\n")]
        [InlineData("x = next(R.iter('b')).tag\n")]
        public void AWalkOverSharedSubtreesStopsAtTheFirstMatch(string tail)
        {
            const string dag = "import xml.etree.ElementTree as ET\nR = ET.Element('r')\nA = ET.Element('a')\nB = ET.Element('b')\n"
                + "B.text = 'b'\nA.extend([B] * 3000)\nR.extend([A] * 3000)\n";
            Assert.Equal("'b'", Ev.Run(dag + tail, ResourceLimits.Default.With(maxSteps: 100_000)).Repr("x"));
        }

        // A walk keeps a frame per level, not every pending sibling: 600 shared copies of a 600-child element
        // held 1199 entries, past a 1000-item cap that CPython's walk never meets.
        [Fact]
        public void AWalkHoldsTheDepthNotTheSiblings()
        {
            const string src = "import xml.etree.ElementTree as ET\na = ET.Element('a')\na.text = 't'\nr = ET.Element('r')\n"
                + "r.extend([a] * 600)\ns = ET.Element('s')\ns.extend([r] * 600)\n"
                + "n = (sum(1 for t in s.itertext()), sum(1 for e in s.iter()), sum(1 for e in s.iterfind('.//a')))\n";
            Assert.Equal("(360000, 360601, 360000)", Ev.Run(src, ResourceLimits.Default.With(maxCollectionItems: 1000)).Repr("n"));
        }

        // factorial(10**7) multiplied until the deadline; Stirling's estimate refuses it before the first product.
        [Fact]
        public void AFactorialPastTheIntCapIsRefusedUpFront()
        {
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run("import math\nmath.factorial(10**7)\n", ResourceLimits.Default.With(maxSteps: 10_000)));
            Assert.Equal("MaxIntBits", ex.LimitName);
        }
    }
}

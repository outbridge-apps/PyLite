using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modern
{
    // Small parity closures: format_map over any mapping, SyntaxError as a nameable class.
    [Trait("Category", "Compatibility")]
    public class QuickParityTests
    {
        [Theory]
        [InlineData("r = '{a}-{b}'.format_map({'a': 1, 'b': 2})", "'1-2'")]
        [InlineData("from collections import defaultdict\nd = defaultdict(lambda: 'x')\nr = '{a}{b}'.format_map(d)", "'xx'")]
        [InlineData("from collections import ChainMap\nr = '{a}{b}'.format_map(ChainMap({'a': 1}, {'b': 2}))", "'12'")]
        [InlineData("from collections import Counter\nr = '{a}'.format_map(Counter('aab'))", "'2'")]
        [InlineData("from collections import OrderedDict\nr = '{a}'.format_map(OrderedDict(a=5))", "'5'")]
        [InlineData("class P:\n    x: int\n    def __init__(self, x):\n        self.x = x\nr = '{k.x}'.format_map({'k': P(3)})", "'3'")]
        public void FormatMapTakesAnyMapping(string body, string expected)
        {
            Assert.Equal(expected, Ev.Run(body + "\n").Repr("r"));
        }

        [Theory]
        [InlineData("'{a}'.format_map({})", "KeyError")]
        [InlineData("'{a}'.format_map(3)", "TypeError")]
        [InlineData("'{a}'.format_map({}, {})", "TypeError")]
        public void FormatMapErrors(string expr, string exc)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("r = " + expr + "\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
        }

        // translate / maketrans walk by code point, so an astral character is one key, not two units.
        [Theory]
        [InlineData("r = '\\U0001f600'.translate({0x1f600: 'X'})", "'X'")]
        [InlineData("r = 'a\\U0001f600b'.translate({0x1f600: None})", "'ab'")]
        [InlineData("r = 'a\\U0001f600b'.translate({0x1f600: 0x1f601}) == 'a\\U0001f601b'", "True")]
        [InlineData("r = 'ab'.translate(str.maketrans('ab', '\\U0001f600\\U0001f601')) == '\\U0001f600\\U0001f601'", "True")]
        [InlineData("r = '\\U0001f600x'.translate(str.maketrans('\\U0001f600', 'y'))", "'yx'")]
        [InlineData("r = '\\U0001f600x'.translate(str.maketrans('', '', '\\U0001f600'))", "'x'")]
        [InlineData("r = 'ab'.translate(str.maketrans({'\\U0001f600': 'z', 'a': 'A'}))", "'Ab'")]
        [InlineData("from collections import defaultdict\nr = 'ab'.translate(defaultdict(lambda: 'z'))", "'zz'")]   // any mapping
        [InlineData("r = 'abc'.translate(['X', 'Y'])", "'abc'")]                                                  // IndexError keeps
        public void TranslateByCodePoint(string body, string expected)
        {
            Assert.Equal(expected, Ev.Run(body + "\n").Repr("r"));
        }

        [Theory]
        [InlineData("str.maketrans('a', '\\U0001f600\\U0001f601')", "ValueError")]        // 1 vs 2 code points
        [InlineData("str.maketrans({'\\U0001f600\\U0001f601': 'x'})", "ValueError")]
        public void MaketransCountsCodePoints(string expr, string exc)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("r = " + expr + "\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
        }

        // SyntaxError is a class a script can name and catch; nothing in the engine raises it itself
        // (compile errors happen before the script runs), but ParseError descends from it as in CPython.
        [Theory]
        [InlineData("try:\n    raise SyntaxError('x')\nexcept SyntaxError as e:\n    r = str(e)", "'x'")]
        [InlineData("try:\n    raise ValueError('v')\nexcept (ValueError, SyntaxError) as e:\n    r = type(e).__name__", "'ValueError'")]
        [InlineData("import xml.etree.ElementTree as ET\ntry:\n    ET.fromstring('<a')\nexcept SyntaxError as e:\n    r = type(e).__name__", "'ParseError'")]
        [InlineData("r = issubclass(SyntaxError, Exception)", "True")]
        public void SyntaxErrorIsNameable(string body, string expected)
        {
            Assert.Equal(expected, Ev.Run(body + "\n").Repr("r"));
        }
    }
}

using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // An int argument past int range, by CPython's two policies. Where CPython's module is C, the
    // parameter goes through PyLong_AsSsize_t / PyLong_AsInt and a value past the range is an
    // OverflowError (Coerce.ToSsize / ToCInt); where the module is pure Python, a huge value is just a
    // number and the call proceeds (Coerce.ToInt32's clamp). re's group index is "no such group" and
    // bisect's lo past the end appends, as list.insert does. Ten sites used to cast unchecked and reached
    // the host as an EngineFault; found by the audit after the R1-R9 review.
    [Trait("Category", "Compatibility")]
    public class CIntArgumentTests
    {
        [Theory]
        [InlineData("textwrap.wrap('x y', width=10**30)", "['x y']")]
        [InlineData("textwrap.fill('x y', 10**30)", "'x y'")]
        [InlineData("textwrap.shorten('x y', 10**30)", "'x y'")]
        [InlineData("textwrap.wrap('x y', width=5, max_lines=10**30)", "['x y']")]
        [InlineData("textwrap.wrap('x y', width=-10**30)", "ValueError")]
        [InlineData("textwrap.wrap('x\\ty', tabsize=10**30)", "OverflowError")]
        [InlineData("re.match('a', 'a')[10**30]", "IndexError")]
        [InlineData("re.match('a', 'a')[-10**30]", "IndexError")]
        [InlineData("re.match('a', 'a').group(10**30)", "IndexError")]
        [InlineData("re.match('a', 'a').start(10**30)", "IndexError")]
        [InlineData("'%*d' % (10**30, 1)", "OverflowError")]
        [InlineData("'%*d' % (-10**30, 1)", "OverflowError")]
        [InlineData("'%.*f' % (10**30, 1.0)", "OverflowError")]
        [InlineData("'%*d' % (5, 1)", "'    1'")]
        [InlineData("urllib.parse.parse_qs('a=1', max_num_fields=10**30)", "{'a': ['1']}")]
        [InlineData("urllib.parse.parse_qsl('a=1', max_num_fields=-10**30)", "ValueError")]
        [InlineData("ET.indent(ET.Element('a'), level=-10**30)", "ValueError")]
        [InlineData("itertools.tee([1], 10**30)", "OverflowError")]
        [InlineData("itertools.tee([1], -10**30)", "ValueError")]
        [InlineData("itertools.batched([1], 10**30)", "OverflowError")]
        [InlineData("b'x'.hex(' ', 10**30)", "OverflowError")]
        [InlineData("b'x'.hex(' ', -10**30)", "OverflowError")]
        [InlineData("b'xy'.hex(' ', 1)", "'78 79'")]
        [InlineData("random.randbytes(10**30)", "OverflowError")]
        [InlineData("random.randbytes(-10**30)", "ValueError")]
        [InlineData("(lambda l: (bisect.insort(l, 2, lo=10**30), l)[1])([1])", "[1, 2]")]
        [InlineData("(lambda l: (bisect.insort(l, 0, lo=2**40), l)[1])([1])", "[1, 0]")]
        public void AnIntPastTheCRangeAnswersAsCPythonDoes(string expr, string expected)
        {
            string src = "import itertools, random, re, textwrap, urllib.parse, bisect\nimport xml.etree.ElementTree as ET\n"
                + "try:\n    r = " + expr + "\nexcept Exception as e:\n    r = type(e).__name__\n";
            string got = Outbridge.PyLite.Tests.Evaluator.Ev.Run(src).Repr("r");
            Assert.Equal(expected.EndsWith("Error") ? "'" + expected + "'" : expected, got);
        }

        [Fact]
        public void AHugeIndentLevelIsABudgetAbortNotAFault()
        {
            var ex = Record.Exception(() => Outbridge.PyLite.Tests.Evaluator.Ev.Run(
                "import xml.etree.ElementTree as ET\ne = ET.Element('a')\nET.SubElement(e, 'b')\nET.indent(e, level=10**30)\n"));
            Assert.True(ex == null || ex is Outbridge.PyLite.Runtime.Errors.EngineAbort, ex == null ? "" : ex.GetType().Name);
        }
    }
}

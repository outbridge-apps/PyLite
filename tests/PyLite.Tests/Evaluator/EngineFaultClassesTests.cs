using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Evaluator
{
    // Review 3, P0: script inputs that ended as an engine fault (uncatchable, "internal engine error") end as
    // CPython's error instead, and the lazy XPath charges what it builds.
    [Trait("Category", "Sandbox")]
    public class EngineFaultClassesTests
    {
        private static readonly ScriptEngine Engine = new ScriptEngine(new EngineOptions());

        private static RunResult Run(string src, ResourceLimits limits = null)
        {
            return Engine.Run(src, new RunRequest { Limits = limits, ScalarOutputNames = new[] { "r" } });
        }

        private static string Out(string src)
        {
            RunResult r = Run(src);
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.Kind + " " + r.Error.Message);
            return r.ScalarsOut["r"];
        }

        [Theory]
        [InlineData("print([0 for 1 in [5]])\n", "cannot assign to literal")]
        [InlineData("r = {0 for f() in [5]}\n", "cannot assign to function call")]
        [InlineData("r = [0 for x in [1] for 1 in [5]]\n", "cannot assign to literal")]
        [InlineData("x = *[1, 2]\n", "can't use starred expression here")]
        [InlineData("a = [1]\n*a\n", "can't use starred expression here")]
        [InlineData("a = b = *[1]\n", "can't use starred expression here")]
        [InlineData("x: int = *[1]\n", "can't use starred expression here")]
        [InlineData("r = [*a for a in [[1]]]\n", "iterable unpacking cannot be used in comprehension")]
        [InlineData("def f(b=0):\n    return b\nf((b)=1)\n", "expression cannot contain assignment, perhaps you meant \"==\"?")]
        public void ParserGapsAreSyntaxErrors(string src, string message)
        {
            RunResult r = Run(src);
            Assert.Equal(ScriptErrorKind.SyntaxError, r.Error.Kind);
            Assert.Equal(message, r.Error.Message);
        }

        [Fact]
        public void StarredUnpackingStillWorks()
        {
            Assert.Equal("([1, 2], 3, [1, 2, 3])", Out("a, *b = [0, 1, 2]\nc = *b, 3\nr = (b, c[2], [*b, 3])\n"));
        }

        [Theory]
        [InlineData("e.insert(0.0, ET.Element('x'))", "TypeError")]
        [InlineData("e.insert('0', ET.Element('x'))", "TypeError")]
        public void ElementInsertTakesAnIntIndex(string call, string pythonType)
        {
            RunResult r = Run("import xml.etree.ElementTree as ET\ne = ET.Element('a')\n" + call + "\n");
            Assert.Equal(pythonType, r.Error.PythonType);
        }

        [Fact]
        public void ElementInsertCountsANegativeIndexFromTheEnd()
        {
            Assert.Equal("['x', 'y', 'n', 'z']", Out("import xml.etree.ElementTree as ET\ne = ET.Element('a')\n"
                + "for t in 'xyz':\n    ET.SubElement(e, t)\ne.insert(-1, ET.Element('n'))\nr = [c.tag for c in e]\n"));
        }

        [Theory]
        [InlineData("S = type('')\nclass str:\n    pass\nr = S.upper(str())\n")]
        [InlineData("import collections\nP = collections.namedtuple('dict', 'a')\nr = type({}).keys(P(1))\n")]
        public void AScriptClassNamedLikeABuiltinIsNotTheBuiltin(string src)
        {
            RunResult r = Run(src);
            Assert.Equal("TypeError", r.Error.PythonType);
        }

        [Theory]
        [InlineData("S = type('')\nclass str:\n    pass\nr = isinstance(str(), S)\n", "False")]
        [InlineData("import collections\nr = isinstance(collections.namedtuple('int', 'a b')(1, 2), int)\n", "False")]
        [InlineData("import dataclasses\nr = isinstance(dataclasses.make_dataclass('list', ['a'])(1), list)\n", "False")]
        [InlineData("import collections\nr = isinstance(collections.namedtuple('int', 'a b')(1, 2), tuple)\n", "True")]
        public void IsinstanceGoesByIdentityForScriptNamedClasses(string src, string expected)
        {
            Assert.Equal(expected, Out(src));
        }

        [Theory]
        [InlineData("import xml.etree.ElementTree as ET\nET.Element('r').find('x/' * 4000000 + 'x')\n")]
        [InlineData("import xml.etree.ElementTree as ET\nET.Element('r').find('x' + '[@a]' * 3900000)\n")]
        public void AHugeXPathIsChargedAsItIsParsed(string src)
        {
            RunResult r = Run(src, ResourceLimits.Default.With(maxAllocBytes: 64L << 20, deadlineMs: 20000));
            Assert.Equal(ScriptErrorKind.BudgetExceeded, r.Error.Kind);
            Assert.Equal("MaxAllocBytes", r.Error.LimitName);
        }

        [Fact]
        public void LongPathsStillMatch()
        {
            Assert.Equal("d", Out("import xml.etree.ElementTree as ET\nr = ET.fromstring('<a><b><c><d/></c></b></a>').find('b/c/d').tag\n"));
            Assert.Equal("d", Out("import xml.etree.ElementTree as ET\nr = ET.fromstring('<a><b><c><d/></c></b></a>').find('.//c//d').tag\n"));
        }
    }
}

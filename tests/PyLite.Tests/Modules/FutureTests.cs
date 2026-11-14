using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // Review 6, f1-07: there was no __future__, so the commonest future line raised at run time. Answers, lines and
    // offsets are CPython 3.14's.
    [Trait("Category", "Compatibility")]
    public class FutureTests
    {
        [Theory]
        [InlineData("from __future__ import annotations\nx: int = 1\nr = x\n", "1")]
        [InlineData("'''doc'''\nfrom __future__ import annotations, division\nfrom __future__ import (generator_stop)\nr = 7 / 2\n", "3.5")]
        [InlineData("from __future__ import annotations as a\nr = a.compiler_flag\n", "16777216")]
        [InlineData("from __future__ import annotations\ndef f(x: Later) -> Later:\n    return 2\nr = f(0)\n", "2")]
        [InlineData("import __future__\nr = (__future__.annotations.getMandatoryRelease(), __future__.division.getOptionalRelease())\n",
            "(None, (2, 2, 0, 'alpha', 2))")]
        [InlineData("import __future__\nr = (len(__future__.all_feature_names), repr(__future__.generator_stop))\n",
            "(10, \"_Feature((3, 5, 0, 'beta', 1), (3, 7, 0, 'alpha', 0), 8388608)\")")]
        [InlineData("import __future__\nr = ' '.join(sorted(n for n in dir(__future__) if not n.startswith('_')))\n",
            "'CO_FUTURE_ABSOLUTE_IMPORT CO_FUTURE_ANNOTATIONS CO_FUTURE_BARRY_AS_BDFL CO_FUTURE_DIVISION CO_FUTURE_GENERATOR_STOP"
            + " CO_FUTURE_PRINT_FUNCTION CO_FUTURE_UNICODE_LITERALS CO_FUTURE_WITH_STATEMENT CO_GENERATOR_ALLOWED CO_NESTED"
            + " absolute_import all_feature_names annotations barry_as_FLUFL division generator_stop generators nested_scopes"
            + " print_function unicode_literals with_statement'")]
        public void KnownFeaturesAreNoOps(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        [Theory]
        [InlineData("from __future__ import braces\n", "not a chance", 1, 24)]
        [InlineData("from __future__ import nonexistent\n", "future feature nonexistent is not defined", 1, 24)]
        [InlineData("from __future__ import annotations, braces\n", "not a chance", 1, 37)]
        [InlineData("x = 1\nfrom __future__ import annotations\n", "from __future__ imports must occur at the beginning of the file", 2, 1)]
        [InlineData("'''a'''\n'''b'''\nfrom __future__ import annotations\n", "from __future__ imports must occur at the beginning of the file", 3, 1)]
        [InlineData("def f():\n    from __future__ import annotations\n", "from __future__ imports must occur at the beginning of the file", 2, 5)]
        [InlineData("from __future__ import barry_as_FLUFL\n", "future feature barry_as_FLUFL is not supported in this dialect", 1, 24)]
        public void MisplacedOrUnknownFeaturesAreSyntaxErrors(string src, string message, int line, int column)
        {
            CompiledScript compiled;
            ScriptError error;
            Assert.False(new ScriptEngine(new EngineOptions()).TryCompile(src, "<script>", out compiled, out error));
            Assert.Equal(ScriptErrorKind.SyntaxError, error.Kind);
            Assert.Equal(message, error.Message);
            Assert.Equal(line, error.Line);
            Assert.Equal(column, error.Column);
        }
    }
}

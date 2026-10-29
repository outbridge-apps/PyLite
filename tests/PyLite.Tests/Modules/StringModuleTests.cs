using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // string: the constants and capwords().
    [Trait("Category", "Compatibility")]
    public class StringModuleTests
    {
        private static string R(string expr)
        {
            return Ev.ReprOf("import string\n", expr);
        }

        [Theory]
        [InlineData("string.ascii_lowercase", "'abcdefghijklmnopqrstuvwxyz'")]
        [InlineData("string.ascii_uppercase", "'ABCDEFGHIJKLMNOPQRSTUVWXYZ'")]
        [InlineData("string.ascii_letters == string.ascii_lowercase + string.ascii_uppercase", "True")]
        [InlineData("string.digits", "'0123456789'")]
        [InlineData("string.hexdigits", "'0123456789abcdefABCDEF'")]
        [InlineData("string.octdigits", "'01234567'")]
        [InlineData("len(string.punctuation)", "32")]
        [InlineData("string.whitespace", "' \\t\\n\\r\\x0b\\x0c'")]
        [InlineData("len(string.printable)", "100")]
        [InlineData("string.capwords('hello  world')", "'Hello World'")]
        [InlineData("string.capwords('  a b  ')", "'A B'")]
        [InlineData("string.capwords('hELLO wORLD')", "'Hello World'")]
        [InlineData("string.capwords('a-b-c', '-')", "'A-B-C'")]
        [InlineData("string.capwords('a--b', sep='-')", "'A--B'")]
        [InlineData("string.capwords('')", "''")]
        public void Surface(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void CapwordsNeedsStr()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("import string\nstring.capwords(3)\n"));
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
        }
    }
}

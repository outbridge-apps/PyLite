using System.Text;
using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    // A private name inside a class is `_Class__name` (CPython's mangling). The class name is repeated in every mangled
    // name, so a long one multiplied the compile's memory by the occurrences (160 KB of source took 980 MB on the
    // host's thread, review 4 C1): each distinct name is mangled once per class, and all of them together are capped.
    [Trait("Category", "Sandbox")]
    public class PrivateNameMangleTests
    {
        private static string ClassWith(int classNameLength, int names, bool distinct)
        {
            var sb = new StringBuilder("class ").Append('C', classNameLength).Append(":\n    def m(self, x):\n        return (");
            for (int i = 0; i < names; i++)
                sb.Append("x.__a").Append(distinct ? i.ToString(System.Globalization.CultureInfo.InvariantCulture) : "").Append(", ");
            return sb.Append(")\n").ToString();
        }

        [Fact]
        public void ARepeatedPrivateNameIsMangledOnce()
        {
            // 10,000 occurrences x a 50,000-char class name would be 500 M characters made one by one
            var s = new ScriptEngine(new EngineOptions()).Compile(ClassWith(50000, 10000, false), "long.py");
            Assert.NotNull(s);
        }

        [Fact]
        public void ManyLongPrivateNamesAreRefused()
        {
            var ex = Assert.Throws<PyLiteSyntaxException>(() => new ScriptEngine(new EngineOptions()).Compile(ClassWith(50000, 2000, true), "long.py"));
            Assert.Contains("private names in class bodies", ex.Error.Message);
        }

        [Fact]
        public void PrivateNamesStillMangle()
        {
            RunResult r = new ScriptEngine(new EngineOptions()).Run(
                "class A:\n    def __init__(self):\n        self.__x = 1\n    def get(self):\n        return self.__x\n"
                + "a = A()\nr = (a.get(), a._A__x, hasattr(a, '__x'))\n",
                new RunRequest { ScalarOutputNames = new[] { "r" } });
            Assert.True(r.Succeeded, r.Error == null ? "" : r.Error.Message);
            Assert.Equal("(1, 1, False)", r.ScalarsOut["r"]);
        }
    }
}

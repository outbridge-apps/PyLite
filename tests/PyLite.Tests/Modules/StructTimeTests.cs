using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/09 T-MDT-13: time.struct_time (9-tuple + named slots).
    [Trait("Category", "Compatibility")]
    public class StructTimeTests
    {
        private const string Prelude = "import time\n_t = time.struct_time((1993, 6, 6, 8, 15, 0, 6, 157, 0))\n";

        private static string R(string expr)
        {
            return Ev.ReprOf(Prelude, expr);
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run("import time\n" + body + "\n"));
        }

        [Theory]
        [InlineData("_t.tm_year", "1993")]
        [InlineData("_t[0]", "1993")]
        [InlineData("_t[0] == _t.tm_year", "True")]
        [InlineData("_t[-1] == _t.tm_isdst", "True")]
        [InlineData("_t.tm_isdst", "0")]
        [InlineData("len(_t)", "9")]
        [InlineData("_t[1:3]", "(6, 6)")]
        [InlineData("_t[0:100]", "(1993, 6, 6, 8, 15, 0, 6, 157, 0)")]
        [InlineData("list(_t)", "[1993, 6, 6, 8, 15, 0, 6, 157, 0]")]
        [InlineData("tuple(_t)", "(1993, 6, 6, 8, 15, 0, 6, 157, 0)")]
        [InlineData("_t == tuple(_t)", "True")]
        [InlineData("_t == (1993, 6, 6, 8, 15, 0, 6, 157, 0)", "True")]
        [InlineData("6 in _t", "True")]
        [InlineData("bool(_t)", "True")]
        [InlineData("repr(_t)", "'time.struct_time(tm_year=1993, tm_mon=6, tm_mday=6, tm_hour=8, tm_min=15, tm_sec=0, tm_wday=6, tm_yday=157, tm_isdst=0)'")]
        public void StructTimeBehavior(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void FromRangeNine()
        {
            string r = Ev.Run("import time\nr = time.struct_time(range(9))\n").Repr("r");
            Assert.Equal("time.struct_time(tm_year=0, tm_mon=1, tm_mday=2, tm_hour=3, tm_min=4, tm_sec=5, tm_wday=6, tm_yday=7, tm_isdst=8)", r);
        }

        [Fact]
        public void UnpackStar()
        {
            var res = Ev.Run(Prelude + "a, b, *rest = _t\nn = len(rest)\n");
            Assert.Equal("1993", res.Repr("a"));
            Assert.Equal("7", res.Repr("n"));
        }

        [Fact]
        public void ShortSequenceIsTypeError()
        {
            var ex = Raises("x = time.struct_time(range(8))");
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void ShortSequenceMessage()
        {
            string src = "import time\nmsg = ''\ntry:\n    time.struct_time(range(8))\nexcept TypeError as e:\n    msg = str(e)\n";
            Assert.Equal("'time.struct_time() takes a 9-sequence (8-sequence given)'", Ev.Run(src).Repr("msg"));
        }

        [Fact]
        public void IndexOutOfRangeIsIndexError()
        {
            Assert.Equal("IndexError", Raises("_t = time.struct_time(range(9))\nx = _t[9]").Value.ExcType.Name);
        }
    }
}

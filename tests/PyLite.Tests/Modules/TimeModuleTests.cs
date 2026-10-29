using System;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/09 T-MDT-14/15: time gmtime/localtime/mktime/asctime/ctime + tz attributes.
    [Trait("Category", "Compatibility")]
    public class TimeModuleTests
    {
        // LocalOffset +02:00, 'CET', UtcNow = 1970-01-01T00:16:40Z (epoch 1000).
        private static readonly IScriptClock Cet = new FrozenClock(
            new DateTime(1970, 1, 1, 0, 16, 40, DateTimeKind.Utc), TimeSpan.FromHours(2), "CET");

        private static string R(string expr, IScriptClock clock = null)
        {
            return Ev.ReprOf("import time\n", expr, clock: clock ?? Cet);
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run("import time\n" + body + "\n", clock: Cet));
        }

        [Theory]
        [InlineData("time.gmtime(0) == (1970,1,1,0,0,0,3,1,0)", "True")]
        [InlineData("time.gmtime(-1) == (1969,12,31,23,59,59,2,365,0)", "True")]
        [InlineData("time.gmtime(1.99).tm_sec", "1")]
        [InlineData("time.localtime(0) == (1970,1,1,2,0,0,3,1,0)", "True")]
        [InlineData("time.mktime(time.localtime(1000000))", "1000000.0")]
        [InlineData("time.mktime((1970,1,1,2,0,0,0,1,-1))", "0.0")]
        [InlineData("time.asctime((1993,6,6,8,15,0,6,157,0))", "'Sun Jun  6 08:15:00 1993'")]
        [InlineData("time.asctime((1993,6,15,8,15,0,0,157,0))", "'Mon Jun 15 08:15:00 1993'")]
        [InlineData("time.asctime((1993,6,6,8,15,0,-1,157,0))", "'Sun Jun  6 08:15:00 1993'")]  // wday=-1 -> Sun
        [InlineData("time.ctime(0)", "'Thu Jan  1 02:00:00 1970'")]
        // ---- T-MDT-15 tz attrs / clocks ----
        [InlineData("time.timezone", "-7200")]
        [InlineData("time.altzone", "-7200")]
        [InlineData("time.daylight", "0")]
        [InlineData("time.tzname", "('CET', 'CET')")]
        [InlineData("time.time()", "1000.0")]
        [InlineData("time.perf_counter() >= 0", "True")]
        public void TimeBehavior(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void TimezoneSignForNegativeOffset()
        {
            var est = new FrozenClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(-5), "EST");
            Assert.Equal("18000", R("time.timezone", est));
        }

        [Fact]
        public void MonotonicNonDecreasing()
        {
            Assert.Equal("True", R("(lambda a, b: b >= a)(time.monotonic(), time.monotonic())"));
        }

        [Theory]
        [InlineData("x = time.mktime((1970,1,1,2,0,0,99,999,9))", "ValueError")]   // yday out of range
        [InlineData("x = time.asctime((1993,13,6,8,15,0,6,157,0))", "ValueError")] // month out of range
        [InlineData("x = time.mktime('x')", "TypeError")]
        [InlineData("x = time.gmtime(253402300800)", "OverflowError")]
        public void TimeErrors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }
    }
}

using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules.Support
{
    // impl/09 T-MDT-07: double<->(sec,us) and epoch<->broken-down conversions.
    [Trait("Category", "Compatibility")]
    public class TimestampConvTests
    {
        private static (long sec, int us) Split(double t)
        {
            EvalContext ctx = Rt.Ctx();
            long sec;
            int us;
            CalendarMath.SplitTimestamp(ctx, t, out sec, out us);
            return (sec, us);
        }

        [Theory]
        [InlineData(0.0, 0, 0)]
        [InlineData(1.5, 1, 500000)]
        [InlineData(-1.5, -2, 500000)]
        [InlineData(0.0000005, 0, 0)]        // half-even to even 0
        [InlineData(0.0000015, 0, 2)]        // half-even to even 2
        [InlineData(-0.0000005, 0, 0)]       // round(-0.5) -> 0
        [InlineData(1.9999999999, 2, 0)]     // carry into sec
        [InlineData(-86400.000001, -86401, 999999)]
        public void SplitKnown(double t, long sec, int us)
        {
            Assert.Equal((sec, us), Split(t));
        }

        [Fact]
        public void SplitNaNIsValueError()
        {
            var ex = Assert.Throws<ScriptException>(() => Split(double.NaN));
            Assert.Equal("ValueError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void SplitInfIsOverflowError()
        {
            var ex = Assert.Throws<ScriptException>(() => Split(double.PositiveInfinity));
            Assert.Equal("OverflowError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void SplitHugeIsOverflowError()
        {
            var ex = Assert.Throws<ScriptException>(() => Split(9223372036854775808.0));   // 2**63
            Assert.Equal("OverflowError", ex.Value.ExcType.Name);
        }

        private static (int y, int mo, int d, int h, int mi, int s) Epoch(long sec)
        {
            EvalContext ctx = Rt.Ctx();
            int y, mo, d, h, mi, s, us;
            CalendarMath.EpochToYmdHms(ctx, sec, 0, out y, out mo, out d, out h, out mi, out s, out us);
            return (y, mo, d, h, mi, s);
        }

        [Theory]
        [InlineData(0, 1970, 1, 1, 0, 0, 0)]
        [InlineData(-1, 1969, 12, 31, 23, 59, 59)]
        [InlineData(253402300799, 9999, 12, 31, 23, 59, 59)]
        public void EpochToYmdKnown(long sec, int y, int mo, int d, int h, int mi, int s)
        {
            Assert.Equal((y, mo, d, h, mi, s), Epoch(sec));
        }

        [Fact]
        public void EpochOverflowIsOverflowError()
        {
            var ex = Assert.Throws<ScriptException>(() => Epoch(253402300800));
            Assert.Equal("OverflowError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void YmdHmsToEpochNegative()
        {
            Assert.Equal(-1, CalendarMath.YmdHmsToEpoch(1969, 12, 31, 23, 59, 59));
        }

        [Fact]
        public void EpochRoundtripGrid()
        {
            EvalContext ctx = Rt.Ctx();
            for (int year = 1901; year <= 2099; year += 1)
            {
                foreach (int mo in new[] { 1, 6, 12 })
                {
                    long baseSec = CalendarMath.YmdHmsToEpoch(year, mo, 15, 13, 7, 23);
                    int y, m, d, h, mi, s, us;
                    CalendarMath.EpochToYmdHms(ctx, baseSec, 0, out y, out m, out d, out h, out mi, out s, out us);
                    Assert.Equal((year, mo, 15, 13, 7, 23), (y, m, d, h, mi, s));
                }
            }
        }
    }
}

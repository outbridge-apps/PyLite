using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/10 T-MNM-15/16/17: the statistics module (exact-Fraction summation).
    [Trait("Category", "Compatibility")]
    public class StatisticsTests
    {
        private const string Prelude = "import statistics\nfrom fractions import Fraction\n";

        private static string R(string expr)
        {
            return Ev.ReprOf(Prelude, expr);
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(Prelude + body + "\n"));
        }

        // median_grouped converts like mean does; Decimal data was a TypeError (D7).
        [Fact]
        public void MedianGroupedTakesDecimals()
        {
            Assert.Equal("1.5", Ev.ReprOf(Prelude + "from decimal import Decimal\n", "statistics.median_grouped([Decimal(1), Decimal(2)])"));
        }

        [Theory]
        // ---- mean / variance (exact) ----
        [InlineData("statistics.mean([1,2,3])", "2")]
        [InlineData("statistics.mean([1,2])", "1.5")]
        [InlineData("statistics.mean([Fraction(1,2),Fraction(1,4)])", "Fraction(3, 8)")]
        [InlineData("statistics.mean([1e16, 1., 1., -1e16])", "0.5")]
        [InlineData("statistics.variance([1,2,3])", "1")]
        [InlineData("statistics.pvariance([1,2,3])", "0.6666666666666666")]
        [InlineData("statistics.variance([Fraction(1,2), Fraction(3,2), Fraction(5,2)])", "Fraction(1, 1)")]
        // ---- median ----
        [InlineData("statistics.median([1,2,3])", "2")]
        [InlineData("statistics.median([1,2,3,4])", "2.5")]
        [InlineData("statistics.median([1,3])", "2.0")]
        [InlineData("statistics.median([Fraction(1,2),Fraction(3,2)])", "Fraction(1, 1)")]
        [InlineData("statistics.median_low([1,2,3,4])", "2")]
        [InlineData("statistics.median_high([1,2,3,4])", "3")]
        [InlineData("statistics.median_grouped([1,2,2,3,4,4,4,4,4,5])", "3.7")]
        [InlineData("statistics.median_grouped([Fraction(1), Fraction(2), Fraction(3)])", "2.0")]
        // ---- mode (3.8+: first encountered) ----
        [InlineData("statistics.mode([1,1,2,3])", "1")]
        [InlineData("statistics.mode(['a','a','b'])", "'a'")]
        [InlineData("statistics.mode([1,1,2,2])", "1")]
        [InlineData("statistics.mode([1,2,3])", "1")]
        [InlineData("statistics.mode([1.5, 1.5, 2.5])", "1.5")]
        [InlineData("statistics.mode([(1,2),(1,2),(3,4)])", "(1, 2)")]
        [InlineData("statistics.mode([1,1,1,2,2,3])", "1")]
        public void StatisticsBehavior(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("abs(statistics.stdev([1.5,2.5,2.5,2.75,3.25,4.75]) - 1.0810874155219827) < 1e-12")]
        [InlineData("abs(statistics.pstdev([1,2,3]) - 0.816496580927726) < 1e-12")]
        public void StatisticsTolerance(string boolExpr)
        {
            Assert.Equal("True", R(boolExpr));
        }

        [Theory]
        [InlineData("x = statistics.mean([])", "StatisticsError")]
        [InlineData("x = statistics.variance([1])", "StatisticsError")]
        [InlineData("x = statistics.median([])", "StatisticsError")]
        [InlineData("x = statistics.mode([])", "StatisticsError")]
        [InlineData("x = statistics.mode([[1],[1]])", "TypeError")]
        [InlineData("x = statistics.mean(['x'])", "TypeError")]
        public void StatisticsErrors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Fact]
        public void StatisticsErrorIsValueError()
        {
            string src = Prelude + "caught = 0\ntry:\n    statistics.mode([])\nexcept ValueError:\n    caught = 1\n";
            Assert.Equal("1", Ev.Run(src).Repr("caught"));
        }

        [Fact]
        public void ExcludedMemberIsAttributeError()
        {
            // harmonic_mean landed with the v2 B7 modernization; NormalDist (3.8, class-based) remains out.
            Assert.Equal("AttributeError", Raises("x = statistics.NormalDist").Value.ExcType.Name);
        }

        [Fact]
        public void NanAbsorbsInMean()
        {
            Assert.Equal("True", R("statistics.mean([1.0, float('nan')]) != statistics.mean([1.0, float('nan')])"));
        }
    }
}

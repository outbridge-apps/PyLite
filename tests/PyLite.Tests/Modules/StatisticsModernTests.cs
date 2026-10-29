using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // v2 B7: statistics members from CPython 3.6-3.11.
    [Trait("Category", "Compatibility")]
    public class StatisticsModernTests
    {
        private const string P = "import statistics\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        // ---- fmean ----
        [InlineData("r = statistics.fmean([3.5, 4.0, 5.25])", "4.25")]
        [InlineData("r = statistics.fmean([1, 2, 3, 4])", "2.5")]
        [InlineData("r = statistics.fmean([3.5, 4.0], weights=[1.0, 0.0])", "3.5")]
        [InlineData("r = statistics.fmean([2, 4], [3, 1])", "2.5")]
        // ---- geometric_mean ----
        [InlineData("r = round(statistics.geometric_mean([54, 24, 36]), 9)", "36.0")]
        [InlineData("r = statistics.geometric_mean([4, 9])", "6.0")]
        // ---- harmonic_mean ----
        [InlineData("r = statistics.harmonic_mean([40, 60])", "48.0")]
        [InlineData("r = statistics.harmonic_mean([2, 0, 4])", "0.0")]
        [InlineData("r = statistics.harmonic_mean([40, 60], weights=[5, 30])", "56.0")]
        // ---- multimode ----
        [InlineData("r = statistics.multimode('aabbbbccddddeeffffgg')", "['b', 'd', 'f']")]
        [InlineData("r = statistics.multimode([1, 1, 2, 2])", "[1, 2]")]
        [InlineData("r = statistics.multimode([])", "[]")]
        [InlineData("r = statistics.multimode([3])", "[3]")]
        // ---- quantiles ----
        [InlineData("r = statistics.quantiles([1, 2, 3, 4, 5])", "[1.5, 3.0, 4.5]")]
        [InlineData("r = statistics.quantiles([1, 2, 3, 4, 5], method='inclusive')", "[2.0, 3.0, 4.0]")]
        [InlineData("r = statistics.quantiles([1, 2], n=2)", "[1.5]")]
        [InlineData("r = len(statistics.quantiles(range(100), n=10))", "9")]
        // ---- correlation / covariance ----
        [InlineData("r = statistics.correlation([1, 2, 3], [1, 2, 3])", "1.0")]
        [InlineData("r = statistics.correlation([1, 2, 3], [3, 2, 1])", "-1.0")]
        [InlineData("r = statistics.covariance([1, 2, 3], [1, 2, 3])", "1.0")]
        [InlineData("r = statistics.covariance([1, 2, 3, 4], [4, 3, 2, 1])", "-1.6666666666666667")]
        // ---- linear_regression ----
        [InlineData("lr = statistics.linear_regression([1, 2, 3], [2, 4, 6])\nr = (lr.slope, lr.intercept)", "(2.0, 0.0)")]
        [InlineData("lr = statistics.linear_regression([1, 2, 3], [3, 5, 7])\nr = (round(lr.slope, 9), round(lr.intercept, 9))", "(2.0, 1.0)")]
        [InlineData("lr = statistics.linear_regression([1, 2], [2, 4], proportional=True)\nr = (lr.slope, lr.intercept)", "(2.0, 0.0)")]
        [InlineData("r = repr(statistics.linear_regression([1, 2], [2, 4]))", "'LinearRegression(slope=2.0, intercept=0.0)'")]
        public void Modern(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("statistics.fmean([])", "StatisticsError")]
        [InlineData("statistics.fmean([1, 2], weights=[1])", "StatisticsError")]
        [InlineData("statistics.geometric_mean([])", "StatisticsError")]
        [InlineData("statistics.geometric_mean([1, -2])", "StatisticsError")]
        [InlineData("statistics.harmonic_mean([])", "StatisticsError")]
        [InlineData("statistics.harmonic_mean([-1, 2])", "StatisticsError")]
        [InlineData("statistics.quantiles([1])", "StatisticsError")]
        [InlineData("statistics.quantiles([1, 2], n=0)", "StatisticsError")]
        [InlineData("statistics.quantiles([1, 2, 3], method='nearest')", "ValueError")]
        [InlineData("statistics.correlation([1, 2], [1, 2, 3])", "StatisticsError")]
        [InlineData("statistics.correlation([1, 1, 1], [1, 2, 3])", "StatisticsError")]
        [InlineData("statistics.covariance([1], [2])", "StatisticsError")]
        [InlineData("statistics.linear_regression([1, 1], [2, 3])", "StatisticsError")]
        public void Errors(string body, string excType)
        {
            Assert.Equal(excType, Raises("x = " + body).Value.ExcType.Name);
        }

        [Fact]
        public void StatisticsErrorIsCatchable()
        {
            string src = P + "caught = False\ntry:\n    statistics.fmean([])\nexcept statistics.StatisticsError:\n    caught = True\nr = caught\n";
            Assert.Equal("True", Ev.Run(src).Repr("r"));
        }

        // Each input is read once, so an iterator works like a list (harmonic_mean of a generator raised
        // ZeroDivisionError, linear_regression of two iterators answered intercept 0.0, B11).
        [Theory]
        [InlineData("r = statistics.harmonic_mean(x for x in [40, 60])", "48.0")]
        [InlineData("lr = statistics.linear_regression(iter([1, 2, 3]), iter([2, 4, 7]))\nr = lr == statistics.linear_regression([1, 2, 3], [2, 4, 7])", "True")]
        [InlineData("r = round(statistics.correlation(iter([1, 2, 3]), iter([1, 2, 3])), 9)", "1.0")]
        public void IteratorsAreReadOnce(string body, string expected)
        {
            Assert.Equal(expected, Body(body + "\n"));
        }

        // The data copy is charged and capped like every materialization (B12). A small cap keeps it cheap: at the
        // default 5,000,000 items the copy took seconds and could run out of time on a busy machine.
        [Fact]
        public void MaterializingIsCapped()
        {
            var ex = Assert.Throws<Outbridge.PyLite.Runtime.Errors.EngineAbort>(() => Ev.Run(P + "import itertools\nr = statistics.mean(itertools.repeat(1, 10 ** 6))\n",
                Outbridge.PyLite.Hosting.ResourceLimits.Default.With(maxCollectionItems: 100_000)));
            Assert.Contains("MaxCollectionItems", ex.Message);
        }
    }
}


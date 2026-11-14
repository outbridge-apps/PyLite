using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // Every int-to-float conversion as CPython's PyLong_AsDouble (second review, Q02): 2**1024 - 1 has 1024
    // bits and still rounds past the largest double, so (2**1024 - 1) + 0.5 was inf; four copies of the
    // check differed in their text. On the way, the other places a number becomes a float: math's predicates,
    // log of a big int (CPython's loghelper), statistics, random.choices, struct, time, and the sign of the
    // NaN that float('nan') and math.nan give. The expected answers are CPython 3.14's (Pyodide).
    [Trait("Category", "Compatibility")]
    public class IntToFloatTests
    {
        private const string Imports = "import math, statistics, struct, time, datetime, random, json\nfrom fractions import Fraction\nB = 2**1024\n";

        private static string Answer(string expr)
        {
            string src = Imports + "try:\n    r = repr(" + expr + ")\nexcept Exception as e:\n    r = type(e).__name__ + ': ' + str(e)\n";
            return ((Outbridge.PyLite.Runtime.Values.StrValue)Ev.Run(src).Get("r")).Value;
        }

        private const string TooLarge = "OverflowError: int too large to convert to float";

        [Theory]
        [InlineData("(B - 1) + 0.5", TooLarge)]
        [InlineData("0.5 + (B - 1)", TooLarge)]
        [InlineData("(B - 1) * 1.0", TooLarge)]
        [InlineData("(B - 1) ** 0.5", TooLarge)]
        [InlineData("2.0 ** (B - 1)", TooLarge)]
        [InlineData("(B - 1) // 1.0", TooLarge)]
        [InlineData("(B - 1) % 1.0", TooLarge)]
        [InlineData("divmod(B - 1, 1.0)", TooLarge)]
        [InlineData("float(B - 2**970)", TooLarge)]
        [InlineData("float(B - 2**970 - 1)", "1.7976931348623157e+308")]
        [InlineData("(B - 1) < 1e308", "False")]
        [InlineData("format(B - 1, 'f')", TooLarge)]
        [InlineData("'%e' % (B - 1)", TooLarge)]
        [InlineData("math.sqrt(B - 1)", TooLarge)]
        [InlineData("math.isfinite(B - 1)", TooLarge)]
        [InlineData("math.isinf(10**400)", TooLarge)]
        [InlineData("math.isfinite(Fraction(1, 3))", "True")]
        [InlineData("math.log(B - 1)", "709.782712893384")]
        [InlineData("math.log(2**1023 + 12345)", "709.0895657128241")]
        [InlineData("math.log(10**400, 2)", "1328.7712379549448")]
        [InlineData("math.log(3**700)", "769.0286020676768")]
        [InlineData("math.log2(B - 1)", "1024.0")]
        [InlineData("math.log10(B - 1)", "308.25471555991675")]
        public void Arithmetic(string expr, string expected)
        {
            Assert.Equal(expected, Answer(expr));
        }

        [Theory]
        [InlineData("statistics.fmean([10**400, 1])", TooLarge)]
        [InlineData("statistics.median_grouped([10**400, 1])", TooLarge)]
        [InlineData("statistics.harmonic_mean([10**400, 1])", "2.0")]
        [InlineData("statistics.geometric_mean([10**400, 1])", "9.999999999999653e+199")]
        [InlineData("statistics.geometric_mean([10**400, 10**400])", "OverflowError: math range error")]
        [InlineData("statistics.geometric_mean([54, 24, 36])", "36.000000000000014")]
        [InlineData("statistics.geometric_mean([0, 1])", "0.0")]
        [InlineData("statistics.geometric_mean([float('inf'), 0])", "nan")]
        [InlineData("statistics.geometric_mean([-1, 2])", "StatisticsError: ('No negative inputs allowed', -1)")]
        [InlineData("statistics.geometric_mean([])", "StatisticsError: Must have a non-empty dataset")]
        [InlineData("statistics.geometric_mean(iter([2, 8]))", "4.0")]
        [InlineData("random.choices(['a'], weights=[B - 1])", TooLarge)]
        public void Statistics(string expr, string expected)
        {
            Assert.Equal(expected, Answer(expr));
        }

        [Theory]
        [InlineData("struct.pack('d', B - 1)", "error: required argument is not a float")]
        [InlineData("struct.pack('d', Fraction(1, 3))", "b'UUUUUU\\xd5?'")]
        [InlineData("struct.pack('f', 1e39)", "b'\\x00\\x00\\x80\\x7f'")]
        [InlineData("struct.pack('@f', -1e39)", "b'\\x00\\x00\\x80\\xff'")]
        [InlineData("struct.pack('=f', 1e39)", "OverflowError: float too large to pack with f format")]
        [InlineData("struct.pack('<f', 3.4028234663852886e38 * (1 + 2**-25))", "b'\\xff\\xff\\x7f\\x7f'")]
        [InlineData("time.gmtime(10**20)", "OverflowError: timestamp out of range for platform time_t")]
        [InlineData("time.gmtime(1e20)", "OverflowError: timestamp out of range for platform time_t")]
        [InlineData("datetime.date.fromtimestamp(10**400)", "OverflowError: timestamp out of range for platform time_t")]
        [InlineData("time.sleep(10**400)", "OverflowError: timestamp too large to convert to C PyTime_t")]
        public void StructAndTime(string expr, string expected)
        {
            Assert.Equal(expected, Answer(expr));
        }

        // CPython's Py_NAN is positive; .NET's double.NaN has the sign bit set.
        [Theory]
        [InlineData("math.copysign(1, float('nan'))", "1.0")]
        [InlineData("math.copysign(1, math.nan)", "1.0")]
        [InlineData("math.copysign(1, float('-nan'))", "-1.0")]
        [InlineData("math.copysign(1, -math.nan)", "-1.0")]
        [InlineData("math.copysign(1, json.loads('NaN'))", "1.0")]
        [InlineData("math.copysign(1, statistics.geometric_mean([math.nan, 1]))", "1.0")]
        [InlineData("struct.pack('<d', math.nan)", "b'\\x00\\x00\\x00\\x00\\x00\\x00\\xf8\\x7f'")]
        [InlineData("struct.pack('<f', float('nan'))", "b'\\x00\\x00\\xc0\\x7f'")]
        public void NanIsPositive(string expr, string expected)
        {
            Assert.Equal(expected, Answer(expr));
        }
    }
}

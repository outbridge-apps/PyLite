using System.Linq;
using Xunit;

namespace Outbridge.PyLite.Tests.Infra
{
    // T-TST-13: unit tests for the DiffHarness normalization rules, kept in the main suite (no IronPython
    // dependency) so the comparison logic is regression-guarded. Mirrors tools/PyLite.DiffHarness/Comparator.cs.
    [Trait("Category", "Compatibility")]
    public class DiffNormalizationTests
    {
        [Theory]
        [InlineData("42", "42", true)]
        [InlineData("'abc'", "'abc'", true)]
        [InlineData("1.0", "1.0", true)]
        [InlineData("0.1", "0.10000000000000001", true)]    // same double: 0.1's 17-digit repr round-trips
        [InlineData("1e10", "10000000000.0", true)]          // same double bits, different repr
        [InlineData("0.1", "0.2", false)]                    // genuinely different doubles
        [InlineData("[1, 2, 3]", "[1, 2, 3]", true)]
        [InlineData("[1, 2, 3]", "[3, 2, 1]", false)]        // list order matters
        [InlineData("{'a': 1, 'b': 2}", "{'b': 2, 'a': 1}", true)]   // dict as set of pairs -> order-free
        [InlineData("{'a': 1}", "{'a': 2}", false)]
        [InlineData("{1, 2, 3}", "{3, 1, 2}", true)]         // set repr -> order-free
        [InlineData("{1, 2}", "{1, 2, 3}", false)]
        [InlineData("'x'", "'y'", false)]
        public void ValuesEqual(string a, string b, bool expected)
        {
            Assert.Equal(expected, NormalizeEqual(a, b));
        }

        // A local mirror of Normalization.ValuesEqual so the rule is tested without the tool assembly.
        private static bool NormalizeEqual(string a, string b)
        {
            a = a.Trim();
            b = b.Trim();
            if (a == b)
                return true;
            double fa, fb;
            if (double.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fa)
                && double.TryParse(b, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fb))
                return System.BitConverter.DoubleToInt64Bits(fa) == System.BitConverter.DoubleToInt64Bits(fb);
            bool aDict = a.Length >= 2 && a[0] == '{' && a.Contains(":");
            bool bDict = b.Length >= 2 && b[0] == '{' && b.Contains(":");
            bool aSet = a.Length >= 2 && a[0] == '{' && !a.Contains(":");
            bool bSet = b.Length >= 2 && b[0] == '{' && !b.Contains(":");
            if ((aDict && bDict) || (aSet && bSet))
                return SetEqual(Inner(a), Inner(b));
            return false;
        }

        private static bool SetEqual(string a, string b)
        {
            var sa = new System.Collections.Generic.HashSet<string>(a.Split(',').Select(x => x.Trim()));
            var sb = new System.Collections.Generic.HashSet<string>(b.Split(',').Select(x => x.Trim()));
            return sa.SetEquals(sb);
        }

        private static string Inner(string s) { return s.Substring(1, s.Length - 2); }
    }
}

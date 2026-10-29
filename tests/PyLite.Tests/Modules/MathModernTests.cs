using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // v2 B3: math members from CPython 3.5-3.13 (RFC-003 §4 modernization).
    [Trait("Category", "Compatibility")]
    public class MathModernTests
    {
        private const string P = "import math\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        // ---- constants ----
        [InlineData("r = math.tau", "6.283185307179586")]
        [InlineData("r = math.inf", "inf")]
        [InlineData("r = (math.nan != math.nan)", "True")]
        [InlineData("r = -math.inf", "-inf")]
        // ---- isclose ----
        [InlineData("r = math.isclose(1.0, 1.0)", "True")]
        [InlineData("r = math.isclose(1.0, 1.0000000001)", "True")]
        [InlineData("r = math.isclose(1.0, 1.001)", "False")]
        [InlineData("r = math.isclose(0.0, 1e-10)", "False")]
        [InlineData("r = math.isclose(0.0, 1e-10, abs_tol=1e-9)", "True")]
        [InlineData("r = math.isclose(100, 101, rel_tol=0.02)", "True")]
        [InlineData("r = math.isclose(math.inf, math.inf)", "True")]
        [InlineData("r = math.isclose(math.inf, 1e308)", "False")]
        [InlineData("r = math.isclose(math.nan, math.nan)", "False")]
        // ---- gcd / lcm ----
        [InlineData("r = math.gcd(12, 18)", "6")]
        [InlineData("r = math.gcd(-12, 18)", "6")]
        [InlineData("r = math.gcd()", "0")]
        [InlineData("r = math.gcd(7)", "7")]
        [InlineData("r = math.gcd(12, 18, 8)", "2")]
        [InlineData("r = math.lcm(4, 6)", "12")]
        [InlineData("r = math.lcm()", "1")]
        [InlineData("r = math.lcm(3, 0)", "0")]
        [InlineData("r = math.lcm(2, 3, 5)", "30")]
        // ---- isqrt ----
        [InlineData("r = math.isqrt(0)", "0")]
        [InlineData("r = math.isqrt(24)", "4")]
        [InlineData("r = math.isqrt(25)", "5")]
        [InlineData("r = math.isqrt(10**20)", "10000000000")]
        [InlineData("r = math.isqrt(10**20 - 1)", "9999999999")]
        // ---- prod ----
        [InlineData("r = math.prod([1, 2, 3, 4])", "24")]
        [InlineData("r = math.prod([])", "1")]
        [InlineData("r = math.prod([1, 2], start=10)", "20")]
        [InlineData("r = math.prod([0.5, 4.0])", "2.0")]
        // ---- comb / perm ----
        [InlineData("r = math.comb(5, 2)", "10")]
        [InlineData("r = math.comb(5, 0)", "1")]
        [InlineData("r = math.comb(5, 6)", "0")]
        [InlineData("r = math.comb(52, 5)", "2598960")]
        [InlineData("r = math.perm(5, 2)", "20")]
        [InlineData("r = math.perm(5)", "120")]
        [InlineData("r = math.perm(5, 6)", "0")]
        // ---- hypot n-ary / dist ----
        [InlineData("r = math.hypot(3, 4)", "5.0")]
        [InlineData("r = math.hypot(2, 3, 6)", "7.0")]
        [InlineData("r = math.hypot()", "0.0")]
        [InlineData("r = math.hypot(1e200, 1e200)", "1.414213562373095e+200")]
        [InlineData("r = math.dist([0, 0], [3, 4])", "5.0")]
        [InlineData("r = math.dist((1, 2, 3), (1, 2, 3))", "0.0")]
        // ---- remainder ----
        [InlineData("r = math.remainder(7, 4)", "-1.0")]
        [InlineData("r = math.remainder(6, 4)", "-2.0")]
        [InlineData("r = math.remainder(5, 4)", "1.0")]
        // ---- nextafter / ulp ----
        [InlineData("r = math.nextafter(1.0, 2.0)", "1.0000000000000002")]
        [InlineData("r = math.nextafter(1.0, 0.0)", "0.9999999999999999")]
        [InlineData("r = math.nextafter(1.0, 1.0)", "1.0")]
        [InlineData("r = math.nextafter(0.0, 1.0)", "5e-324")]
        [InlineData("r = math.nextafter(1.0, 2.0, steps=3)", "1.0000000000000007")]
        [InlineData("r = math.nextafter(1.0, 2.0, steps=0)", "1.0")]
        [InlineData("r = math.ulp(1.0)", "2.220446049250313e-16")]
        [InlineData("r = math.ulp(0.0)", "5e-324")]
        // ---- cbrt / exp2 ----
        [InlineData("r = math.cbrt(8.0)", "2.0")]
        [InlineData("r = math.cbrt(-27.0)", "-3.0")]
        [InlineData("r = math.cbrt(0.0)", "0.0")]
        [InlineData("r = math.exp2(10)", "1024.0")]
        [InlineData("r = math.exp2(0.5)", "1.4142135623730951")]
        // ---- sumprod ----
        [InlineData("r = math.sumprod([1, 2, 3], [4, 5, 6])", "32")]
        [InlineData("r = math.sumprod([], [])", "0")]
        [InlineData("r = math.sumprod([1.5, 2.0], [2.0, 3.0])", "9.0")]
        // ---- fma ----
        [InlineData("r = math.fma(2.0, 3.0, 4.0)", "10.0")]
        [InlineData("r = math.fma(0.0, 0.0, 0.0)", "0.0")]
        [InlineData("r = math.fma(1e308, 2.0, -1e308)", "1e+308")]
        public void Modern(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        // CPython's isqrt: exact on every size, and a big argument costs about one full-size division.
        [Fact]
        public void IsqrtExactAcrossSizes()
        {
            string body = "ok = True\nfor k in range(1, 300):\n    for n in (k, k * k, k * k - 1, k * k + 1, 3 ** k, 7 ** k + 5, (1 << k) - 1):\n"
                + "        s = math.isqrt(n)\n        if not (s * s <= n < (s + 1) * (s + 1)):\n            ok = False\n"
                + "r = (ok, math.isqrt(1 << 299990) == 1 << 149995, math.isqrt((1 << 299990) - 1) == (1 << 149995) - 1, math.isqrt(1), math.isqrt(3))";
            Assert.Equal("(True, True, True, 1, 1)", Body(body));
        }

        [Theory]
        [InlineData("math.isqrt(-1)", "ValueError")]
        [InlineData("math.comb(-1, 2)", "ValueError")]
        [InlineData("math.comb(5, -1)", "ValueError")]
        [InlineData("math.perm(-1)", "ValueError")]
        [InlineData("math.isclose(1, 2, rel_tol=-0.1)", "ValueError")]
        [InlineData("math.remainder(math.inf, 2)", "ValueError")]
        [InlineData("math.remainder(1, 0)", "ValueError")]
        [InlineData("math.dist([1, 2], [1, 2, 3])", "ValueError")]
        [InlineData("math.sumprod([1, 2], [1])", "ValueError")]
        [InlineData("math.fma(0.0, math.inf, 1.0)", "ValueError")]
        [InlineData("math.nextafter(1.0, 2.0, steps=-1)", "ValueError")]
        [InlineData("math.gcd(1.5)", "TypeError")]
        [InlineData("math.fma(1e308, 10.0, 0.0)", "OverflowError")]
        public void Errors(string body, string excType)
        {
            Assert.Equal(excType, Raises("x = " + body).Value.ExcType.Name);
        }

        [Fact]
        public void FmaInvalidWithNanReturnsNan()
        {
            Assert.Equal("True", Body("v = math.fma(0.0, math.inf, math.nan)\nr = (v != v)"));
        }

        [Fact]
        public void FmaFusedPrecision()
        {
            // Classic fused check: x*y overflows in double, but fma(x, y, z) is finite.
            Assert.Equal("1e+308", Body("r = math.fma(2.0, 1e308, -1e308)"));
        }
    }
}

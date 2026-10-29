using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/10 T-MNM-03..07: the math module (roundings/trig/pow/log/fsum/factorial).
    [Trait("Category", "Compatibility")]
    public class MathModuleTests
    {
        private const string Prelude = "import math\n";

        private static string R(string expr)
        {
            return Ev.ReprOf(Prelude, expr);
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(Prelude + body + "\n"));
        }

        [Theory]
        // ---- roundings / signs (T-MNM-03) ----
        [InlineData("math.ceil(-0.5)", "0")]
        [InlineData("math.floor(2.7)", "2")]
        [InlineData("math.trunc(-2.7)", "-2")]
        [InlineData("math.floor(True)", "1")]
        [InlineData("math.fabs(-3)", "3.0")]
        [InlineData("math.copysign(1.0, -0.0)", "-1.0")]
        [InlineData("math.fmod(-7, 3)", "-1.0")]
        [InlineData("math.modf(-2.5)", "(-0.5, -2.0)")]
        [InlineData("math.frexp(8.0)", "(0.5, 4)")]
        [InlineData("math.ldexp(1, 3)", "8.0")]
        [InlineData("math.sqrt(-0.0)", "-0.0")]
        [InlineData("math.sin(float('nan'))", "nan")]
        [InlineData("math.atan2(-0.0, -1.0)", "-3.141592653589793")]
        [InlineData("math.isinf(float('inf'))", "True")]
        [InlineData("math.isnan(5)", "False")]
        [InlineData("math.isfinite(2)", "True")]
        // ---- pow (T-MNM-04) ----
        [InlineData("math.pow(2.0, 0)", "1.0")]
        [InlineData("math.pow(float('nan'), 0)", "1.0")]
        [InlineData("math.pow(1.0, float('nan'))", "1.0")]
        [InlineData("math.pow(1.0, float('inf'))", "1.0")]
        [InlineData("math.pow(float('nan'), 2)", "nan")]
        [InlineData("math.pow(float('inf'), 2)", "inf")]
        [InlineData("math.pow(float('inf'), -1)", "0.0")]
        [InlineData("math.pow(float('-inf'), 3)", "-inf")]
        [InlineData("math.pow(float('-inf'), 2)", "inf")]
        [InlineData("math.pow(float('-inf'), -3)", "-0.0")]
        [InlineData("math.pow(2.0, float('inf'))", "inf")]
        [InlineData("math.pow(0.5, float('inf'))", "0.0")]
        [InlineData("math.pow(-1.0, float('inf'))", "1.0")]
        [InlineData("math.pow(-0.0, 3)", "-0.0")]
        [InlineData("math.pow(-0.0, 2)", "0.0")]
        [InlineData("math.pow(-2.0, 3.0)", "-8.0")]
        [InlineData("math.hypot(3, 4)", "5.0")]
        // ---- fsum / factorial (T-MNM-06/07) ----
        [InlineData("math.fsum([])", "0.0")]
        [InlineData("math.fsum([1, 2, 3])", "6.0")]
        [InlineData("math.fsum([0.1]*10)", "1.0")]
        [InlineData("math.fsum([1, 1e100, 1, -1e100])", "2.0")]
        [InlineData("math.fsum([1e16, 1, -1e16])", "1.0")]
        [InlineData("math.fsum([float('inf')])", "inf")]
        [InlineData("math.fsum([float('inf'), float('-inf')])", "nan")]
        [InlineData("math.fsum([float('nan'), 1.0])", "nan")]
        [InlineData("math.fsum(range(10001))", "50005000.0")]
        [InlineData("math.fsum([-0.0])", "0.0")]
        [InlineData("math.fsum([2.0**53, 1.0, -2.0**53])", "1.0")]
        [InlineData("math.factorial(0)", "1")]
        [InlineData("math.factorial(5)", "120")]
        [InlineData("math.factorial(20)", "2432902008176640000")]
        [InlineData("math.factorial(3.0)", "6")]
        [InlineData("math.factorial(True)", "1")]
        [InlineData("math.pi", "3.141592653589793")]
        public void MathBehavior(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        // tolerance-checked results
        [InlineData("abs(math.log(1) - 0.0) < 1e-15")]
        [InlineData("abs(math.log(math.e) - 1.0) < 1e-15")]
        [InlineData("abs(math.log(8, 2) - 3.0) < 1e-12")]
        [InlineData("abs(math.log10(1000) - 3.0) < 1e-12")]
        [InlineData("abs(math.log10(10**400) - 400.0) < 1e-9")]
        [InlineData("math.log2(8) == 3.0")]
        [InlineData("math.log2(2**100) == 100.0")]
        [InlineData("math.log2(0.5) == -1.0")]
        [InlineData("900.0 < math.log(10**400) < 922.0")]
        [InlineData("abs(math.asinh(1.0) - 0.8813735870195430) < 1e-15")]
        [InlineData("math.exp(0) == 1.0")]
        public void MathTolerance(string boolExpr)
        {
            Assert.Equal("True", R(boolExpr));
        }

        [Theory]
        [InlineData("x = math.ceil(float('inf'))", "OverflowError")]
        [InlineData("x = math.floor(float('nan'))", "ValueError")]
        [InlineData("x = math.fmod(3.5, 0)", "ValueError")]
        [InlineData("x = math.sqrt(-1)", "ValueError")]
        [InlineData("x = math.exp(1000)", "OverflowError")]
        [InlineData("x = math.asin(2)", "ValueError")]
        [InlineData("x = math.acosh(0.5)", "ValueError")]
        [InlineData("x = math.fabs(10**400)", "OverflowError")]
        [InlineData("x = math.pow(0.0, -1)", "ValueError")]
        [InlineData("x = math.pow(-2.0, 0.5)", "ValueError")]
        [InlineData("x = math.pow(10.0, 400.0)", "OverflowError")]
        [InlineData("x = math.log(0)", "ValueError")]
        [InlineData("x = math.log(-5)", "ValueError")]
        [InlineData("x = math.log(5, 1)", "ZeroDivisionError")]
        [InlineData("x = math.factorial(-1)", "ValueError")]
        [InlineData("x = math.factorial(3.5)", "ValueError")]
        [InlineData("x = math.factorial('5')", "TypeError")]
        [InlineData("x = math.fsum([1.0, 'x'])", "TypeError")]
        [InlineData("x = math.fsum([1e308, 1e308, -1e308, -1e308])", "OverflowError")]  // intermediate overflow (real CPython)
        public void MathErrors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Fact]
        public void FactorialHugeIsEngineAbort()
        {
            Assert.Throws<EngineAbort>(() => Ev.Run(Prelude + "x = math.factorial(10**9)\n"));
        }

        [Theory]
        // gamma/lgamma/erf/erfc remain excluded (no BCL implementation; a Lanczos port is deferred).
        [InlineData("math.gamma")]
        [InlineData("math.lgamma")]
        [InlineData("math.erf")]
        public void ExcludedNamesAreAttributeError(string expr)
        {
            Assert.Equal("AttributeError", Raises("x = " + expr).Value.ExcType.Name);
        }

        [Fact]
        public void FromImportSqrt()
        {
            Assert.Equal("2.0", Ev.Run("from math import sqrt\nr = sqrt(4)\n").Repr("r"));
        }

        [Fact]
        public void DomainErrorText()
        {
            string src = Prelude + "msg = ''\ntry:\n    math.sqrt(-1)\nexcept ValueError as e:\n    msg = str(e)\n";
            Assert.Equal("'math domain error'", Ev.Run(src).Repr("msg"));
        }
    }
}

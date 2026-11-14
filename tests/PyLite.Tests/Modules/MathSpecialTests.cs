using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // math.erf/erfc (fdlibm s_erf.c) and gamma/lgamma (CPython's Lanczos port). Every expected value is what
    // CPython 3.14 answers, to the bit; over a 1,900-point grid the two agree on 98% of the values and stay
    // within 2 ulps elsewhere (the platform exp/pow/sin differ in the last bit).
    [Trait("Category", "Compatibility")]
    public class MathSpecialTests
    {
        [Theory]
        [InlineData("erf(0.5)", "0.5204998778130465")]
        [InlineData("erf(1.0)", "0.8427007929497149")]
        [InlineData("erf(-1.0)", "-0.8427007929497149")]
        [InlineData("erf(2.5)", "0.999593047982555")]
        [InlineData("erf(1e-10)", "1.1283791670955126e-10")]
        [InlineData("erf(10.0)", "1.0")]
        [InlineData("erf(-0.0)", "-0.0")]
        [InlineData("erf(math.inf)", "1.0")]
        [InlineData("erfc(0.5)", "0.4795001221869535")]
        [InlineData("erfc(1.0)", "0.15729920705028513")]
        [InlineData("erfc(3.0)", "2.2090496998585438e-05")]
        [InlineData("erfc(10.0)", "2.0884875837625446e-45")]
        [InlineData("erfc(26.5)", "2.2109076642637343e-307")]
        [InlineData("erfc(30.0)", "0.0")]
        [InlineData("erfc(-math.inf)", "2.0")]
        [InlineData("gamma(0.5)", "1.7724538509055159")]
        [InlineData("gamma(1.5)", "0.886226925452758")]
        [InlineData("gamma(-0.5)", "-3.544907701811032")]
        [InlineData("gamma(-1.5)", "2.3632718012073544")]
        [InlineData("gamma(5.5)", "52.34277778455352")]
        [InlineData("gamma(10)", "362880.0")]
        [InlineData("gamma(30.0)", "8.841761993739703e+30")]
        [InlineData("gamma(100.5)", "9.320963104082718e+156")]
        [InlineData("gamma(171.5)", "9.483367566824801e+307")]
        [InlineData("gamma(-170.5)", "-3.312739521538607e-308")]
        [InlineData("gamma(1e-300)", "9.999999999999999e+299")]
        [InlineData("gamma(-250.5)", "-0.0")]
        [InlineData("gamma(math.inf)", "inf")]
        [InlineData("gamma(math.nan)", "nan")]
        [InlineData("lgamma(0.5)", "0.5723649429247004")]
        [InlineData("lgamma(-0.5)", "1.265512123484645")]
        [InlineData("lgamma(100)", "359.1342053695754")]
        [InlineData("lgamma(1e10)", "220258509288.81058")]
        [InlineData("lgamma(1e300)", "6.897755278982137e+302")]
        [InlineData("lgamma(3.0)", "0.693147180559945")]
        [InlineData("lgamma(-2.5)", "-0.05624371649767457")]
        [InlineData("lgamma(1e-300)", "690.7755278982137")]
        [InlineData("lgamma(-math.inf)", "inf")]
        [InlineData("lgamma(2.0)", "0.0")]
        public void ValuesAreCPythons(string call, string expected)
        {
            Assert.Equal(expected, Ev.Run("import math\nr = math." + call + "\n").Repr("r"));
        }

        // CPython's math_1a: a pole or -inf is a domain error, a result past the float range an overflow.
        [Theory]
        [InlineData("gamma(0.0)", "ValueError: expected a noninteger or positive integer, got 0.0")]
        [InlineData("gamma(-0.0)", "ValueError: expected a noninteger or positive integer, got -0.0")]
        [InlineData("gamma(-1.0)", "ValueError: expected a noninteger or positive integer, got -1.0")]
        [InlineData("gamma(-math.inf)", "ValueError: expected a noninteger or positive integer, got -inf")]
        [InlineData("gamma(172.0)", "OverflowError")]
        [InlineData("gamma(1e-310)", "OverflowError")]
        [InlineData("lgamma(0)", "ValueError: expected a noninteger or positive integer, got 0.0")]
        [InlineData("lgamma(-3.0)", "ValueError: expected a noninteger or positive integer, got -3.0")]
        [InlineData("lgamma(1.7e308)", "OverflowError")]
        [InlineData("erf('x')", "TypeError")]
        [InlineData("gamma(10 ** 400)", "OverflowError")]
        [InlineData("erf(x=1.0)", "TypeError")]
        public void ErrorsAreCPythons(string call, string expected)
        {
            string src = "import math\ntry:\n    r = math." + call + "\nexcept Exception as e:\n    r = type(e).__name__ + ': ' + str(e)\n";
            string got = Ev.Run(src).Repr("r");
            Assert.StartsWith("'" + expected, got);   // 3.14's texts for the domain errors
        }
    }
}

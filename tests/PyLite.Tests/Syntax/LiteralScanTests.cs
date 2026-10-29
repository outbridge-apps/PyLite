using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    // str, bytes and f-string literals share one scan skeleton (RequireLiteralChar / ClosingRun /
    // AppendLiteralChar), and Decimal and Fraction one number scanner (NumberScan). The rows below are
    // the edges that skeleton owns: a source newline inside a triple-quoted literal is one LF whatever
    // the file's line ending (bytes included, which used to keep the CR), a lone quote inside a
    // triple-quoted literal is content, and the number grammars trim, sign and read exponents alike.
    [Trait("Category", "Compatibility")]
    public class LiteralScanTests
    {
        [Theory]
        [InlineData("'''a\r\nb'''", "'a\\nb'")]
        [InlineData("b'''a\r\nb'''", "b'a\\nb'")]
        [InlineData("f'''a\r\n{1}b'''", "'a\\n1b'")]
        [InlineData("f'''{1}\r\n'''", "'1\\n'")]
        [InlineData("'''a'b''c'''", "\"a'b''c\"")]
        [InlineData("b'''a'b''c'''", "b\"a'b''c\"")]
        [InlineData("f'''a'b''{1}'''", "\"a'b''1\"")]
        [InlineData("rb'''a\\\r\nb'''", "b'a\\\\\\nb'")]
        public void ATripleQuotedLiteralReadsALineEndingAsOneLf(string expr, string expected)
        {
            Assert.Equal(expected, Outbridge.PyLite.Tests.Evaluator.Ev.Run("r = " + expr + "\n").Repr("r"));
        }

        [Fact]
        public void ALineInsideATripleQuotedBytesLiteralCountsForTheTraceback()
        {
            var engine = new Outbridge.PyLite.Hosting.ScriptEngine(new Outbridge.PyLite.Hosting.EngineOptions());
            Outbridge.PyLite.Hosting.RunResult r = engine.Run("x = b'''\r\n\r\n'''\r\nraise ValueError('here')\r\n", new Outbridge.PyLite.Hosting.RunRequest());
            Assert.False(r.Succeeded);
            Assert.Equal(4, r.Error.Line);
        }

        [Theory]
        [InlineData("Decimal(' 1e3 ')", "Decimal('1E+3')")]
        [InlineData("Decimal('-.5E-2')", "Decimal('-0.005')")]
        [InlineData("Decimal('1e')", "InvalidOperation(\"[<class 'decimal.ConversionSyntax'>]\")")]
        [InlineData("Decimal('1_0')", "Decimal('10')")]   // PEP 515: underscores dropped, as CPython does
        [InlineData("Fraction(' -1/2 ')", "Fraction(-1, 2)")]
        [InlineData("Fraction('+.25e1')", "Fraction(5, 2)")]
        [InlineData("Fraction('1e')", "ValueError(\"Invalid literal for Fraction: '1e'\")")]
        [InlineData("Fraction('1/')", "ValueError(\"Invalid literal for Fraction: '1/'\")")]
        public void DecimalAndFractionScanNumbersAlike(string expr, string expected)
        {
            string src = "from decimal import Decimal, InvalidOperation\nfrom fractions import Fraction\ntry:\n    r = " + expr
                + "\nexcept Exception as e:\n    r = e\n";
            Assert.Equal(expected, Outbridge.PyLite.Tests.Evaluator.Ev.Run(src).Repr("r"));
        }
    }
}

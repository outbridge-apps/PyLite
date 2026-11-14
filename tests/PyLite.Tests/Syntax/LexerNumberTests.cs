using System;
using System.Numerics;
using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Syntax
{
    [Trait("Category", "Compatibility")]
    public class LexerNumberTests
    {
        private static BigInteger Int(string src)
        {
            var t = L.Go(src);
            Assert.Equal(TokenKind.IntLiteral, t[0].Kind);
            return (BigInteger)t[0].Value;
        }

        private static double Flt(string src)
        {
            var t = L.Go(src);
            Assert.Equal(TokenKind.FloatLiteral, t[0].Kind);
            return (double)t[0].Value;
        }

        [Theory]
        [InlineData("0", 0)]
        [InlineData("000", 0)]
        [InlineData("0xFF", 255)]
        [InlineData("0Xab", 171)]
        [InlineData("0o17", 15)]
        [InlineData("0b101", 5)]
        public void Integers(string src, int expected) => Assert.Equal((BigInteger)expected, Int(src));

        [Fact]
        public void BigDecimal()
            => Assert.Equal(BigInteger.Parse("123456789012345678901234567890"), Int("123456789012345678901234567890"));

        [Fact] public void ExactPastDouble() => Assert.Equal((BigInteger)9007199254740993L, Int("9007199254740993"));

        // CPython 3.14's tokenizer texts
        [Theory]
        [InlineData("0777", "leading zeros in decimal integer literals are not permitted; use an 0o prefix for octal integers")]
        [InlineData("0x", "invalid hexadecimal literal")]
        [InlineData("0o8", "invalid digit '8' in octal literal")]
        [InlineData("0b2", "invalid digit '2' in binary literal")]
        [InlineData("1e", "invalid decimal literal")]
        [InlineData("1e+", "invalid decimal literal")]
        public void InvalidNumber(string src, string message)
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go(src));
            Assert.Equal(message, e.Message);
        }

        [Theory]  // PEP 515 (RFC-003 §2.1): underscores between digits are accepted
        [InlineData("1_000", 1000)]
        [InlineData("1_000_000", 1000000)]
        [InlineData("0xFF_FF", 65535)]
        [InlineData("0b1010_0101", 165)]
        [InlineData("0o1_7", 15)]
        public void UnderscoresAccepted(string src, int expected) => Assert.Equal((BigInteger)expected, Int(src));

        [Fact]
        public void UnderscoreFloat() => Assert.Equal(1000.5, Flt("1_000.5"));

        [Theory]  // dangling / doubled / misplaced '_' is still an error
        [InlineData("1_")]
        [InlineData("1__0")]
        [InlineData("_1")]     // this is actually an identifier start, not a number
        public void UnderscoreMisplaced(string src)
        {
            // "_1" lexes as a NAME, not a number -> the Int() helper's IntLiteral assert fails; the rest
            // throw a syntax error. Either way it does not lex to the integer 1_.
            if (src == "_1")
            {
                var t = L.Go(src);
                Assert.NotEqual(TokenKind.IntLiteral, t[0].Kind);
                return;
            }
            Assert.Throws<PySyntaxErrorException>(() => L.Go(src));
        }

        [Fact]
        public void Complex()
        {
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go("1j"));
            Assert.Equal("complex literals are not supported in this dialect", e.Message);
        }

        [Theory]
        [InlineData("1.", 1.0)]
        [InlineData(".5", 0.5)]
        [InlineData("1e10", 1e10)]
        [InlineData("1E+5", 100000.0)]
        [InlineData("1.5e-3", 0.0015)]
        [InlineData("0777.5", 777.5)]
        public void Floats(string src, double expected) => Assert.Equal(expected, Flt(src));

        [Fact] public void FloatOverflowIsInfinity() => Assert.True(double.IsPositiveInfinity(Flt("1e400")));
        [Fact] public void FloatUnderflowIsZero() => Assert.Equal(0.0, Flt("1e-400"));

        [Fact]
        public void PointOne_ExactBits()
            => Assert.Equal(0x3FB999999999999AL, BitConverter.DoubleToInt64Bits(Flt("0.1")));

        [Fact]
        public void HangLiteralParses()
            => Assert.False(double.IsNaN(Flt("2.2250738585072011e-308")));

        [Fact]
        public void IntThenKeyword()
            => Assert.Equal("IntLiteral KwIf Name KwElse Name NewLine EndOfFile", L.Kinds("1if x else y"));

        [Fact]
        public void DoubleDot()
        {
            var t = L.Go("1..5");
            Assert.Equal(1.0, (double)t[0].Value);
            Assert.Equal(0.5, (double)t[1].Value);
        }

        [Fact]
        public void DigitCap()
        {
            new Lexer(new string('9', 4300), SyntaxLimits.Default).Next(); // ok
            var e = Assert.Throws<PySyntaxErrorException>(() => L.Go(new string('9', 4301)));
            Assert.Equal("integer literal too long (limit is 4300 digits)", e.Message);
        }

        // The power-of-two bases have no digit limit in CPython; the long ones are parsed in one linear pass.
        [Theory]
        [InlineData("0x", 'f', 16)]
        [InlineData("0o", '7', 8)]
        [InlineData("0b", '1', 2)]
        public void RadixLiteralsHaveNoDigitCap(string prefix, char digit, int radix)
        {
            int log2 = radix == 16 ? 4 : radix == 8 ? 3 : 1;
            var v = (System.Numerics.BigInteger)L.Go(prefix + new string(digit, 100000))[0].Value;
            Assert.Equal((System.Numerics.BigInteger.One << (100000 * log2)) - 1, v);
            Assert.Equal(new System.Numerics.BigInteger(0x2DF), (System.Numerics.BigInteger)L.Go(prefix == "0x" ? "0x2df" : prefix == "0o" ? "0o1337" : "0b1011011111")[0].Value);
        }
    }
}

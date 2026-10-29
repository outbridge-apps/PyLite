using System.Numerics;
using System.Text;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    // The evaluator's small-int shortcut (EvalBinOp / CompareOne / augmented assign) must be
    // bit-identical to the full protocol. The grid cross-checks a script-computed result table
    // against an INDEPENDENT C# oracle implementing Python semantics (floor division truncates
    // toward -inf, mod takes the divisor's sign) over BigInteger — not the engine's own code.
    [Trait("Category", "Compatibility")]
    public class SmallIntBinOpTests
    {
        private static BigInteger PyFloorDiv(BigInteger a, BigInteger b)
        {
            BigInteger q = BigInteger.Divide(a, b);
            if (a.Sign * b.Sign < 0 && q * b != a)
                q -= 1;
            return q;
        }

        private static BigInteger PyMod(BigInteger a, BigInteger b)
        {
            BigInteger m = BigInteger.Remainder(a, b);
            if (!m.IsZero && m.Sign != b.Sign)
                m += b;
            return m;
        }

        [Fact]
        public void GridMatchesPythonSemanticsOracle()
        {
            var script = new StringBuilder(
                "res = []\n"
                + "for a in range(-9, 10):\n"
                + "    for b in range(-9, 10):\n"
                + "        res.append(a + b)\n"
                + "        res.append(a - b)\n"
                + "        res.append(a * b)\n"
                + "        res.append(1 if a < b else 0)\n"
                + "        res.append(1 if a <= b else 0)\n"
                + "        res.append(1 if a == b else 0)\n"
                + "        if b != 0:\n"
                + "            res.append(a // b)\n"
                + "            res.append(a % b)\n"
                + "r = repr(res)\n");
            var oracle = new StringBuilder("[");
            bool first = true;
            for (int a = -9; a <= 9; a++)
                for (int b = -9; b <= 9; b++)
                {
                    void Add(BigInteger v)
                    {
                        if (!first)
                            oracle.Append(", ");
                        first = false;
                        oracle.Append(v.ToString());
                    }
                    Add(a + b);
                    Add(a - b);
                    Add((BigInteger)a * b);
                    Add(a < b ? 1 : 0);
                    Add(a <= b ? 1 : 0);
                    Add(a == b ? 1 : 0);
                    if (b != 0)
                    {
                        Add(PyFloorDiv(a, b));
                        Add(PyMod(a, b));
                    }
                }
            oracle.Append(']');
            Assert.Equal(oracle.ToString(), Ev.Run(script.ToString()).Repr("r").Trim('\''));
        }

        // Overflow / edge inputs must fall back to the exact big path and still be right.
        [Theory]
        [InlineData("r = 2**62 + 2**62", "9223372036854775808")]
        [InlineData("r = -(2**63) - 1", "-9223372036854775809")]
        [InlineData("r = (2**31) * (2**31) * 4", "18446744073709551616")]
        [InlineData("r = (-2**63) // -1", "9223372036854775808")]
        [InlineData("r = (-2**63) % -1", "0")]
        [InlineData("r = 1 << 100", "1267650600228229401496703205376")]
        [InlineData("r = -5 >> 200", "-1")]
        [InlineData("r = 2**10", "1024")]
        [InlineData("r = 5 / 2", "2.5")]
        [InlineData("r = 7 // 2", "3")]
        [InlineData("r = -7 // 2", "-4")]
        [InlineData("r = 7 % -2", "-1")]
        [InlineData("r = -7 % 2", "1")]
        public void EdgesAndFallbacks(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        [Theory]
        [InlineData("r = 1 // 0", "integer division or modulo by zero")]
        [InlineData("r = 1 % 0", "integer division or modulo by zero")]
        [InlineData("r = 1 / 0", "division by zero")]
        [InlineData("r = 1 << -1", "negative shift count")]
        public void ErrorMessagesUnchanged(string src, string message)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(src));
            Assert.Equal(message, ex.Value.GetMessageText(Rt.Ctx()));
        }

        // Bools never enter the shortcut (Kind check): arithmetic promotes, & | ^ stay bool.
        [Theory]
        [InlineData("r = True + True", "2")]
        [InlineData("r = True & True", "True")]
        [InlineData("r = True | False", "True")]
        [InlineData("r = True ^ True", "False")]
        [InlineData("r = True < 2", "True")]
        public void BoolSemanticsUntouched(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void AugmentedAssignChain()
        {
            var r = Ev.Run("x = 5\nx += 3\nx -= 10\nx *= -2\nx //= 3\nx %= 4\nr = x\n");
            Assert.Equal("1", r.Repr("r"));   // 5+3=8; -2; 4; 4//3=1; 1%4=1
        }

        [Fact]
        public void AugmentedTypeErrorStillAugmented()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("x = 1\nx += 'a'"));
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void ChainShortCircuitSkipsLaterOperands()
        {
            // 3 < 2 is False -> 1//0 must never evaluate.
            Assert.Equal("False", Ev.Run("r = 3 < 2 < (1 // 0)").Repr("r"));
        }

        // Cross-representation comparisons still work (big vs small never conflated).
        [Theory]
        [InlineData("r = 10**30 == 10**30", "True")]
        [InlineData("r = 1 == 10**30", "False")]
        [InlineData("r = 1 < 10**30", "True")]
        [InlineData("r = -(10**30) < 1", "True")]
        [InlineData("r = 1 == 1.0", "True")]
        [InlineData("r = 1 < 1.5", "True")]
        public void MixedRepresentationCompares(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r"));
        }
    }
}

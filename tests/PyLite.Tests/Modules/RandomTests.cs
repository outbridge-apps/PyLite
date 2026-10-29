using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/10 T-MNM-19..24: the random module (wraps System.Random; determinism per seed, API + validation).
    [Trait("Category", "Compatibility")]
    public class RandomTests
    {
        private const string P = "import random\n";

        private static string R(string boolExpr)
        {
            return Ev.ReprOf(P, boolExpr);
        }

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        // ---- ranges / properties ----
        [InlineData("random.seed(1) or all(random.getrandbits(1) in (0, 1) for _ in range(50))")]
        [InlineData("random.seed(1) or random.getrandbits(64) < 2**64")]
        [InlineData("random.seed(1) or random.getrandbits(33).bit_length() <= 33")]
        [InlineData("random.seed(1) or all(0 <= random.randrange(10) < 10 for _ in range(50))")]
        [InlineData("random.seed(1) or all(5 <= random.randrange(5, 10) < 10 for _ in range(50))")]
        [InlineData("random.seed(1) or all(random.randrange(0, 10, 2) % 2 == 0 for _ in range(50))")]
        [InlineData("random.seed(1) or all(1 <= random.randint(1, 6) <= 6 for _ in range(50))")]
        [InlineData("random.randint(5, 5) == 5")]
        [InlineData("random.seed(1) or all(random.choice([1, 2, 3]) in (1, 2, 3) for _ in range(50))")]
        [InlineData("random.seed(1) or random.choice(range(100)) in range(100)")]
        [InlineData("random.seed(1) or 1.0 <= random.uniform(1, 2) <= 2.0")]
        [InlineData("random.triangular(5, 5, 5) == 5.0")]
        [InlineData("random.seed(1) or 0.0 <= random.triangular() <= 1.0")]
        [InlineData("random.seed(1) or random.expovariate(1.0) > 0")]
        [InlineData("random.seed(1) or random.gammavariate(2.0, 1.0) > 0")]
        [InlineData("random.seed(1) or random.gammavariate(1.0, 1.0) > 0")]
        [InlineData("random.seed(1) or random.gammavariate(0.5, 1.0) > 0")]
        [InlineData("random.seed(1) or 0.0 < random.betavariate(2.0, 3.0) < 1.0")]
        [InlineData("isinstance(random.gauss(0, 1), float)")]
        [InlineData("isinstance(random.normalvariate(0, 1), float)")]
        public void RandomProperties(string boolExpr)
        {
            Assert.Equal("True", R(boolExpr));
        }

        [Theory]
        // ---- determinism per seed ----
        [InlineData("random.seed(42)\na = random.random()\nrandom.seed(42)\nb = random.random()\nr = (a == b)\n")]
        [InlineData("random.seed(-5)\na = random.random()\nrandom.seed(5)\nb = random.random()\nr = (a == b)\n")]
        [InlineData("random.seed(True)\na = random.random()\nrandom.seed(1)\nb = random.random()\nr = (a == b)\n")]
        [InlineData("random.seed(42)\na = [random.random() for _ in range(5)]\nrandom.seed(42)\nb = [random.random() for _ in range(5)]\nr = (a == b)\n")]
        [InlineData("random.seed(1)\na = random.uniform(0, 1)\nrandom.seed(1)\nb = random.random()\nr = (a == b)\n")]
        [InlineData("random.seed(7)\nx = [1,2,3,4,5]\nrandom.shuffle(x)\nr = (sorted(x) == [1,2,3,4,5])\n")]
        [InlineData("x = []\nrandom.shuffle(x)\nr = (x == [])\n")]
        [InlineData("random.seed(1)\ns = random.sample(range(10), 3)\nr = (len(s) == 3 and len(set(s)) == 3 and all(0 <= v < 10 for v in s))\n")]
        [InlineData("random.seed(1)\ns = random.sample(range(10**8), 3)\nr = (len(set(s)) == 3)\n")]
        [InlineData("p = [1,2,3]\nrandom.seed(1)\nrandom.sample(p, 2)\nr = (p == [1,2,3])\n")]
        [InlineData("r = (random.sample([1,2,3], 0) == [])\n")]
        [InlineData("random.seed(1)\nx = list(range(6))\nrandom.shuffle(x, random=lambda: 0.5)\nr = (sorted(x) == list(range(6)))\n")]
        public void RandomDeterminism(string body)
        {
            Assert.Equal("True", Body(body));
        }

        [Theory]
        [InlineData("x = random.getrandbits(-1)", "ValueError")]
        [InlineData("x = random.randrange(0)", "ValueError")]
        [InlineData("x = random.randrange(5, 5)", "ValueError")]
        [InlineData("x = random.randrange(0, 10, 0)", "ValueError")]
        [InlineData("x = random.randrange(3.0)", "TypeError")]
        [InlineData("x = random.randint(6, 1)", "ValueError")]
        [InlineData("x = random.choice([])", "IndexError")]
        [InlineData("x = random.sample([1,2,3], 4)", "ValueError")]
        [InlineData("x = random.sample([1,2,3], -1)", "ValueError")]
        [InlineData("x = random.expovariate(0)", "ZeroDivisionError")]
        [InlineData("x = random.gammavariate(0, 1)", "ValueError")]
        [InlineData("x = random.gammavariate(1, 0)", "ValueError")]
        [InlineData("x = random.getstate()", "AttributeError")]
        [InlineData("x = random.Random", "AttributeError")]
        public void RandomErrors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Fact]
        public void GetRandBitsMessage()
        {
            string src = P + "msg = ''\ntry:\n    random.getrandbits(-1)\nexcept ValueError as e:\n    msg = str(e)\nz = random.getrandbits(0)\n";
            var r = Ev.Run(src);
            Assert.Equal("'number of bits must be non-negative'", r.Repr("msg"));
            Assert.Equal("0", r.Repr("z"));
        }
    }
}

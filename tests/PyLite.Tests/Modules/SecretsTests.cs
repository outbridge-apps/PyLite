using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // secrets: the shapes and ranges (the values are random, from the OS RNG); compare_digest is hmac's.
    [Trait("Category", "Compatibility")]
    public class SecretsTests
    {
        private const string Imp = "import secrets\nfrom secrets import token_bytes, token_hex, token_urlsafe, randbelow, randbits, choice, compare_digest\n";

        [Theory]
        [InlineData("(len(token_bytes()), len(token_bytes(16)), type(token_bytes(4)).__name__, token_bytes(0))", "(32, 16, 'bytes', b'')")]
        [InlineData("(len(token_hex()), len(token_hex(8)), all([c in '0123456789abcdef' for c in token_hex(50)]))", "(64, 16, True)")]
        [InlineData("(len(token_urlsafe(32)), '=' in token_urlsafe(), all([c.isalnum() or c in '-_' for c in token_urlsafe(100)]))", "(43, False, True)")]
        [InlineData("token_hex(8) != token_hex(8)", "True")]
        [InlineData("all([0 <= randbelow(10) < 10 for _ in range(200)])", "True")]
        [InlineData("(randbelow(1), len({randbelow(3) for _ in range(300)}))", "(0, 3)")]
        [InlineData("(randbits(0), all([0 <= randbits(7) < 128 for _ in range(200)]), randbits(200) < 2 ** 200, randbits(64).bit_length() <= 64)", "(0, True, True, True)")]
        [InlineData("choice('a') + choice(['b']) + choice(('c',))", "'abc'")]
        [InlineData("len({choice([1, 2, 3]) for _ in range(300)})", "3")]
        [InlineData("(compare_digest('a', 'a'), compare_digest(b'a', b'b'), secrets.DEFAULT_ENTROPY)", "(True, False, 32)")]
        [InlineData("randbelow(10 ** 30) < 10 ** 30", "True")]
        public void Surface(string expr, string expected)
        {
            Assert.Equal(expected, Ev.Run(Imp + "r = (" + expr + ")\n").Repr("r"));
        }

        [Theory]
        [InlineData("randbelow(0)", "ValueError", "Upper bound must be positive.")]
        [InlineData("randbelow(-1)", "ValueError", "Upper bound must be positive.")]
        [InlineData("randbits(-1)", "ValueError", "number of bits must be non-negative")]
        [InlineData("choice([])", "IndexError", "Cannot choose from an empty sequence")]
        [InlineData("choice(5)", "TypeError", "object of type 'int' has no len()")]
        [InlineData("token_bytes(-1)", "ValueError", "negative argument not allowed")]
        [InlineData("token_hex('8')", "TypeError", "'str' object cannot be interpreted as an integer")]
        public void Errors(string expr, string exc, string message)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Imp + "r = " + expr + "\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
            Assert.Equal(message, ex.Value.GetMessageText(Rt.Ctx()));
        }
    }
}

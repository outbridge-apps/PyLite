using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // The str/bytes hash seed is FIXED by default (EngineOptions.StrHashSeed), so hash('a') is the same
    // number in every run, in every engine and after the host process restarts. The engine is deterministic
    // everywhere else it can be, and a script that stores a hash beside its data would otherwise find it
    // stale after a restart. A host that would rather have hash-flooding resistance sets the seed to
    // null and accepts that a hash no longer survives the process.
    [Trait("Category", "Sandbox")]
    public class HashSeedTests
    {
        private static string Hash(ScriptEngine e, string expr)
        {
            RunResult r = e.Run("x = " + expr + "\n", new RunRequest { ScalarOutputNames = new[] { "x" } });
            Assert.True(r.Succeeded);
            return r.ScalarsOut["x"];
        }

        [Theory]
        [InlineData("hash('a')")]
        [InlineData("hash('the quick brown fox')")]
        [InlineData("hash(b'a')")]
        [InlineData("hash(('a', 'b'))")]
        [InlineData("hash(frozenset(['a', 'b']))")]
        public void TheSameStringHashesTheSameInEveryEngine(string expr)
        {
            var first = new ScriptEngine(new EngineOptions());
            var second = new ScriptEngine(new EngineOptions());
            string a = Hash(first, expr);
            Assert.Equal(a, Hash(first, expr));     // stable across runs of one engine
            Assert.Equal(a, Hash(second, expr));    // and across engines, which is what a recycle makes
        }

        [Fact]
        public void ANullSeedGoesBackToARandomOnePerEngine()
        {
            var fixedSeed = new ScriptEngine(new EngineOptions());
            string baseline = Hash(fixedSeed, "hash('a')");

            // 4 bytes of cryptographic seed: the chance that four engines all land on the default is nil
            bool anyDiffers = false;
            for (int i = 0; i < 4 && !anyDiffers; i++)
            {
                var random = new ScriptEngine(new EngineOptions { StrHashSeed = null });
                anyDiffers = Hash(random, "hash('a')") != baseline;
            }
            Assert.True(anyDiffers, "StrHashSeed = null should seed each engine afresh");
        }

        [Fact]
        public void AnExplicitSeedIsHonoured()
        {
            var a = new ScriptEngine(new EngineOptions { StrHashSeed = 12345u });
            var b = new ScriptEngine(new EngineOptions { StrHashSeed = 12345u });
            var c = new ScriptEngine(new EngineOptions { StrHashSeed = 54321u });
            Assert.Equal(Hash(a, "hash('a')"), Hash(b, "hash('a')"));
            Assert.NotEqual(Hash(a, "hash('a')"), Hash(c, "hash('a')"));
        }

        // Numbers were never seeded: the dict invariant hash(1) == hash(1.0) == hash(True) requires it.
        [Fact]
        public void NumericHashesAreUnseeded()
        {
            var e = new ScriptEngine(new EngineOptions { StrHashSeed = null });
            Assert.Equal("True", Hash(e, "hash(1) == hash(1.0) == hash(True)"));
            Assert.Equal("1", Hash(e, "hash(1)"));
        }
    }
}

using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modern
{
    // The str.is* predicates walk code points: a surrogate pair is one character, so an emoji is
    // printable, an astral letter is alpha and cased, and a lone surrogate fails every test (CPython).
    // The string model stays UTF-16 (len counts units); only the predicates changed.
    [Trait("Category", "Compatibility")]
    public class StrPredicateAstralTests
    {
        [Theory]
        [InlineData("'\\U0001F600'.isprintable()", "True")]
        [InlineData("'ab\\U0001F600cd'.isprintable()", "True")]
        [InlineData("('\\U0001F600'.isalpha(), '\\U0001F600'.isalnum(), '\\U0001F600'.isspace())", "(False, False, False)")]
        [InlineData("('\\U0001D400'.isalpha(), '\\U0001D400'.isupper(), '\\U0001D41A'.islower(), '\\U0001D400'.isalnum())", "(True, True, True, True)")]   // MATHEMATICAL BOLD CAPITAL A / SMALL A
        [InlineData("('\\U00010400'.isupper(), '\\U00010428'.islower(), '\\U00010400\\U00010428'.istitle())", "(True, True, True)")]                      // Deseret
        [InlineData("('\\U0001D7CE'.isdigit(), '\\U0001D7CE'.isdecimal(), '\\U0001D7CE'.isnumeric())", "(True, True, True)")]                            // MATHEMATICAL BOLD DIGIT ZERO
        [InlineData("('\\U00010107'.isnumeric(), '\\U00010107'.isdigit())", "(True, False)")]                                                            // AEGEAN NUMBER ONE
        [InlineData("('\\ud83d'.isprintable(), '\\ud83d'.isalpha(), 'a\\ud83d'.isprintable())", "(False, False, False)")]
        [InlineData("('Ab\\U0001F600'.istitle(), '\\U0001F600'.islower(), 'a\\U0001F600'.islower())", "(True, False, True)")]
        [InlineData("('\\U0001F600'.isascii(), '\\U0001F600'.isidentifier(), '\\U0001D400b'.isidentifier())", "(False, False, True)")]
        [InlineData("(len('\\U0001F600'), '\\U0001F600'.isprintable())", "(2, True)")]   // the model is unchanged: two units, one printable character
        public void Predicates(string expr, string expected)
        {
            Assert.Equal(expected, Ev.Run("r = (" + expr + ")\n").Repr("r"));
        }
    }
}

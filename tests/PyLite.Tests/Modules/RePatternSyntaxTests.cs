using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // Pattern syntax .NET does not share with Python, rewritten by RegexTranslator (closed 2026-09-11):
    // possessive quantifiers, the open lower bound {,n}, and \N{NAME}.
    [Trait("Category", "Compatibility")]
    public class RePatternSyntaxTests
    {
        private static string R(string expr)
        {
            return Ev.ReprOf("import re\n", expr);
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run("import re\n" + body + "\n"));
        }

        // A possessive quantifier (3.11) is an atomic group: it never gives back what it matched.
        [Theory]
        [InlineData(@"re.search(r'a*+b', 'aaab').group()", "'aaab'")]
        [InlineData(@"re.search(r'a*+a', 'aaa')", "None")]                  // nothing left to give back
        [InlineData(@"re.search(r'a++b', 'aaab').group()", "'aaab'")]
        [InlineData(@"re.search(r'a++', 'aaa').group()", "'aaa'")]
        [InlineData(@"re.search(r'a?+b', 'ab').group()", "'ab'")]
        [InlineData(@"re.match(r'a?+a', 'a')", "None")]                    // the one 'a' is not given back
        [InlineData(@"re.search(r'a{1,3}+b', 'aab').group()", "'aab'")]
        [InlineData(@"re.search(r'a{2}+b', 'aab').group()", "'aab'")]
        [InlineData(@"re.search(r'[ab]*+c', 'abac').group()", "'abac'")]
        [InlineData(@"re.search(r'[ab]*+a', 'aba')", "None")]
        [InlineData(@"re.search(r'(ab)*+c', 'ababc').group()", "'ababc'")]
        [InlineData(@"re.search(r'(ab)*+c', 'ababc').group(1)", "'ab'")]
        [InlineData(@"re.search(r'\d*+x', '123x').group()", "'123x'")]
        [InlineData(@"re.match(r'(?:ab|a)*+b', 'aba')", "None")]           // 'aba' is eaten whole, no 'b' left
        [InlineData(@"re.match(r'a*+a', 'aaa')", "None")]
        [InlineData(@"re.match(r'a*a', 'aaa').group()", "'aaa'")]          // greedy DOES give one back
        [InlineData(@"re.fullmatch(r'\w*+', 'abc').group()", "'abc'")]
        // the lazy form is spelled the same in .NET and must stay lazy
        [InlineData(@"re.search(r'a*?b', 'aaab').group()", "'aaab'")]
        [InlineData(@"re.findall(r'a+?', 'aaa')", "['a', 'a', 'a']")]
        [InlineData(@"re.match(r'<.*?>', '<a><b>').group()", "'<a>'")]
        public void PossessiveQuantifiers(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        // {,n} means {0,n}; .NET would read the brace as a literal.
        [Theory]
        [InlineData(@"re.findall(r'a{,3}', 'aaaa')", "['aaa', 'a', '']")]
        [InlineData(@"re.match(r'a{,2}', 'aaa').group()", "'aa'")]
        [InlineData(@"re.match(r'a{,0}', 'aaa').group()", "''")]
        [InlineData(@"re.match(r'a{,}', 'aaa').group()", "'aaa'")]
        [InlineData(@"re.search(r'a{,3}+b', 'aab').group()", "'aab'")]
        // the forms .NET already understands are untouched
        [InlineData(@"re.match(r'a{2}', 'aaa').group()", "'aa'")]
        [InlineData(@"re.match(r'a{2,}', 'aaa').group()", "'aaa'")]
        [InlineData(@"re.match(r'a{2,3}', 'aaaa').group()", "'aaa'")]
        // a brace that is not a repeat stays a literal, in both engines
        [InlineData(@"re.findall(r'x{}', 'x{}')", "['x{}']")]
        [InlineData(@"re.findall(r'x{a}', 'x{a}')", "['x{a}']")]
        [InlineData(@"re.findall(r'x{1', 'x{1')", "['x{1']")]
        public void OpenLowerBound(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        // \N{NAME} in a pattern (3.8), off the same table the lexer uses for string literals.
        [Theory]
        [InlineData(@"ord(re.search(r'\N{DEGREE SIGN}', '5°').group())", "176")]
        [InlineData(@"[ord(c) for c in re.findall(r'[\N{DEGREE SIGN}x]', 'x°y')]", "[120, 176]")]
        [InlineData(@"re.findall(r'\N{FULL STOP}', 'a.b')", "['.']")]            // no metacharacter leaks
        [InlineData(@"re.findall(r'.', 'a.b')", "['a', '.', 'b']")]
        [InlineData(@"bool(re.search(r'\N{NUL}', '\x00'))", "True")]             // an alias
        [InlineData(@"ord(re.search(r'(?x) \N{DEGREE SIGN}  # deg', '5°').group())", "176")]
        [InlineData(@"re.search(r'\N{GRINNING FACE}', '\U0001F600').group() == '\U0001F600'", "True")]
        [InlineData(@"re.findall(r'[\N{GRINNING FACE}]', '\U0001F600') != []", "True")]
        [InlineData(@"re.search(r'\N{DEGREE SIGN}+', '5°°').group() == '°°'", "True")]
        public void NamedCharacterEscape(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData(@"re.compile(r'\N{NO SUCH NAME}')", "undefined character name 'NO SUCH NAME'")]
        [InlineData(@"re.compile(r'\N{DEGREE')", "missing }, unterminated name")]
        [InlineData(@"re.compile(r'\Nx')", @"bad escape \N")]                    // bare \N is still an error
        public void NamedCharacterEscapeErrors(string expr, string message)
        {
            var ex = Raises("r = " + expr);
            Assert.Equal("error", ex.Value.ExcType.Name);
            Assert.Equal(message, ex.Value.GetMessageText(Rt.Ctx()));
        }
    }
}

using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // textwrap: wrap/fill/shorten with TextWrapper's options, dedent, indent. Expectations are CPython's
    // (Lib/test/test_textwrap.py).
    [Trait("Category", "Compatibility")]
    public class TextwrapTests
    {
        private const string Imp = "import textwrap\nfrom textwrap import wrap, fill, shorten, dedent, indent\n";
        private const string Text = "text = \"Hello there, how are you this fine day?  I'm glad to hear it!\"\n";

        [Theory]
        [InlineData(Text + "r = wrap(text, 12)", "['Hello there,', 'how are you', 'this fine', \"day?  I'm\", 'glad to hear', 'it!']")]
        [InlineData(Text + "r = wrap(text, 42)", "['Hello there, how are you this fine day?', \"I'm glad to hear it!\"]")]
        [InlineData(Text + "r = wrap(text, 80)", "[\"Hello there, how are you this fine day?  I'm glad to hear it!\"]")]
        [InlineData(Text + "r = fill(text, 12)", "\"Hello there,\\nhow are you\\nthis fine\\nday?  I'm\\nglad to hear\\nit!\"")]
        [InlineData("r = wrap('  This is a  paragraph that\\n\\tcontains tabs   and spaces.  ', 30)", "['  This is a  paragraph that', 'contains tabs   and spaces.']")]   // leading whitespace of the first line is kept (CPython)
        [InlineData("r = wrap('this-is-a-useful-feature-for-reformatting-posts-from-tim-peters', 20)", "['this-is-a-useful-', 'feature-for-', 'reformatting-posts-', 'from-tim-peters']")]
        [InlineData("r = wrap('this-is-a-useful-feature-for-reformatting-posts-from-tim-peters', 20, break_on_hyphens=False)", "['this-is-a-useful-fea', 'ture-for-reformattin', 'g-posts-from-tim-pet', 'ers']")]
        [InlineData("r = wrap('Em-dashes should be written -- thus.', 25)", "['Em-dashes should be', 'written -- thus.']")]
        [InlineData("r = wrap('Did you say \"supercalifragilisticexpialidocious?\" How *do* you spell that odd word, anyways?', 30)",
            "['Did you say \"supercalifragilis', 'ticexpialidocious?\" How *do*', 'you spell that odd word,', 'anyways?']")]
        [InlineData("r = wrap('Did you say \"supercalifragilisticexpialidocious?\" How *do* you spell that odd word, anyways?', 30, break_long_words=False)",
            "['Did you say', '\"supercalifragilisticexpialidocious?\"', 'How *do* you spell that odd', 'word, anyways?']")]
        [InlineData("r = wrap('This is a short line.', 30, initial_indent='* ', subsequent_indent='  ')", "['* This is a short line.']")]
        [InlineData("r = fill('This is a short line that wraps once.', 20, initial_indent='* ', subsequent_indent='  ')", "'* This is a short\\n  line that wraps\\n  once.'")]
        [InlineData("r = wrap('A short line.  Note the single space.', 30, fix_sentence_endings=True)", "['A short line.  Note the single', 'space.']")]
        [InlineData("r = wrap('   ', 5)", "[]")]
        [InlineData("r = wrap('', 5)", "[]")]
        [InlineData("r = wrap('one two three four five', 9, max_lines=2)", "['one two', '[...]']")]   // 'three [...]' does not fit 9 (CPython)
        [InlineData("r = wrap('one two three four five', 9, max_lines=2, placeholder='...')", "['one two', 'three...']")]
        [InlineData(Text + "r = wrap(text, 12, max_lines=2)", "['Hello there,', 'how [...]']")]
        [InlineData(Text + "r = wrap(text, 12, max_lines=3)", "['Hello there,', 'how are you', 'this [...]']")]
        [InlineData(Text + "r = wrap(text, 12, max_lines=6)", "['Hello there,', 'how are you', 'this fine', \"day?  I'm\", 'glad to hear', 'it!']")]
        [InlineData("r = shorten('Hello  world!', width=12)", "'Hello world!'")]
        [InlineData("r = shorten('Hello  world!', width=11)", "'Hello [...]'")]
        [InlineData("r = shorten('Hello world', width=10, placeholder='...')", "'Hello...'")]
        [InlineData("r = shorten('The quick brown fox jumps over the lazy dog', 25)", "'The quick brown fox [...]'")]
        [InlineData("r = dedent('    hello\\n      world\\n    ')", "'hello\\n  world\\n'")]
        [InlineData("r = dedent('\\thello\\n\\t  world')", "'hello\\n  world'")]
        [InlineData("r = dedent('  a\\n\\n  b\\n   \\n')", "'a\\n\\nb\\n\\n'")]
        [InlineData("r = dedent('a\\n  b')", "'a\\n  b'")]
        [InlineData("r = indent('a\\n\\nb\\n', '  ')", "'  a\\n\\n  b\\n'")]
        [InlineData("r = indent('a\\n \\nb', '> ', lambda line: True)", "'> a\\n>  \\n> b'")]
        [InlineData("r = indent('a\\nb\\n', prefix='# ', predicate=lambda l: l.startswith('b'))", "'a\\n# b\\n'")]
        // indent() splits like splitlines(keepends=True) and its default predicate is line.strip()
        [InlineData("r = indent('a\\rb\\r\\nc\\u2028d', '> ')", "'> a\\r> b\\r\\n> c\\u2028> d'")]
        [InlineData("r = indent('a\\n\\xa0\\nb', '> ')", "'> a\\n\\xa0\\n> b'")]
        // a long word breaks after the last hyphen strictly before the cut, and only when something other
        // than hyphens precedes it (TextWrapper._handle_long_word)
        [InlineData("r = wrap('abcde-fgh', 5)", "['abcde', '-fgh']")]
        [InlineData("r = wrap('a1-b2-c3-dddddddd', 10)", "['a1-b2-c3-', 'dddddddd']")]
        [InlineData("r = wrap('---abcdefgh', 6)", "['---abc', 'defgh']")]
        [InlineData("r = textwrap.fill(textwrap.dedent('''\\\n    Lorem ipsum dolor sit amet, consectetur\\n    adipiscing elit.'''), width=30)", "'Lorem ipsum dolor sit amet,\\nconsectetur adipiscing elit.'")]
        public void Surface(string body, string expected)
        {
            Assert.Equal(expected, Ev.Run(Imp + body + "\n").Repr("r"));
        }

        [Theory]
        [InlineData("wrap('x', 0)", "ValueError", "invalid width 0 (must be > 0)")]
        [InlineData("wrap('x', 5, max_lines=1, placeholder=' [........]')", "ValueError", "placeholder too large for max width")]
        [InlineData("wrap(1)", "TypeError", "wrap() argument 'text' must be str, not int")]
        [InlineData("wrap('x', bogus=1)", "TypeError", "wrap() got an unexpected keyword argument 'bogus'")]
        [InlineData("shorten('x')", "TypeError", "shorten() missing required argument 'width'")]
        [InlineData("indent('x')", "TypeError", "indent() missing required argument 'prefix'")]
        public void Errors(string expr, string exc, string message)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Imp + "r = " + expr + "\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
            Assert.Equal(message, ex.Value.GetMessageText(Rt.Ctx()));
        }
    }
}

using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // T3.1: the one-entry sub-template cache on a Pattern must be invisible — switching repl
    // strings on the same pattern object re-parses, group references keep expanding, and the
    // callable-repl branch (which now materializes the Match wrapper lazily) still works.
    [Trait("Category", "Compatibility")]
    public class ReSubTemplateCacheTests
    {
        [Fact]
        public void SamePatternDifferentReplsNeverServeStaleTemplates()
        {
            var res = Ev.Run(
                "import re\n"
                + "p = re.compile('[0-9]+')\n"
                + "a = p.sub('#', 'a1b22')\n"
                + "b = p.sub('*', 'a1b22')\n"     // different repl: cache must refresh
                + "c = p.sub('#', 'a1b22')\n"     // back to the first: correct either way
                + "d = p.sub('#', 'x9y')\n"       // same repl, new subject: cache hit
                + "r = (a, b, c, d)\n");
            Assert.Equal("('a#b#', 'a*b*', 'a#b#', 'x#y')", res.Repr("r"));
        }

        [Fact]
        public void GroupReferencesExpandThroughTheCache()
        {
            var res = Ev.Run(
                "import re\n"
                + "p = re.compile('([a-z])([0-9])')\n"
                + "a = p.sub(r'\\2\\1', 'a1 b2')\n"
                + "b = p.sub(r'\\2\\1', 'c3')\n"   // cached template, fresh match
                + "r = (a, b)\n");
            Assert.Equal("('1a 2b', '3c')", res.Repr("r"));
        }

        [Fact]
        public void CallableReplStillGetsAMatchObject()
        {
            var res = Ev.Run(
                "import re\n"
                + "p = re.compile('[0-9]+')\n"
                + "a = p.sub(lambda m: '<' + m.group(0) + '>', 'a1b22')\n"
                + "b = p.sub('#', 'a1b22')\n"                              // then a template on the same pattern
                + "c = p.sub(lambda m: m.group(0) * 2, 'a3')\n"            // and a callable again
                + "r = (a, b, c)\n");
            Assert.Equal("('a<1>b<22>', 'a#b#', 'a33')", res.Repr("r"));
        }

        [Fact]
        public void SubnCountAndModuleLevelSubUnchanged()
        {
            var res = Ev.Run(
                "import re\n"
                + "a = re.subn('[0-9]+', '#', 'a1b22c333')\n"
                + "b = re.sub('[0-9]+', '#', 'a1b22', 1)\n"    // count honoured
                + "r = (a, b)\n");
            Assert.Equal("(('a#b#c#', 3), 'a#b22')", res.Repr("r"));
        }

        [Fact]
        public void EmptyMatchAdvanceUnchanged()
        {
            Assert.Equal("'-a-b-'", Ev.Run("import re\nr = re.sub('x*', '-', 'ab')").Repr("r"));
        }
    }
}

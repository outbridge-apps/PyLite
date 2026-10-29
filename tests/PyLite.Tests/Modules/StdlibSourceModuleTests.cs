using System;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // string.Template and email.utils (1.1.0) are written in the dialect (Modules/Python/*.py), compiled
    // once and run per importing run. What only the engine can get wrong: the run's budget and clock
    // reach them, subclassing works, and their private names stay inside.
    [Trait("Category", "Compatibility")]
    public class StdlibSourceModuleTests
    {
        // 2001-09-09T01:46:40Z = epoch 1,000,000,000; the local zone is +02:00 'CET'.
        private static readonly IScriptClock Cet = new FrozenClock(
            new DateTime(2001, 9, 9, 1, 46, 40, DateTimeKind.Utc), TimeSpan.FromHours(2), "CET");

        private static string R(string src)
        {
            return Ev.Run(src, clock: Cet).Repr("r");
        }

        private static RunResult Run(string src, ResourceLimits limits)
        {
            var engine = new ScriptEngine(new EngineOptions { DefaultLimits = limits });
            return engine.Run(src, new RunRequest { ScalarOutputNames = new[] { "r" } });
        }

        [Fact]
        public void TemplateSubclassesLikeAnyClass()
        {
            Assert.Equal("('HELLO, ANN!', '#', True)", R("from string import Template\n"
                + "class Greeting(Template):\n    delimiter = '#'\n"
                + "    def render(self, **kw):\n        return super().substitute(kw).upper()\n"
                + "g = Greeting('hello, #{name}!')\nr = (g.render(name='ann'), Greeting.delimiter, isinstance(g, Template))\n"));
        }

        [Fact]
        public void TheStringModuleKeepsTemplateItsOnlyNewName()
        {
            Assert.Equal("(True, False, False, 'Template')", R("import string as a\nimport string as b\n"
                + "names = dir(a)\nr = (a.Template is b.Template, '_re' in names, '_sentinel_dict' in names,"
                + " [n for n in names if n[0].isupper()][0])\n"));
        }

        [Fact]
        public void TemplateOutputIsCappedAndItsWorkIsCharged()
        {
            RunResult big = Run("from string import Template\nr = Template('$a' * 1000).substitute(a='y' * 10000)\n",
                ResourceLimits.Default.With(maxStrChars: 2_000_000));
            Assert.Equal("MaxStrChars", big.Error.LimitName);
            RunResult steps = Run("from string import Template\nr = Template('$a' * 200000).substitute(a='x')\n",
                ResourceLimits.Default.With(maxSteps: 300_000));
            Assert.Equal("MaxSteps", steps.Error.LimitName);
        }

        [Fact]
        public void FormatdateReadsTheEngineClock()
        {
            Assert.Equal("('Sun, 09 Sep 2001 01:46:40 -0000', 'Sun, 09 Sep 2001 03:46:40 +0200', 'Sun, 09 Sep 2001 01:46:40 GMT')",
                R("from email.utils import formatdate\nr = (formatdate(), formatdate(localtime=True), formatdate(usegmt=True))\n"));
        }

        [Fact]
        public void MktimeTzWithoutAZoneUsesTheEngineLocalOffset()
        {
            Assert.Equal("(1000000000.0, 1000000000)", R("import email.utils\n"
                + "r = (email.utils.mktime_tz((2001, 9, 9, 3, 46, 40, 0, 1, -1, None)),"
                + " email.utils.mktime_tz(email.utils.parsedate_tz('Sun, 09 Sep 2001 01:46:40 GMT')))\n"));
        }
    }
}

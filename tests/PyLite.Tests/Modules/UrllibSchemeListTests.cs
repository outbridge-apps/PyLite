using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // The three scheme lists are CPython's extension point: appending to uses_relative and uses_netloc is
    // the documented way to teach urljoin a scheme of its own. They used to be a snapshot of a private C#
    // set, so an append silently did nothing (closed 2026-09-11).
    [Trait("Category", "Compatibility")]
    public class UrllibSchemeListTests
    {
        private const string P = "import urllib.parse as up\n";

        private static string R(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        [Theory]
        // untaught, a custom scheme is not relative: the url wins whole, which is CPython's answer too
        [InlineData("r = up.urljoin('myapp://h/a/b', 'c')\n", "'c'")]
        [InlineData("up.uses_relative.append('myapp')\nup.uses_netloc.append('myapp')\n"
            + "r = up.urljoin('myapp://h/a/b', 'c')\n", "'myapp://h/a/c'")]
        [InlineData("up.uses_relative.append('myapp')\nup.uses_netloc.append('myapp')\n"
            + "r = up.urljoin('myapp://h/a/b', '../c')\n", "'myapp://h/c'")]
        [InlineData("up.uses_relative.append('myapp')\nup.uses_netloc.append('myapp')\n"
            + "r = up.urljoin('myapp://h/a/b', '//other/x')\n", "'myapp://other/x'")]
        // urlunsplit reads uses_netloc, so geturl() on a result honours the list as well
        [InlineData("up.uses_netloc.append('myapp')\nr = up.urlunsplit(('myapp', 'h', 'p', '', ''))\n", "'myapp://h/p'")]
        [InlineData("up.uses_netloc.append('myapp')\nr = up.urlparse('myapp://h/p').geturl()\n", "'myapp://h/p'")]
        // urlparse reads uses_params
        [InlineData("r = up.urlparse('myapp://h/p;q').params\n", "''")]
        [InlineData("up.uses_params.append('myapp')\nr = up.urlparse('myapp://h/p;q').params\n", "'q'")]
        // and the lists work in the other direction too
        [InlineData("r = up.urljoin('http://h/a/b', 'c')\n", "'http://h/a/c'")]
        [InlineData("up.uses_relative.remove('http')\nr = up.urljoin('http://h/a/b', 'c')\n", "'c'")]
        public void TheSchemeListsAreLive(string body, string expected)
        {
            Assert.Equal(expected, R(body));
        }

        // A module instance is built per RUN (ModuleRegistry.Import caches in the EvalContext), so what
        // one script teaches cannot reach the next.
        [Fact]
        public void TeachingDoesNotLeakBetweenRuns()
        {
            Assert.Equal("'myapp://h/a/c'", Ev.Run(P
                + "up.uses_relative.append('myapp')\nup.uses_netloc.append('myapp')\n"
                + "r = up.urljoin('myapp://h/a/b', 'c')\n").Repr("r"));
            Assert.Equal("'c'", Ev.Run(P + "r = up.urljoin('myapp://h/a/b', 'c')\n").Repr("r"));
            Assert.Equal("False", Ev.Run(P + "r = 'myapp' in up.uses_relative\n").Repr("r"));
        }

        [Theory]
        [InlineData("r = up.unwrap('<URL:http://h/p>')\n", "'http://h/p'")]
        [InlineData("r = up.unwrap('<http://h/>')\n", "'http://h/'")]
        [InlineData("r = up.unwrap('  http://h/  ')\n", "'http://h/'")]
        [InlineData("r = up.unwrap('URL:http://h/')\n", "'http://h/'")]
        [InlineData("r = up.clear_cache()\n", "None")]          // nothing is cached here; the call exists
        [InlineData("r = up.scheme_chars[:8]\n", "'abcdefgh'")]
        [InlineData("r = '+' in up.scheme_chars\n", "True")]
        public void TheSmallSurface(string body, string expected)
        {
            Assert.Equal(expected, R(body));
        }

        [Theory]
        [InlineData("r = up.unwrap()")]
        [InlineData("r = up.unwrap('a', 'b')")]
        [InlineData("r = up.unwrap(1)")]
        [InlineData("r = up.clear_cache(1)")]
        public void ArityIsChecked(string body)
        {
            Assert.Equal("TypeError", Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n")).Value.ExcType.Name);
        }
    }
}

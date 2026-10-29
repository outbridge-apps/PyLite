using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/11 T-MFD-20/22: html.escape (own) and html.unescape (WebUtility, RFC-002 deviation).
    [Trait("Category", "Compatibility")]
    public class HtmlTests
    {
        private const string P = "from html import escape, unescape\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        [Theory]
        // ---- escape (own; the full mandatory table) ----
        [InlineData("r = escape('a<b>c')", "'a&lt;b&gt;c'")]
        [InlineData("r = escape('a & b')", "'a &amp; b'")]
        [InlineData("r = escape('\"quote\"')", "'&quot;quote&quot;'")]
        [InlineData("r = escape(\"it's\")", "'it&#x27;s'")]
        [InlineData("r = escape(\"it's\", quote=False)", "\"it's\"")]
        [InlineData("r = escape('\"', quote=False)", "'\"'")]
        [InlineData("r = escape('&amp;')", "'&amp;amp;'")]
        [InlineData("r = escape('')", "''")]
        [InlineData("r = escape('<>&\"\\'')", "'&lt;&gt;&amp;&quot;&#x27;'")]
        [InlineData("r = (escape('a', quote=True) == escape('a'))", "True")]
        public void Escape(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        // ---- unescape (WebUtility-backed; common cases that match CPython) ----
        [InlineData("r = unescape('&amp;')", "'&'")]
        [InlineData("r = unescape('&lt;div&gt;')", "'<div>'")]
        [InlineData("r = unescape('&#65;')", "'A'")]
        [InlineData("r = unescape('&#x41;')", "'A'")]
        [InlineData("r = unescape('no entities here')", "'no entities here'")]
        [InlineData("r = unescape('&quot;hi&quot;')", "'\"hi\"'")]
        [InlineData("r = unescape('&#x27;')", "\"'\"")]
        public void Unescape(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Fact]
        public void EscapeUnescapeRoundTripBasic()
        {
            Assert.Equal("True", Body("s = 'a<b> & \"c\"'\nr = (unescape(escape(s)) == s)"));
        }

        [Fact]
        public void DoubleEscape()
        {
            Assert.Equal("'&amp;amp;'", Body("r = escape('&amp;')"));
        }
    }
}

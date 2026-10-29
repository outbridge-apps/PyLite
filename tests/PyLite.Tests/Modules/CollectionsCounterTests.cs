using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/11 T-MFD-12: collections.Counter.
    [Trait("Category", "Compatibility")]
    public class CollectionsCounterTests
    {
        private const string P = "from collections import Counter\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        [InlineData("r = Counter('abca').total()", "4")]
        [InlineData("r = (Counter().total(), Counter({'x': 2, 'y': -3}).total(), Counter({'f': 1.5, 'g': 2}).total())", "(0, -1, 3.5)")]
        [InlineData("c = Counter('ab')\nc['a'] += 5\nr = c.total()", "7")]
        [InlineData("r = dict(Counter('aabbbc'))", "{'a': 2, 'b': 3, 'c': 1}")]
        [InlineData("r = Counter('aabbbc').most_common(2)", "[('b', 3), ('a', 2)]")]
        [InlineData("r = Counter([1,1,2]).most_common()", "[(1, 2), (2, 1)]")]
        [InlineData("r = Counter('abc')['z']", "0")]
        [InlineData("c = Counter('abc')\nc['z']\nr = 'z' in c", "False")]
        [InlineData("r = (Counter(a=0) == Counter())", "False")]
        [InlineData("r = (Counter() == {})", "True")]
        [InlineData("r = list(Counter(a=3, b=1).elements())", "['a', 'a', 'a', 'b']")]
        [InlineData("r = list(Counter(a=2, b=-1, c=0).elements())", "['a', 'a']")]
        [InlineData("r = dict(Counter(a=3) + Counter(a=1, b=2))", "{'a': 4, 'b': 2}")]
        [InlineData("r = dict(Counter(a=3, b=1) - Counter(a=1, b=2))", "{'a': 2}")]
        [InlineData("r = dict(Counter(a=1, b=2) & Counter(a=3, b=1))", "{'a': 1, 'b': 1}")]
        [InlineData("r = dict(Counter(a=1, b=2) | Counter(a=3, b=1))", "{'a': 3, 'b': 2}")]
        [InlineData("c = Counter(a=1)\nc.subtract(a=3)\nr = dict(c)", "{'a': -2}")]
        [InlineData("c = Counter('aab')\nc.update('bc')\nr = dict(c)", "{'a': 2, 'b': 2, 'c': 1}")]
        [InlineData("r = Counter('aaabbc').most_common(1)", "[('a', 3)]")]
        [InlineData("r = Counter('aaabbc').most_common(-1)", "[]")]
        [InlineData("r = dict(Counter({'x': 2, 'y': 3}))", "{'x': 2, 'y': 3}")]
        [InlineData("r = list(Counter(a=1, b=-1).elements())", "['a']")]
        public void Counters(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Fact]
        public void FromKeysUndefined()
        {
            ScriptException ex = Raises("x = Counter.fromkeys('abc')");
            Assert.Equal("NotImplementedError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void FromKeysMessage()
        {
            string src = P + "msg = ''\ntry:\n    Counter.fromkeys('abc')\nexcept NotImplementedError as e:\n    msg = str(e)\n";
            Assert.Equal("'Counter.fromkeys() is undefined.  Use Counter(iterable) instead.'", Ev.Run(src).Repr("msg"));
        }
    }
}

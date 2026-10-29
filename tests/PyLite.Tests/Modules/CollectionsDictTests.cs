using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/11 T-MFD-10/11: collections.OrderedDict and defaultdict.
    [Trait("Category", "Compatibility")]
    public class CollectionsDictTests
    {
        private const string P = "from collections import OrderedDict, defaultdict\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        // ---- OrderedDict ----
        [InlineData("r = list(OrderedDict([('a',1),('b',2)]))", "['a', 'b']")]
        [InlineData("d = OrderedDict([('a',1),('b',2),('c',3)])\nd.move_to_end('a')\nr = list(d)", "['b', 'c', 'a']")]
        [InlineData("d = OrderedDict([('a',1),('b',2),('c',3)])\nd.move_to_end('c', last=False)\nr = list(d)", "['c', 'a', 'b']")]
        // move_to_end(last=False) moves the entry in place (N3, no rebuild): order, values and pops after moves
        [InlineData("d = OrderedDict.fromkeys('abcde')\nd.move_to_end('c', last=False)\nd.move_to_end('e', last=False)\ndel d['a']\nd['z'] = 1\nd.move_to_end('z', last=False)\nr = (list(d), d['z'], d.popitem(last=False), d.popitem())",
            "(['z', 'e', 'c', 'b', 'd'], 1, ('z', 1), ('d', None))")]
        [InlineData("d = OrderedDict.fromkeys(range(2000))\nfor i in range(0, 2000, 7):\n    d.move_to_end(i, last=False)\nr = (list(d)[:3], all(k in d for k in range(2000)), len(d))", "([1995, 1988, 1981], True, 2000)")]
        [InlineData("d = OrderedDict([('a',1),('b',2)])\nr = d.popitem()", "('b', 2)")]
        [InlineData("d = OrderedDict([('a',1),('b',2)])\nr = d.popitem(last=False)", "('a', 1)")]
        [InlineData("r = OrderedDict([('a',1),('b',2)]) == OrderedDict([('b',2),('a',1)])", "False")]
        [InlineData("r = OrderedDict([('a',1),('b',2)]) == OrderedDict([('a',1),('b',2)])", "True")]
        [InlineData("r = OrderedDict([('a',1),('b',2)]) == {'a':1,'b':2}", "True")]
        [InlineData("r = {'a':1,'b':2} == OrderedDict([('b',2),('a',1)])", "True")]
        [InlineData("r = list(reversed(OrderedDict([('a',1),('b',2),('c',3)])))", "['c', 'b', 'a']")]
        [InlineData("d = OrderedDict([('a',1),('b',2)])\nr = (d['a'], len(d), sorted(d.keys()))", "(1, 2, ['a', 'b'])")]
        // ---- defaultdict ----
        [InlineData("d = defaultdict(list)\nd['k'].append(1)\nr = d['k']", "[1]")]
        [InlineData("d = defaultdict(list)\nd['k']\nr = 'k' in d", "True")]
        [InlineData("d = defaultdict(int)\nd['a'] += 5\nr = d['a']", "5")]
        [InlineData("d = defaultdict(list)\nr = d.get('x')", "None")]
        [InlineData("d = defaultdict(list)\nd.get('x')\nr = 'x' in d", "False")]
        [InlineData("d = defaultdict(lambda: 'z')\nr = d[1]", "'z'")]
        [InlineData("d = defaultdict(list, {'a':1})\nr = d['a']", "1")]
        [InlineData("d = defaultdict(int)\nr = (d.default_factory is int)", "True")]
        [InlineData("d = defaultdict(int)\nd[1]\nd[2]\nr = len(d)", "2")]
        public void DictSubtypes(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("OrderedDict().popitem()", "KeyError")]
        [InlineData("OrderedDict([('a',1)]).move_to_end('z')", "KeyError")]
        [InlineData("defaultdict(5)", "TypeError")]
        [InlineData("d = defaultdict(None)\nd['x']", "KeyError")]
        public void Errors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Fact]
        public void PopitemEmptyMessage()
        {
            string src = P + "msg = ''\ntry:\n    OrderedDict().popitem()\nexcept KeyError as e:\n    msg = str(e)\n";
            Assert.Equal("\"'dictionary is empty'\"", Ev.Run(src).Repr("msg"));
        }

        [Fact]
        public void FactoryExceptionLeavesNoEntry()
        {
            string body = "def boom():\n    raise ValueError('x')\n"
                + "d = defaultdict(boom)\n"
                + "caught = False\n"
                + "try:\n    d['k']\nexcept ValueError:\n    caught = True\n"
                + "r = (caught, 'k' in d)\n";
            Assert.Equal("(True, False)", Ev.Run(P + body).Repr("r"));
        }
    }
}

using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // v2 E15: collections.ChainMap.
    [Trait("Category", "Compatibility")]
    public class CollectionsChainMapTests
    {
        private const string P = "from collections import ChainMap\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        // ---- lookup: first map wins ----
        [InlineData("cm = ChainMap({'a': 1}, {'a': 9, 'b': 2})\nr = (cm['a'], cm['b'])", "(1, 2)")]
        [InlineData("cm = ChainMap({'a': 1}, {'b': 2})\nr = cm.get('z', 'd')", "'d'")]
        [InlineData("cm = ChainMap({'a': 1}, {'b': 2})\nr = ('b' in cm, 'z' in cm)", "(True, False)")]
        [InlineData("r = len(ChainMap({'a': 1, 'b': 2}, {'b': 9, 'c': 3}))", "3")]
        [InlineData("r = len(ChainMap())", "0")]
        [InlineData("r = (bool(ChainMap()), bool(ChainMap({'a': 1})))", "(False, True)")]
        // ---- writes go to the first map only ----
        [InlineData("d1 = {'a': 1}\nd2 = {'b': 2}\ncm = ChainMap(d1, d2)\ncm['x'] = 9\nr = (d1, d2)", "({'a': 1, 'x': 9}, {'b': 2})")]
        [InlineData("d1 = {'a': 1}\ncm = ChainMap(d1, {'a': 9})\ndel cm['a']\nr = (d1, cm['a'])", "({}, 9)")]
        // ---- live view ----
        [InlineData("d = {'a': 1}\ncm = ChainMap(d)\nd['b'] = 2\nr = cm['b']", "2")]
        // ---- maps / new_child / parents ----
        [InlineData("cm = ChainMap({'a': 1}, {'b': 2})\nr = cm.maps", "[{'a': 1}, {'b': 2}]")]
        [InlineData("cm = ChainMap({'b': 2})\nc2 = cm.new_child({'a': 1})\nr = (c2['a'], c2['b'])", "(1, 2)")]
        [InlineData("cm = ChainMap({'b': 2})\nc2 = cm.new_child(x=5)\nr = c2['x']", "5")]
        [InlineData("cm = ChainMap({'a': 1}, {'b': 2})\nr = cm.parents.maps", "[{'b': 2}]")]
        [InlineData("r = ChainMap({'a': 1}).parents.maps", "[{}]")]
        // ---- iteration / conversion: union, first-map order wins ----
        [InlineData("cm = ChainMap({'a': 1}, {'b': 2, 'a': 9})\nr = sorted(cm)", "['a', 'b']")]
        [InlineData("cm = ChainMap({'a': 1}, {'b': 2})\nr = dict(cm)", "{'b': 2, 'a': 1}")]
        [InlineData("cm = ChainMap({'a': 1}, {'a': 9, 'b': 2})\nr = dict(cm)['a']", "1")]
        [InlineData("cm = ChainMap({'a': 1}, {'b': 2})\nr = sorted(cm.keys())", "['a', 'b']")]
        [InlineData("cm = ChainMap({'a': 1}, {'b': 2})\nr = sorted(cm.items())", "[('a', 1), ('b', 2)]")]
        // ---- copy: first map copied, rest shared ----
        [InlineData("d2 = {'b': 2}\ncm = ChainMap({'a': 1}, d2)\nc = cm.copy()\nc['a'] = 99\nr = (cm['a'], c['a'], c.maps[1] is d2)", "(1, 99, True)")]
        // ---- repr ----
        [InlineData("r = repr(ChainMap({'a': 1}, {'b': 2}))", "\"ChainMap({'a': 1}, {'b': 2})\"")]
        public void ChainMaps(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("x = ChainMap({'a': 1})['z']", "KeyError")]
        [InlineData("cm = ChainMap({}, {'a': 1})\ndel cm['a']", "KeyError")]   // delete only touches the first map
        [InlineData("x = ChainMap([1, 2])", "TypeError")]
        public void Errors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Theory]
        // ---- update/pop/popitem/setdefault/clear (all first-map, CPython overrides) ----
        [InlineData("d1 = {'a': 1}\ncm = ChainMap(d1, {'b': 2})\ncm.update({'x': 9}, y=8)\nr = d1", "{'a': 1, 'x': 9, 'y': 8}")]
        [InlineData("d1 = {'a': 1}\ncm = ChainMap(d1, {'a': 9})\nr = (cm.pop('a'), cm['a'], d1)", "(1, 9, {})")]
        [InlineData("cm = ChainMap({}, {'a': 9})\nr = cm.pop('a', 'dflt')", "'dflt'")]
        [InlineData("d1 = {'a': 1, 'b': 2}\ncm = ChainMap(d1)\nr = (cm.popitem(), d1)", "(('b', 2), {'a': 1})")]
        [InlineData("cm = ChainMap({'a': 1}, {'b': 2})\nr = (cm.setdefault('b', 99), 'b' in cm.maps[0])", "(2, False)")]
        [InlineData("d1 = {}\ncm = ChainMap(d1, {'b': 2})\nr = (cm.setdefault('x', 7), d1)", "(7, {'x': 7})")]
        [InlineData("d1 = {'a': 1}\nd2 = {'b': 2}\ncm = ChainMap(d1, d2)\ncm.clear()\nr = (d1, d2)", "({}, {'b': 2})")]
        public void MutableMappingTails(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("cm = ChainMap({}, {'a': 9})\nx = cm.pop('a')", "KeyError")]
        [InlineData("cm = ChainMap({}, {'a': 9})\nx = cm.popitem()", "KeyError")]
        public void TailErrors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        [Fact]
        public void DefaultdictFirstMapRunsFactory()
        {
            // CPython parity: ChainMap.__getitem__ does mapping[key], so a defaultdict factory fires.
            string body = "from collections import defaultdict\n"
                + "dd = defaultdict(list)\ncm = ChainMap(dd, {'b': 2})\n"
                + "cm['new'].append(1)\nr = dd['new']\n";
            Assert.Equal("[1]", Body(body));
        }
    }
}

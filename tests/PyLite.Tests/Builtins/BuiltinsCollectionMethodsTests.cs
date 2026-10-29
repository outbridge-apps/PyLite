using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Builtins
{
    // impl/06 T-BLT-18/19/20: list/tuple, dict (+views), set/frozenset methods.
    [Trait("Category", "Compatibility")]
    public class BuiltinsCollectionMethodsTests
    {
        private static string R(string expr)
        {
            return Ev.ReprOf(expr);
        }

        private static string Repr(string src, string name)
        {
            return Ev.Run(src).Repr(name);
        }

        // ---- list / tuple (T-BLT-18) ----

        [Fact]
        public void ListMutators()
        {
            Assert.Equal("[1, 2]", Repr("xs = [1]\nxs.append(2)\n", "xs"));
            Assert.Equal("None", Repr("xs = [1]\nn = xs.append(2)\n", "n"));
            Assert.Equal("[1, 2, 3]", Repr("xs = [1]\nxs.extend((2, 3))\n", "xs"));
            Assert.Equal("[1, 2, 1, 2]", Repr("xs = [1, 2]\nxs.extend(xs)\n", "xs"));
            Assert.Equal("[0, 1]", Repr("xs = [1]\nxs.insert(-100, 0)\n", "xs"));
            Assert.Equal("[2, 1]", Repr("xs = [1, 2, 1]\nxs.remove(1)\n", "xs"));
        }

        [Theory]
        [InlineData("[1, 2, 3].pop()", "3")]
        [InlineData("[1, 2, 3].pop(0)", "1")]
        [InlineData("[1, 2, 3, 2].index(2, 2)", "3")]
        [InlineData("[True, 1, 1.0].count(1)", "3")]
        [InlineData("(1, 2, 1).count(1)", "2")]
        [InlineData("(1, 2).index(2)", "1")]
        public void ListTupleQueries(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("[1].remove(9)", "list.remove(x): x not in list")]
        [InlineData("[].pop()", "pop from empty list")]
        [InlineData("[1].pop(5)", "pop index out of range")]
        [InlineData("[1].index(9)", "9 is not in list")]
        [InlineData("(1,).index(9)", "tuple.index(x): x not in tuple")]
        public void ListErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err("x = " + expr + "\n"));
        }

        [Fact]
        public void ListCopyIsIndependent()
        {
            Assert.Equal("[1, 2]", Repr("xs = [1, 2]\nys = xs.copy()\nys.append(9)\n", "xs"));
        }

        [Fact]
        public void ListSortInPlace()
        {
            var r = Ev.Run("xs = [3, 1, 2]\nn = xs.sort()\n");
            Assert.Equal("[1, 2, 3]", r.Repr("xs"));
            Assert.Equal("None", r.Repr("n"));
        }

        [Fact]
        public void ListSortModifiedDuringSort()
        {
            Assert.Equal("list modified during sort",
                Ev.Err("xs = [3, 1, 2]\ndef k(v):\n    xs.append(0)\n    return v\nxs.sort(key=k)\n"));
        }

        // ---- dict (+views) (T-BLT-19) ----

        [Theory]
        [InlineData("{'a': 1}.get('a')", "1")]
        [InlineData("{'a': 1}.get('x')", "None")]
        [InlineData("{'a': 1}.get('x', 9)", "9")]
        [InlineData("{'a': 1, 'b': 2}.popitem()", "('b', 2)")]
        [InlineData("{'a': 1}.pop('x', 0)", "0")]
        [InlineData("('a', 1) in {'a': 1}.items()", "True")]
        [InlineData("('a', 2) in {'a': 1}.items()", "False")]
        [InlineData("1 in {'x': True}.values()", "True")]
        [InlineData("repr({'a': 1}.keys())", "\"dict_keys(['a'])\"")]
        [InlineData("list({'a': 1, 'b': 2}.items())", "[('a', 1), ('b', 2)]")]
        public void DictQueries(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void DictMutators()
        {
            Assert.Equal("{'a': 9, 'b': 2}", Repr("d = {'a': 1}\nd.update({'a': 9, 'b': 2})\n", "d"));
            Assert.Equal("[1, 1]", Repr("d = {}\nd.setdefault('n', []).append(1)\nd.setdefault('n', []).append(1)\nr = d['n']\n", "r"));
        }

        [Theory]
        [InlineData("{}.popitem()", "'popitem(): dictionary is empty'")]
        public void DictErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err("x = " + expr + "\n"));
        }

        [Fact]
        public void DictPopTwiceKeyError()
        {
            Assert.Equal("'a'", Ev.Err("d = {'a': 1}\nd.pop('a')\nd.pop('a')\n"));
        }

        [Fact]
        public void ViewIsLive()
        {
            Assert.Equal("2", Repr("d = {'a': 1}\nks = d.keys()\nd['z'] = 1\nn = len(ks)\n", "n"));
        }

        [Fact]
        public void VersionGuardOnStructuralChange()
        {
            Assert.Equal("dictionary changed size during iteration",
                Ev.Err("d = {'a': 1, 'b': 2}\nfor k in d:\n    d['new'] = 1\n"));
        }

        [Fact]
        public void OverwriteDuringIterationIsFine()
        {
            var r = Ev.Run("d = {'a': 1, 'b': 2}\nfor k in d:\n    d[k] = 0\n");
            Assert.Equal("{'a': 0, 'b': 0}", r.Repr("d"));
        }

        // ---- set / frozenset (T-BLT-20) ----

        [Theory]
        [InlineData("{1, 2}.union([2, 3], (4,))", "{1, 2, 3, 4}")]
        [InlineData("{1, 2} & {2, 3}", "{2}")]
        [InlineData("{1, 2} - {2}", "{1}")]
        [InlineData("{1, 2} ^ {2, 3}", "{1, 3}")]
        [InlineData("{1}.issubset([1, 2])", "True")]
        [InlineData("{1} <= {1}", "True")]
        [InlineData("{1} < {1}", "False")]
        [InlineData("{1, 2}.isdisjoint([3])", "True")]
        [InlineData("len({True, 1, 1.0})", "1")]
        public void SetOps(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void SetMutators()
        {
            Assert.Equal("3", Repr("s = {3, 1}\nx = s.pop()\n", "x"));
            Assert.Equal("None", Repr("s = {1}\nr = s.discard(99)\n", "r"));
        }

        [Theory]
        [InlineData("{1, 2} | [2, 3]", "unsupported operand type(s) for |: 'set' and 'list'")]
        [InlineData("set().pop()", "'pop from an empty set'")]
        [InlineData("{1}.remove(2)", "2")]
        [InlineData("frozenset([1]).add(1)", "'frozenset' object has no attribute 'add'")]
        public void SetErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err("x = " + expr + "\n"));
        }

        [Fact]
        public void FrozenSetCopyIsSelf()
        {
            Assert.Equal("True", Repr("fs = frozenset([1])\nsame = fs.copy() is fs\n", "same"));
        }

        [Fact]
        public void FrozenSetUnionKeepsType()
        {
            Assert.Equal("frozenset({1, 2})", R("frozenset([1]).union({2})"));
        }

        // Methods and operators share one SetOps (D4): N operands, in-place forms, a set as its own operand.
        [Theory]
        [InlineData("a.intersection(b, [3, 9])", "{3}")]
        [InlineData("a.difference(b, [4])", "{1}")]
        [InlineData("(a.intersection(), a.difference() is a)", "({1, 3, 4}, False)")]
        [InlineData("[s.update(s), s][1]", "{1, 2}")]
        [InlineData("[s.intersection_update(s), s][1]", "{1, 2}")]
        [InlineData("[s.difference_update(s), s][1]", "set()")]
        [InlineData("[s.symmetric_difference_update(s), s][1]", "set()")]
        [InlineData("a.issuperset(iter([1, 4]))", "True")]
        public void SetAlgebraShapes(string expr, string expected)
        {
            Assert.Equal(expected, Ev.ReprOf("a = {1, 3, 4}\nb = {3, 4, 5}\ns = {1, 2}\n", expr));
        }

        [Theory]
        [InlineData("s |= {3}", "(True, {1, 2, 3})")]
        [InlineData("s &= {2}", "(True, {2})")]
        [InlineData("s -= {2}", "(True, {1})")]
        [InlineData("s ^= {2, 3}", "(True, {1, 3})")]
        [InlineData("s |= s", "(True, {1, 2})")]
        [InlineData("s ^= s", "(True, set())")]
        public void AugmentedSetOpsKeepTheObject(string stmt, string expected)
        {
            Assert.Equal(expected, Ev.ReprOf("s = {1, 2}\nt = s\n" + stmt + "\n", "t is s, s"));
        }
    }
}

using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Builtins
{
    // impl/06 T-BLT-07/11/12/13: range, containers, print, isinstance/issubclass.
    [Trait("Category", "Compatibility")]
    public class BuiltinsContainersTests
    {
        private static string R(string expr)
        {
            return Ev.ReprOf(expr);
        }

        private static string Out(string src)
        {
            return Ev.Run(src).PrintOutput;
        }

        // ---- range (T-BLT-07) ----

        [Theory]
        [InlineData("list(range(5))", "[0, 1, 2, 3, 4]")]
        [InlineData("list(range(2, 10, 3))", "[2, 5, 8]")]
        [InlineData("list(range(5, 0, -2))", "[5, 3, 1]")]
        [InlineData("len(range(10**30))", "1000000000000000000000000000000")]
        [InlineData("10**29 * 2 in range(10**30)", "True")]
        [InlineData("range(10)[3]", "3")]
        [InlineData("range(10)[-1]", "9")]
        [InlineData("repr(range(3))", "'range(0, 3)'")]
        [InlineData("repr(range(1, 10, 2))", "'range(1, 10, 2)'")]
        public void Range(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("range(1, 2, 0)", "range() arg 3 must not be zero")]
        [InlineData("range(1.0)", "'float' object cannot be interpreted as an integer")]
        [InlineData("range(10)[10]", "range object index out of range")]
        public void RangeErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err("x = " + expr + "\n"));
        }

        // ---- containers (T-BLT-13) ----

        [Theory]
        [InlineData("list('abc')", "['a', 'b', 'c']")]
        [InlineData("list({'a': 1, 'b': 2})", "['a', 'b']")]
        [InlineData("tuple([1, 2])", "(1, 2)")]
        [InlineData("tuple()", "()")]
        [InlineData("dict([('a', 1), ('b', 2)], b=3)", "{'a': 1, 'b': 3}")]
        [InlineData("list(set('abca'))", "['a', 'b', 'c']")]
        [InlineData("frozenset([1, 2]) == set([1, 2])", "True")]
        [InlineData("dict(x=1, y=2)", "{'x': 1, 'y': 2}")]
        public void Containers(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Fact]
        public void DictCopyIsIndependent()
        {
            var r = Ev.Run("a = {'k': 1}\nb = dict(a)\nb['k'] = 9\nsame = a['k']\n");
            Assert.Equal("1", r.Repr("same"));
        }

        [Theory]
        [InlineData("dict([('a', 1), (1, 2, 3)])", "dictionary update sequence element #1 has length 3; 2 is required")]
        [InlineData("dict([1])", "cannot convert dictionary update sequence element #0 to a sequence")]
        [InlineData("dict({'a': 1}, {'b': 2})", "dict expected at most 1 argument, got 2")]
        [InlineData("set([[1]])", "unhashable type: 'list'")]
        public void ContainerErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err("x = " + expr + "\n"));
        }

        // ---- print (T-BLT-11) ----

        [Theory]
        [InlineData("print(1, 2, 3)\n", "1 2 3\n")]
        [InlineData("print()\n", "\n")]
        [InlineData("print('a', 'b', sep=', ', end='!')\n", "a, b!")]
        [InlineData("print(None, True, 0.1)\n", "None True 0.1\n")]
        [InlineData("print([1, 'a'])\n", "[1, 'a']\n")]
        [InlineData("print(1, flush=True)\n", "1\n")]
        public void Print(string src, string expected)
        {
            Assert.Equal(expected, Out(src));
        }

        [Theory]
        [InlineData("print(1, sep=0)", "sep must be None or a string, not int")]
        [InlineData("print(1, file=1)", "print() 'file' argument is not supported in this environment")]
        public void PrintErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err(expr + "\n"));
        }

        // ---- isinstance / issubclass (T-BLT-12) ----

        [Theory]
        [InlineData("isinstance(True, int)", "True")]
        [InlineData("isinstance(1, bool)", "False")]
        [InlineData("isinstance(1.0, int)", "False")]
        [InlineData("isinstance('a', (int, str))", "True")]
        [InlineData("isinstance(1, ())", "False")]
        [InlineData("issubclass(bool, int)", "True")]
        [InlineData("issubclass(int, bool)", "False")]
        [InlineData("issubclass(ZeroDivisionError, ArithmeticError)", "True")]
        [InlineData("issubclass(bool, bool)", "True")]
        public void InstanceChecks(string expr, string expected)
        {
            Assert.Equal(expected, R(expr));
        }

        [Theory]
        [InlineData("isinstance('a', 1)", "isinstance() arg 2 must be a type or tuple of types")]
        [InlineData("issubclass(1, int)", "issubclass() arg 1 must be a class")]
        public void InstanceCheckErrors(string expr, string msg)
        {
            Assert.Equal(msg, Ev.Err("x = " + expr + "\n"));
        }

        // Engine-defined subtypes resolve by name through the dialect hierarchy; a script-created type
        // (namedtuple) resolves by identity only, so a namedtuple named 'dict' never matches a real dict.
        [Theory]
        [InlineData("isinstance(Counter(), dict)", "True")]
        [InlineData("isinstance(defaultdict(int), dict)", "True")]
        [InlineData("isinstance(OrderedDict(), dict)", "True")]
        [InlineData("isinstance({}, Counter)", "False")]
        [InlineData("issubclass(Counter, dict)", "True")]
        [InlineData("issubclass(dict, Counter)", "False")]
        [InlineData("issubclass(type(Counter()), dict)", "True")]
        [InlineData("isinstance(P(1, 2), tuple)", "True")]
        [InlineData("isinstance(P(1, 2), P)", "True")]
        [InlineData("isinstance((1, 2), P)", "False")]
        [InlineData("issubclass(P, tuple)", "True")]
        [InlineData("issubclass(tuple, P)", "False")]
        [InlineData("isinstance(D(1), tuple)", "True")]
        [InlineData("isinstance({}, D)", "False")]
        [InlineData("issubclass(dict, D)", "False")]
        [InlineData("isinstance(datetime.datetime(2024, 1, 2), datetime.date)", "True")]
        [InlineData("isinstance(datetime.date(2024, 1, 2), datetime.datetime)", "False")]
        [InlineData("issubclass(datetime.datetime, datetime.date)", "True")]
        public void HostSubtypeChecks(string expr, string expected)
        {
            var r = Ev.Run("from collections import Counter, defaultdict, OrderedDict, namedtuple\n"
                + "import datetime\nP = namedtuple('P', 'x y')\nD = namedtuple('dict', 'a')\n"
                + "r = " + expr + "\n");
            Assert.Equal(expected, r.Repr("r"));
        }

        [Fact]
        public void ExceptDispatchConsistentWithIsSubclass()
        {
            var r = Ev.Run("r = 0\ntry:\n    1 / 0\nexcept ArithmeticError:\n    r = 1\n");
            Assert.Equal("1", r.Repr("r"));
        }
    }
}

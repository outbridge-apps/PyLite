using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // operator: the functional forms of the operators, and the three getter factories.
    [Trait("Category", "Compatibility")]
    public class OperatorTests
    {
        // star imports are not in the dialect, so the names are spelled out
        private const string Imp = "import operator\nfrom operator import add, sub, mul, truediv, floordiv, mod, pow, lshift, rshift, "
            + "and_, or_, xor, concat, neg, pos, invert, abs, not_, truth, lt, le, eq, ne, ge, gt, is_, is_not, contains, countOf, "
            + "indexOf, getitem, setitem, delitem, index, itemgetter, attrgetter, methodcaller\n";

        [Theory]
        [InlineData("add(2, 3)", "5")]
        [InlineData("sub(2, 3)", "-1")]
        [InlineData("mul('ab', 2)", "'abab'")]
        [InlineData("truediv(7, 2)", "3.5")]
        [InlineData("floordiv(7, 2)", "3")]
        [InlineData("mod(7, 3)", "1")]
        [InlineData("pow(2, 10)", "1024")]
        [InlineData("lshift(1, 4)", "16")]
        [InlineData("rshift(16, 2)", "4")]
        [InlineData("and_(6, 3)", "2")]
        [InlineData("or_(6, 3)", "7")]
        [InlineData("xor(6, 3)", "5")]
        [InlineData("concat([1], [2])", "[1, 2]")]
        [InlineData("neg(3)", "-3")]
        [InlineData("pos(-3)", "-3")]
        [InlineData("invert(0)", "-1")]
        [InlineData("abs(-2.5)", "2.5")]
        [InlineData("not_(0)", "True")]
        [InlineData("truth([])", "False")]
        [InlineData("(lt(1, 2), le(2, 2), eq(1, 1), ne(1, 1), ge(1, 2), gt(3, 2))", "(True, True, True, False, False, True)")]
        [InlineData("(is_(None, None), is_not(None, None))", "(True, False)")]
        [InlineData("contains([1, 2], 2)", "True")]
        [InlineData("countOf([1, 2, 1], 1)", "2")]
        [InlineData("indexOf([5, 6, 7], 6)", "1")]
        [InlineData("getitem({'a': 1}, 'a')", "1")]
        [InlineData("index(7)", "7")]
        [InlineData("itemgetter(1)([10, 20, 30])", "20")]
        [InlineData("itemgetter(2, 0)([10, 20, 30])", "(30, 10)")]
        [InlineData("itemgetter('k')({'k': 'v'})", "'v'")]
        [InlineData("list(map(itemgetter(0), [(1, 'a'), (2, 'b')]))", "[1, 2]")]
        [InlineData("sorted([{'n': 2}, {'n': 1}], key=itemgetter('n'))", "[{'n': 1}, {'n': 2}]")]
        [InlineData("methodcaller('upper')('ab')", "'AB'")]
        [InlineData("methodcaller('split', ',', maxsplit=1)('a,b,c')", "['a', 'b,c']")]
        [InlineData("methodcaller('replace', 'a', 'x')('banana')", "'bxnxnx'")]
        public void Surface(string expr, string expected)
        {
            Assert.Equal(expected, Ev.Run(Imp + "r = (" + expr + ")\n").Repr("r"));
        }

        [Theory]
        [InlineData("class P:\n    x: int\n    def __init__(self, x):\n        self.x = x\nr = attrgetter('x')(P(3))", "3")]
        [InlineData("class P:\n    x: int\n    def __init__(self, x):\n        self.x = x\nclass Q:\n    p: P\n    def __init__(self, p):\n        self.p = p\nr = attrgetter('p.x', 'p')(Q(P(4)))[0]", "4")]
        [InlineData("a = [1, 2, 3]\nsetitem(a, 0, 9)\ndelitem(a, 2)\nr = a", "[9, 2]")]
        [InlineData("d = {}\nsetitem(d, 'k', 1)\nr = d", "{'k': 1}")]
        public void Statements(string body, string expected)
        {
            Assert.Equal(expected, Ev.Run(Imp + body + "\n").Repr("r"));
        }

        [Theory]
        [InlineData("indexOf([1], 2)", "ValueError")]
        [InlineData("index(1.5)", "TypeError")]
        [InlineData("itemgetter()", "TypeError")]
        [InlineData("attrgetter(3)", "TypeError")]
        [InlineData("methodcaller()", "TypeError")]
        [InlineData("add(1, 'a')", "TypeError")]
        [InlineData("add(1)", "TypeError")]
        public void Errors(string expr, string exc)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(Imp + "r = " + expr + "\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
        }
    }
}

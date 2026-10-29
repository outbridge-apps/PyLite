using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/11 T-MFD-15/16: functools.reduce and functools.partial.
    [Trait("Category", "Compatibility")]
    public class FunctoolsTests
    {
        private const string P = "from functools import reduce, partial\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        // ---- reduce ----
        [InlineData("r = reduce(lambda a,b: a+b, [1,2,3,4])", "10")]
        [InlineData("r = reduce(lambda a,b: a+b, [1,2,3,4], 100)", "110")]
        [InlineData("r = reduce(lambda a,b: a+b, [5])", "5")]
        [InlineData("r = reduce(lambda a,b: a+b, [], 0)", "0")]
        [InlineData("r = reduce(lambda a,b: a*b, range(1,6))", "120")]
        [InlineData("r = reduce(lambda a,b: a+b, 'abc')", "'abc'")]
        [InlineData("r = reduce(lambda a,b: a+b, ['a'])", "'a'")]
        // ---- partial ----
        [InlineData("r = partial(lambda a,b: a+b, 10)(5)", "15")]
        [InlineData("r = partial(lambda a,b,c: (a,b,c), 1, 2)(3)", "(1, 2, 3)")]
        [InlineData("p = partial(lambda a,b: (a,b), a=1)\nr = p(b=2)", "(1, 2)")]
        [InlineData("p = partial(lambda **k: k, x=1)\nr = p(x=2)", "{'x': 2}")]
        [InlineData("r = partial(partial(lambda *a: a, 1), 2)(3)", "(1, 2, 3)")]
        [InlineData("r = partial(partial(lambda *a: a, 1), 2).args", "(1, 2)")]
        [InlineData("f = lambda *a: a\nr = (partial(partial(f, 1), 2).func is f)", "True")]
        [InlineData("r = partial(lambda: 42)()", "42")]
        [InlineData("p = partial(lambda a,b: a+b, 1)\nr = p.args", "(1,)")]
        [InlineData("p = partial(lambda a,b: a+b, b=2)\nr = list(p.keywords.items())", "[('b', 2)]")]
        public void ReduceAndPartial(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("reduce(lambda a,b: a+b, [])", "TypeError")]
        [InlineData("partial(5)", "TypeError")]
        public void Errors(string body, string excType)
        {
            Assert.Equal(excType, Raises("x = " + body).Value.ExcType.Name);
        }

        [Fact]
        public void ReduceEmptyMessage()
        {
            // Modern CPython (3.11+/RFC-003 §4) says "empty iterable" (not "empty sequence").
            string src = P + "msg = ''\ntry:\n    reduce(lambda a,b: a+b, [])\nexcept TypeError as e:\n    msg = str(e)\n";
            Assert.Equal("'reduce() of empty iterable with no initial value'", Ev.Run(src).Repr("msg"));
        }

        // Flattening a chain of partials copies the bound args each time: charged, so it ends on the budget.
        [Fact]
        public void PartialChainChargesItsCopies()
        {
            string src = P + "p = partial(print)\nfor i in range(100000):\n    p = partial(p, 1)\n";
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(src, ResourceLimits.Default.With(maxAllocBytes: 64L << 20)));
            Assert.Equal("MaxAllocBytes", ex.LimitName);
        }

        // Past eight bound keywords the merge looks names up by table; the call's keywords still win.
        [Fact]
        public void PartialManyKeywordsMerge()
        {
            string body = "d = {('k%d' % i): i for i in range(20)}\np = partial(partial(dict, **d), k3=-3, z=1)\n"
                + "r = p(k7=-7)\nr = (len(r), r['k3'], r['k7'], r['k19'], r['z'])";
            Assert.Equal("(21, -3, -7, 19, 1)", Body(body));
        }

        [Fact]
        public void PartialNonCallableMessage()
        {
            string src = P + "msg = ''\ntry:\n    partial(5)\nexcept TypeError as e:\n    msg = str(e)\n";
            Assert.Equal("'the first argument must be callable'", Ev.Run(src).Repr("msg"));
        }

        // lru_cache takes any callable (a partial too), and its keyword marker is private: f('kw', 'a', 1) and
        // f(a=1) used to share a key through the string marker (B13, B14).
        [Theory]
        [InlineData("import functools\nr = functools.lru_cache(partial(max, 0))(5)", "5")]
        [InlineData("import functools\nf = functools.lru_cache(lambda *a, **k: (a, k))\nr = (f('\\x00kw\\x00', 'a', 1), f(a=1))",
            "((('\\x00kw\\x00', 'a', 1), {}), ((), {'a': 1}))")]
        public void LruCacheKeys(string body, string expected)
        {
            Assert.Equal(expected, Body(body + "\n"));
        }

        // typed=True keys on the argument types too; maxsize=0 (or below) caches nothing, unhashable arguments
        // included, as in CPython.
        [Theory]
        [InlineData("typed=True", "[1, 1.0, 1]", "['int', 'float', 'int'] [1, 1.0]")]
        [InlineData("typed=False", "[1, 1.0, 1]", "['int', 'int', 'int'] [1]")]
        [InlineData("maxsize=0", "[1, 1, 1]", "['int', 'int', 'int'] [1, 1, 1]")]
        [InlineData("maxsize=-5", "[1, 1]", "['int', 'int'] [1, 1]")]
        [InlineData("maxsize=0", "[[1], [1]]", "['list', 'list'] [[1], [1]]")]
        [InlineData("None, True", "[2, 2.0]", "['int', 'float'] [2, 2.0]")]
        public void LruCacheTypedAndMaxsizeZero(string opts, string calls, string expected)
        {
            string body = "import functools\nseen = []\n@functools.lru_cache(" + opts + ")\ndef f(x):\n"
                + "    seen.append(x)\n    return type(x).__name__\nr = str([f(x) for x in " + calls + "]) + ' ' + str(seen)\n";
            Assert.Equal("\"" + expected + "\"", Body(body));
        }
    }
}

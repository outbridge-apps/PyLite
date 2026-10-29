using Outbridge.PyLite.Runtime.Errors;
using Xunit;

namespace Outbridge.PyLite.Tests.Evaluator
{
    // T2.2: the arrayless single-arg hook feeds filter/map/sort-key/groupby/takewhile/dropwhile/
    // filterfalse/re.sub-repl. Every callable kind must keep working (script def/lambda, closures,
    // defaults, builtins, partial), and errors/recursion must be transparent.
    [Trait("Category", "Compatibility")]
    public class CallHook1Tests
    {
        [Theory]
        [InlineData("r = list(filter(lambda x: x % 2 == 0, range(10)))", "[0, 2, 4, 6, 8]")]
        [InlineData("def f(x):\n    return x > 5\nr = list(filter(f, range(10)))", "[6, 7, 8, 9]")]
        [InlineData("r = list(filter(None, [0, 1, '', 'a', [], [1]]))", "[1, 'a', [1]]")]
        [InlineData("r = list(map(lambda x: x * 3, range(5)))", "[0, 3, 6, 9, 12]")]
        [InlineData("r = list(map(str, range(3)))", "['0', '1', '2']")]                    // builtin callee
        [InlineData("r = list(map(lambda a, b: a + b, [1, 2], [10, 20]))", "[11, 22]")]    // 2-iterable stays on the array path
        [InlineData("import functools\nadd1 = functools.partial(lambda a, b: a + b, 1)\nr = list(map(add1, range(3)))", "[1, 2, 3]")]
        public void FilterMapAcrossCalleeKinds(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r").Trim('\''));
        }

        [Fact]
        public void ClosureAndDefaultCalleesThroughTheHook()
        {
            var res = Ev.Run(
                "def make(n):\n"
                + "    def inner(x):\n"
                + "        return x + n\n"
                + "    return inner\n"
                + "def withdef(x, d=100):\n"
                + "    return x + d\n"
                + "a = list(map(make(10), range(3)))\n"
                + "b = list(map(withdef, range(3)))\n"
                + "r = (a, b)\n");
            Assert.Equal("([10, 11, 12], [100, 101, 102])", res.Repr("r"));
        }

        [Theory]
        [InlineData("r = sorted(range(10), key=lambda x: -x)", "[9, 8, 7, 6, 5, 4, 3, 2, 1, 0]")]
        [InlineData("lst = [3, 1, 2]\nlst.sort(key=lambda x: x % 2)\nr = lst", "[2, 3, 1]")]
        [InlineData("import itertools\nr = [k for k, g in itertools.groupby([1, 1, 2, 2, 3], key=lambda v: v * 10)]", "[10, 20, 30]")]
        [InlineData("import itertools\nr = list(itertools.takewhile(lambda x: x < 3, range(10)))", "[0, 1, 2]")]
        [InlineData("import itertools\nr = list(itertools.dropwhile(lambda x: x < 7, range(10)))", "[7, 8, 9]")]
        [InlineData("import itertools\nr = list(itertools.filterfalse(lambda x: x % 2, range(6)))", "[0, 2, 4]")]
        [InlineData("import re\nr = re.sub('[0-9]+', lambda m: '<' + m.group(0) + '>', 'a1b22')", "a<1>b<22>")]
        public void KeyAndPredicateSitesThroughTheHook(string src, string expected)
        {
            Assert.Equal(expected, Ev.Run(src).Repr("r").Trim('\''));
        }

        [Fact]
        public void HookCalleeErrorsPropagateTyped()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run(
                "r = list(map(lambda x: x // 0, range(3)))"));
            Assert.Equal("ZeroDivisionError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void HookRecursionStaysDepthGuarded()
        {
            // The callee recurses through the hook path: MaxCallDepth must still fire (Recursion).
            var ex = Assert.Throws<EngineAbort>(() => Ev.Run(
                "def f(x):\n    return f(x + 1)\nr = list(map(f, [0]))\n"));
            Assert.Equal(EngineAbortKind.Recursion, ex.Kind);
        }

        [Fact]
        public void NonCallableThroughHookStillTypeError()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("r = list(map(5, range(3)))"));
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
        }
    }
}

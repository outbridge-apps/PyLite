using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    // A protocol miss is asked, not caught: PyOps.TryGetIterator answers null where GetIterator would
    // raise, and DictValue.TryGetItem carries defaultdict's factory and Counter's 0 so a ChainMap walks
    // its maps without a KeyError per miss. The callers' own texts are what the script sees.
    [Trait("Category", "Compatibility")]
    public class ProtocolMissTests
    {
        [Theory]
        [InlineData("''.join(5)", "TypeError('can only join an iterable')")]
        [InlineData("{}.update([5])", "TypeError('cannot convert dictionary update sequence element #0 to a sequence')")]
        [InlineData("f(*5)", "TypeError('f() argument after * must be a sequence, not int')")]
        [InlineData("zip(5)", "TypeError('zip argument #1 must support iteration')")]
        [InlineData("5 in 5", "TypeError(\"argument of type 'int' is not iterable\")")]
        [InlineData("ChainMap(Counter(), {'a': 1})['a']", "0")]
        [InlineData("ChainMap({}, defaultdict(list))['x']", "[]")]
        [InlineData("ChainMap({'a': 1}, defaultdict(None))['x']", "KeyError('x')")]
        [InlineData("(urlencode({'a': 5}), urlencode({'a': [1, 2]}, doseq=True), urlencode({'a': 'xy'}, doseq=True))", "('a=5', 'a=1&a=2', 'a=xy')")]
        public void AMissIsAskedNotCaught(string expr, string expected)
        {
            string src = "from collections import ChainMap, Counter, defaultdict\nfrom urllib.parse import urlencode\n"
                + "def f(*a):\n    return a\ntry:\n    r = " + expr + "\nexcept Exception as e:\n    r = e\n";
            Assert.Equal(expected, Outbridge.PyLite.Tests.Evaluator.Ev.Run(src).Repr("r"));
        }
    }
}

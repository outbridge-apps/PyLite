using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/11 T-MFD-14: collections.deque.
    [Trait("Category", "Compatibility")]
    public class CollectionsDequeTests
    {
        private const string P = "from collections import deque\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        [InlineData("d = deque([1,2,3])\nd.append(4)\nr = list(d)", "[1, 2, 3, 4]")]
        [InlineData("d = deque([1,2,3])\nd.appendleft(0)\nr = list(d)", "[0, 1, 2, 3]")]
        [InlineData("d = deque([1,2,3])\nr = (d.pop(), list(d))", "(3, [1, 2])")]
        [InlineData("d = deque([1,2,3])\nr = (d.popleft(), list(d))", "(1, [2, 3])")]
        [InlineData("d = deque()\nd.extendleft([1,2,3])\nr = list(d)", "[3, 2, 1]")]
        [InlineData("d = deque([1,2,3,4,5])\nd.rotate(2)\nr = list(d)", "[4, 5, 1, 2, 3]")]
        [InlineData("d = deque([1,2,3,4,5])\nd.rotate(-1)\nr = list(d)", "[2, 3, 4, 5, 1]")]
        [InlineData("d = deque([1,2,3,4,5])\nd.rotate(7)\nr = list(d)", "[4, 5, 1, 2, 3]")]
        [InlineData("d = deque([1,2,3], maxlen=2)\nr = list(d)", "[2, 3]")]
        [InlineData("d = deque(maxlen=2)\nd.append(1)\nd.append(2)\nd.append(3)\nr = list(d)", "[2, 3]")]
        [InlineData("d = deque(maxlen=2)\nd.appendleft(1)\nd.appendleft(2)\nd.appendleft(3)\nr = list(d)", "[3, 2]")]
        [InlineData("d = deque([1,2,3])\nd.remove(2)\nr = list(d)", "[1, 3]")]
        [InlineData("r = deque([1,2,2,3]).count(2)", "2")]
        [InlineData("d = deque([1,2,3])\nr = d[1]", "2")]
        [InlineData("d = deque([1,2,3])\nr = d[-1]", "3")]
        [InlineData("d = deque([1,2,3])\nd[1] = 9\nr = list(d)", "[1, 9, 3]")]
        [InlineData("d = deque([1,2,3])\ndel d[0]\nr = list(d)", "[2, 3]")]
        [InlineData("d = deque([1,2,3])\nd.reverse()\nr = list(d)", "[3, 2, 1]")]
        [InlineData("r = deque([1,2], maxlen=5).maxlen", "5")]
        [InlineData("r = deque([1,2]).maxlen", "None")]
        [InlineData("r = repr(deque([1,2], maxlen=3))", "'deque([1, 2], maxlen=3)'")]
        [InlineData("r = repr(deque([1,2,3]))", "'deque([1, 2, 3])'")]
        [InlineData("r = (bool(deque()), bool(deque([0])))", "(False, True)")]
        [InlineData("r = (deque([1,2]) == deque([1,2]))", "True")]
        [InlineData("r = (deque([1,2]) == [1,2])", "False")]
        [InlineData("d = deque(maxlen=0)\nd.append(1)\nr = list(d)", "[]")]
        [InlineData("d = deque([1,2,3])\nr = (2 in d, 9 in d, len(d))", "(True, False, 3)")]
        [InlineData("r = (deque([1,2]) < deque([1,3]))", "True")]
        public void Deques(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("deque().pop()", "IndexError")]
        [InlineData("deque().popleft()", "IndexError")]
        [InlineData("deque([1,2,3]).remove(9)", "ValueError")]
        [InlineData("deque([1,2,3])[9]", "IndexError")]
        [InlineData("deque(maxlen=-1)", "ValueError")]
        [InlineData("deque([1,2], maxlen=2).insert(0, 9)", "IndexError")]
        [InlineData("deque([1,2]).index(9)", "ValueError")]
        public void Errors(string body, string excType)
        {
            Assert.Equal(excType, Raises("x = " + body).Value.ExcType.Name);
        }

        [Fact]
        public void MutationDuringIterationRaises()
        {
            string body = "d = deque([1,2,3])\ncaught = False\ntry:\n    for x in d:\n        d.append(x)\nexcept RuntimeError:\n    caught = True\nr = caught\n";
            Assert.Equal("True", Body(body));
        }

        [Fact]
        public void PopEmptyMessage()
        {
            string src = P + "msg = ''\ntry:\n    deque().pop()\nexcept IndexError as e:\n    msg = str(e)\n";
            Assert.Equal("'pop from an empty deque'", Ev.Run(src).Repr("msg"));
        }
    }
}

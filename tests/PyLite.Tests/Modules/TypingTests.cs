using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // v2 A1: the typing runtime stub — imports, subscription, cast/TypeVar/NewType, decorators.
    [Trait("Category", "Compatibility")]
    public class TypingTests
    {
        private static string Body(string body)
        {
            return Ev.Run(body).Repr("r");
        }

        [Theory]
        // ---- imports and subscription are inert ----
        [InlineData("import typing\nr = True", "True")]
        [InlineData("from typing import Optional, List, Dict, Any, Union\nr = True", "True")]
        [InlineData("from typing import Optional\nr = repr(Optional[int])", "'typing.Optional'")]
        [InlineData("from typing import Dict\nx = Dict[str, int]\nr = repr(x)", "'typing.Dict'")]
        [InlineData("from typing import Callable\nr = repr(Callable[[int], str])", "'typing.Callable'")]
        [InlineData("from typing import Union\nr = repr(Union[int, str, None])", "'typing.Union'")]
        [InlineData("from typing import Literal\nr = repr(Literal['a', 'b'])", "'typing.Literal'")]
        [InlineData("import typing\nr = typing.TYPE_CHECKING", "False")]
        // ---- runtime helpers ----
        [InlineData("from typing import cast\nr = cast(int, '5')", "'5'")]
        [InlineData("from typing import TypeVar\nT = TypeVar('T')\nr = repr(T)", "'typing.T'")]
        [InlineData("from typing import NewType\nUserId = NewType('UserId', int)\nr = UserId(42)", "42")]
        [InlineData("from typing import get_type_hints\nr = get_type_hints(len)", "{}")]
        [InlineData("from typing import get_type_hints\nr = get_type_hints(int)", "{}")]
        // function annotations survive the same way: the plain names, parameters then 'return'
        [InlineData("from typing import get_type_hints\ndef f(x: int, y: str = 'a', *, z: float) -> bool:\n    return True\nr = get_type_hints(f)",
            "{'x': 'int', 'y': 'str', 'z': 'float', 'return': 'bool'}")]
        [InlineData("from typing import get_type_hints\ndef g(x: list[int], y) -> None:\n    pass\nr = get_type_hints(g)", "{'return': 'None'}")]
        [InlineData("from typing import get_type_hints\nr = get_type_hints(lambda x: x)", "{}")]
        [InlineData("from typing import get_type_hints\nclass P:\n    def m(self, n: int) -> str:\n        return ''\nr = get_type_hints(P.m)", "{'n': 'int', 'return': 'str'}")]
        // field annotations survive as the NAME they were written with (the __future__ shape)
        [InlineData("from typing import get_type_hints\nfrom dataclasses import dataclass\n@dataclass\nclass P:\n    x: int\n    y: str\nr = get_type_hints(P)",
            "{'x': 'int', 'y': 'str'}")]
        [InlineData("from typing import get_type_hints\nfrom dataclasses import dataclass\n@dataclass\nclass P:\n    x: int\nr = get_type_hints(P(1))",
            "{'x': 'int'}")]
        [InlineData("from typing import get_type_hints\nclass P:\n    n: int\n    def __init__(self, n):\n        self.n = n\nr = get_type_hints(P)",
            "{'n': 'int'}")]
        [InlineData("from typing import get_type_hints\nfrom dataclasses import dataclass\n@dataclass\nclass B:\n    a: int\n@dataclass\nclass D(B):\n    b: str\nr = get_type_hints(D)",
            "{'a': 'int', 'b': 'str'}")]
        [InlineData("from typing import get_args, get_origin, List\nr = (get_args(List[int]), get_origin(List))", "((), None)")]
        [InlineData("from typing import assert_type\nr = assert_type(7, int)", "7")]
        // ---- decorators are identity ----
        [InlineData("from typing import overload\n@overload\ndef f(x):\n    return x * 2\nr = f(21)", "42")]
        [InlineData("from typing import final\n@final\ndef g():\n    return 'ok'\nr = g()", "'ok'")]
        // ---- annotated code using typing forms runs ----
        [InlineData("from typing import Optional, List\ndef f(a: Optional[int], b: List[int] = []) -> Optional[str]:\n    return str(a)\nr = f(1)", "'1'")]
        [InlineData("from typing import Final\nMAX: Final = 99\nr = MAX", "99")]
        // ---- TYPE_CHECKING guard pattern ----
        [InlineData("from typing import TYPE_CHECKING\nif TYPE_CHECKING:\n    import nonexistent_module\nr = 'skipped'", "'skipped'")]
        public void TypingStub(string body, string expected)
        {
            Assert.Equal(expected, Body(body + "\n"));
        }

        [Fact]
        public void NamedTupleFunctionalForm()
        {
            string src = "from typing import NamedTuple\n"
                + "P = NamedTuple('P', [('x', int), ('y', int)])\n"
                + "p = P(1, 2)\n"
                + "r = (p.x, p.y, p == (1, 2), repr(p))\n";
            Assert.Equal("(1, 2, True, 'P(x=1, y=2)')", Body(src));
        }

        [Fact]
        public void SubscriptionReturnsSameForm()
        {
            Assert.Equal("True", Body("from typing import Optional\nr = (Optional[int] is Optional)\n"));
        }

        [Fact]
        public void FormsAreNotCallable()
        {
            var ex = Assert.Throws<Outbridge.PyLite.Runtime.Errors.ScriptException>(
                () => Ev.Run("from typing import List\nx = List()\n"));
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
        }
    }
}

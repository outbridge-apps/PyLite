using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // impl/11 T-MFD-13: collections.namedtuple.
    [Trait("Category", "Compatibility")]
    public class CollectionsNamedTupleTests
    {
        private const string P = "from collections import namedtuple\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        [InlineData("P = namedtuple('P','x y')\nr = P(1,2).x", "1")]
        [InlineData("P = namedtuple('P','x y')\nr = P(1,2)[1]", "2")]
        [InlineData("P = namedtuple('P','x y')\nr = repr(P(1,2))", "'P(x=1, y=2)'")]
        [InlineData("P = namedtuple('P',['x','y'])\nr = P(x=3,y=4).y", "4")]
        [InlineData("P = namedtuple('P','x y')\nr = P._make([5,6]).x", "5")]
        [InlineData("P = namedtuple('P','x y')\nr = repr(P(1,2)._replace(y=9))", "'P(x=1, y=9)'")]
        [InlineData("P = namedtuple('P','x y')\nr = list(P(1,2)._asdict().items())", "[('x', 1), ('y', 2)]")]
        [InlineData("P = namedtuple('P','x y')\nr = P._fields", "('x', 'y')")]
        // defaults= (3.7+): applied to the rightmost fields; _field_defaults names them
        [InlineData("P = namedtuple('P', 'x y z', defaults=[0])\nr = repr(P(1, 2))", "'P(x=1, y=2, z=0)'")]
        [InlineData("P = namedtuple('P', 'x y z', defaults=(5, 6))\nr = (repr(P(1)), repr(P(1, 2)), repr(P(1, 2, 3)), repr(P(1, z=9)))", "('P(x=1, y=5, z=6)', 'P(x=1, y=2, z=6)', 'P(x=1, y=2, z=3)', 'P(x=1, y=5, z=9)')")]
        [InlineData("P = namedtuple('P', 'x y', defaults=[0])\nr = (P._field_defaults, P(1)._replace(y=2), P._make([3, 4]))", "({'y': 0}, P(x=1, y=2), P(x=3, y=4))")]
        [InlineData("P = namedtuple('P', 'x y', defaults=iter([1, 2]))\nr = (P(), P._field_defaults)", "(P(x=1, y=2), {'x': 1, 'y': 2})")]
        [InlineData("P = namedtuple('P', 'x y')\nr = P._field_defaults", "{}")]
        [InlineData("P = namedtuple('P', 'x y', defaults=None)\nr = P._field_defaults", "{}")]
        [InlineData("P = namedtuple('P','x y')\nr = P(1,2)._fields", "('x', 'y')")]
        [InlineData("P = namedtuple('P','x,y')\nr = P(1,2).y", "2")]
        [InlineData("P = namedtuple('P','x y')\nr = (P(1,2) == (1,2))", "True")]
        [InlineData("P = namedtuple('P','x y')\nQ = namedtuple('Q','a b')\nr = (P(1,2) == Q(1,2))", "True")]
        [InlineData("P = namedtuple('P',['abc','def','x'], rename=True)\nr = P._fields", "('abc', '_1', 'x')")]
        [InlineData("P = namedtuple('P','x _y 1 x', rename=True)\nr = P._fields", "('x', '_1', '_2', '_3')")]
        [InlineData("P = namedtuple('P','x y')\nr = (P(1,2)._asdict() == {'x':1,'y':2})", "True")]
        public void NamedTuples(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("namedtuple('P','x x')", "ValueError", "Encountered duplicate field name: 'x'")]
        [InlineData("namedtuple('P','x _y')", "ValueError", "Field names cannot start with an underscore: '_y'")]
        [InlineData("namedtuple('P','x def')", "ValueError", "Type names and field names cannot be a keyword: 'def'")]
        [InlineData("namedtuple('P','x 1a')", "ValueError", "Type names and field names must be valid identifiers: '1a'")]
        public void ValidationErrors(string body, string excType, string msgFragment)
        {
            ScriptException ex = Raises("x = " + body);
            Assert.Equal(excType, ex.Value.ExcType.Name);
            AssertMessage(body, msgFragment);
        }

        private static void AssertMessage(string body, string expected)
        {
            // The messages embed a single-quoted name, so their repr is double-quoted.
            string src = P + "msg = ''\ntry:\n    " + body + "\nexcept ValueError as e:\n    msg = str(e)\n";
            Assert.Equal("\"" + expected + "\"", Ev.Run(src).Repr("msg"));
        }

        [Theory]
        [InlineData("namedtuple('P', 'x', defaults=[1, 2])", "Got more default values than field names")]
        [InlineData("namedtuple('P', 'x y z', defaults=[0])(1)", "__new__() missing 1 required positional argument: 'y'")]
        public void DefaultsErrors(string body, string message)
        {
            ScriptException ex = Raises("x = " + body);
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
            Assert.Equal(message, ex.Value.GetMessageText(Rt.Ctx()));
        }

        [Fact]
        public void ReplaceUnexpectedField()
        {
            ScriptException ex = Raises("P = namedtuple('P','x y')\nP(1,2)._replace(z=1)");
            Assert.Equal("ValueError", ex.Value.ExcType.Name);
            string src = P + "P = namedtuple('P','x y')\nmsg = ''\ntry:\n    P(1,2)._replace(z=1)\nexcept ValueError as e:\n    msg = str(e)\n";
            Assert.Equal("\"Got unexpected field names: ['z']\"", Ev.Run(src).Repr("msg"));
        }

        [Fact]
        public void MakeWrongLength()
        {
            ScriptException ex = Raises("P = namedtuple('P','x y')\nP._make([1])");
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
        }

        // type(p) is the class namedtuple() returned, never a process-wide type looked up by the script's
        // name: that one was not callable, and namedtuple('int', ...) made type(p) is int (B8).
        [Theory]
        [InlineData("P = namedtuple('P','x y')\nr = type(P(1,2)) is P", "True")]
        [InlineData("P = namedtuple('P','x y')\nr = type(P(1,2))(3, 4)", "P(x=3, y=4)")]
        [InlineData("I = namedtuple('int','a')\nr = (type(I(1)) is int, type(I(1)) is I)", "(False, True)")]
        [InlineData("P = namedtuple('P','x y')\nr = type(iter(P(1,2))).__name__", "'tuple_iterator'")]
        public void TypeIsTheReturnedClass(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        // Type, field and regex group names are identifiers by str.isidentifier (PEP 3131), not ASCII only (D16).
        [Theory]
        [InlineData("P = namedtuple('\\u0422', '\\u0438 \\u0439')\nr = P(1, 2)[1]", "2")]
        [InlineData("import re\nr = re.match('(?P<\\u0438>a)', 'a').group('\\u0438')", "'a'")]
        [InlineData("try:\n    namedtuple('T', ['1x'])\n    r = 'accepted'\nexcept ValueError:\n    r = 'refused'", "'refused'")]
        public void NamesAreUnicodeIdentifiers(string body, string expected)
        {
            Assert.Equal(expected, Body(body + "\n"));
        }
    }
}

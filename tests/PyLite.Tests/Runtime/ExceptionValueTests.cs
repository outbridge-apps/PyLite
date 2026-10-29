using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    [Trait("Category", "Compatibility")]
    public class ExceptionValueTests
    {
        private static ExceptionValue Exc(PyExceptionType t, params ScriptValue[] args) => new ExceptionValue(t, args);

        [Fact]
        public void KeyErrorStrIsRepr()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("'k'", Exc(PyExceptionTypes.KeyError, ctx.Values.Str("k")).GetMessageText(ctx));
        }

        [Fact]
        public void ValueErrorStrOneArg()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("bad", Exc(PyExceptionTypes.ValueError, ctx.Values.Str("bad")).GetMessageText(ctx));
        }

        [Fact]
        public void ValueErrorStrNoArgs()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("", Exc(PyExceptionTypes.ValueError).GetMessageText(ctx));
        }

        [Fact]
        public void ValueErrorStrTwoArgs()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("(1, 2)", Exc(PyExceptionTypes.ValueError, ctx.Values.Int(1), ctx.Values.Int(2)).GetMessageText(ctx));
        }

        [Fact]
        public void ValueErrorReprOneArgNoTrailingComma()
        {
            // 3.7+ form (RFC-003 §4): repr of a single-arg exception has no trailing comma.
            var ctx = Rt.Ctx();
            Assert.Equal("ValueError('x')", Exc(PyExceptionTypes.ValueError, ctx.Values.Str("x")).Repr(ctx));
        }

        [Fact]
        public void ValueErrorReprNoArgs()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("ValueError()", Exc(PyExceptionTypes.ValueError).Repr(ctx));
        }

        [Fact]
        public void NameErrorText()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("name 'foo' is not defined", Raise.NameError(ctx, "foo").Value.GetMessageText(ctx));
        }

        [Fact]
        public void UnboundLocalText()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("local variable 'x' referenced before assignment", Raise.UnboundLocal(ctx, "x").Value.GetMessageText(ctx));
        }

        [Fact]
        public void AttributeErrorText()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("'int' object has no attribute 'encode'", Raise.Attribute(ctx, "int", "encode").Value.GetMessageText(ctx));
        }

        [Fact]
        public void MessageTruncatedTo2048()
        {
            var ctx = Rt.Ctx();
            var ev = Raise.Make(ctx, PyExceptionTypes.ValueError, new string('a', 10_000)).Value;
            Assert.Equal(2048, ((StrValue)ev.Args[0]).Value.Length);
        }
    }
}

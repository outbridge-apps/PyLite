using Outbridge.PyLite.Runtime.Errors;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    [Trait("Category", "Compatibility")]
    public class ExceptionTreeTests
    {
        [Fact] public void ZeroDivIsArithmetic() => Assert.True(PyExceptionTypes.ZeroDivisionError.IsSubtypeOf(PyExceptionTypes.ArithmeticError));
        [Fact] public void ZeroDivNotLookup() => Assert.False(PyExceptionTypes.ZeroDivisionError.IsSubtypeOf(PyExceptionTypes.LookupError));

        [Fact]
        public void UnboundLocalChain()
        {
            Assert.True(PyExceptionTypes.UnboundLocalError.IsSubtypeOf(PyExceptionTypes.NameError));
            Assert.True(PyExceptionTypes.UnboundLocalError.IsSubtypeOf(PyExceptionTypes.Exception_));
        }

        [Fact] public void JsonDecodeIsValueError() => Assert.True(PyExceptionTypes.JSONDecodeError.IsSubtypeOf(PyExceptionTypes.ValueError));
        [Fact] public void UnicodeDecodeIsUnicodeAndValueError()
        {
            Assert.True(PyExceptionTypes.UnicodeDecodeError.IsSubtypeOf(PyExceptionTypes.UnicodeError));
            Assert.True(PyExceptionTypes.UnicodeEncodeError.IsSubtypeOf(PyExceptionTypes.ValueError));
        }
        [Fact] public void MemoryErrorAbsent() => Assert.Null(PyExceptionTypes.GetByName("MemoryError"));
        [Fact] public void KeyErrorParentIsLookup() => Assert.Equal("LookupError", PyExceptionTypes.GetByName("KeyError").Parent.Name);
        [Fact] public void TotalTypeCount() => Assert.Equal(28, PyExceptionTypes.All.Count);   // + RecursionError (2026-09-11), EOFError (2026-10-28)
    }
}

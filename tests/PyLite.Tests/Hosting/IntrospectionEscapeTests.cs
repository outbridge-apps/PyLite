using Outbridge.PyLite.Hosting;
using Xunit;

namespace Outbridge.PyLite.Tests.Hosting
{
    // The classic ways out of a Python sandbox, through the public Run: climb from any object to
    // object.__subclasses__(), read a function's globals or code or a traceback's frame, reach the builtins or
    // the import machinery, walk attributes through a format string, or ask a value for its CLR type. Each one
    // must end as the script's own Python exception; none may hand the script anything of the host.
    [Trait("Category", "Sandbox")]
    public class IntrospectionEscapeTests
    {
        private static readonly ScriptEngine Engine = new ScriptEngine(new EngineOptions());

        private const string Prelude = "def fn():\n    return 1\nclass C:\n    def __init__(self):\n        self.a = 1\n";

        [Theory]
        [InlineData("().__class__.__bases__[0].__subclasses__()", "AttributeError")]
        [InlineData("[].__class__.__base__", "AttributeError")]
        [InlineData("type.__mro__", "AttributeError")]
        [InlineData("type(1).__bases__", "AttributeError")]
        [InlineData("ValueError.__subclasses__()", "AttributeError")]
        [InlineData("object.__subclasses__()", "NameError")]
        [InlineData("fn.__globals__", "AttributeError")]
        [InlineData("fn.__code__.co_consts", "AttributeError")]
        [InlineData("fn.__closure__", "AttributeError")]
        [InlineData("(lambda: 0).__globals__", "AttributeError")]
        [InlineData("C.__init__.__globals__", "AttributeError")]
        [InlineData("C().__init__.__func__.__globals__", "AttributeError")]
        [InlineData("C().__init__.__self__", "AttributeError")]
        [InlineData("C.__dict__", "AttributeError")]
        [InlineData("getattr(C(), '__dict__')", "AttributeError")]
        [InlineData("'{0.__init__.__globals__}'.format(C())", "AttributeError")]
        [InlineData("'{0.__class__.__mro__}'.format(1)", "AttributeError")]
        [InlineData("__builtins__", "NameError")]
        [InlineData("__import__('os')", "NameError")]
        [InlineData("eval('1')", "NameError")]
        [InlineData("vars(C())", "NameError")]
        [InlineData("getattr(1, 'GetType')()", "AttributeError")]
        [InlineData("(1).__reduce_ex__(2)", "AttributeError")]
        public void EscapeRouteIsAPythonError(string expr, string pythonType)
        {
            RunResult r = Engine.Run(Prelude + "x = " + expr + "\n", null);
            Assert.False(r.Succeeded, expr + " succeeded");
            Assert.Equal(ScriptErrorKind.RuntimeError, r.Error.Kind);
            Assert.Equal(pythonType, r.Error.PythonType);
        }

        // A traceback carries no frame: the usual route from an exception to the globals of every frame.
        [Fact]
        public void ATracebackIsNotReachable()
        {
            RunResult r = Engine.Run("try:\n    1 / 0\nexcept ZeroDivisionError as e:\n    x = e.__traceback__.tb_frame.f_globals\n", null);
            Assert.Equal(ScriptErrorKind.RuntimeError, r.Error.Kind);
            Assert.Equal("AttributeError", r.Error.PythonType);
        }

        [Theory]
        [InlineData("import os")]
        [InlineData("import sys")]
        [InlineData("import subprocess")]
        [InlineData("import socket")]
        [InlineData("import ctypes")]
        [InlineData("import clr")]
        [InlineData("import System")]
        [InlineData("from importlib import import_module")]
        public void NoHostModuleIsImportable(string statement)
        {
            RunResult r = Engine.Run(statement + "\n", null);
            Assert.Equal(ScriptErrorKind.RuntimeError, r.Error.Kind);
            Assert.Equal("ImportError", r.Error.PythonType);
        }
    }
}

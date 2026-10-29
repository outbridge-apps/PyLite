using System.Collections.Generic;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    // Class-body constants (1.1.0): lowered by the parser to hidden variables assigned before the class
    // statement. What only the engine can get wrong - evaluation order and count, scopes, hidden names.
    [Trait("Category", "Compatibility")]
    public class ClassConstantTests
    {
        private static string R(string src)
        {
            return Ev.Run(src).Repr("r");
        }

        [Fact]
        public void EachValueIsEvaluatedOnceInOrder()
        {
            Assert.Equal("(['a', 'b', 'c'], 1, 2)", R("log = []\ndef note(x):\n    log.append(x)\n    return len(log)\n"
                + "class C:\n    A = note('a')\n    def m(self):\n        return 0\n    B = note('b')\n    x: int = note('c')\n"
                + "C()\nC()\nr = (log, C.A, C.B)\n"));
        }

        [Fact]
        public void AReferenceToALaterConstantIsANameErrorAsInCPython()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("class C:\n    A = B\n    B = 1\n"));
            Assert.Equal("NameError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void ARebindingSeesTheEarlierValue()
        {
            Assert.Equal("(3, 2)", R("class C:\n    A = 1\n    A = A + 2\n    B = A - 1\nr = (C.A, C.B)\n"));
        }

        [Fact]
        public void AClassMadeInAFunctionOrALoopGetsItsOwnValues()
        {
            Assert.Equal("([0, 1, 2], [0, 10, 20], 'x')", R("def make(n):\n    class K:\n        N = n\n        TEN = N * 10\n    return K\n"
                + "ks = [make(i) for i in range(3)]\nloop = []\nfor j in range(3):\n    class L:\n        V = j\n    loop.append(L)\n"
                + "def outer():\n    tag = 'x'\n    class M:\n        T = tag\n        def get(self):\n            return self.T\n    return M().get()\n"
                + "r = ([k.N for k in ks], [k.TEN for k in ks], outer())\n"));
        }

        [Fact]
        public void TheHiddenVariablesAreNotModuleAttributes()
        {
            var options = new EngineOptions
            {
                DefaultLimits = ResourceLimits.Default,
                PythonModules = new Dictionary<string, string>
                {
                    ["codes"] = "class Status:\n    NEW = 'new'\n    DONE = 'done'\n",
                },
            };
            var engine = new ScriptEngine(options);
            RunResult r = engine.Run("import codes\nnames = ','.join(n for n in dir(codes) if not n.startswith('__'))\nv = codes.Status.DONE\n",
                new RunRequest { ScalarOutputNames = new[] { "names", "v" } });
            Assert.True(r.Succeeded, r.Error?.Message);
            Assert.Equal("Status", r.ScalarsOut["names"]);
            Assert.Equal("done", r.ScalarsOut["v"]);
        }

        [Fact]
        public void AnErrorInAConstantReportsItsLine()
        {
            var engine = new ScriptEngine(new EngineOptions { DefaultLimits = ResourceLimits.Default });
            RunResult r = engine.Run("class C:\n    A = 1\n    B = A // 0\n", new RunRequest());
            Assert.Equal("ZeroDivisionError", r.Error.PythonType);
            Assert.Equal(3, r.Error.Line);
        }

        [Fact]
        public void AMethodBodyNeitherSeesNorRenamesTheClassNames()
        {
            Assert.Equal("('module', 'class', 'inner')", R("X = 'module'\nclass C:\n    X = 'class'\n    def f(self):\n        return X\n"
                + "    def g(self):\n        class D:\n            X = 'inner'\n        return D.X\nr = (C().f(), C.X, C().g())\n"));
        }

        [Fact]
        public void LambdasAndComprehensionsInTheBodySeeEarlierConstants()
        {
            // dialect: CPython hides the class scope from lambda and comprehension bodies (NameError);
            // here they see it, so a script that works in CPython still works.
            Assert.Equal("([0, 2, 4], 5)", R("class C:\n    K = 2\n    ROW = [i * K for i in range(3)]\n    F = lambda: K + 3\nr = (C.ROW, C.F())\n"));
        }

        [Fact]
        public void HashNoneMustBeNone()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("class C:\n    __hash__ = 5\n"));
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void ManyConstantsAndALargeValueAreCharged()
        {
            var lim = ResourceLimits.Default.With(maxAllocBytes: 16 * 1024 * 1024);
            EngineAbort a = Assert.Throws<EngineAbort>(() => Ev.Run("class Big:\n    DATA = 'x' * 4000000\n    MORE = DATA * 4\n", lim));
            Assert.Equal(EngineAbortKind.Memory, a.Kind);
            var src = new System.Text.StringBuilder("class Many:\n");
            for (int i = 0; i < 500; i++)
                src.Append("    C").Append(i).Append(" = ").Append(i).Append('\n');
            src.Append("r = Many.C499 + Many().C0\n");
            Assert.Equal("499", R(src.ToString()));
        }
    }
}

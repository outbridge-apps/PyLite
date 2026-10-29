using System.Collections.Generic;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    // super() (1.1.0): the parser writes super() in a method as super(<hidden class variable>, <first
    // parameter>). What only the engine can get wrong - which class and which parameter, the hidden name.
    [Trait("Category", "Compatibility")]
    public class SuperTests
    {
        private static string R(string src)
        {
            return Ev.Run(src).Repr("r");
        }

        [Fact]
        public void TheFirstParameterIsTheObjectWhateverItsName()
        {
            Assert.Equal("'AB'", R("class A:\n    def f(this):\n        return 'A'\n"
                + "class B(A):\n    def f(this):\n        return super().f() + 'B'\nr = B().f()\n"));
        }

        [Fact]
        public void AClassInsideAMethodHasItsOwnSuper()
        {
            Assert.Equal("('A', 'O+A')", R("class A:\n    def f(self):\n        return 'A'\n"
                + "class O(A):\n    def f(self):\n        class I(A):\n            def f(self):\n                return super().f()\n"
                + "        return (I().f(), 'O+' + super().f())\nr = O().f()\n"));
        }

        [Fact]
        public void ADecoratorMayCallAMethodThatUsesSuper()
        {
            Assert.Equal("'AB'", R("seen = []\ndef probe(cls):\n    seen.append(cls().f())\n    return cls\n"
                + "class A:\n    def f(self):\n        return 'A'\n@probe\nclass B(A):\n    def f(self):\n        return super().f() + 'B'\n"
                + "r = seen[0]\n"));
        }

        [Fact]
        public void SuperInAClassBodyHasNoArguments()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("class A:\n    X = super()\n"));
            Assert.Equal("RuntimeError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void ADataclassSubclassRunsItsGeneratedInitNotABaseWrittenOne()
        {
            // CPython's rule; in this dialect the base's self.x slot is a field of the dataclass too
            Assert.Equal("('x', 'v')", R("from dataclasses import dataclass\n"
                + "class Named:\n    def __init__(self, name):\n        self.name = 'named:' + name\n"
                + "@dataclass\nclass Tagged(Named):\n    tag: str = ''\nt = Tagged('x', 'v')\nr = (t.name, t.tag)\n"));
        }

        [Fact]
        public void TheHiddenClassVariableIsNotAModuleAttribute()
        {
            var options = new EngineOptions
            {
                DefaultLimits = ResourceLimits.Default,
                PythonModules = new Dictionary<string, string>
                {
                    ["shapes"] = "class Shape:\n    def name(self):\n        return 'shape'\n"
                        + "class Box(Shape):\n    def name(self):\n        return super().name() + '/box'\n",
                },
            };
            var engine = new ScriptEngine(options);
            RunResult r = engine.Run("import shapes\nnames = ','.join(n for n in dir(shapes) if not n.startswith('__'))\n"
                + "class Crate(shapes.Box):\n    def name(self):\n        return super().name() + '/crate'\nv = Crate().name()\n",
                new RunRequest { ScalarOutputNames = new[] { "names", "v" } });
            Assert.True(r.Succeeded, r.Error?.Message);
            Assert.Equal("Box,Shape", r.ScalarsOut["names"]);
            Assert.Equal("shape/box/crate", r.ScalarsOut["v"]);
        }

        [Fact]
        public void RecursionThroughSuperStopsOnTheCallDepth()
        {
            var limits = ResourceLimits.Default.With(maxCallDepth: 64);
            var engine = new ScriptEngine(new EngineOptions { DefaultLimits = limits });
            RunResult r = engine.Run("class A:\n    def f(self):\n        return self.f()\n"
                + "class B(A):\n    def f(self):\n        return super().f()\nB().f()\n", new RunRequest());
            Assert.False(r.Succeeded);
            Assert.Equal("MaxCallDepth", r.Error.LimitName);
        }
    }
}

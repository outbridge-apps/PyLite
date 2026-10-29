using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    [Trait("Category", "Sandbox")]
    public class ModuleRegistryTests
    {
        private static readonly IReadOnlyDictionary<string, ScriptValue> NoMembers =
            new Dictionary<string, ScriptValue>(StringComparer.Ordinal);

        private static ModuleValue Mv(string name)
        {
            return new ModuleValue(name, NoMembers, 1L);
        }

        [Fact]
        public void ImportRegisteredCallsFactoryOnce()
        {
            var ctx = Rt.Ctx();
            int calls = 0;
            var reg = new ModuleRegistry();
            reg.Register("m", c => { calls++; return Mv("m"); });
            reg.Freeze();
            var m = reg.Import("m", ctx);
            Assert.NotNull(m);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void ImportTwiceIsCached()
        {
            var ctx = Rt.Ctx();
            int calls = 0;
            var reg = new ModuleRegistry();
            reg.Register("m", c => { calls++; return Mv("m"); });
            reg.Freeze();
            var a = reg.Import("m", ctx);
            var b = reg.Import("m", ctx);
            Assert.Same(a, b);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void ImportUnregisteredIsImportError()
        {
            var ctx = Rt.Ctx();
            var reg = new ModuleRegistry();
            reg.Freeze();
            var ex = Assert.Throws<ScriptException>(() => reg.Import("foo", ctx));
            Assert.Same(PyExceptionTypes.ImportError, ex.Value.ExcType);
            Assert.Equal("No module named 'foo'", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void PerCtxInstancesAreDistinct()
        {
            var reg = new ModuleRegistry();
            reg.Register("m", c => Mv("m"));
            reg.Freeze();
            var m1 = reg.Import("m", Rt.Ctx());
            var m2 = reg.Import("m", Rt.Ctx());
            Assert.NotSame(m1, m2);
        }

        [Fact]
        public void RegisterAfterFreezeThrows()
        {
            var reg = new ModuleRegistry();
            reg.Freeze();
            Assert.Throws<InvalidOperationException>(() => reg.Register("m", c => Mv("m")));
        }

        [Fact]
        public void RegisterDuplicateThrows()
        {
            var reg = new ModuleRegistry();
            reg.Register("m", c => Mv("m"));
            Assert.Throws<InvalidOperationException>(() => reg.Register("m", c => Mv("m")));
        }

        [Fact]
        public void FailedInitIsNotCachedAndRetried()
        {
            var ctx = Rt.Ctx();
            int calls = 0;
            var reg = new ModuleRegistry();
            reg.Register("m", c =>
            {
                calls++;
                throw Raise.ValueError(c, "boom");
            });
            reg.Freeze();
            Assert.Throws<ScriptException>(() => reg.Import("m", ctx));
            Assert.Empty(ctx.ModuleInstances);
            Assert.Throws<ScriptException>(() => reg.Import("m", ctx));
            Assert.Equal(2, calls);
        }

        [Fact]
        public void FactoryMayImportAnother()
        {
            var ctx = Rt.Ctx();
            var reg = new ModuleRegistry();
            reg.Register("b", c => Mv("b"));
            reg.Register("a", c => { reg.Import("b", c); return Mv("a"); });
            reg.Freeze();
            var a = reg.Import("a", ctx);
            Assert.NotNull(a);
            Assert.True(ctx.ModuleInstances.ContainsKey("b"));
        }

        [Fact]
        public void CyclicInitIsImportError()
        {
            var ctx = Rt.Ctx();
            var reg = new ModuleRegistry();
            reg.Register("A", c => { reg.Import("A", c); return Mv("A"); });
            reg.Freeze();
            var ex = Assert.Throws<ScriptException>(() => reg.Import("A", ctx));
            Assert.Same(PyExceptionTypes.ImportError, ex.Value.ExcType);
            Assert.Equal("circular module initialization: 'A'", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void UnusedModuleFactoryNotCalled()
        {
            var ctx = Rt.Ctx();
            int calls = 0;
            var reg = new ModuleRegistry();
            reg.Register("m", c => { calls++; return Mv("m"); });
            reg.Freeze();
            Assert.Equal(0, calls);
            Assert.Empty(ctx.ModuleInstances);
        }

        [Fact]
        public void StepChargingMissThenHit()
        {
            var ctx = Rt.Ctx();
            var reg = new ModuleRegistry();
            reg.Register("m", c => Mv("m"));
            reg.Freeze();
            long before = ctx.Budget.StepsUsed;
            reg.Import("m", ctx);
            Assert.Equal(BudgetCost.ImportModule, ctx.Budget.StepsUsed - before);
            before = ctx.Budget.StepsUsed;
            reg.Import("m", ctx);
            Assert.Equal(1, ctx.Budget.StepsUsed - before);
        }
    }
}

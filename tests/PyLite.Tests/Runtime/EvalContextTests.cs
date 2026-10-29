using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Outbridge.PyLite.Runtime;
using Xunit;

namespace Outbridge.PyLite.Tests.Runtime
{
    [Trait("Category", "Compatibility")]
    public class EvalContextTests
    {
        [Fact]
        public void ConstructorIsNotPublic()
        {
            var pub = typeof(EvalContext).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            Assert.Empty(pub);
        }

        [Fact]
        public void NoCloneOrCopyMembers()
        {
            var names = typeof(EvalContext).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(m => m.Name);
            Assert.DoesNotContain("Clone", names);
            Assert.DoesNotContain("Copy", names);
            Assert.DoesNotContain("DeepCopy", names);
        }

        [Fact]
        public void ModuleStateIsPerContext()
        {
            var a = Rt.Ctx();
            var b = Rt.Ctx();
            Assert.NotSame(a.ModuleState, b.ModuleState);
            Assert.NotSame(a.ModuleInstances, b.ModuleInstances);
        }

        [Fact]
        public void FreshContextDefaults()
        {
            var ctx = Rt.Ctx();
            Assert.Equal(0, ctx.CallDepth);
            Assert.Equal("<module>", ctx.CurrentFunctionName);
            Assert.NotNull(ctx.Stats);
            Assert.NotNull(ctx.Clock);
        }

        [Fact]
        public void ContextIsUnrootedAfterUse()
        {
            var wr = MakeWeak();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.False(wr.IsAlive);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference MakeWeak()
        {
            return new WeakReference(Rt.Ctx());
        }
    }
}

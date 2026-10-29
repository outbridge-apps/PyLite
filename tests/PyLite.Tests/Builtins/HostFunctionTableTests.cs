using System;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Tests;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Builtins
{
    // impl/06 T-BLT-01: the default builtins table, host override/removal, freeze, and the "no" names.
    [Trait("Category", "Sandbox")]
    public class HostFunctionTableTests
    {
        [Fact]
        public void DefaultHasExceptionClasses()
        {
            var t = HostFunctionTable.CreateDefault();
            ScriptValue v;
            Assert.True(t.TryGet("ValueError", out v));
            Assert.IsType<TypeValue>(v);
            Assert.Same(PyExceptionTypes.ValueError, ((TypeValue)v).ExcType);
        }

        [Fact]
        public void UnsupportedBuiltinGivesSpecialNameError()
        {
            Assert.Equal("name 'eval' is not defined: this builtin is not supported in this environment", Ev.Err("eval('1')\n"));
        }

        [Fact]
        public void UnknownNameGivesPlainNameError()
        {
            Assert.Equal("name 'nosuch' is not defined", Ev.Err("nosuch(1)\n"));
        }

        [Fact]
        public void SetAfterFreezeThrows()
        {
            var ctx = Rt.Ctx();
            var t = HostFunctionTable.CreateDefault();
            t.Freeze();
            Assert.Throws<InvalidOperationException>(() => t.SetValue("x", ctx.Values.Int(1)));
        }

        [Fact]
        public void RemoveMissingIsNoOp()
        {
            var t = HostFunctionTable.CreateDefault();
            t.Remove("open");   // not in the table
            Assert.False(t.Contains("open"));
        }

        [Fact]
        public void CloneOfFrozenIsMutable()
        {
            var ctx = Rt.Ctx();
            var t = HostFunctionTable.CreateDefault();
            t.Freeze();
            var c = t.Clone();
            c.SetValue("k", ctx.Values.Int(7));   // clone is unfrozen -> Set does not throw
            Assert.True(c.Contains("k"));
        }

        [Fact]
        public void SetNullNameThrows()
        {
            var t = HostFunctionTable.CreateDefault();
            Assert.Throws<ArgumentException>(() => t.Set("", (self, args, kw, c) => self));
        }

        [Fact]
        public void KnownMissingAttributeHints()
        {
            var hints = HostFunctionTable.KnownMissingAttributeHints;
            Assert.False(hints.ContainsKey("encode"));     // RFC-001 §2 made str.encode / bytes.decode real
            Assert.False(hints.ContainsKey("decode"));
            Assert.False(hints.ContainsKey("fromkeys"));   // P2 gap-closure made these real
            Assert.False(hints.ContainsKey("format_map"));
            Assert.False(hints.ContainsKey("translate"));
            Assert.False(hints.ContainsKey("upper"));
        }
    }
}

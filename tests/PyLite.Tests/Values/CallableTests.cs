using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    [Trait("Category", "Compatibility")]
    public class CallableTests
    {
        private static FunctionInfo Info(string name)
        {
            var noStr = new string[0];
            return new FunctionInfo(name, ScopeKind.Function, 0, 0, 0, false, false,
                noStr, new int[0], new bool[0], noStr, 0, noStr, noStr);
        }

        private static FunctionValue Fn(EvalContext ctx, string name, Cell[] closure = null)
            => ctx.Values.Function(name, Info(name), null, null, closure, null, null);

        [Fact]
        public void FunctionRepr()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("<function f>", Fn(ctx, "f").Repr(ctx));
            Assert.Equal("<function <lambda>>", Fn(ctx, "<lambda>").Repr(ctx));
        }

        [Fact]
        public void BuiltinFunctionRepr()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("<built-in function len>", ctx.Values.BuiltinFunction("len", (self, args, kw, c) => self).Repr(ctx));
        }

        [Fact]
        public void BoundMethodRepr()
        {
            var ctx = Rt.Ctx();
            var upper = SlotDescriptor.MakeMethod("upper", (self, args, kw, c) => self);
            Assert.Equal("<built-in method upper of str object>", ctx.Values.BoundMethod(ctx.Values.Str("a"), upper).Repr(ctx));
        }

        [Fact]
        public void BoundMethodEquality()
        {
            var ctx = Rt.Ctx();
            var upper = SlotDescriptor.MakeMethod("upper", (self, args, kw, c) => self);
            var s = ctx.Values.Str("a");
            Assert.True(PyOps.Equals(ctx.Values.BoundMethod(s, upper), ctx.Values.BoundMethod(s, upper), ctx, 0));
            Assert.False(PyOps.Equals(ctx.Values.BoundMethod(s, upper), ctx.Values.BoundMethod(ctx.Values.Str("b"), upper), ctx, 0));
        }

        [Fact]
        public void BoundMethodUnhashableSelf()
        {
            var ctx = Rt.Ctx();
            var lst = ctx.Values.List(1); lst.Add(ctx.Values.Int(1), ctx);
            var append = SlotDescriptor.MakeMethod("append", (self, args, kw, c) => self);
            var ex = Assert.Throws<ScriptException>(() => ctx.Values.BoundMethod(lst, append).PyHash(ctx));
            Assert.Equal("unhashable type: 'list'", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void FunctionIdentityEqualityAndHash()
        {
            var ctx = Rt.Ctx();
            var f = Fn(ctx, "f");
            var g = Fn(ctx, "g");
            Assert.True(PyOps.Equals(f, f, ctx, 0));
            Assert.False(PyOps.Equals(f, g, ctx, 0));
            Assert.Equal(f.PyHash(ctx), f.PyHash(ctx));
            Assert.NotEqual(f.PyHash(ctx), g.PyHash(ctx));
        }

        [Fact]
        public void CellSharing()
        {
            var ctx = Rt.Ctx();
            var c = new Cell { Value = ctx.Values.Int(1) };
            var f = Fn(ctx, "f", new[] { c });
            var g = Fn(ctx, "g", new[] { c });
            Assert.Same(f.Closure[0], g.Closure[0]);
            c.Value = ctx.Values.Int(2);
            Assert.Equal((BigInteger)2, ((IntValue)f.Closure[0].Value).Value);
        }

        [Fact]
        public void CallableMarker()
        {
            var ctx = Rt.Ctx();
            Assert.True(Fn(ctx, "f").IsCallable);
            Assert.False(ctx.Values.Int(1).IsCallable);
        }

        [Fact]
        public void GetAttrMethodPropertyAbsent()
        {
            var ctx = Rt.Ctx();
            var slots = new Dictionary<string, SlotDescriptor>
            {
                { "m", SlotDescriptor.MakeMethod("m", (self, args, kw, c) => self) },
                { "p", SlotDescriptor.MakeProperty("p", (self, c) => c.Values.Int(42)) }
            };
            var obj = new Slotted(new ScriptTypeInfo("thing", slots));

            var a = PyOps.GetAttr(obj, "m", ctx);
            var b = PyOps.GetAttr(obj, "m", ctx);
            Assert.IsType<BoundMethodValue>(a);
            Assert.False(ReferenceEquals(a, b));
            Assert.True(PyOps.Equals(a, b, ctx, 0));

            Assert.Equal((BigInteger)42, ((IntValue)PyOps.GetAttr(obj, "p", ctx)).Value);

            var ex = Assert.Throws<ScriptException>(() => PyOps.GetAttr(obj, "nope", ctx));
            Assert.Equal("'thing' object has no attribute 'nope'", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void ModuleAndTypeRepr()
        {
            var ctx = Rt.Ctx();
            Assert.Equal("<module 'math' (built-in)>", ctx.Values.Module("math", new Dictionary<string, ScriptValue>()).Repr(ctx));
            Assert.Equal("<class 'int'>", ctx.Values.Type("int", null, new ScriptTypeInfo("int", null), null).Repr(ctx));
        }

        [Fact]
        public void TypeStaticSlots()
        {
            var ctx = Rt.Ctx();
            var statics = new Dictionary<string, ScriptValue> { { "max", ctx.Values.Int(99) } };
            var t = ctx.Values.Type("date", null, new ScriptTypeInfo("date", null), statics);
            Assert.Equal((BigInteger)99, ((IntValue)PyOps.GetAttr(t, "max", ctx)).Value);
            var ex = Assert.Throws<ScriptException>(() => PyOps.GetAttr(t, "nope", ctx));
            Assert.Equal("type object 'date' has no attribute 'nope'", ex.Value.GetMessageText(ctx));
        }

        private sealed class Slotted : ScriptValue
        {
            private readonly ScriptTypeInfo _t;
            public Slotted(ScriptTypeInfo t) { _t = t; }
            public override ScriptTypeInfo TypeInfo => _t;
            internal override ValueKind Kind => ValueKind.Opaque;
        }
    }
}

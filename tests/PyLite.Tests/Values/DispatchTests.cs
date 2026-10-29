using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    [Trait("Category", "Compatibility")]
    public class DispatchTests
    {
        [Fact]
        public void BinaryUnsupported()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<ScriptException>(() => PyOps.BinaryOp(PyBinOp.Add, ctx.Values.Int(1), ctx.Values.Str("a"), ctx));
            Assert.Equal("unsupported operand type(s) for +: 'int' and 'str'", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void PowSymbolIsSpecial()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<ScriptException>(() => PyOps.BinaryOp(PyBinOp.Pow, ctx.Values.Str("a"), ctx.Values.Int(2), ctx));
            Assert.Equal("unsupported operand type(s) for ** or pow(): 'str' and 'int'", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void UnaryNegOnStr()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<ScriptException>(() => PyOps.UnaryOp(PyUnaryOp.Neg, ctx.Values.Str("a"), ctx));
            Assert.Equal("bad operand type for unary -: 'str'", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void UnaryInvertOnFloat()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<ScriptException>(() => PyOps.UnaryOp(PyUnaryOp.Invert, ctx.Values.Float(1.0), ctx));
            Assert.Equal("bad operand type for unary ~: 'float'", ex.Value.GetMessageText(ctx));
        }

        [Fact] public void TruthOfNone() { var ctx = Rt.Ctx(); Assert.False(PyOps.Truth(ctx.Values.None, ctx)); }

        [Fact]
        public void LengthOfIntFails()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<ScriptException>(() => PyOps.Length(ctx.Values.Int(5), ctx));
            Assert.Equal("object of type 'int' has no len()", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void IterFloatFails()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<ScriptException>(() => PyOps.GetIterator(ctx.Values.Float(1.0), ctx));
            Assert.Equal("'float' object is not iterable", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void ContainsNonIterable()
        {
            var ctx = Rt.Ctx();
            var ex = Assert.Throws<ScriptException>(() => PyOps.Contains(ctx.Values.Int(1), ctx.Values.Int(5), ctx));
            Assert.Equal("argument of type 'int' is not iterable", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void DunderSlotRejected()
        {
            var slots = new Dictionary<string, SlotDescriptor>
            {
                { "__class__", SlotDescriptor.MakeMethod("__class__", (self, args, kw, ctx) => self) }
            };
            Assert.Throws<ArgumentException>(() => new ScriptTypeInfo("t", slots));
        }

        [Fact]
        public void SlotsAreOrdinal()
        {
            var slots = new Dictionary<string, SlotDescriptor>
            {
                { "upper", SlotDescriptor.MakeMethod("upper", (self, args, kw, ctx) => self) }
            };
            var ti = new ScriptTypeInfo("str", slots);
            Assert.True(ti.Slots.ContainsKey("upper"));
            Assert.False(ti.Slots.ContainsKey("UPPER"));
        }

        [Fact]
        public void IteratorSameInstanceEachAccess()
        {
            var ctx = Rt.Ctx();
            var it = ctx.Values.Iterator(new EmptyIter(), "x");
            Assert.Same(it.GetIteratorCore(ctx), it.GetIteratorCore(ctx));
        }

        [Fact]
        public void MoveNextChargesStep()
        {
            var ctx = Rt.Ctx(ResourceLimits.Default.With(maxSteps: 1));
            var it = new EmptyIter();
            ScriptValue v;
            it.MoveNext(ctx, out v);   // step 1 ok
            Assert.Throws<EngineAbort>(() => it.MoveNext(ctx, out v));
        }

        [Fact]
        public void ReflectedOrderDifferentTypes()
        {
            var ctx = Rt.Ctx();
            var a = new ProbeValue(new ScriptTypeInfo("A", null)) { ReturnOnReflected = false };
            var b = new ProbeValue(new ScriptTypeInfo("B", null)) { ReturnOnReflected = true };
            var r = PyOps.BinaryOp(PyBinOp.Add, a, b, ctx);
            Assert.Same(b, r);
            Assert.Equal(1, a.DirectCalls);
            Assert.Equal(0, a.ReflectedCalls);
            Assert.Equal(1, b.ReflectedCalls);
        }

        [Fact]
        public void ReflectedSkippedWhenSameType()
        {
            var ctx = Rt.Ctx();
            var t = new ScriptTypeInfo("A", null);
            var a = new ProbeValue(t);
            var b = new ProbeValue(t);
            Assert.Throws<ScriptException>(() => PyOps.BinaryOp(PyBinOp.Add, a, b, ctx));
            Assert.Equal(1, a.DirectCalls);
            Assert.Equal(0, a.ReflectedCalls);
            Assert.Equal(0, b.DirectCalls);
        }

        private sealed class EmptyIter : ScriptIteratorBase
        {
            protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value) { value = null; return true; }
        }

        private sealed class ProbeValue : ScriptValue
        {
            private readonly ScriptTypeInfo _t;
            public bool ReturnOnReflected;
            public int DirectCalls;
            public int ReflectedCalls;

            public ProbeValue(ScriptTypeInfo t) { _t = t; }
            public override ScriptTypeInfo TypeInfo => _t;
            internal override ValueKind Kind => ValueKind.Opaque;

            protected internal override ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
            {
                if (reflected)
                {
                    ReflectedCalls++;
                    return ReturnOnReflected ? this : null;
                }
                DirectCalls++;
                return null;
            }
        }
    }
}

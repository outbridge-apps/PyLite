using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    [Trait("Category", "Compatibility")]
    public class DictValueTests
    {
        private static DictValue D(EvalContext ctx, params (string, int)[] kv)
        {
            var d = ctx.Values.Dict(0);
            foreach (var(k, v)in kv)
                d.SetItem(ctx.Values.Str(k), ctx.Values.Int(v), ctx);
            return d;
        }
        private static List<string> Keys(EvalContext ctx, DictValue d)
        {
            var it = d.GetIteratorCore(ctx);
            var r = new List<string>();
            ScriptValue k;
            while (it.MoveNext(ctx, out k))
                r.Add(((StrValue)k).Value);
            return r;
        }

        [Fact]
        public void KeyIterationOrder()
        {
            var ctx = Rt.Ctx();
            Assert.Equal(new[] { "a", "b" }, Keys(ctx, D(ctx, ("a", 1), ("b", 2))));
        }

        [Fact]
        public void MissingKeyError()
        {
            var ctx = Rt.Ctx();
            var d = D(ctx, ("a", 1));
            var ex = Assert.Throws<ScriptException>(() => d.GetItemOrThrow(ctx.Values.Str("x"), ctx));
            Assert.Equal("KeyError", ex.Value.ExcType.Name);
            Assert.Equal("'x'", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void InsertDuringIterationAborts()
        {
            var ctx = Rt.Ctx();
            var d = D(ctx, ("a", 1), ("b", 2));
            var it = d.GetIteratorCore(ctx);
            ScriptValue k;
            it.MoveNext(ctx, out k);
            d.SetItem(ctx.Values.Str("c"), ctx.Values.Int(3), ctx);
            var ex = Assert.Throws<ScriptException>(() => it.MoveNext(ctx, out k));
            Assert.Equal("dictionary changed size during iteration", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void ValueReplacementDoesNotAbortIteration()
        {
            var ctx = Rt.Ctx();
            var d = D(ctx, ("a", 1), ("b", 2));
            var it = d.GetIteratorCore(ctx);
            ScriptValue k;
            it.MoveNext(ctx, out k);
            d.SetItem(ctx.Values.Str("a"), ctx.Values.Int(9), ctx);   // update -> version unchanged
            Assert.True(it.MoveNext(ctx, out k));
            Assert.Equal("b", ((StrValue)k).Value);
        }

        [Fact]
        public void DeleteDuringIterationAborts()
        {
            var ctx = Rt.Ctx();
            var d = D(ctx, ("a", 1), ("b", 2));
            var it = d.GetIteratorCore(ctx);
            ScriptValue k;
            it.MoveNext(ctx, out k);
            d.DelItemOrThrow(ctx.Values.Str("a"), ctx);
            Assert.Throws<ScriptException>(() => it.MoveNext(ctx, out k));
        }

        [Fact]
        public void NumericTowerKeyLookup()
        {
            var ctx = Rt.Ctx();
            var d = ctx.Values.Dict(0);
            d.SetItem(ctx.Values.Float(1.0), ctx.Values.Str("v"), ctx);
            Assert.True(PyOps.Contains(ctx.Values.Int(1), d, ctx));
            var d2 = ctx.Values.Dict(0);
            d2.SetItem(ctx.Values.Int(1), ctx.Values.Str("v"), ctx);
            Assert.True(PyOps.Contains(ctx.Values.Bool(true), d2, ctx));
        }

        [Fact]
        public void LiveKeysViewLength()
        {
            var ctx = Rt.Ctx();
            var d = D(ctx, ("a", 1));
            var view = d.KeysView(ctx);
            d.SetItem(ctx.Values.Str("b"), ctx.Values.Int(2), ctx);
            long n;
            view.TryLengthCore(out n);
            Assert.Equal(2, n);
        }

        [Fact]
        public void ItemsAndValuesContains()
        {
            var ctx = Rt.Ctx();
            var d = D(ctx, ("a", 1));
            var items = d.ItemsView(ctx);
            Assert.True(PyOps.Contains(ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Str("a"), ctx.Values.Int(1) }), items, ctx));
            Assert.False(PyOps.Contains(ctx.Values.Tuple(new ScriptValue[] { ctx.Values.Str("a"), ctx.Values.Int(2) }), items, ctx));
            Assert.False(PyOps.Contains(ctx.Values.Str("a"), items, ctx));
            Assert.True(PyOps.Contains(ctx.Values.Int(1), d.ValuesView(ctx), ctx));
        }

        [Fact]
        public void UnhashableKeyRejected()
        {
            var ctx = Rt.Ctx();
            var d = ctx.Values.Dict(0);
            var lst = ctx.Values.List(1); lst.Add(ctx.Values.Int(1), ctx);
            var ex = Assert.Throws<ScriptException>(() => d.SetItem(lst, ctx.Values.Int(0), ctx));
            Assert.Equal("unhashable type: 'list'", ex.Value.GetMessageText(ctx));
        }

        [Fact]
        public void NanKeyIdentityVsDistinct()
        {
            var ctx = Rt.Ctx();
            var d = ctx.Values.Dict(0);
            var nan = ctx.Values.Float(double.NaN);
            d.SetItem(nan, ctx.Values.Int(1), ctx);
            ScriptValue got;
            Assert.True(d.TryGet(nan, ctx, out got));
            Assert.False(d.TryGet(ctx.Values.Float(double.NaN), ctx, out got));
        }

        [Fact]
        public void DictEquality()
        {
            var ctx = Rt.Ctx();
            var a = D(ctx, ("a", 1), ("b", 2));
            var b = D(ctx, ("b", 2), ("a", 1));
            Assert.True(((BoolValue)PyOps.RichCompare(PyCmpOp.Eq, a, b, ctx)).Value);
            Assert.False(((BoolValue)PyOps.RichCompare(PyCmpOp.Eq, a, D(ctx, ("a", 1), ("b", 9)), ctx)).Value);
            Assert.False(((BoolValue)PyOps.RichCompare(PyCmpOp.Eq, a, D(ctx, ("a", 1), ("c", 2)), ctx)).Value);
        }

        [Fact]
        public void NestedDictEquality()
        {
            var ctx = Rt.Ctx();
            var inner1 = ctx.Values.Dict(0); inner1.SetItem(ctx.Values.Int(2), ctx.Values.Int(3), ctx);
            var d1 = ctx.Values.Dict(0); d1.SetItem(ctx.Values.Int(1), inner1, ctx);
            var inner2 = ctx.Values.Dict(0); inner2.SetItem(ctx.Values.Int(2), ctx.Values.Int(3), ctx);
            var d2 = ctx.Values.Dict(0); d2.SetItem(ctx.Values.Int(1), inner2, ctx);
            Assert.True(PyOps.Equals(d1, d2, ctx, 0));
        }
    }
}

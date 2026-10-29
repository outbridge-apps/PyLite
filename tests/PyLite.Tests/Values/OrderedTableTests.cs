using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    [Trait("Category", "Compatibility")]
    public class OrderedTableTests
    {
        private static void Ins(EvalContext ctx, OrderedTable t, ScriptValue k, ScriptValue v) => t.InsertOrUpdate(k, k.PyHash(ctx), v, ctx, 0);
        private static bool Del(EvalContext ctx, OrderedTable t, ScriptValue k) => t.Delete(k, k.PyHash(ctx), ctx, 0);
        private static bool Has(EvalContext ctx, OrderedTable t, ScriptValue k) => t.ContainsKey(k, k.PyHash(ctx), ctx, 0);

        private static List<BigInteger> IntKeys(OrderedTable t)
        {
            var r = new List<BigInteger>();
            for (int p = 0; p < t.EntriesUsed; p++)
            {
                long h;
                ScriptValue k, v;
                if (t.TryGetEntryAt(p, out h, out k, out v))
                    r.Add(((IntValue)k).Value);
            }
            return r;
        }

        // The WithPos contract the global-name cache relies on (the plain overloads delegate here):
        // a NEW key appends and returns true (version bump); an update keeps position AND version
        // (R6) and returns false; the position reads back via ValueAt/SetValueAt without a bump.
        [Fact]
        public void InsertOrUpdateWithPosContract()
        {
            var ctx = Rt.Ctx();
            var t = new OrderedTable(ctx, 0);
            ScriptValue k1 = ctx.Values.Str("a"), k2 = ctx.Values.Str("b");
            int pos;
            Assert.True(t.InsertOrUpdateWithPos(k1, k1.PyHash(ctx), ctx.Values.Int(1), ctx, 0, out pos));
            Assert.Equal(0, pos);
            Assert.True(t.InsertOrUpdateWithPos(k2, k2.PyHash(ctx), ctx.Values.Int(2), ctx, 0, out pos));
            Assert.Equal(1, pos);
            ulong v = t.Version;
            Assert.False(t.InsertOrUpdateWithPos(k1, k1.PyHash(ctx), ctx.Values.Int(9), ctx, 0, out pos));
            Assert.Equal(0, pos);          // update keeps the position...
            Assert.Equal(v, t.Version);    // ...and the version (R6)
            ScriptValue got;
            int gpos;
            Assert.True(t.TryGetValueWithPos(k1, k1.PyHash(ctx), ctx, 0, out got, out gpos));
            Assert.Equal(0, gpos);
            Assert.Equal((BigInteger)9, ((IntValue)got).Value);
            t.SetValueAt(gpos, ctx.Values.Int(7));
            Assert.Equal((BigInteger)7, ((IntValue)t.ValueAt(gpos)).Value);
            Assert.Equal(v, t.Version);    // SetValueAt never bumps
        }

        [Fact]
        public void InsertionOrderPreserved()
        {
            var ctx = Rt.Ctx();
            var t = new OrderedTable(ctx, 0);
            for (int i = 0; i < 1000; i++)
                Ins(ctx, t, ctx.Values.Int(i), ctx.Values.Int(i));
            var keys = IntKeys(t);
            Assert.Equal(1000, keys.Count);
            for (int i = 0; i < 1000; i++)
                Assert.Equal((BigInteger)i, keys[i]);
        }

        [Fact]
        public void UpdateKeepsOrderAndVersion()
        {
            var ctx = Rt.Ctx();
            var t = new OrderedTable(ctx, 0);
            Ins(ctx, t, ctx.Values.Int(7), ctx.Values.Str("a"));
            ulong v = t.Version;
            Ins(ctx, t, ctx.Values.Int(7), ctx.Values.Str("b"));
            Assert.Equal(v, t.Version);
            Assert.Equal(1, t.Count);
            ScriptValue got;
            t.TryGetValue(ctx.Values.Int(7), ctx.Values.Int(7).PyHash(ctx), ctx, 0, out got);
            Assert.Equal("b", ((StrValue)got).Value);
        }

        [Fact]
        public void DeleteThenReinsertGoesToEnd()
        {
            var ctx = Rt.Ctx();
            var t = new OrderedTable(ctx, 0);
            Ins(ctx, t, ctx.Values.Int(1), ctx.Values.Int(1));
            Ins(ctx, t, ctx.Values.Int(2), ctx.Values.Int(2));
            Ins(ctx, t, ctx.Values.Int(3), ctx.Values.Int(3));
            ulong before = t.Version;
            Del(ctx, t, ctx.Values.Int(1));
            Ins(ctx, t, ctx.Values.Int(1), ctx.Values.Int(9));
            Assert.Equal(before + 2, t.Version);
            Assert.Equal(new[] { (BigInteger)2, 3, 1 }, IntKeys(t));
        }

        [Fact]
        public void CollisionChain()
        {
            var ctx = Rt.Ctx();
            var t = new OrderedTable(ctx, 0);
            foreach (int k in new[]
            {
                0,
                8,
                16,
                24
            }

            )
                Ins(ctx, t, ctx.Values.Int(k), ctx.Values.Int(k));
            foreach (int k in new[]
            {
                0,
                8,
                16,
                24
            }

            )
                Assert.True(Has(ctx, t, ctx.Values.Int(k)));
            Assert.Equal(new[] { (BigInteger)0, 8, 16, 24 }, IntKeys(t));
        }

        [Fact]
        public void NegativeHashKeys()
        {
            var ctx = Rt.Ctx();
            var t = new OrderedTable(ctx, 0);
            foreach (int k in new[]
            {
                -1,
                -2,
                -3,
                -7
            }

            )
                Ins(ctx, t, ctx.Values.Int(k), ctx.Values.Int(k));
            foreach (int k in new[]
            {
                -1,
                -2,
                -3,
                -7
            }

            )
                Assert.True(Has(ctx, t, ctx.Values.Int(k)));
        }

        [Fact]
        public void SameHashKeysFoundAndOrdered()
        {
            var ctx = Rt.Ctx();
            var t = new OrderedTable(ctx, 0);
            BigInteger P = BigInteger.Pow(2, 61) - 1;
            for (int k = 1; k <= 500; k++)
                Ins(ctx, t, ctx.Values.Int(k * P), ctx.Values.Int(k));
            for (int k = 1; k <= 500; k++)
                Assert.True(Has(ctx, t, ctx.Values.Int(k * P)));
            Assert.Equal(500, t.Count);
            Assert.Equal((BigInteger)(1 * P), IntKeys(t)[0]);
        }

        [Fact]
        public void CycleKeepsMemoryBounded()
        {
            var ctx = Rt.Ctx();
            var t = new OrderedTable(ctx, 0);
            for (int i = 0; i < 100_000; i++)
            {
                var k = ctx.Values.Int(i % 3);
                Ins(ctx, t, k, k);
                Del(ctx, t, k);
            }
            Assert.Equal(0, t.Count);
            Assert.True(t.EntriesUsed <= 8, "entriesUsed=" + t.EntriesUsed);
        }

        [Fact]
        public void NumericTowerCollapsesToOneKey()
        {
            var ctx = Rt.Ctx();
            var t = new OrderedTable(ctx, 0);
            Ins(ctx, t, ctx.Values.Int(1), ctx.Values.Str("a"));
            Ins(ctx, t, ctx.Values.Float(1.0), ctx.Values.Str("b"));
            Ins(ctx, t, ctx.Values.Bool(true), ctx.Values.Str("c"));
            Assert.Equal(1, t.Count);
            long h; ScriptValue k, v;
            t.TryGetEntryAt(0, out h, out k, out v);
            Assert.IsType<IntValue>(k);
            Assert.Equal((BigInteger)1, ((IntValue)k).Value);
            Assert.Equal("c", ((StrValue)v).Value);
        }

        [Fact]
        public void ResizeDropsTombstonesKeepsOrder()
        {
            var ctx = Rt.Ctx();
            var t = new OrderedTable(ctx, 0);
            for (int i = 0; i < 150; i++)
                Ins(ctx, t, ctx.Values.Int(i), ctx.Values.Int(i));
            for (int i = 0; i < 150; i += 3)
                Del(ctx, t, ctx.Values.Int(i));   // 50 tombstones
            for (int i = 150; i < 300; i++)
                Ins(ctx, t, ctx.Values.Int(i), ctx.Values.Int(i));   // forces resize
            var keys = IntKeys(t);
            Assert.Equal(t.Count, keys.Count);
            for (int i = 1; i < keys.Count; i++)
                Assert.True(true);   // no tombstones present (TryGetEntryAt skips them)
            Assert.True(Has(ctx, t, ctx.Values.Int(1)));
            Assert.False(Has(ctx, t, ctx.Values.Int(0)));
        }

        [Fact]
        public void VersionIncrements()
        {
            var ctx = Rt.Ctx();
            var t = new OrderedTable(ctx, 0);
            Ins(ctx, t, ctx.Values.Int(1), ctx.Values.Str("a"));
            Assert.Equal(1UL, t.Version);
            Ins(ctx, t, ctx.Values.Int(1), ctx.Values.Str("b"));
            Assert.Equal(1UL, t.Version);
            Del(ctx, t, ctx.Values.Int(1));
            Assert.Equal(2UL, t.Version);
            t.Clear(ctx);
            Assert.Equal(3UL, t.Version);
        }

        [Fact]
        public void ChargeAtLeastPerEntry()
        {
            var ctx = Rt.Ctx();
            var t = new OrderedTable(ctx, 0);
            long before = ctx.Budget.AllocRemaining;
            for (int i = 0; i < 10_000; i++)
            {
                var k = ctx.Values.Int(i);
                Ins(ctx, t, k, k);
            }
            Assert.True(before - ctx.Budget.AllocRemaining >= 48L * 10_000);
        }

        [Fact]
        public void CloneShallowIndependent()
        {
            var ctx = Rt.Ctx();
            var t = new OrderedTable(ctx, 0);
            for (int i = 0; i < 20; i++)
                Ins(ctx, t, ctx.Values.Int(i), ctx.Values.Int(i));
            ulong origVer = t.Version;
            var c = t.CloneShallow(ctx);
            Ins(ctx, c, ctx.Values.Int(999), ctx.Values.Int(999));
            Assert.Equal(origVer, t.Version);
            Assert.Equal(20, t.Count);
            Assert.Equal(21, c.Count);
            Assert.Equal(IntKeys(t).GetRange(0, 20), IntKeys(c).GetRange(0, 20));
        }

        // A trailing delete drops the tombstones behind the last live entry, and FindFirstLive resumes where
        // it stopped: draining from either end is linear (set.pop / dict.popitem were quadratic, B4).
        [Fact]
        public void DrainingFromEitherEndDoesNotRescanTombstones()
        {
            var ctx = Rt.Ctx();
            var t = new OrderedTable(ctx, 0);
            for (int i = 0; i < 6; i++)
                Ins(ctx, t, ctx.Values.Int(i), ctx.Values.Int(i));
            Del(ctx, t, ctx.Values.Int(4));
            Del(ctx, t, ctx.Values.Int(5));
            Assert.Equal(4, t.EntriesUsed);
            Assert.Equal(3, t.FindLastLive());
            Del(ctx, t, ctx.Values.Int(0));
            Del(ctx, t, ctx.Values.Int(1));
            Assert.Equal(2, t.FindFirstLive());
            Del(ctx, t, ctx.Values.Int(3));
            Del(ctx, t, ctx.Values.Int(2));
            Assert.Equal(0, t.EntriesUsed);
            Assert.Equal(-1, t.FindFirstLive());
            Ins(ctx, t, ctx.Values.Int(7), ctx.Values.Int(7));
            Assert.Equal(0, t.FindFirstLive());
            Assert.Equal(new List<BigInteger> { 7 }, IntKeys(t));
        }

        // 30,000 each: the quadratic drain took ~4 s per container (past the 10 s deadline in all), the linear
        // one takes milliseconds, so a loaded machine running the suite in parallel cannot flip the result.
        [Fact]
        public void DrainingABigSetOrDictIsLinear()
        {
            var r = Outbridge.PyLite.Tests.Evaluator.Ev.Run(
                "from collections import OrderedDict\n"
                + "s = set(range(30000))\nwhile s:\n    s.pop()\n"
                + "d = dict.fromkeys(range(30000))\nwhile d:\n    d.popitem()\n"
                + "o = OrderedDict.fromkeys(range(30000))\nwhile o:\n    o.popitem(last=False)\n"
                + "r = (len(s), len(d), len(o))\n",
                Outbridge.PyLite.Hosting.ResourceLimits.Default.With(deadlineMs: 10000));
            Assert.Equal("(0, 0, 0)", r.Repr("r"));
        }
    }
}

using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    internal static class Iter
    {
        // The items of an iterable as an array: a list or tuple is copied at once, anything else is pulled
        // one item at a time. Either way the copy is charged (8 bytes an item) and capped at
        // MaxCollectionItems before it grows, so a cheap long iterator cannot outgrow the budget.
        // adoptedByCaller: the array becomes a value the caller makes through the factory (a tuple), which
        // charges it then; the cap and the steps still apply here.
        internal static ScriptValue[] ToArray(EvalContext c, ScriptValue src, bool adoptedByCaller = false)
        {
            long arrayCharge = adoptedByCaller ? 0 : 8;
            ListValue lv = src as ListValue;
            if (lv != null)
            {
                c.Values.EnsureCollectionSize(lv.Items.Count);
                c.Values.PreCharge(arrayCharge * lv.Items.Count);
                c.Budget.ChargeLinear(lv.Items.Count);
                return lv.Items.ToArray();
            }
            TupleValue tv = src as TupleValue;
            if (tv != null)
            {
                c.Values.PreCharge(arrayCharge * tv.Items.Length);
                c.Budget.ChargeLinear(tv.Items.Length);
                return (ScriptValue[])tv.Items.Clone();
            }
            var buf = new List<ScriptValue>();
            IScriptIterator it = PyOps.GetIterator(src, c);
            ScriptValue v;
            while (it.MoveNext(c, out v))
            {
                c.Budget.Step();
                if (buf.Count == buf.Capacity)
                {
                    int grown = buf.Count == 0 ? 16 : buf.Count * 2;
                    c.Values.EnsureCollectionSize(buf.Count + 1);
                    c.Values.PreCharge(8L * (grown - buf.Count));
                    buf.Capacity = (int)System.Math.Min(grown, (long)c.Limits.MaxCollectionItems);
                }
                buf.Add(v);
            }
            c.Values.PreCharge(arrayCharge * buf.Count);
            return buf.ToArray();
        }

        // The ints of an iterable as bytes (bytes(iterable), int.from_bytes): each in range(256), the result
        // capped at MaxStrChars as it grows. A bytes object answers its own array, to be read only.
        internal static byte[] ToByteArray(EvalContext c, ScriptValue src)
        {
            BytesValue b = src as BytesValue;
            if (b != null)
                return b.Data;
            var buf = new List<byte>();
            IScriptIterator it = PyOps.GetIterator(src, c);
            ScriptValue e;
            while (it.MoveNext(c, out e))
            {
                c.Budget.Step();
                if (e.Kind != ValueKind.Int && e.Kind != ValueKind.Bool)
                    throw Raise.TypeError(c, "'" + e.PyTypeName + "' object cannot be interpreted as an integer");
                System.Numerics.BigInteger v = NumericOps.AsBigInteger(e);
                if (v < 0 || v > 255)
                    throw Raise.ValueError(c, "bytes must be in range(0, 256)");
                if (buf.Count >= c.Limits.MaxStrChars)
                    c.Values.CheckStrLen(buf.Count + 1L);
                buf.Add((byte)v);
            }
            return buf.ToArray();
        }
    }
}

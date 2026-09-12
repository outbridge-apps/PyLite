using System;
using System.Numerics;
using System.Text;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // immutable bytes. Elements are ints (0-255); slicing yields bytes. Ordering is
    // lexicographic (via TryCompareLeafCore, consulted by PyOps.Order); hashable like str (seeded).
    public sealed class BytesValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = new ScriptTypeInfo("bytes", BytesMethods.BuildSlots());
        internal static ScriptTypeInfo BytesType { get { return Type; } }

        internal readonly byte[] Data;
        private long _hashCache = long.MinValue;
        internal BytesValue(byte[] data) { Data = data; }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Bytes; } }

        protected internal override bool IsTruthyCore(EvalContext ctx) { return Data.Length > 0; }
        protected internal override bool TryLengthCore(out long length) { length = Data.Length; return true; }
        protected internal override IScriptIterator GetIteratorCore(EvalContext ctx) { return new BytesIterator(this); }

        protected internal override long HashLeafCore(EvalContext ctx, int depth)
        {
            if (_hashCache != long.MinValue)
                return _hashCache;
            var sb = new StringBuilder(Data.Length);
            for (int i = 0; i < Data.Length; i++)
                sb.Append((char)Data[i]);   // latin-1 view — internally consistent, seeded
            _hashCache = NumericHash.HashString(sb.ToString(), ctx.StrHashSeed);
            return _hashCache;
        }

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            BytesValue o = other as BytesValue;
            if (o == null || o.Data.Length != Data.Length)
                return false;
            for (int i = 0; i < Data.Length; i++)
                if (Data[i] != o.Data[i])
                    return false;
            return true;
        }

        protected internal override bool TryCompareLeafCore(ScriptValue other, EvalContext ctx, int depth, out int cmp)
        {
            BytesValue o = other as BytesValue;
            if (o == null)
            {
                cmp = 0;
                return false;
            }
            int n = Math.Min(Data.Length, o.Data.Length);
            for (int i = 0; i < n; i++)
            {
                if (Data[i] != o.Data[i])
                {
                    cmp = Data[i] < o.Data[i] ? -1 : 1;
                    return true;
                }
            }
            cmp = Data.Length.CompareTo(o.Data.Length);
            return true;
        }

        protected internal override bool TryContainsCore(ScriptValue item, EvalContext ctx, int depth, out bool found)
        {
            if (item.Kind == ValueKind.Int || item.Kind == ValueKind.Bool)
            {
                BigInteger v = NumericOps.AsBigInteger(item);
                if (v < 0 || v > 255)
                    throw Raise.ValueError(ctx, "byte must be in range(0, 256)");
                found = Array.IndexOf(Data, (byte)v) >= 0;
                return true;
            }
            if (item.Kind == ValueKind.Bytes)
            {
                found = PySearch.IndexOf(ctx, Data, ((BytesValue)item).Data, 0, Data.Length) >= 0;
                return true;
            }
            throw Raise.TypeError(ctx, "a bytes-like object is required, not '" + item.PyTypeName + "'");
        }

        internal int IndexOf(EvalContext ctx, byte[] needle, int start)
        {
            return PySearch.IndexOf(ctx, Data, needle, start, Data.Length);
        }

        protected internal override ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        {
            switch (op)
            {
                case PyBinOp.Add:
                    if (other.Kind != ValueKind.Bytes)
                        return null;
                    BytesValue ob = (BytesValue)other;
                    BytesValue left = reflected ? ob : this;
                    BytesValue right = reflected ? this : ob;
                    var cat = new byte[left.Data.Length + right.Data.Length];
                    Array.Copy(left.Data, 0, cat, 0, left.Data.Length);
                    Array.Copy(right.Data, 0, cat, left.Data.Length, right.Data.Length);
                    return ctx.Values.Bytes(cat);
                case PyBinOp.Mul:
                    if (other.Kind != ValueKind.Int && other.Kind != ValueKind.Bool)
                        return null;
                    return Repeat(NumericOps.AsBigInteger(other), ctx);
                case PyBinOp.Mod:
                    if (reflected)
                        return null;
                    if (ctx.Format == null)
                        throw Raise.NotImplemented(ctx, "%-formatting is not available in this context");
                    return ctx.Format.PercentFormatBytes(ctx, this, other);
                default:
                    return null;
            }
        }

        private ScriptValue Repeat(BigInteger n, EvalContext ctx)
        {
            if (n <= 0 || Data.Length == 0)
                return ValueFactory.EmptyBytes;
            BigInteger total = (BigInteger)Data.Length * n;
            if (total > ctx.Limits.MaxStrChars)
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxStrChars", ctx.Limits.MaxStrChars,
                    total > long.MaxValue ? long.MaxValue : (long)total);
            int count = (int)n;
            var buf = new byte[(int)total];
            for (int i = 0; i < count; i++)
                Array.Copy(Data, 0, buf, i * Data.Length, Data.Length);
            return ctx.Values.Bytes(buf);
        }

        internal ScriptValue GetItem(ScriptValue index, EvalContext ctx)
        {
            if (index.Kind == ValueKind.Slice)
            {
                SliceIndices ind = ((SliceValue)index).Indices(Data.Length, ctx);
                int start = (int)ind.Start, step = ind.StepInt, len = (int)ind.Length;
                if (len == 0)
                    return ValueFactory.EmptyBytes;
                var buf = new byte[len];
                int idx = start;
                for (int k = 0; k < len; k++)
                {
                    buf[k] = Data[idx];
                    idx += step;
                }
                return ctx.Values.Bytes(buf);
            }
            if (index.Kind == ValueKind.Int || index.Kind == ValueKind.Bool)
            {
                BigInteger i = NumericOps.AsBigInteger(index);
                if (i < 0)
                    i += Data.Length;
                if (i < 0 || i >= Data.Length)
                    throw Raise.IndexError(ctx, "index out of range");
                return ctx.Values.Int(Data[(int)i]);
            }
            throw Raise.TypeError(ctx, "byte indices must be integers");
        }

        // Port of bytes_repr: b'...' with \\, \t, \n, \r, printable ASCII, else \xNN. Quote picking like str.
        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            char quote = '\'';
            if (Array.IndexOf(Data, (byte)'\'') >= 0 && Array.IndexOf(Data, (byte)'"') < 0)
                quote = '"';
            sb.Append('b');
            sb.Append(quote);
            for (int i = 0; i < Data.Length; i++)
            {
                byte b = Data[i];
                if (b == (byte)'\\' || b == quote)
                {
                    sb.Append('\\');
                    sb.Append((char)b);
                }
                else if (b == (byte)'\t')
                    sb.Append("\\t");
                else if (b == (byte)'\n')
                    sb.Append("\\n");
                else if (b == (byte)'\r')
                    sb.Append("\\r");
                else if (b >= 0x20 && b < 0x7F)
                    sb.Append((char)b);
                else
                {
                    sb.Append("\\x");
                    sb.Append("0123456789abcdef"[b >> 4]);
                    sb.Append("0123456789abcdef"[b & 0xF]);
                }
            }
            sb.Append(quote);
        }
    }

    internal sealed class BytesIterator : ScriptIteratorBase
    {
        private readonly BytesValue _b;
        private int _pos;
        internal BytesIterator(BytesValue b) { _b = b; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_pos < _b.Data.Length)
            {
                value = ctx.Values.Int(_b.Data[_pos]);
                _pos++;
                return true;
            }
            value = null;
            return false;
        }
    }
}

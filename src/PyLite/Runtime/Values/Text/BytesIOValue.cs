using System;
using System.Collections.Generic;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // io.BytesIO: an in-memory binary file. The buffer is capped at MaxStrChars (the bytes cap) and every
    // reallocation is charged in full, so growth costs at most twice the final size; copies cost steps.
    // The same class is zipfile.ZipExtFile, a read-only view of one archive member.
    internal sealed class BytesIOValue : MemoryFileValue
    {
        private static readonly ScriptTypeInfo SType = new ScriptTypeInfo("io.BytesIO", BuildSlots(false));
        private static readonly ScriptTypeInfo ExtType = new ScriptTypeInfo("zipfile.ZipExtFile", BuildSlots(true));
        internal static ScriptTypeInfo BytesIOType { get { return SType; } }
        internal static ScriptTypeInfo ZipExtFileType { get { return ExtType; } }

        private byte[] _buf;
        private int _len;
        private readonly ScriptTypeInfo _type = SType;
        private readonly string _name;               // a ZipExtFile's member name
        private readonly int _compressType;
        private Func<EvalContext, Exception> _failAtEnd;   // raised by the read that reaches the end

        internal BytesIOValue(EvalContext ctx, byte[] initial)
        {
            ctx.Values.PreCharge(48 + initial.Length);
            ctx.Budget.ChargeLinear(initial.Length);
            _buf = (byte[])initial.Clone();
            _len = initial.Length;
        }

        // A zip member, already charged by whoever produced `data`; failAtEnd carries a bad CRC to the read
        // that reaches the end, as CPython's ZipExtFile raises it there and not when it is opened.
        private BytesIOValue(byte[] data, string name, int compressType, Func<EvalContext, Exception> failAtEnd)
        {
            _buf = data;
            _len = data.Length;
            _type = ExtType;
            _name = name;
            _compressType = compressType;
            _failAtEnd = failAtEnd;
        }

        internal static BytesIOValue ZipMember(EvalContext ctx, byte[] data, string name, int compressType,
            Func<EvalContext, Exception> failAtEnd)
        {
            ctx.Values.PreCharge(64 + 2L * name.Length);
            return new BytesIOValue(data, name, compressType, failAtEnd);
        }

        // CPython's ZipExtFile repr; a plain BytesIO keeps the default one.
        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            if (_type != ExtType)
            {
                base.ReprLeafCore(sb, ctx, depth);
                return;
            }
            sb.Append("<zipfile.ZipExtFile");
            if (IsClosed)
            {
                sb.Append(" [closed]>");
                return;
            }
            sb.Append(" name=" + ctx.Values.Str(_name).Repr(ctx) + " mode='r'");
            if (_compressType == 8)
                sb.Append(" compress_type=deflate");
            sb.Append('>');
        }

        public override ScriptTypeInfo TypeInfo { get { return _type; } }

        protected override string ClosedText { get { return "I/O operation on closed file."; } }
        protected override string NegativeSizeText { get { return "negative size value"; } }
        protected override int ContentLength { get { return _len; } }
        protected override void Shrink(int length) { _len = length; }

        protected override long WriteItem(EvalContext ctx, ScriptValue item)
        {
            return Write(ctx, RequireBytes(ctx, item));
        }

        internal override void Close()
        {
            base.Close();   // the buffer goes with it
            _buf = Array.Empty<byte>();
            _len = 0;
        }

        internal bool Closed { get { return IsClosed; } }
        internal int Length { get { return _len; } }

        // zipfile reads and writes the archive in place through these.
        internal long Position { get { return Pos; } set { Pos = value; } }
        internal byte[] RawBuffer { get { return _buf; } }   // valid in [0, Length)

        internal void TruncateAtPosition()
        {
            if (Pos < _len)
                _len = (int)Pos;
        }

        private void FailIfAtEnd(EvalContext ctx, long end)
        {
            if (_failAtEnd != null && end >= _len)
                throw _failAtEnd(ctx);
        }

        // The whole content, as getvalue() returns it; the copy is charged by the caller's Bytes().
        internal byte[] Snapshot(EvalContext ctx)
        {
            ctx.Budget.ChargeLinear(_len);
            var r = new byte[_len];
            Buffer.BlockCopy(_buf, 0, r, 0, _len);
            return r;
        }

        internal int Write(EvalContext ctx, byte[] data)
        {
            CheckOpen(ctx);
            if (data.Length == 0)
                return 0;
            long end = Pos + data.Length;
            EnsureCapacity(ctx, end);
            ctx.Budget.ChargeLinear(data.Length);
            if (Pos > _len)
                Array.Clear(_buf, _len, (int)(Pos - _len));   // truncate() may have left stale bytes there
            Buffer.BlockCopy(data, 0, _buf, (int)Pos, data.Length);
            Pos = end;
            if (end > _len)
                _len = (int)end;
            return data.Length;
        }

        internal byte[] Read(EvalContext ctx, long size)
        {
            CheckOpen(ctx);
            if (Pos >= _len)
            {
                if (size != 0)
                    FailIfAtEnd(ctx, Pos);
                return Array.Empty<byte>();
            }
            int avail = _len - (int)Pos;
            int take = size < 0 || size > avail ? avail : (int)size;
            if (take > 0)
                FailIfAtEnd(ctx, Pos + take);
            ctx.Budget.ChargeLinear(take);
            var r = new byte[take];
            Buffer.BlockCopy(_buf, (int)Pos, r, 0, take);
            Pos += take;
            return r;
        }

        // At most `size` bytes (all when negative), stopping after a newline; null at the end of the data.
        protected override ScriptValue ReadLine(EvalContext ctx, long size, out int length)
        {
            byte[] b = ReadLineBytes(ctx, size);
            length = b == null ? 0 : b.Length;
            return b == null ? null : ctx.Values.Bytes(b);
        }

        private byte[] ReadLineBytes(EvalContext ctx, long size)
        {
            if (Pos >= _len)
            {
                FailIfAtEnd(ctx, Pos);
                return null;
            }
            int start = (int)Pos;
            int limit = size < 0 || size > _len - start ? _len : start + (int)size;
            ctx.Budget.ChargeLinear(limit - start);
            int nl = Array.IndexOf(_buf, (byte)'\n', start, limit - start);
            int stop = nl < 0 ? limit : nl + 1;
            FailIfAtEnd(ctx, stop);
            var r = new byte[stop - start];
            Buffer.BlockCopy(_buf, start, r, 0, r.Length);
            Pos = stop;
            return r;
        }

        private void EnsureCapacity(EvalContext ctx, long needed)
        {
            int cap = ctx.Limits.MaxStrChars;
            if (needed > cap)
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxStrChars", cap, needed);
            if (needed <= _buf.Length)
                return;
            long grown = Math.Max(needed, Math.Max(64L, 2L * _buf.Length));
            int size = (int)Math.Min(grown, cap);
            ctx.Values.PreCharge(size);   // a new backing array, charged before it exists
            var nb = new byte[size];
            Buffer.BlockCopy(_buf, 0, nb, 0, _len);
            _buf = nb;
        }

        internal static byte[] RequireBytes(EvalContext ctx, ScriptValue v)
        {
            BytesValue b = v as BytesValue;
            if (b == null)
                throw Raise.TypeError(ctx, "a bytes-like object is required, not '" + v.PyTypeName + "'");
            return b.Data;
        }

        private static IDictionary<string, SlotDescriptor> BuildSlots(bool zipMember)
        {
            Dictionary<string, SlotDescriptor> s = CommonSlots();
            BuiltinDelegate read = (self, a, kw, ctx) =>
            {
                Args.AtMost(ctx, a, kw, "read", 1);
                BytesIOValue io = Open<BytesIOValue>(self, ctx);
                return ctx.Values.Bytes(io.Read(ctx, FileArgs.OptionalSize(ctx, a, 0)));
            };
            s["read"] = SlotDescriptor.MakeMethod("read", read);
            s["read1"] = SlotDescriptor.MakeMethod("read1", read);
            s["readline"] = SlotDescriptor.MakeMethod("readline", (self, a, kw, ctx) =>
            {
                Args.AtMost(ctx, a, kw, "readline", 1);
                BytesIOValue io = Open<BytesIOValue>(self, ctx);
                long size = FileArgs.OptionalSize(ctx, a, 0);
                if (size == 0)
                    return ValueFactory.EmptyBytes;
                int unused;
                return io.ReadLine(ctx, size, out unused) ?? ValueFactory.EmptyBytes;
            });
            s["seek"] = SlotDescriptor.MakeMethod("seek", (self, a, kw, ctx) =>
            {
                Args.Between(ctx, a, kw, "seek", 1, 2);
                BytesIOValue io = Open<BytesIOValue>(self, ctx);
                long pos = FileArgs.Ssize(ctx, Coerce.ToIndex(ctx, a[0], "seek"));
                int whence = a.Length == 2 ? Coerce.ToCInt(ctx, a[1], "seek") : 0;
                if (pos < 0 && whence == 0)
                    throw Raise.ValueError(ctx, "negative seek value " + pos);
                if (whence == 1)
                    pos += io.Pos;
                else if (whence == 2)
                    pos += io._len;
                else if (whence != 0)
                    throw Raise.ValueError(ctx, "invalid whence (" + whence + ", should be 0, 1 or 2)");
                io.Pos = pos < 0 ? 0 : pos;
                return ctx.Values.Int(io.Pos);
            });
            if (zipMember)
            {
                // ZipExtFile reads only: no write side, no getvalue, and it knows its member's name.
                s.Remove("writelines");
                s.Remove("truncate");
                s["writable"] = SlotDescriptor.MakeMethod("writable", Answer("writable", false));
                s["name"] = SlotDescriptor.MakeProperty("name", (self, ctx) => ctx.Values.Str(((BytesIOValue)self)._name));
                s["mode"] = SlotDescriptor.MakeProperty("mode", (self, ctx) => ctx.Values.Str("r"));
                return s;
            }
            s["getvalue"] = SlotDescriptor.MakeMethod("getvalue", (self, a, kw, ctx) =>
            {
                Args.None(ctx, a, kw, "getvalue");
                return ctx.Values.Bytes(Open<BytesIOValue>(self, ctx).Snapshot(ctx));
            });
            s["write"] = SlotDescriptor.MakeMethod("write", (self, a, kw, ctx) =>
            {
                Args.Exactly(ctx, a, kw, "write", 1);
                return ctx.Values.Int(Open<BytesIOValue>(self, ctx).WriteItem(ctx, a[0]));
            });
            return s;
        }
    }
}

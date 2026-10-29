using System;
using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Runtime.Values
{
    // io.StringIO: an in-memory text buffer with a read/write position. write() overwrites at the
    // position (CPython semantics); iteration yields lines. Growth is charged and capped at MaxStrChars.
    internal sealed class StringIOValue : MemoryFileValue
    {
        private static readonly ScriptTypeInfo SType = new ScriptTypeInfo("io.StringIO", BuildSlots());
        internal static ScriptTypeInfo StringIOType { get { return SType; } }

        private readonly StringBuilder _buf;

        internal StringIOValue(EvalContext ctx, string initial)
        {
            ctx.Values.PreCharge(24 + 2L * initial.Length);
            _buf = new StringBuilder(initial);
        }

        public override ScriptTypeInfo TypeInfo { get { return SType; } }

        protected override string ClosedText { get { return "I/O operation on closed file"; } }
        protected override string NegativeSizeText { get { return "Negative size value"; } }
        protected override int ContentLength { get { return _buf.Length; } }
        protected override void Shrink(int length) { _buf.Length = length; }

        protected override long WriteItem(EvalContext ctx, ScriptValue item)
        {
            string text = RequireStr(ctx, item);
            Write(ctx, text);
            return text.Length;
        }

        // At most `size` characters (all when negative), stopping after a newline; null at the end.
        protected override ScriptValue ReadLine(EvalContext ctx, long size, out int length)
        {
            length = 0;
            if (Pos >= _buf.Length)
                return null;
            int start = (int)Pos;
            int limit = size < 0 || size > _buf.Length - start ? _buf.Length : start + (int)size;
            int p = start;
            while (p < limit && _buf[p] != '\n')
                p++;
            if (p < limit)
                p++;   // include the newline
            Pos = p;
            length = p - start;
            return ctx.Values.Str(_buf.ToString(start, length));
        }

        // Overwrites at the position; a position past the end pads the gap with NULs first, as CPython does.
        internal void Write(EvalContext ctx, string s)
        {
            if (s.Length == 0)
                return;
            long end = Pos + s.Length;
            if (end > ctx.Limits.MaxStrChars)
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxStrChars", ctx.Limits.MaxStrChars, end);
            ctx.Values.PreCharge(2L * Math.Max(0, end - _buf.Length));
            ctx.Budget.ChargeLinear(s.Length);
            int pos = (int)Pos;
            if (pos > _buf.Length)
                _buf.Append('\0', pos - _buf.Length);
            int overlap = Math.Min(s.Length, _buf.Length - pos);
            for (int i = 0; i < overlap; i++)
                _buf[pos + i] = s[i];
            if (overlap < s.Length)
                _buf.Append(s, overlap, s.Length - overlap);
            Pos = end;
        }

        private static string RequireStr(EvalContext ctx, ScriptValue v)
        {
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, "string argument expected, got '" + v.PyTypeName + "'");
            return s.Value;
        }

        private static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            Dictionary<string, SlotDescriptor> s = CommonSlots();
            s["getvalue"] = SlotDescriptor.MakeMethod("getvalue", Args.Positional("getvalue", 0, 0, (self, a, kw, ctx) =>
            {
                StringIOValue io = Open<StringIOValue>(self, ctx);
                ctx.Budget.ChargeLinear(io._buf.Length);
                return ctx.Values.Str(io._buf.ToString());
            }));
            s["write"] = SlotDescriptor.MakeMethod("write", Args.Positional("write", 1, 1, (self, a, kw, ctx) =>
            {
                return ctx.Values.Int(Open<StringIOValue>(self, ctx).WriteItem(ctx, a[0]));
            }));
            s["read"] = SlotDescriptor.MakeMethod("read", Args.Positional("read", 0, 1, (self, a, kw, ctx) =>
            {
                StringIOValue io = Open<StringIOValue>(self, ctx);
                long size = FileArgs.OptionalSize(ctx, a, 0);
                if (io.Pos >= io._buf.Length)
                    return StrValue.Empty;
                int avail = io._buf.Length - (int)io.Pos;
                int take = size < 0 || size > avail ? avail : (int)size;
                ctx.Budget.ChargeLinear(take);
                string result = io._buf.ToString((int)io.Pos, take);
                io.Pos += take;
                return ctx.Values.Str(result);
            }));
            s["readline"] = SlotDescriptor.MakeMethod("readline", Args.Positional("readline", 0, 1, (self, a, kw, ctx) =>
            {
                StringIOValue io = Open<StringIOValue>(self, ctx);
                long size = FileArgs.OptionalSize(ctx, a, 0);
                if (size == 0)
                    return StrValue.Empty;
                int unused;
                return io.ReadLine(ctx, size, out unused) ?? StrValue.Empty;
            }));
            s["seek"] = SlotDescriptor.MakeMethod("seek", Args.Positional("seek", 1, 2, (self, a, kw, ctx) =>
            {
                StringIOValue io = Open<StringIOValue>(self, ctx);
                long pos = FileArgs.Ssize(ctx, Coerce.ToIndex(ctx, a[0], "seek"));
                int whence = a.Length == 2 ? Coerce.ToCInt(ctx, a[1], "seek") : 0;
                if (whence == 1)
                    pos += io.Pos;
                else if (whence == 2)
                    pos += io._buf.Length;
                else if (whence != 0)
                    throw Raise.ValueError(ctx, "invalid whence (" + whence + ", should be 0, 1 or 2)");
                if (pos < 0)
                    throw Raise.ValueError(ctx, "negative seek position " + pos);
                io.Pos = Math.Min(pos, int.MaxValue);   // the buffer can never be longer anyway
                return ctx.Values.Int(pos);
            }));
            return s;
        }
    }
}

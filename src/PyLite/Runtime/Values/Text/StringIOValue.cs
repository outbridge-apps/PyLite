using System;
using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Runtime.Values
{
    // io.StringIO: an in-memory text buffer with a read/write position. write() overwrites at the
    // position (CPython semantics); iteration yields lines. Growth is charged against the budget.
    internal sealed class StringIOValue : ScriptValue
    {
        private static readonly ScriptTypeInfo SType = new ScriptTypeInfo("io.StringIO", BuildSlots());

        private readonly StringBuilder _buf;
        private int _pos;
        private bool _closed;

        // Every I/O method refuses a closed buffer (io.StringIO parity).
        private void CheckOpen(EvalContext ctx)
        {
            if (_closed)
                throw Raise.ValueError(ctx, "I/O operation on closed file");
        }

        // readline(size): at most `size` characters, stopping after a newline.
        internal string ReadLineRaw(int size)
        {
            if (_pos >= _buf.Length || size == 0)
                return _pos >= _buf.Length ? null : "";
            int start = _pos;
            int limit = size < 0 ? _buf.Length : Math.Min(_buf.Length, _pos + size);
            while (_pos < limit && _buf[_pos] != '\n')
                _pos++;
            if (_pos < limit)
                _pos++;   // include the newline
            return _buf.ToString(start, _pos - start);
        }

        internal StringIOValue(EvalContext ctx, string initial)
        {
            ctx.Values.PreCharge(24 + 2L * initial.Length);
            _buf = new StringBuilder(initial);
            _pos = 0;
        }

        public override ScriptTypeInfo TypeInfo { get { return SType; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        protected internal override IScriptIterator GetIteratorCore(EvalContext ctx) { return new StringIOLineIterator(this); }

        internal string ReadLineRaw()
        {
            if (_pos >= _buf.Length)
                return null;
            int start = _pos;
            while (_pos < _buf.Length && _buf[_pos] != '\n')
                _pos++;
            if (_pos < _buf.Length)
                _pos++;   // include the newline
            return _buf.ToString(start, _pos - start);
        }

        internal void Write(EvalContext ctx, string s)
        {
            ctx.Values.PreCharge(2L * s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (_pos < _buf.Length)
                    _buf[_pos] = s[i];
                else
                    _buf.Append(s[i]);
                _pos++;
            }
        }

        private static StringIOValue Self(ScriptValue v) { return (StringIOValue)v; }

        private static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            var s = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
            s["getvalue"] = SlotDescriptor.MakeMethod("getvalue", (self, a, kw, ctx) =>
            {
                Self(self).CheckOpen(ctx);
                return ctx.Values.Str(Self(self)._buf.ToString());
            });
            s["write"] = SlotDescriptor.MakeMethod("write", (self, a, kw, ctx) =>
            {
                Self(self).CheckOpen(ctx);
                if (a.Length != 1 || a[0].Kind != ValueKind.Str)
                    throw Raise.TypeError(ctx, "string argument expected, got '" + (a.Length > 0 ? a[0].PyTypeName : "nothing") + "'");
                string text = ((StrValue)a[0]).Value;
                Self(self).Write(ctx, text);
                return ctx.Values.Int(text.Length);
            });
            s["read"] = SlotDescriptor.MakeMethod("read", (self, a, kw, ctx) =>
            {
                StringIOValue io = Self(self);
                io.CheckOpen(ctx);
                int size = -1;
                if (a.Length >= 1 && a[0].Kind != ValueKind.None)
                    size = (int)NumericOps.AsBigInteger(a[0]);
                int avail = io._buf.Length - io._pos;
                int take = size < 0 || size > avail ? avail : size;
                string result = io._buf.ToString(io._pos, take);
                io._pos += take;
                return ctx.Values.Str(result);
            });
            s["readline"] = SlotDescriptor.MakeMethod("readline", (self, a, kw, ctx) =>
            {
                Self(self).CheckOpen(ctx);
                int size = -1;
                if (a.Length >= 1 && a[0].Kind != ValueKind.None)
                    size = (int)NumericOps.AsBigInteger(a[0]);
                string line = Self(self).ReadLineRaw(size);
                return ctx.Values.Str(line ?? "");
            });
            s["readlines"] = SlotDescriptor.MakeMethod("readlines", (self, a, kw, ctx) =>
            {
                Self(self).CheckOpen(ctx);
                ListValue list = ctx.Values.List(4);
                string line;
                while ((line = Self(self).ReadLineRaw()) != null)
                {
                    ctx.Budget.Step();
                    list.Add(ctx.Values.Str(line), ctx);
                }
                return list;
            });
            s["seek"] = SlotDescriptor.MakeMethod("seek", (self, a, kw, ctx) =>
            {
                StringIOValue io = Self(self);
                io.CheckOpen(ctx);
                Args.Between(ctx, a, "seek", 1, 2);
                int pos = (int)NumericOps.AsBigInteger(a[0]);
                int whence = a.Length == 2 ? (int)NumericOps.AsBigInteger(a[1]) : 0;
                if (whence == 1)
                    pos += io._pos;
                else if (whence == 2)
                    pos += io._buf.Length;
                else if (whence != 0)
                    throw Raise.ValueError(ctx, "invalid whence (" + whence + ", should be 0, 1 or 2)");
                if (pos < 0)
                    throw Raise.ValueError(ctx, "negative seek position " + pos);
                io._pos = pos;
                return ctx.Values.Int(pos);
            });
            s["tell"] = SlotDescriptor.MakeMethod("tell", (self, a, kw, ctx) =>
            {
                Self(self).CheckOpen(ctx);
                return ctx.Values.Int(Self(self)._pos);
            });
            s["close"] = SlotDescriptor.MakeMethod("close", (self, a, kw, ctx) =>
            {
                Self(self)._closed = true;   // idempotent
                return ctx.Values.None;
            });
            s["closed"] = SlotDescriptor.MakeProperty("closed", (self, ctx) => ctx.Values.Bool(Self(self)._closed));
            return s;
        }
    }

    internal sealed class StringIOLineIterator : ScriptIteratorBase
    {
        private readonly StringIOValue _io;
        internal StringIOLineIterator(StringIOValue io) { _io = io; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            string line = _io.ReadLineRaw();
            if (line == null)
            {
                value = null;
                return false;
            }
            value = ctx.Values.Str(line);
            return true;
        }
    }
}

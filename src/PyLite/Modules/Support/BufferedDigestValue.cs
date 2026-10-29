using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // A hashlib hash or hmac object: the fed bytes are buffered (bounded by the run budget), so copy() is
    // exact. Every byte is charged when it is buffered or copied and again when it is hashed; a digest is
    // kept until the next update, so digest() and hexdigest() hash the buffer once.
    internal abstract class BufferedDigestValue : ScriptValue
    {
        internal readonly string Algo;
        private readonly List<byte> _buffer;
        private byte[] _digest;

        protected BufferedDigestValue(string algo, List<byte> buffer)
        {
            Algo = algo;
            _buffer = buffer;
        }

        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        protected abstract string BytesRequiredText(ScriptValue data);
        protected abstract byte[] Hash(byte[] message);
        protected abstract BufferedDigestValue CloneWith(List<byte> buffer);

        internal void Update(EvalContext ctx, ScriptValue data)
        {
            BytesValue b = data as BytesValue;
            if (b == null)
                throw Raise.TypeError(ctx, BytesRequiredText(data));
            ctx.Values.PreCharge(b.Data.Length);
            ctx.Budget.ChargeLinear(b.Data.Length);
            _buffer.AddRange(b.Data);
            _digest = null;
        }

        internal byte[] Digest(EvalContext ctx)
        {
            if (_digest == null)
            {
                ctx.Budget.ChargeLinear(_buffer.Count);
                _digest = Hash(_buffer.ToArray());
            }
            return _digest;
        }

        private BufferedDigestValue Copy(EvalContext ctx)
        {
            ctx.Values.PreCharge(48 + _buffer.Count);
            ctx.Budget.ChargeLinear(_buffer.Count);
            BufferedDigestValue c = CloneWith(new List<byte>(_buffer));
            c._digest = _digest;
            return c;
        }

        protected static Dictionary<string, SlotDescriptor> CommonSlots()
        {
            var d = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
            d["update"] = SlotDescriptor.MakeMethod("update", Args.Positional("update", 1, 1, (self, a, kw, c) =>
            {
                ((BufferedDigestValue)self).Update(c, a[0]);
                return c.Values.None;
            }));
            d["digest"] = SlotDescriptor.MakeMethod("digest", Args.Positional("digest", 0, 0, (self, a, kw, c) =>
                c.Values.Bytes((byte[])((BufferedDigestValue)self).Digest(c).Clone())));
            d["hexdigest"] = SlotDescriptor.MakeMethod("hexdigest", Args.Positional("hexdigest", 0, 0, (self, a, kw, c) =>
                c.Values.Str(Hex.Encode(((BufferedDigestValue)self).Digest(c)))));
            d["copy"] = SlotDescriptor.MakeMethod("copy", Args.Positional("copy", 0, 0, (self, a, kw, c) =>
                ((BufferedDigestValue)self).Copy(c)));
            d["digest_size"] = SlotDescriptor.MakeProperty("digest_size", (self, c) =>
                c.Values.Int(HashAlgos.DigestSize(((BufferedDigestValue)self).Algo)));
            d["block_size"] = SlotDescriptor.MakeProperty("block_size", (self, c) =>
                c.Values.Int(HashAlgos.BlockSize(((BufferedDigestValue)self).Algo)));
            return d;
        }
    }
}

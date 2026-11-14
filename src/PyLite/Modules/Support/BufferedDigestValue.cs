using System;
using System.Collections.Generic;
using System.Security.Cryptography;
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
        private readonly List<byte[]> _chunks;   // the bytes objects themselves: they do not change
        private byte[] _tail;   // small updates gathered here: a list slot per 1-byte update cost 8x its charge
        private int _tailLen;
        private long _total;
        private byte[] _digest;

        protected BufferedDigestValue(string algo, List<byte[]> chunks)
        {
            Algo = algo;
            _chunks = chunks;
            foreach (byte[] c in chunks)
                _total += c.Length;
        }

        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        protected abstract string BytesRequiredText(ScriptValue data);
        protected abstract HashAlgorithm NewHash();
        protected abstract BufferedDigestValue CloneWith(List<byte[]> chunks);

        internal void Update(EvalContext ctx, ScriptValue data)
        {
            BytesValue b = data as BytesValue;
            if (b == null)
                throw Raise.TypeError(ctx, BytesRequiredText(data));
            ctx.Values.PreCharge(b.Data.Length);
            ctx.Budget.ChargeLinear(b.Data.Length);
            if (b.Data.Length >= SmallChunk)
            {
                SealTail();
                _chunks.Add(b.Data);
            }
            else if (b.Data.Length > 0)
            {
                AppendTail(ctx, b.Data);
            }
            _total += b.Data.Length;
            _digest = null;
        }

        internal byte[] Digest(EvalContext ctx)
        {
            if (_digest == null)
            {
                ctx.Budget.ChargeLinear(_total);
                using (HashAlgorithm h = NewHash())
                {
                    foreach (byte[] c in _chunks)
                    {
                        h.TransformBlock(c, 0, c.Length, null, 0);
                        ctx.Budget.CheckDeadlineNow();
                    }
                    if (_tailLen > 0)
                        h.TransformBlock(_tail, 0, _tailLen, null, 0);
                    h.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    _digest = h.Hash;
                }
            }
            return _digest;
        }

        private const int SmallChunk = 4096, TailCap = 65536;

        private void AppendTail(EvalContext ctx, byte[] data)
        {
            if (_tailLen + data.Length > TailCap)
                SealTail();
            if (_tail == null || _tailLen + data.Length > _tail.Length)
            {
                int cap = Math.Min(TailCap, Math.Max(64, Math.Max(_tailLen + data.Length, (_tail == null ? 0 : _tail.Length) * 2)));
                ctx.Values.PreCharge(24 + cap);   // the buffer itself: a 1-byte update was charged 1 byte for a 256-byte tail
                byte[] grown = new byte[cap];
                if (_tailLen > 0)
                    Buffer.BlockCopy(_tail, 0, grown, 0, _tailLen);
                _tail = grown;
            }
            Buffer.BlockCopy(data, 0, _tail, _tailLen, data.Length);
            _tailLen += data.Length;
        }

        // the gathered bytes become one chunk, so the order of the fed bytes is kept
        private void SealTail()
        {
            if (_tailLen == 0)
                return;
            byte[] sealedTail = _tail;
            if (_tailLen < sealedTail.Length)
                Array.Resize(ref sealedTail, _tailLen);
            _chunks.Add(sealedTail);
            _tail = null;
            _tailLen = 0;
        }

        private BufferedDigestValue Copy(EvalContext ctx)
        {
            ctx.Values.PreCharge(48 + _total);
            ctx.Budget.ChargeLinear(_total);
            BufferedDigestValue c = CloneWith(new List<byte[]>(_chunks));
            if (_tailLen > 0)
            {
                c._tail = new byte[_tailLen];
                Buffer.BlockCopy(_tail, 0, c._tail, 0, _tailLen);
                c._tailLen = _tailLen;
                c._total += _tailLen;
            }
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

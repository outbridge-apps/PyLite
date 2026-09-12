using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // hmac: new(key, msg=None, digestmod), update/digest/hexdigest, compare_digest (constant time).
    public static class HmacModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["new"] = BuiltinFunctionValue.Make("new", (s, a, kw, c) => NewHmac(c, a, kw));
            m["compare_digest"] = BuiltinFunctionValue.Make("compare_digest", (s, a, kw, c) => CompareDigest(c, a));
            return ctx.Values.Module("hmac", m);
        }

        private static ScriptValue NewHmac(EvalContext ctx, ScriptValue[] args, KwArgs kw)
        {
            if (args.Length < 1)
                throw Raise.TypeError(ctx, "new() missing required argument 'key'");
            BytesValue key = args[0] as BytesValue;
            if (key == null)
                throw Raise.TypeError(ctx, "key: expected bytes, not '" + args[0].PyTypeName + "'");
            string algo = "md5";
            ScriptValue dm;
            if (kw.TryGet("digestmod", out dm) || args.Length >= 3)
                algo = AlgoName(ctx, kw.TryGet("digestmod", out dm) ? dm : args[2]);
            var h = new HmacObjectValue((byte[])key.Data.Clone(), algo);
            if (args.Length >= 2 && args[1].Kind != ValueKind.None)
                h.Update(ctx, args[1]);
            return h;
        }

        private static string AlgoName(EvalContext ctx, ScriptValue digestmod)
        {
            StrValue s = digestmod as StrValue;
            string name = s != null ? s.Value : (digestmod as BuiltinFunctionValue)?.Name;
            if (name == null)
                throw Raise.TypeError(ctx, "digestmod must be a string or a hash constructor");
            name = name.ToLowerInvariant();
            if (!HashlibModule.IsSupported(name))
                throw Raise.Make(ctx, PyExceptionTypes.ValueError, "unsupported hash type " + name);
            return name;
        }

        // HMAC by the RFC 2104 construction over any hashlib algorithm: the framework has no
        // HMACSHA224, and one construction for all of them keeps hmac and hashlib in step.
        internal static byte[] ComputeHmac(string algo, byte[] key, byte[] msg)
        {
            int block = HashlibModule.BlockSize(algo);
            byte[] k = key;
            if (k.Length > block)
            {
                using (HashAlgorithm h = HashlibModule.CreateAlgo(algo))
                    k = h.ComputeHash(k);
            }
            byte[] inner;
            using (HashAlgorithm h = HashlibModule.CreateAlgo(algo))
                inner = h.ComputeHash(Pad(0x36, k, block, msg));
            using (HashAlgorithm h = HashlibModule.CreateAlgo(algo))
                return h.ComputeHash(Pad(0x5c, k, block, inner));
        }

        // (key zero-padded to the block size, XORed with pad) || tail
        private static byte[] Pad(byte pad, byte[] key, int block, byte[] tail)
        {
            var buf = new byte[block + tail.Length];
            for (int i = 0; i < block; i++)
                buf[i] = (byte)(pad ^ (i < key.Length ? key[i] : 0));
            Buffer.BlockCopy(tail, 0, buf, block, tail.Length);
            return buf;
        }

        // Constant-time equality of two bytes (or two ASCII str). Returns bool.
        internal static ScriptValue CompareDigest(EvalContext ctx, ScriptValue[] a)
        {
            Args.Exactly(ctx, a, "compare_digest", 2);
            byte[] x = AsBytes(ctx, a[0]), y = AsBytes(ctx, a[1]);
            int diff = x.Length ^ y.Length;
            int n = Math.Max(x.Length, y.Length);
            for (int i = 0; i < n; i++)
                diff |= (i < x.Length ? x[i] : 0) ^ (i < y.Length ? y[i] : 0);
            return ctx.Values.Bool(diff == 0);
        }

        private static byte[] AsBytes(EvalContext ctx, ScriptValue v)
        {
            BytesValue b = v as BytesValue;
            if (b != null)
                return b.Data;
            StrValue s = v as StrValue;
            if (s != null)
            {
                var buf = new byte[s.Value.Length];
                for (int i = 0; i < s.Value.Length; i++)
                    buf[i] = (byte)s.Value[i];
                return buf;
            }
            throw Raise.TypeError(ctx, "unsupported operand types(s) or combination of types");
        }
    }

    internal sealed class HmacObjectValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = BuildType();

        internal readonly byte[] Key;
        internal readonly string Algo;
        internal readonly List<byte> Msg;
        internal HmacObjectValue(byte[] key, string algo) { Key = key; Algo = algo; Msg = new List<byte>(); }
        private HmacObjectValue(byte[] key, string algo, List<byte> msg) { Key = key; Algo = algo; Msg = msg; }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        internal void Update(EvalContext ctx, ScriptValue data)
        {
            BytesValue b = data as BytesValue;
            if (b == null)
                throw Raise.TypeError(ctx, "expected bytes, not '" + data.PyTypeName + "'");
            ctx.Values.PreCharge(b.Data.Length);
            Msg.AddRange(b.Data);
        }

        internal byte[] Compute()
        {
            return HmacModule.ComputeHmac(Algo, Key, Msg.ToArray());
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("<hmac." + Algo + " object>");
        }

        private static ScriptTypeInfo BuildType()
        {
            var d = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
            d["update"] = SlotDescriptor.MakeMethod("update", (self, a, kw, c) =>
            {
                Args.AtLeast(c, a, "update", 1);
                ((HmacObjectValue)self).Update(c, a[0]);
                return c.Values.None;
            });
            d["digest"] = SlotDescriptor.MakeMethod("digest", (self, a, kw, c) => c.Values.Bytes(((HmacObjectValue)self).Compute()));
            d["hexdigest"] = SlotDescriptor.MakeMethod("hexdigest", (self, a, kw, c) => c.Values.Str(HashObjectValue.Hex(((HmacObjectValue)self).Compute())));
            d["copy"] = SlotDescriptor.MakeMethod("copy", (self, a, kw, c) =>
            {
                HmacObjectValue o = (HmacObjectValue)self;
                return new HmacObjectValue(o.Key, o.Algo, new List<byte>(o.Msg));
            });
            d["name"] = SlotDescriptor.MakeProperty("name", (self, c) => c.Values.Str("hmac-" + ((HmacObjectValue)self).Algo));
            return new ScriptTypeInfo("HMAC", d);
        }
    }
}

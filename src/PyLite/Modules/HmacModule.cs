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
    internal static class HmacModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["new"] = BuiltinFunctionValue.Make("new", (s, a, kw, c) => NewHmac(c, a, kw));
            m["compare_digest"] = BuiltinFunctionValue.Make("compare_digest", Args.Positional("compare_digest", 2, 2, (s, a, kw, c) => CompareDigest(c, a)));
            return ctx.Values.Module("hmac", m);
        }

        // new(key, msg=None, digestmod=...): bound by name, so msg= is read and an unknown keyword refused
        // (msg= used to be dropped silently, giving the HMAC of an empty message).
        private static readonly ArgSpec NewSpec = ArgSpec.Create("new")
            .Req("key").Opt("msg", ArgSpec.MISSING).Opt("digestmod", ArgSpec.MISSING).Seal();

        private static ScriptValue NewHmac(EvalContext ctx, ScriptValue[] args, KwArgs kw)
        {
            var slots = new ScriptValue[NewSpec.SlotCount];
            ScriptValue[] star;
            NewSpec.Bind(ctx, args, kw, slots, out star);
            BytesValue key = slots[0] as BytesValue;
            if (key == null)
                throw Raise.TypeError(ctx, "key: expected bytes, not '" + slots[0].PyTypeName + "'");
            if (ReferenceEquals(slots[2], ArgSpec.MISSING) || slots[2].Kind == ValueKind.None)
                throw Raise.TypeError(ctx, "Missing required argument 'digestmod'.");   // no md5 default since 3.8
            string algo = AlgoName(ctx, slots[2]);
            ctx.Budget.ChargeLinear(key.Data.Length);
            var h = new HmacObjectValue(HashAlgos.MacKey(algo, key.Data), algo);
            if (!ReferenceEquals(slots[1], ArgSpec.MISSING) && slots[1].Kind != ValueKind.None)
                h.Update(ctx, slots[1]);
            return h;
        }

        private static string AlgoName(EvalContext ctx, ScriptValue digestmod)
        {
            StrValue s = digestmod as StrValue;
            string name = s != null ? s.Value : (digestmod as BuiltinFunctionValue)?.Name;
            if (name == null)
                throw Raise.TypeError(ctx, "digestmod must be a string or a hash constructor");
            name = name.ToLowerInvariant();
            if (!HashAlgos.IsSupported(name))
                throw Raise.Make(ctx, PyExceptionTypes.ValueError, "unsupported hash type " + name);
            return name;
        }

        // Constant-time equality of two bytes (or two ASCII str). Returns bool.
        internal static ScriptValue CompareDigest(EvalContext ctx, ScriptValue[] a)
        {
            Args.Exactly(ctx, a, "compare_digest", 2);
            if ((a[0] is StrValue) != (a[1] is StrValue))
                throw Raise.TypeError(ctx, "a bytes-like object is required, not 'str'");   // two str or two bytes
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
                {
                    if (s.Value[i] > 0x7F)
                        throw Raise.TypeError(ctx, "comparing strings with non-ASCII characters is not supported");
                    buf[i] = (byte)s.Value[i];
                }
                return buf;
            }
            throw Raise.TypeError(ctx, "unsupported operand types(s) or combination of types");
        }
    }

    internal sealed class HmacObjectValue : BufferedDigestValue
    {
        private static readonly ScriptTypeInfo Type = BuildType();

        private readonly byte[] _key;

        internal HmacObjectValue(byte[] key, string algo) : this(key, algo, new List<byte[]>()) { }
        private HmacObjectValue(byte[] key, string algo, List<byte[]> msg) : base(algo, msg) { _key = key; }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }

        protected override string BytesRequiredText(ScriptValue data)
        {
            return "expected bytes, not '" + data.PyTypeName + "'";
        }

        protected override HashAlgorithm NewHash() { return HashAlgos.NewMac(Algo, _key); }

        protected override BufferedDigestValue CloneWith(List<byte[]> chunks) { return new HmacObjectValue(_key, Algo, chunks); }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("<hmac." + Algo + " object>");
        }

        private static ScriptTypeInfo BuildType()
        {
            Dictionary<string, SlotDescriptor> d = CommonSlots();
            d["name"] = SlotDescriptor.MakeProperty("name", (self, c) => c.Values.Str("hmac-" + ((HmacObjectValue)self).Algo));
            return new ScriptTypeInfo("HMAC", d);
        }
    }
}

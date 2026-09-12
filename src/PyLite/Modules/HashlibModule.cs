using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // hashlib: md5/sha1/sha224/sha256/sha384/sha512 + new(name). Hash objects buffer their
    // input (bounded by the run budget) so update/digest/hexdigest/copy are trivial and copy is exact.
    // Note: md5 uses the framework MD5 here; a pure-managed MD5 port (FIPS) is a documented follow-up.
    public static partial class HashlibModule
    {
        internal static readonly string[] Algorithms = { "md5", "sha1", "sha224", "sha256", "sha384", "sha512" };

        internal static bool IsSupported(string name)
        {
            return Array.IndexOf(Algorithms, name) >= 0;
        }

        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            foreach (string algo in Algorithms)
            {
                string a = algo;
                m[a] = BuiltinFunctionValue.Make(a, (s, args, kw, c) => New(c, a, StrMethods.WithKw(c, args, kw, a, "data")));
            }
            m["new"] = BuiltinFunctionValue.Make("new", (s, args, kw, c) => NewByName(c, StrMethods.WithKw(c, args, kw, "new", "name", "data")));
            m["pbkdf2_hmac"] = BuiltinFunctionValue.Make("pbkdf2_hmac", (s, args, kw, c) =>
                Pbkdf2Hmac(c, StrMethods.WithKw(c, args, kw, "pbkdf2_hmac", "hash_name", "password", "salt", "iterations", "dklen")));
            return ctx.Values.Module("hashlib", m);
        }

        private static ScriptValue New(EvalContext ctx, string algo, ScriptValue[] args)
        {
            var h = new HashObjectValue(algo);
            if (args.Length >= 1)
                h.Update(ctx, args[0]);
            return h;
        }

        private static ScriptValue NewByName(EvalContext ctx, ScriptValue[] args)
        {
            StrValue name = args.Length >= 1 ? args[0] as StrValue : null;
            if (name == null)
                throw Raise.TypeError(ctx, "new() argument 1 must be str");
            string algo = name.Value.ToLowerInvariant();
            if (!IsSupported(algo))
                throw Raise.Make(ctx, PyExceptionTypes.ValueError, "unsupported hash type " + name.Value);
            var h = new HashObjectValue(algo);
            if (args.Length >= 2)
                h.Update(ctx, args[1]);
            return h;
        }

        internal static HashAlgorithm CreateAlgo(string name)
        {
            switch (name)
            {
                case "md5": return MD5.Create();
                case "sha1": return SHA1.Create();
                case "sha224": return new Support.Sha224Managed();   // no SHA-224 in the framework
                case "sha384": return SHA384.Create();
                case "sha512": return SHA512.Create();
                default: return SHA256.Create();
            }
        }

        internal static int DigestSize(string name)
        {
            switch (name)
            {
                case "md5": return 16;
                case "sha1": return 20;
                case "sha224": return 28;
                case "sha384": return 48;
                case "sha512": return 64;
                default: return 32;
            }
        }

        // The SHA-512 family (sha384 included) works on 1024-bit blocks; everything else on 512-bit ones.
        internal static int BlockSize(string name) { return name == "sha512" || name == "sha384" ? 128 : 64; }
    }

    // A hashlib hash object (opaque). Buffers fed bytes; computes on digest/hexdigest.
    internal sealed class HashObjectValue : ScriptValue
    {
        private static readonly ScriptTypeInfo Type = BuildType();

        internal readonly string AlgoName;
        internal readonly List<byte> Buffer;
        internal HashObjectValue(string algo) { AlgoName = algo; Buffer = new List<byte>(); }
        private HashObjectValue(string algo, List<byte> buf) { AlgoName = algo; Buffer = buf; }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        internal void Update(EvalContext ctx, ScriptValue data)
        {
            BytesValue b = data as BytesValue;
            if (b == null)
                throw Raise.TypeError(ctx, "object supporting the buffer API required, not '" + data.PyTypeName + "'");
            ctx.Values.PreCharge(b.Data.Length);
            ctx.Budget.ChargeLinear(2L * b.Data.Length);   // the copy now and the hash at digest time
            Buffer.AddRange(b.Data);
        }

        internal byte[] Compute()
        {
            using (HashAlgorithm h = HashlibModule.CreateAlgo(AlgoName))
                return h.ComputeHash(Buffer.ToArray());
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("<" + AlgoName + " hash object>");
        }

        private static ScriptTypeInfo BuildType()
        {
            var d = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
            d["update"] = SlotDescriptor.MakeMethod("update", (self, a, kw, c) =>
            {
                Args.AtLeast(c, a, "update", 1);
                ((HashObjectValue)self).Update(c, a[0]);
                return c.Values.None;
            });
            d["digest"] = SlotDescriptor.MakeMethod("digest", (self, a, kw, c) => c.Values.Bytes(((HashObjectValue)self).Compute()));
            d["hexdigest"] = SlotDescriptor.MakeMethod("hexdigest", (self, a, kw, c) => c.Values.Str(Hex(((HashObjectValue)self).Compute())));
            d["copy"] = SlotDescriptor.MakeMethod("copy", (self, a, kw, c) =>
            {
                HashObjectValue o = (HashObjectValue)self;
                return new HashObjectValue(o.AlgoName, new List<byte>(o.Buffer));
            });
            d["name"] = SlotDescriptor.MakeProperty("name", (self, c) => c.Values.Str(((HashObjectValue)self).AlgoName));
            d["digest_size"] = SlotDescriptor.MakeProperty("digest_size", (self, c) => c.Values.Int(HashlibModule.DigestSize(((HashObjectValue)self).AlgoName)));
            d["block_size"] = SlotDescriptor.MakeProperty("block_size", (self, c) => c.Values.Int(HashlibModule.BlockSize(((HashObjectValue)self).AlgoName)));
            return new ScriptTypeInfo("HASH", d);
        }

        internal static string Hex(byte[] data)
        {
            const string H = "0123456789abcdef";
            var sb = new StringBuilder(data.Length * 2);
            for (int i = 0; i < data.Length; i++)
            {
                sb.Append(H[data[i] >> 4]);
                sb.Append(H[data[i] & 0xF]);
            }
            return sb.ToString();
        }
    }
}

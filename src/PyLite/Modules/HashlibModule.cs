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
    // hashlib: md5/sha1/sha224/sha256/sha384/sha512 + new(name) + pbkdf2_hmac (BufferedDigestValue objects).
    // Note: md5 uses the framework MD5 here; a pure-managed MD5 port (FIPS) is a documented follow-up.
    internal static partial class HashlibModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            foreach (string algo in HashAlgos.Names)
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
            if (!HashAlgos.IsSupported(algo))
                throw Raise.Make(ctx, PyExceptionTypes.ValueError, "unsupported hash type " + name.Value);
            return New(ctx, algo, args.Length >= 2 ? new[] { args[1] } : Array.Empty<ScriptValue>());
        }
    }

    // A hashlib hash object.
    internal sealed class HashObjectValue : BufferedDigestValue
    {
        private static readonly ScriptTypeInfo Type = BuildType();

        internal HashObjectValue(string algo) : base(algo, new List<byte>()) { }
        private HashObjectValue(string algo, List<byte> buffer) : base(algo, buffer) { }

        public override ScriptTypeInfo TypeInfo { get { return Type; } }

        protected override string BytesRequiredText(ScriptValue data)
        {
            return "object supporting the buffer API required, not '" + data.PyTypeName + "'";
        }

        protected override byte[] Hash(byte[] message)
        {
            using (HashAlgorithm h = HashAlgos.Create(Algo))
                return h.ComputeHash(message);
        }

        protected override BufferedDigestValue CloneWith(List<byte> buffer) { return new HashObjectValue(Algo, buffer); }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("<" + Algo + " hash object>");
        }

        private static ScriptTypeInfo BuildType()
        {
            Dictionary<string, SlotDescriptor> d = CommonSlots();
            d["name"] = SlotDescriptor.MakeProperty("name", (self, c) => c.Values.Str(((HashObjectValue)self).Algo));
            return new ScriptTypeInfo("HASH", d);
        }
    }
}

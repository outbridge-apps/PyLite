using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // secrets: cryptographically strong tokens and choices over the OS RNG (RNGCryptoServiceProvider on
    // net472), unlike random's System.Random. SystemRandom (a class) is absent; compare_digest is hmac's.
    public static class SecretsModule
    {
        private const int DefaultEntropy = 32;
        private const int MaxTokenBytes = 1 << 20;   // a sandbox cap; tokens are tens of bytes
        private static readonly RNGCryptoServiceProvider Rng = new RNGCryptoServiceProvider();

        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal)
            {
                ["token_bytes"] = BuiltinFunctionValue.Make("token_bytes", (s, a, kw, c) => c.Values.Bytes(Bytes(c, a, kw, "token_bytes"))),
                ["token_hex"] = BuiltinFunctionValue.Make("token_hex", (s, a, kw, c) => c.Values.Str(Hex(Bytes(c, a, kw, "token_hex")))),
                ["token_urlsafe"] = BuiltinFunctionValue.Make("token_urlsafe", (s, a, kw, c) => c.Values.Str(UrlSafe(Bytes(c, a, kw, "token_urlsafe")))),
                ["randbelow"] = BuiltinFunctionValue.Make("randbelow", RandBelow),
                ["randbits"] = BuiltinFunctionValue.Make("randbits", RandBits),
                ["choice"] = BuiltinFunctionValue.Make("choice", Choice),
                ["compare_digest"] = BuiltinFunctionValue.Make("compare_digest", (s, a, kw, c) => HmacModule.CompareDigest(c, a)),
                ["DEFAULT_ENTROPY"] = ctx.Values.Int(DefaultEntropy),
            };
            return ctx.Values.Module("secrets", m);
        }

        private static byte[] Bytes(EvalContext ctx, ScriptValue[] a, KwArgs kw, string fn)
        {
            ScriptValue v = a.Length >= 1 ? a[0] : null;
            ScriptValue kv;
            if (kw.TryGet("nbytes", out kv))
                v = kv;
            Args.AtMost(ctx, a, fn, 1);
            int n = DefaultEntropy;
            if (v != null && v.Kind != ValueKind.None)
            {
                if (v.Kind != ValueKind.Int && v.Kind != ValueKind.Bool)
                    throw Raise.TypeError(ctx, "'" + v.PyTypeName + "' object cannot be interpreted as an integer");
                BigInteger b = NumericOps.AsBigInteger(v);
                if (b < 0)
                    throw Raise.ValueError(ctx, "negative argument not allowed");
                if (b > MaxTokenBytes)
                    throw Raise.Overflow(ctx, "token too large for this environment");
                n = (int)b;
            }
            ctx.Values.PreCharge(24 + n);
            var buf = new byte[n];
            Rng.GetBytes(buf);
            return buf;
        }

        private static string Hex(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 2);
            foreach (byte b in data)
                sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        // base64.urlsafe_b64encode without the '=' padding, as CPython's token_urlsafe
        private static string UrlSafe(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static BigInteger Below(EvalContext ctx, BigInteger n)
        {
            if (n <= 0)
                throw Raise.ValueError(ctx, "Upper bound must be positive.");
            // rejection sampling over the smallest byte width holding n, unbiased
            int bytes = n.ToByteArray().Length;
            var buf = new byte[bytes + 1];   // the extra zero byte keeps the BigInteger non-negative
            BigInteger limit = BigInteger.One << (bytes * 8);
            BigInteger cutoff = limit - limit % n;
            while (true)
            {
                ctx.Budget.Step();
                Rng.GetBytes(buf);
                buf[bytes] = 0;
                var r = new BigInteger(buf);
                if (r < cutoff)
                    return r % n;
            }
        }

        private static ScriptValue RandBelow(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, a, kw, "randbelow", 1);
            if (a[0].Kind != ValueKind.Int && a[0].Kind != ValueKind.Bool)
                throw Raise.TypeError(ctx, "'" + a[0].PyTypeName + "' object cannot be interpreted as an integer");
            return ctx.Values.Int(Below(ctx, NumericOps.AsBigInteger(a[0])));
        }

        private static ScriptValue RandBits(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, a, kw, "randbits", 1);
            if (a[0].Kind != ValueKind.Int && a[0].Kind != ValueKind.Bool)
                throw Raise.TypeError(ctx, "'" + a[0].PyTypeName + "' object cannot be interpreted as an integer");
            BigInteger k = NumericOps.AsBigInteger(a[0]);
            if (k < 0)
                throw Raise.ValueError(ctx, "number of bits must be non-negative");
            if (k > 1 << 20)
                throw Raise.Overflow(ctx, "too many bits for this environment");
            int bits = (int)k;
            if (bits == 0)
                return ctx.Values.Int(0);
            var buf = new byte[(bits + 7) / 8 + 1];
            Rng.GetBytes(buf);
            buf[buf.Length - 1] = 0;
            int extra = buf.Length * 8 - 8 - bits;
            if (extra > 0)
                buf[buf.Length - 2] &= (byte)(0xFF >> extra);
            return ctx.Values.Int(new BigInteger(buf));
        }

        private static ScriptValue Choice(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.Exactly(ctx, a, kw, "choice", 1);
            long n;
            if (!a[0].TryLengthCore(out n))
                throw Raise.TypeError(ctx, "object of type '" + a[0].PyTypeName + "' has no len()");
            if (n == 0)
                throw Raise.Make(ctx, PyExceptionTypes.IndexError, "Cannot choose from an empty sequence");
            BigInteger i = Below(ctx, n);
            return PyOps.GetItem(a[0], ctx.Values.Int(i), ctx);
        }
    }
}

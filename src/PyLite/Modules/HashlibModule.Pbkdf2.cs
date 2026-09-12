using System;
using System.Security.Cryptography;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // pbkdf2_hmac (PKCS#5 v2.0). Written out rather than handed to Rfc2898DeriveBytes because the whole
    // point of the function is to be slow: a million iterations inside one uninterruptible native call
    // would outlive the deadline and leak the runner thread. The loop charges a step per iteration, so
    // the budget stops it exactly like any other loop.
    public static partial class HashlibModule
    {
        private static ScriptValue Pbkdf2Hmac(EvalContext ctx, ScriptValue[] a)
        {
            if (a.Length < 4)
                throw Raise.TypeError(ctx, "pbkdf2_hmac() missing required arguments");
            StrValue nameV = a[0] as StrValue;
            if (nameV == null)
                throw Raise.TypeError(ctx, "pbkdf2_hmac() argument 'hash_name' must be str");
            string algo = nameV.Value.ToLowerInvariant();
            if (!IsSupported(algo))
                throw Raise.Make(ctx, PyExceptionTypes.ValueError, "unsupported hash type " + nameV.Value);
            byte[] password = Bytes(ctx, a[1], "password");
            byte[] salt = Bytes(ctx, a[2], "salt");
            long iterations = (long)Support.Coerce.ToIndex(ctx, a[3], "iterations must be an integer");
            if (iterations < 1)
                throw Raise.ValueError(ctx, "iteration value must be greater than 0.");

            int hashLen = DigestSize(algo);
            int dkLen = hashLen;
            if (a.Length >= 5 && a[4].Kind != ValueKind.None)
            {
                dkLen = Support.Coerce.ToInt32(ctx, a[4], "dklen must be an integer");
                if (dkLen < 1)
                    throw Raise.ValueError(ctx, "key length must be greater than 0.");
            }
            ctx.Values.PreCharge(24 + dkLen);

            int blocks = (dkLen + hashLen - 1) / hashLen;
            var dk = new byte[blocks * hashLen];
            using (KeyedHashAlgorithm mac = NewMac(algo, password))
            {
                var input = new byte[salt.Length + 4];
                Array.Copy(salt, input, salt.Length);
                for (int b = 1; b <= blocks; b++)
                {
                    input[salt.Length] = (byte)(b >> 24);
                    input[salt.Length + 1] = (byte)(b >> 16);
                    input[salt.Length + 2] = (byte)(b >> 8);
                    input[salt.Length + 3] = (byte)b;
                    byte[] u = mac.ComputeHash(input);
                    var acc = (byte[])u.Clone();
                    for (long i = 1; i < iterations; i++)
                    {
                        ctx.Budget.Step();
                        u = mac.ComputeHash(u);
                        for (int k = 0; k < acc.Length; k++)
                            acc[k] ^= u[k];
                    }
                    Array.Copy(acc, 0, dk, (b - 1) * hashLen, hashLen);
                }
            }
            var result = new byte[dkLen];
            Array.Copy(dk, result, dkLen);
            return ctx.Values.Bytes(result);
        }

        private static KeyedHashAlgorithm NewMac(string algo, byte[] key)
        {
            switch (algo)
            {
                case "md5": return new HMACMD5(key);
                case "sha1": return new HMACSHA1(key);
                case "sha384": return new HMACSHA384(key);
                case "sha512": return new HMACSHA512(key);
                case "sha224": return new Support.HmacGeneric(new Support.Sha224Managed(), key, 64);
                default: return new HMACSHA256(key);
            }
        }

        private static byte[] Bytes(EvalContext ctx, ScriptValue v, string what)
        {
            BytesValue b = v as BytesValue;
            if (b == null)
                throw Raise.TypeError(ctx, what + " must be bytes, not " + v.PyTypeName);
            return b.Data;
        }
    }
}

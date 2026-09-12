using System;
using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // base64 module: b64/standard/urlsafe/b32/b16 encode+decode. encode -> bytes; decode
    // accepts bytes OR an ASCII str (CPython parity). Invalid input -> binascii.Error (ValueError text).
    public static class Base64Module
    {
        private const string B32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["b64encode"] = BuiltinFunctionValue.Make("b64encode", (s, a, kw, c) => B64Encode(c, a, false));
            m["b64decode"] = BuiltinFunctionValue.Make("b64decode", (s, a, kw, c) => B64Decode(c, a, false));
            m["standard_b64encode"] = BuiltinFunctionValue.Make("standard_b64encode", (s, a, kw, c) => B64Encode(c, a, false));
            m["standard_b64decode"] = BuiltinFunctionValue.Make("standard_b64decode", (s, a, kw, c) => B64Decode(c, a, false));
            m["urlsafe_b64encode"] = BuiltinFunctionValue.Make("urlsafe_b64encode", (s, a, kw, c) => B64Encode(c, a, true));
            m["urlsafe_b64decode"] = BuiltinFunctionValue.Make("urlsafe_b64decode", (s, a, kw, c) => B64Decode(c, a, true));
            m["b16encode"] = BuiltinFunctionValue.Make("b16encode", (s, a, kw, c) => B16Encode(c, a));
            m["b16decode"] = BuiltinFunctionValue.Make("b16decode", (s, a, kw, c) => B16Decode(c, a));
            m["b32encode"] = BuiltinFunctionValue.Make("b32encode", (s, a, kw, c) => B32Encode(c, a));
            m["b32decode"] = BuiltinFunctionValue.Make("b32decode", (s, a, kw, c) => B32Decode(c, a));
            return ctx.Values.Module("base64", m);
        }

        // Accept bytes or an ASCII str.
        private static byte[] InBytes(EvalContext c, ScriptValue[] a, string fn)
        {
            Args.AtLeast(c, a, fn, 1);
            BytesValue bv = a[0] as BytesValue;
            if (bv != null)
                return bv.Data;
            StrValue sv = a[0] as StrValue;
            if (sv != null)
            {
                var buf = new byte[sv.Value.Length];
                for (int i = 0; i < sv.Value.Length; i++)
                {
                    if (sv.Value[i] > 0x7F)
                        throw Raise.ValueError(c, "string argument should contain only ASCII characters");
                    buf[i] = (byte)sv.Value[i];
                }
                return buf;
            }
            throw Raise.TypeError(c, "argument should be a bytes-like object or ASCII string, not '" + a[0].PyTypeName + "'");
        }

        private static ScriptValue B64Encode(EvalContext c, ScriptValue[] a, bool urlsafe)
        {
            string s = Convert.ToBase64String(InBytes(c, a, urlsafe ? "urlsafe_b64encode" : "b64encode"));
            if (urlsafe)
                s = s.Replace('+', '-').Replace('/', '_');
            return c.Values.Bytes(Ascii(s));
        }

        private static ScriptValue B64Decode(EvalContext c, ScriptValue[] a, bool urlsafe)
        {
            var sb = new StringBuilder();
            foreach (byte b in InBytes(c, a, urlsafe ? "urlsafe_b64decode" : "b64decode"))
                sb.Append((char)b);
            string s = sb.ToString();
            if (urlsafe)
                s = s.Replace('-', '+').Replace('_', '/');
            try
            {
                return c.Values.Bytes(Convert.FromBase64String(s));
            }
            catch (FormatException)
            {
                throw Raise.ValueError(c, "Invalid base64-encoded string");
            }
        }

        private static ScriptValue B16Encode(EvalContext c, ScriptValue[] a)
        {
            byte[] data = InBytes(c, a, "b16encode");
            const string H = "0123456789ABCDEF";
            var buf = new byte[data.Length * 2];
            for (int i = 0; i < data.Length; i++)
            {
                buf[i * 2] = (byte)H[data[i] >> 4];
                buf[i * 2 + 1] = (byte)H[data[i] & 0xF];
            }
            return c.Values.Bytes(buf);
        }

        private static ScriptValue B16Decode(EvalContext c, ScriptValue[] a)
        {
            byte[] data = InBytes(c, a, "b16decode");
            if ((data.Length & 1) != 0)
                throw Raise.ValueError(c, "Odd-length string");
            var buf = new byte[data.Length / 2];
            for (int i = 0; i < buf.Length; i++)
            {
                int hi = HexDigit(c, data[i * 2]), lo = HexDigit(c, data[i * 2 + 1]);
                buf[i] = (byte)(hi * 16 + lo);
            }
            return c.Values.Bytes(buf);
        }

        private static int HexDigit(EvalContext c, byte b)
        {
            if (b >= '0' && b <= '9') return b - '0';
            if (b >= 'A' && b <= 'F') return b - 'A' + 10;
            if (b >= 'a' && b <= 'f') return b - 'a' + 10;
            throw Raise.ValueError(c, "Non-base16 digit found");
        }

        private static ScriptValue B32Encode(EvalContext c, ScriptValue[] a)
        {
            byte[] data = InBytes(c, a, "b32encode");
            var sb = new StringBuilder();
            for (int i = 0; i < data.Length; i += 5)
            {
                int n = Math.Min(5, data.Length - i);
                ulong buf = 0;
                for (int k = 0; k < 5; k++)
                {
                    ulong bb = k < n ? data[i + k] : (ulong)0;
                    buf = (buf << 8) | bb;
                }
                int outChars = n == 1 ? 2 : n == 2 ? 4 : n == 3 ? 5 : n == 4 ? 7 : 8;
                for (int k = 0; k < 8; k++)
                {
                    if (k < outChars)
                        sb.Append(B32[(int)((buf >> (35 - k * 5)) & 0x1F)]);
                    else
                        sb.Append('=');
                }
            }
            return c.Values.Bytes(Ascii(sb.ToString()));
        }

        private static ScriptValue B32Decode(EvalContext c, ScriptValue[] a)
        {
            byte[] data = InBytes(c, a, "b32decode");
            var outB = new List<byte>();
            for (int i = 0; i < data.Length; i += 8)
            {
                ulong buf = 0;
                int bits = 0;
                for (int k = 0; k < 8 && i + k < data.Length; k++)
                {
                    byte ch = data[i + k];
                    if (ch == '=')
                        break;
                    int v = B32.IndexOf(char.ToUpperInvariant((char)ch));
                    if (v < 0)
                        throw Raise.ValueError(c, "Non-base32 digit found");
                    buf = (buf << 5) | (uint)v;
                    bits += 5;
                }
                int fullBytes = bits / 8;
                buf <<= (40 - bits);
                for (int k = 0; k < fullBytes; k++)
                    outB.Add((byte)((buf >> (32 - k * 8)) & 0xFF));
            }
            return c.Values.Bytes(outB.ToArray());
        }

        private static byte[] Ascii(string s)
        {
            var buf = new byte[s.Length];
            for (int i = 0; i < s.Length; i++)
                buf[i] = (byte)s[i];
            return buf;
        }
    }
}

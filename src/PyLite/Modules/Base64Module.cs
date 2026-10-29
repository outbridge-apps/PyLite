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
    internal static class Base64Module
    {
        private const string B32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            // b64encode(s, altchars=None), b64decode(s, altchars=None, validate=False); the standard_ and
            // urlsafe_ forms take s alone. Keywords bind by name (altchars= and validate= used to be dropped).
            m["b64encode"] = BuiltinFunctionValue.Make("b64encode", (s, a, kw, c) =>
                B64Encode(c, StrMethods.WithKw(c, a, kw, "b64encode", "s", "altchars"), "b64encode", null));
            m["b64decode"] = BuiltinFunctionValue.Make("b64decode", (s, a, kw, c) =>
                B64Decode(c, StrMethods.WithKw(c, a, kw, "b64decode", "s", "altchars", "validate"), "b64decode", null));
            m["standard_b64encode"] = BuiltinFunctionValue.Make("standard_b64encode", (s, a, kw, c) =>
                B64Encode(c, StrMethods.WithKw(c, a, kw, "standard_b64encode", "s"), "standard_b64encode", null));
            m["standard_b64decode"] = BuiltinFunctionValue.Make("standard_b64decode", (s, a, kw, c) =>
                B64Decode(c, StrMethods.WithKw(c, a, kw, "standard_b64decode", "s"), "standard_b64decode", null));
            m["urlsafe_b64encode"] = BuiltinFunctionValue.Make("urlsafe_b64encode", (s, a, kw, c) =>
                B64Encode(c, StrMethods.WithKw(c, a, kw, "urlsafe_b64encode", "s"), "urlsafe_b64encode", UrlSafe));
            m["urlsafe_b64decode"] = BuiltinFunctionValue.Make("urlsafe_b64decode", (s, a, kw, c) =>
                B64Decode(c, StrMethods.WithKw(c, a, kw, "urlsafe_b64decode", "s"), "urlsafe_b64decode", UrlSafe));
            m["b16encode"] = BuiltinFunctionValue.Make("b16encode", Args.Positional("b16encode", 1, 1, (s, a, kw, c) => B16Encode(c, a)));
            m["b16decode"] = BuiltinFunctionValue.Make("b16decode", (s, a, kw, c) => B16Decode(c, Args.Named(c, a, kw, "b16decode", "s", "casefold")));
            m["b32encode"] = BuiltinFunctionValue.Make("b32encode", Args.Positional("b32encode", 1, 1, (s, a, kw, c) => B32Encode(c, a)));
            m["b32decode"] = BuiltinFunctionValue.Make("b32decode", (s, a, kw, c) => B32Decode(c, Args.Named(c, a, kw, "b32decode", "s", "casefold", "map01")));
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

        private static readonly byte[] UrlSafe = { (byte)'-', (byte)'_' };

        // The two characters standing for '+' and '/': a[1] when given (altchars), else the fixed pair.
        private static byte[] AltChars(EvalContext c, ScriptValue[] a, string fn, byte[] fixedPair)
        {
            if (fixedPair != null || a.Length < 2 || a[1].Kind == ValueKind.None)
                return fixedPair;
            byte[] alt = InBytes(c, new[] { a[1] }, fn);
            if (alt.Length != 2)
                throw Raise.ValueError(c, "altchars must be a bytes-like object of length 2");
            return alt;
        }

        private static ScriptValue B64Encode(EvalContext c, ScriptValue[] a, string fn, byte[] fixedPair)
        {
            Args.Between(c, a, fn, 1, fixedPair == null && fn == "b64encode" ? 2 : 1);
            byte[] alt = AltChars(c, a, fn, fixedPair);
            string s = Convert.ToBase64String(InBytes(c, a, fn));
            if (alt != null)
                s = s.Replace('+', (char)alt[0]).Replace('/', (char)alt[1]);
            return c.Values.Bytes(Ascii(s));
        }

        // CPython's b64decode: without validate, characters outside the alphabet (after altchars are mapped
        // back) are discarded; with it, any such character is a binascii.Error.
        private static ScriptValue B64Decode(EvalContext c, ScriptValue[] a, string fn, byte[] fixedPair)
        {
            Args.Between(c, a, fn, 1, fixedPair == null && fn == "b64decode" ? 3 : 1);
            byte[] alt = AltChars(c, a, fn, fixedPair);
            bool validate = a.Length >= 3 && a[2].IsTruthy(c);
            byte[] data = InBytes(c, a, fn);
            var sb = new StringBuilder(data.Length);
            foreach (byte b in data)
            {
                char ch = (char)b;
                if (alt != null && b == alt[0])
                    ch = '+';
                else if (alt != null && b == alt[1])
                    ch = '/';
                else if (alt != null && (ch == '+' || ch == '/'))
                    ch = '\0';   // replaced by the alternative alphabet
                bool inAlphabet = (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '+' || ch == '/' || ch == '=';
                if (inAlphabet)
                    sb.Append(ch);
                else if (validate)
                    throw Raise.Make(c, PyExceptionTypes.BinasciiError, "Only base64 data is allowed");
            }
            try
            {
                return c.Values.Bytes(Convert.FromBase64String(sb.ToString()));
            }
            catch (FormatException)
            {
                throw Raise.Make(c, PyExceptionTypes.BinasciiError, "Invalid base64-encoded string");
            }
        }

        private static ScriptValue B16Encode(EvalContext c, ScriptValue[] a)
        {
            byte[] data = InBytes(c, a, "b16encode");
            return c.Values.Bytes(Hex.EncodeAscii(data, upper: true));
        }

        private static ScriptValue B16Decode(EvalContext c, ScriptValue[] a)
        {
            byte[] data = InBytes(c, a, "b16decode");
            if ((data.Length & 1) != 0)
                throw Raise.Make(c, PyExceptionTypes.BinasciiError, "Odd-length string");
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
            int v = Hex.Nibble(b);
            if (v >= 0)
                return v;
            throw Raise.Make(c, PyExceptionTypes.BinasciiError, "Non-base16 digit found");
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

        // b32decode(s, casefold=False, map01=None): lower case is always accepted; map01 reads 0 as O and 1 as
        // the given letter, as CPython does.
        private static ScriptValue B32Decode(EvalContext c, ScriptValue[] a)
        {
            byte[] data = InBytes(c, a, "b32decode");
            if (a.Length >= 3 && a[2].Kind != ValueKind.None)
            {
                byte[] map01 = InBytes(c, new[] { a[2] }, "b32decode");
                if (map01.Length != 1)
                    throw Raise.ValueError(c, "map01 must be a single character");
                data = (byte[])data.Clone();
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i] == (byte)'0')
                        data[i] = (byte)'O';
                    else if (data[i] == (byte)'1')
                        data[i] = map01[0];
                }
            }
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
                        throw Raise.Make(c, PyExceptionTypes.BinasciiError, "Non-base32 digit found");
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

using System;
using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
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

        // Accept bytes or an ASCII str. Every function here is linear in its input, so the input pays the
        // steps: b32encode of 9 MB forty times ran 13 s on 155 steps.
        private static byte[] InBytes(EvalContext c, ScriptValue[] a, string fn)
        {
            Args.AtLeast(c, a, fn, 1);
            BytesValue bv = a[0] as BytesValue;
            if (bv != null)
            {
                c.Budget.ChargeLinear(bv.Data.Length);
                return bv.Data;
            }
            StrValue sv = a[0] as StrValue;
            if (sv != null)
            {
                c.Budget.ChargeLinear(sv.Value.Length);
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

        // the encoders take bytes only, as binascii does
        private static byte[] EncodeInput(EvalContext c, ScriptValue[] a, string fn)
        {
            Args.AtLeast(c, a, fn, 1);
            if (a[0] is StrValue)
                throw Raise.TypeError(c, "a bytes-like object is required, not 'str'");
            return InBytes(c, a, fn);
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
            byte[] data = EncodeInput(c, a, fn);
            c.Values.CheckStrLen((data.Length + 2L) / 3 * 4);   // refused before it is built
            string s = Convert.ToBase64String(data);
            if (alt != null)
                s = s.Replace('+', (char)alt[0]).Replace('/', (char)alt[1]);
            return c.Values.Bytes(Ascii(s));
        }

        // binascii.a2b_base64, strict_mode=validate (3.11+). altchars are mapped to '+' and '/' first, which
        // stay valid themselves (bytes.translate). Without validate, characters outside the alphabet are
        // skipped and a full pad sequence ends the data.
        private static ScriptValue B64Decode(EvalContext c, ScriptValue[] a, string fn, byte[] fixedPair)
        {
            Args.Between(c, a, fn, 1, fixedPair == null && fn == "b64decode" ? 3 : 1);
            byte[] alt = AltChars(c, a, fn, fixedPair);
            bool strict = a.Length >= 3 && a[2].IsTruthy(c);
            byte[] data = InBytes(c, a, fn);
            var output = new List<byte>(data.Length / 4 * 3 + 3);
            if (strict && data.Length > 0 && data[0] == '=')
                throw Raise.Make(c, PyExceptionTypes.BinasciiError, "Leading padding not allowed");
            int quadPos = 0, leftChar = 0, pads = 0;
            bool paddingStarted = false;
            for (int i = 0; i < data.Length; i++)
            {
                byte ch = data[i];
                if (alt != null && ch == alt[0])
                    ch = (byte)'+';
                else if (alt != null && ch == alt[1])
                    ch = (byte)'/';
                if (ch == '=')
                {
                    paddingStarted = true;
                    if (strict && quadPos == 0)
                        throw Raise.Make(c, PyExceptionTypes.BinasciiError, "Excess padding not allowed");
                    if (quadPos >= 2 && quadPos + ++pads >= 4)
                    {
                        if (strict && i + 1 < data.Length)
                            throw Raise.Make(c, PyExceptionTypes.BinasciiError, "Excess data after padding");
                        return c.Values.Bytes(output.ToArray());
                    }
                    continue;
                }
                int v = B64Value(ch);
                if (v < 0)
                {
                    if (strict)
                        throw Raise.Make(c, PyExceptionTypes.BinasciiError, "Only base64 data is allowed");
                    continue;
                }
                if (strict && paddingStarted)
                    throw Raise.Make(c, PyExceptionTypes.BinasciiError, "Discontinuous padding not allowed");
                pads = 0;
                switch (quadPos)
                {
                    case 0:
                        quadPos = 1;
                        leftChar = v;
                        break;
                    case 1:
                        quadPos = 2;
                        output.Add((byte)((leftChar << 2) | (v >> 4)));
                        leftChar = v & 0x0f;
                        break;
                    case 2:
                        quadPos = 3;
                        output.Add((byte)((leftChar << 4) | (v >> 2)));
                        leftChar = v & 0x03;
                        break;
                    default:
                        quadPos = 0;
                        output.Add((byte)((leftChar << 6) | v));
                        break;
                }
            }
            if (quadPos == 1)
                throw Raise.Make(c, PyExceptionTypes.BinasciiError, "Invalid base64-encoded string: number of data characters ("
                    + (output.Count / 3 * 4 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ") cannot be 1 more than a multiple of 4");
            if (quadPos != 0)
                throw Raise.Make(c, PyExceptionTypes.BinasciiError, "Incorrect padding");
            return c.Values.Bytes(output.ToArray());
        }

        private static int B64Value(byte ch)
        {
            if (ch >= 'A' && ch <= 'Z')
                return ch - 'A';
            if (ch >= 'a' && ch <= 'z')
                return ch - 'a' + 26;
            if (ch >= '0' && ch <= '9')
                return ch - '0' + 52;
            return ch == '+' ? 62 : ch == '/' ? 63 : -1;
        }

        private static ScriptValue B16Encode(EvalContext c, ScriptValue[] a)
        {
            byte[] data = EncodeInput(c, a, "b16encode");
            c.Values.CheckStrLen(2L * data.Length);
            return c.Values.Bytes(Hex.EncodeAscii(data, upper: true));
        }

        private static ScriptValue B16Decode(EvalContext c, ScriptValue[] a)
        {
            byte[] data = InBytes(c, a, "b16decode");
            bool casefold = a.Length >= 2 && a[1] != null && a[1].IsTruthy(c);
            for (int i = 0; i < data.Length; i++)
            {
                bool upper = (data[i] >= (byte)'0' && data[i] <= (byte)'9') || (data[i] >= (byte)'A' && data[i] <= (byte)'F');
                if (!upper && !(casefold && data[i] >= (byte)'a' && data[i] <= (byte)'f'))
                    throw Raise.Make(c, PyExceptionTypes.BinasciiError, "Non-base16 digit found");   // lower case only with casefold
            }
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
            byte[] data = EncodeInput(c, a, "b32encode");
            long outLen = (data.Length + 4L) / 5 * 8;
            c.Values.CheckStrLen(outLen);   // refused before it is built
            var sb = new StringBuilder((int)outLen);
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

        // b32decode(s, casefold=False, map01=None), base64.py's: upper case unless casefold, whole quanta, padding counted.
        private static ScriptValue B32Decode(EvalContext c, ScriptValue[] a)
        {
            byte[] data = InBytes(c, a, "b32decode");
            if (data.Length % 8 != 0)
                throw Raise.Make(c, PyExceptionTypes.BinasciiError, "Incorrect padding");
            if (a.Length >= 3 && a[2].Kind != ValueKind.None)
            {
                byte[] map01 = InBytes(c, new[] { a[2] }, "b32decode");
                if (map01.Length != 1)
                    throw Raise.Make(c, PyExceptionTypes.AssertionError, a[2].Repr(c));
                data = (byte[])data.Clone();
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i] == (byte)'0')
                        data[i] = (byte)'O';
                    else if (data[i] == (byte)'1')
                        data[i] = map01[0];
                }
            }
            bool casefold = a.Length >= 2 && PyOps.Truth(a[1], c);
            int kept = data.Length;
            while (kept > 0 && data[kept - 1] == (byte)'=')
                kept--;
            int pad = data.Length - kept;
            var outB = new List<byte>((kept + 7) / 8 * 5);
            ulong acc = 0;
            for (int i = 0; i < kept; i += 8)
            {
                acc = 0;
                for (int k = i; k < kept && k < i + 8; k++)
                {
                    int ch = data[k];
                    if (casefold && ch >= 'a' && ch <= 'z')
                        ch -= 32;
                    int v = ch < 128 ? B32.IndexOf((char)ch) : -1;
                    if (v < 0)
                        throw Raise.Make(c, PyExceptionTypes.BinasciiError, "Non-base32 digit found");
                    acc = (acc << 5) + (uint)v;
                }
                for (int k = 32; k >= 0; k -= 8)
                    outB.Add((byte)(acc >> k));   // a short last quantum is cut below, or refused by its padding
            }
            if (pad != 0 && pad != 1 && pad != 3 && pad != 4 && pad != 6)
                throw Raise.Make(c, PyExceptionTypes.BinasciiError, "Incorrect padding");
            if (pad != 0 && outB.Count > 0)
            {
                acc <<= 5 * pad;
                outB.RemoveRange(outB.Count - 5, 5);
                int leftover = (43 - 5 * pad) / 8;
                for (int k = 0; k < leftover; k++)
                    outB.Add((byte)(acc >> (32 - k * 8)));
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

using System;
using System.Collections.Generic;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules
{
    // binascii (corpus-gated): hexlify/unhexlify (+ b2a_hex/a2b_hex aliases)
    // b2a_base64/a2b_base64, crc32, Error (<- ValueError). Rides on BytesValue and the BCL base64.
    public static class BinasciiModule
    {
        private static readonly uint[] Crc32Table = BuildCrc32Table();   // immutable static

        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["hexlify"] = BuiltinFunctionValue.Make("hexlify", (s, a, kw, c) => Hexlify(c, a));
            m["b2a_hex"] = BuiltinFunctionValue.Make("b2a_hex", (s, a, kw, c) => Hexlify(c, a));
            m["unhexlify"] = BuiltinFunctionValue.Make("unhexlify", (s, a, kw, c) => Unhexlify(c, a));
            m["a2b_hex"] = BuiltinFunctionValue.Make("a2b_hex", (s, a, kw, c) => Unhexlify(c, a));
            m["b2a_base64"] = BuiltinFunctionValue.Make("b2a_base64", B2aBase64);
            m["a2b_base64"] = BuiltinFunctionValue.Make("a2b_base64", (s, a, kw, c) => A2bBase64(c, a));
            m["crc32"] = BuiltinFunctionValue.Make("crc32", (s, a, kw, c) => Crc32(c, a));
            m["Error"] = ErrorType();
            return ctx.Values.Module("binascii", m);
        }

        private static TypeValue ErrorType()
        {
            BuiltinDelegate ctor = (self, args, kw, c) => c.Values.Exception(PyExceptionTypes.BinasciiError, args);
            return TypeValue.Make("Error", ctor, null, null, PyExceptionTypes.BinasciiError);
        }

        private static byte[] DataArg(EvalContext ctx, ScriptValue[] a, string name)
        {
            if (a.Length < 1)
                throw Raise.TypeError(ctx, name + "() missing required argument");
            BytesValue b = a[0] as BytesValue;
            if (b == null)
                throw Raise.TypeError(ctx, "argument should be bytes, not " + a[0].PyTypeName);
            return b.Data;
        }

        // a2b_* accept str OR bytes (ASCII text).
        private static string TextArg(EvalContext ctx, ScriptValue[] a, string name)
        {
            if (a.Length < 1)
                throw Raise.TypeError(ctx, name + "() missing required argument");
            StrValue s = a[0] as StrValue;
            if (s != null)
                return s.Value;
            BytesValue b = a[0] as BytesValue;
            if (b != null)
            {
                var sb = new System.Text.StringBuilder(b.Data.Length);
                foreach (byte x in b.Data)
                    sb.Append((char)x);
                return sb.ToString();
            }
            throw Raise.TypeError(ctx, "argument should be a bytes-like object or ASCII string, not " + a[0].PyTypeName);
        }

        private static ScriptValue Hexlify(EvalContext ctx, ScriptValue[] a)
        {
            byte[] data = DataArg(ctx, a, "hexlify");
            ctx.Values.PreCharge(24 + 2L * data.Length);
            var result = new byte[data.Length * 2];
            const string digits = "0123456789abcdef";
            for (int i = 0; i < data.Length; i++)
            {
                result[i * 2] = (byte)digits[data[i] >> 4];
                result[i * 2 + 1] = (byte)digits[data[i] & 0xF];
            }
            return ctx.Values.Bytes(result);
        }

        private static ScriptValue Unhexlify(EvalContext ctx, ScriptValue[] a)
        {
            string hex = TextArg(ctx, a, "unhexlify");
            if ((hex.Length & 1) != 0)
                throw Raise.Make(ctx, PyExceptionTypes.BinasciiError, "Odd-length string");
            ctx.Values.PreCharge(24 + hex.Length / 2);
            var result = new byte[hex.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                int hi = HexDigit(ctx, hex[i * 2]);
                int lo = HexDigit(ctx, hex[i * 2 + 1]);
                result[i] = (byte)((hi << 4) | lo);
            }
            return ctx.Values.Bytes(result);
        }

        private static int HexDigit(EvalContext ctx, char c)
        {
            if (c >= '0' && c <= '9')
                return c - '0';
            if (c >= 'a' && c <= 'f')
                return c - 'a' + 10;
            if (c >= 'A' && c <= 'F')
                return c - 'A' + 10;
            throw Raise.Make(ctx, PyExceptionTypes.BinasciiError, "Non-hexadecimal digit found");
        }

        private static ScriptValue B2aBase64(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            byte[] data = DataArg(ctx, a, "b2a_base64");
            bool newline = true;
            ScriptValue nv;
            if (kw.TryGet("newline", out nv))
                newline = nv.IsTruthy(ctx);
            string s = Convert.ToBase64String(data) + (newline ? "\n" : "");
            ctx.Values.PreCharge(24 + s.Length);
            var result = new byte[s.Length];
            for (int i = 0; i < s.Length; i++)
                result[i] = (byte)s[i];
            return ctx.Values.Bytes(result);
        }

        private static ScriptValue A2bBase64(EvalContext ctx, ScriptValue[] a)
        {
            string s = TextArg(ctx, a, "a2b_base64").Trim();
            try
            {
                return ctx.Values.Bytes(Convert.FromBase64String(s));
            }
            catch (FormatException)
            {
                throw Raise.Make(ctx, PyExceptionTypes.BinasciiError, "Invalid base64-encoded string");
            }
        }

        // crc32(data[, value]) -> unsigned 32-bit CRC-32 (IEEE 802.3). Shared with zlib/gzip.
        internal static ScriptValue Crc32(EvalContext ctx, ScriptValue[] a)
        {
            byte[] data = DataArg(ctx, a, "crc32");
            uint seed = a.Length >= 2 ? SeedArg(ctx, a[1], "crc32") : 0;
            return ctx.Values.Int(Crc32Raw(ctx, data, seed));
        }

        // A checksum seed: any int/bool, masked to 32 bits (CPython accepts arbitrary ints). A non-int
        // raises a script TypeError rather than an unchecked C# cast — shared by crc32/adler32 so a
        // hostile second argument never leaks past the host boundary as an engine fault.
        internal static uint SeedArg(EvalContext ctx, ScriptValue v, string fn)
        {
            System.Numerics.BigInteger n = Coerce.ToIndex(ctx, v, fn + "()");
            return (uint)(((n % 0x100000000) + 0x100000000) % 0x100000000);
        }

        // Slicing-by-8 (Intel, 2006): eight bytes per step through eight derived tables, four
        // independent lookups per half-word instead of one serial lookup per byte. Same polynomial,
        // same answers as the byte loop, which still finishes the tail. The tables are one flat array
        // held in a local for the loop, because a static readonly array is reloaded on every access
        // under the 4.x JIT. Measured 6.5 -> ~2 ns/byte on 8 MB.
        internal static uint Crc32Raw(EvalContext ctx, byte[] data, uint seed)
        {
            uint[] t = Crc32Tables;
            uint crc = ~seed;
            int i = 0, n = data.Length;
            for (; i + 8 <= n; i += 8)
            {
                if ((i & 1023) == 0)
                    ctx.Budget.Step();
                uint one = crc ^ (uint)(data[i] | data[i + 1] << 8 | data[i + 2] << 16 | data[i + 3] << 24);
                uint two = (uint)(data[i + 4] | data[i + 5] << 8 | data[i + 6] << 16 | data[i + 7] << 24);
                crc = t[7 * 256 + (one & 0xFF)] ^ t[6 * 256 + ((one >> 8) & 0xFF)]
                    ^ t[5 * 256 + ((one >> 16) & 0xFF)] ^ t[4 * 256 + (one >> 24)]
                    ^ t[3 * 256 + (two & 0xFF)] ^ t[2 * 256 + ((two >> 8) & 0xFF)]
                    ^ t[1 * 256 + ((two >> 16) & 0xFF)] ^ t[two >> 24];
            }
            for (; i < n; i++)
                crc = t[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return ~crc & 0xFFFFFFFF;
        }

        // Table k, entry i: the CRC of byte i followed by k zero bytes; the byte loop's table is k = 0.
        private static readonly uint[] Crc32Tables = BuildCrc32Tables();

        private static uint[] BuildCrc32Tables()
        {
            uint[] one = BuildCrc32Table();
            var t = new uint[8 * 256];
            Array.Copy(one, t, 256);
            for (int k = 1; k < 8; k++)
            {
                for (int i = 0; i < 256; i++)
                {
                    uint c = t[(k - 1) * 256 + i];
                    t[k * 256 + i] = (c >> 8) ^ one[c & 0xFF];
                }
            }
            return t;
        }

        private static uint[] BuildCrc32Table()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }
    }
}

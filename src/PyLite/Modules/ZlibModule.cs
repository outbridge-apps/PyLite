using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // zlib (integration payloads): one-shot compress/decompress + crc32/adler32. The container is
    // selected by wbits as in CPython: 9..15 zlib, -9..-15 raw deflate, 25..31 gzip, 40..47 auto
    // on decompress. compressobj/decompressobj (streaming) are not provided. Compressed bytes are
    // valid zlib streams but not byte-identical to CPython's; inflation pre-charges the
    // budget per chunk, so a decompression bomb dies as BudgetExceeded, not as host OOM.
    public static class ZlibModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["compress"] = BuiltinFunctionValue.Make("compress", (s, a, kw, c) => Compress(c, a, kw));
            m["decompress"] = BuiltinFunctionValue.Make("decompress", (s, a, kw, c) => Decompress(c, a, kw));
            m["crc32"] = BuiltinFunctionValue.Make("crc32", (s, a, kw, c) => BinasciiModule.Crc32(c, a));
            m["adler32"] = BuiltinFunctionValue.Make("adler32", (s, a, kw, c) => Adler32Fn(c, a));
            m["error"] = ErrorType();
            m["MAX_WBITS"] = ctx.Values.Int(15);
            m["DEFLATED"] = ctx.Values.Int(8);
            m["Z_DEFAULT_COMPRESSION"] = ctx.Values.Int(-1);
            m["Z_NO_COMPRESSION"] = ctx.Values.Int(0);
            m["Z_BEST_SPEED"] = ctx.Values.Int(1);
            m["Z_BEST_COMPRESSION"] = ctx.Values.Int(9);
            return ctx.Values.Module("zlib", m);
        }

        private static TypeValue ErrorType()
        {
            BuiltinDelegate ctor = (self, args, kw, c) => c.Values.Exception(PyExceptionTypes.ZlibError, args);
            return TypeValue.Make("error", ctor, null, null, PyExceptionTypes.ZlibError);
        }

        private static ScriptException Err(EvalContext ctx, string msg)
        {
            return Raise.Make(ctx, PyExceptionTypes.ZlibError, msg);
        }

        private static ScriptValue Compress(EvalContext c, ScriptValue[] a, KwArgs kw)
        {
            byte[] data = DataArg(c, a, "compress");
            int level = -1, wbits = 15;
            if (a.Length >= 2)
                level = IntArg(c, a[1]);
            if (a.Length >= 3)
                wbits = IntArg(c, a[2]);
            var r = new KwReader(c, kw, "compress");
            ScriptValue v;
            if (r.TryGet("level", out v))
                level = IntArg(c, v);
            if (r.TryGet("wbits", out v))
                wbits = IntArg(c, v);
            r.RejectInvalid();
            if (level < -1 || level > 9)
                throw Err(c, "Bad compression level");

            byte[] body = DeflateRaw(c, data, level);
            if (wbits >= -15 && wbits <= -9)
                return c.Values.Bytes(body);
            if (wbits >= 9 && wbits <= 15)
                return c.Values.Bytes(ZlibWrap(c, body, data, level));
            if (wbits >= 25 && wbits <= 31)
                return c.Values.Bytes(GzipModule.GzipWrap(c, body, data, 0, level));
            throw Err(c, "Invalid initialization option");
        }

        private static ScriptValue Decompress(EvalContext c, ScriptValue[] a, KwArgs kw)
        {
            byte[] data = DataArg(c, a, "decompress");
            int wbits = 15;
            if (a.Length >= 2)
                wbits = IntArg(c, a[1]);
            var r = new KwReader(c, kw, "decompress");
            ScriptValue v;
            if (r.TryGet("wbits", out v))
                wbits = IntArg(c, v);
            r.Accept("bufsize");   // only a hint in CPython too
            r.RejectInvalid();
            if (a.Length >= 3)   // positional bufsize — accepted, ignored
            {
                IntArg(c, a[2]);
            }

            if (wbits >= -15 && wbits <= -9)
                return c.Values.Bytes(InflateRaw(c, data, 0, data.Length));
            if (wbits == 0 || (wbits >= 8 && wbits <= 15))
                return c.Values.Bytes(ZlibUnwrap(c, data));
            if (wbits >= 24 && wbits <= 31)
                return GzipModule.Decompress(c, data);
            if (wbits >= 40 && wbits <= 47)
            {
                return data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B
                    ? GzipModule.Decompress(c, data)
                    : c.Values.Bytes(ZlibUnwrap(c, data));
            }
            throw Err(c, "Invalid initialization option");
        }

        // ---- shared deflate core (also used by gzip) ----

        // .NET DeflateStream has three levels; map CPython's 0..9: 0 stored, 1-5 fastest, 6-9/-1 optimal.
        internal static byte[] DeflateRaw(EvalContext c, byte[] data, int level)
        {
            CompressionLevel lvl = level == 0
                ? CompressionLevel.NoCompression
                : (level >= 1 && level <= 5 ? CompressionLevel.Fastest : CompressionLevel.Optimal);
            using (var outMs = new MemoryStream())
            {
                using (var ds = new DeflateStream(outMs, lvl, true))
                {
                    const int Chunk = 1 << 16;
                    for (int off = 0; off < data.Length; off += Chunk)
                    {
                        c.Budget.CheckDeadlineNow();
                        ds.Write(data, off, Math.Min(Chunk, data.Length - off));
                    }
                }
                return outMs.ToArray();
            }
        }

        // Chunked inflate with a per-chunk PreCharge: the logical charge lands BEFORE the
        // output grows, so a bomb aborts on MaxAllocBytes without materializing the payload. The
        // final BytesValue charges again — the double charge is deliberate (conservative direction).
        internal static byte[] InflateRaw(EvalContext c, byte[] input, int offset, int count)
        {
            using (var inMs = new MemoryStream(input, offset, count, false))
            using (var ds = new DeflateStream(inMs, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                var buf = new byte[1 << 16];
                while (true)
                {
                    c.Budget.CheckDeadlineNow();
                    int n;
                    try
                    {
                        n = ds.Read(buf, 0, buf.Length);
                    }
                    catch (InvalidDataException)
                    {
                        throw Err(c, "invalid or truncated compressed data");
                    }
                    if (n <= 0)
                        break;
                    c.Budget.ChargeAllocation(n);
                    if (outMs.Length + n > c.Limits.MaxStrChars)
                        throw c.Budget.CreateAbort(EngineAbortKind.Memory, "MaxStrChars", c.Limits.MaxStrChars, outMs.Length + n);
                    outMs.Write(buf, 0, n);
                }
                return outMs.ToArray();
            }
        }

        // ---- zlib container ----

        private static byte[] ZlibWrap(EvalContext c, byte[] body, byte[] original, int level)
        {
            int flevel = level >= 0 && level <= 1 ? 0 : (level >= 2 && level <= 5 ? 1 : (level == 9 ? 3 : 2));
            int cmf = 0x78;   // deflate, 32K window
            int flg = flevel << 6;
            int rem = ((cmf << 8) | flg) % 31;
            if (rem != 0)
                flg += 31 - rem;
            uint adler = Adler32Raw(c, original, 1);
            var outB = new byte[body.Length + 6];
            outB[0] = (byte)cmf;
            outB[1] = (byte)flg;
            Buffer.BlockCopy(body, 0, outB, 2, body.Length);
            WriteBE32(outB, outB.Length - 4, adler);
            return outB;
        }

        private static byte[] ZlibUnwrap(EvalContext c, byte[] data)
        {
            if (data.Length < 6)
                throw Err(c, "incomplete or truncated stream");
            byte cmf = data[0], flg = data[1];
            if ((cmf & 0x0F) != 8 || (((cmf << 8) | flg) % 31) != 0)
                throw Err(c, "incorrect header check");
            if ((flg & 0x20) != 0)
                throw Err(c, "preset dictionaries are not supported");
            byte[] outp = InflateRaw(c, data, 2, data.Length - 6);
            uint expected = ReadBE32(data, data.Length - 4);
            if (Adler32Raw(c, outp, 1) != expected)
                throw Err(c, "incorrect data check");
            return outp;
        }

        // ---- adler32 ----

        private static ScriptValue Adler32Fn(EvalContext c, ScriptValue[] a)
        {
            byte[] data = DataArg(c, a, "adler32");
            uint seed = a.Length >= 2 ? BinasciiModule.SeedArg(c, a[1], "adler32") : 1;
            return c.Values.Int(Adler32Raw(c, data, seed));
        }

        private static uint Adler32Raw(EvalContext c, byte[] data, uint seed)
        {
            const int NMax = 5552;   // max bytes before the uint sums must be reduced mod 65521
            uint s1 = seed & 0xFFFF, s2 = (seed >> 16) & 0xFFFF;
            // One loop bounded by data.Length itself, so the JIT drops the per-byte bounds check; the
            // reduction runs off a counter instead of an inner loop with a computed end (which kept the
            // check and cost 3x). The same Step per NMax bytes as before.
            int n = 0;
            for (int i = 0; i < data.Length; i++)
            {
                s1 += data[i];
                s2 += s1;
                if (++n == NMax)
                {
                    s1 %= 65521;
                    s2 %= 65521;
                    n = 0;
                    c.Budget.Step();
                }
            }
            s1 %= 65521;
            s2 %= 65521;
            return (s2 << 16) | s1;
        }

        // ---- small shared helpers ----

        internal static byte[] DataArg(EvalContext c, ScriptValue[] a, string name)
        {
            if (a.Length < 1)
                throw Raise.TypeError(c, name + "() missing required argument");
            BytesValue b = a[0] as BytesValue;
            if (b == null)
                throw Raise.TypeError(c, "a bytes-like object is required, not '" + a[0].PyTypeName + "'");
            return b.Data;
        }

        internal static int IntArg(EvalContext c, ScriptValue v)
        {
            System.Numerics.BigInteger b = Coerce.ToIndex(c, v, "an integer is required");
            if (b > int.MaxValue)
                return int.MaxValue;
            if (b < int.MinValue)
                return int.MinValue;
            return (int)b;
        }

        internal static void WriteBE32(byte[] buf, int off, uint v)
        {
            buf[off] = (byte)(v >> 24);
            buf[off + 1] = (byte)(v >> 16);
            buf[off + 2] = (byte)(v >> 8);
            buf[off + 3] = (byte)v;
        }

        internal static uint ReadBE32(byte[] buf, int off)
        {
            return ((uint)buf[off] << 24) | ((uint)buf[off + 1] << 16) | ((uint)buf[off + 2] << 8) | buf[off + 3];
        }

        internal static void WriteLE32(byte[] buf, int off, uint v)
        {
            buf[off] = (byte)v;
            buf[off + 1] = (byte)(v >> 8);
            buf[off + 2] = (byte)(v >> 16);
            buf[off + 3] = (byte)(v >> 24);
        }

        internal static uint ReadLE32(byte[] buf, int off)
        {
            return buf[off] | ((uint)buf[off + 1] << 8) | ((uint)buf[off + 2] << 16) | ((uint)buf[off + 3] << 24);
        }
    }
}

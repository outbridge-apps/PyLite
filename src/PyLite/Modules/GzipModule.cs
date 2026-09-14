using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // gzip (integration payloads): one-shot compress/decompress over the shared zlib deflate core.
    // No GzipFile/open (no filesystem in the sandbox). mtime defaults to 0 — deterministic output,
    // unlike CPython's wall-clock default. Multi-member streams are concatenated, with
    // the member boundaries found by trailer validation (see InflateMember).
    public static class GzipModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["compress"] = BuiltinFunctionValue.Make("compress", (s, a, kw, c) => Compress(c, a, kw));
            m["decompress"] = BuiltinFunctionValue.Make("decompress",
                (s, a, kw, c) => Decompress(c, ZlibModule.DataArg(c, a, "decompress")));
            m["BadGzipFile"] = BadGzipType();
            return ctx.Values.Module("gzip", m);
        }

        private static TypeValue BadGzipType()
        {
            BuiltinDelegate ctor = (self, args, kw, c) => c.Values.Exception(PyExceptionTypes.BadGzipFile, args);
            return TypeValue.Make("BadGzipFile", ctor, null, null, PyExceptionTypes.BadGzipFile);
        }

        private static ScriptException BadGzip(EvalContext ctx, string msg)
        {
            return Raise.Make(ctx, PyExceptionTypes.BadGzipFile, msg);
        }

        private static ScriptValue Compress(EvalContext c, ScriptValue[] a, KwArgs kw)
        {
            byte[] data = ZlibModule.DataArg(c, a, "compress");
            int level = 9;
            uint mtime = 0;
            Args.AtMost(c, a, "compress", 2);
            if (a.Length >= 2)
                level = ZlibModule.IntArg(c, a[1]);
            var r = new KwReader(c, kw, "compress");
            ScriptValue v;
            if (r.TryGet("compresslevel", out v))
                level = ZlibModule.IntArg(c, v);
            if (r.TryGet("mtime", out v))
                mtime = MtimeArg(c, v);
            r.RejectInvalid();
            if (level < 0 || level > 9)
                throw Raise.ValueError(c, "Bad compression level");
            byte[] body = ZlibModule.DeflateRaw(c, data, level);
            return c.Values.Bytes(GzipWrap(c, body, data, mtime, level));
        }

        private static uint MtimeArg(EvalContext c, ScriptValue v)
        {
            if (v.Kind == ValueKind.None)
                return 0;
            FloatValue f = v as FloatValue;
            if (f != null)
                return (uint)Math.Max(0, (long)f.Value);
            return BinasciiModule.SeedArg(c, v, "compress");   // int/bool masked; non-int -> script TypeError
        }

        // header(10) + raw deflate + crc32(LE) + isize(LE). XFL mirrors CPython (2 best / 4 fastest).
        internal static byte[] GzipWrap(EvalContext c, byte[] body, byte[] original, uint mtime, int level)
        {
            var outB = new byte[10 + body.Length + 8];
            outB[0] = 0x1F;
            outB[1] = 0x8B;
            outB[2] = 8;      // deflate
            outB[3] = 0;      // no flags
            ZlibModule.WriteLE32(outB, 4, mtime);
            outB[8] = level == 9 ? (byte)2 : (level == 1 ? (byte)4 : (byte)0);
            outB[9] = 255;    // OS unknown
            Buffer.BlockCopy(body, 0, outB, 10, body.Length);
            ZlibModule.WriteLE32(outB, 10 + body.Length, BinasciiModule.Crc32Raw(c, original, 0));
            ZlibModule.WriteLE32(outB, 14 + body.Length, (uint)original.Length);
            return outB;
        }

        // Shared with zlib.decompress(wbits=16+/32+). Members are concatenated, as in CPython.
        internal static ScriptValue Decompress(EvalContext c, byte[] data)
        {
            List<byte[]> members = null;
            byte[] first = null;
            int offset = 0;
            while (offset < data.Length)
            {
                int pos = ParseHeader(c, data, offset);
                if (data.Length - pos < 8)
                    throw BadGzip(c, "Compressed file ended before the end-of-stream marker was reached");
                int end;
                byte[] outp = InflateMember(c, data, pos, out end);
                offset = end;
                if (first == null)
                {
                    first = outp;
                    continue;
                }
                if (members == null)
                    members = new List<byte[]> { first };
                members.Add(outp);
            }
            if (members == null)
                return c.Values.Bytes(first ?? Array.Empty<byte>());

            long total = 0;
            for (int i = 0; i < members.Count; i++)
                total += members[i].Length;
            if (total > c.Limits.MaxStrChars)
                throw c.Budget.CreateAbort(EngineAbortKind.Memory, "MaxStrChars", c.Limits.MaxStrChars, total);
            var joined = new byte[total];
            int at = 0;
            for (int i = 0; i < members.Count; i++)
            {
                Buffer.BlockCopy(members[i], 0, joined, at, members[i].Length);
                at += members[i].Length;
            }
            return c.Values.Bytes(joined);
        }

        // One member's payload, with `end` set past its 8-byte trailer. DeflateStream cannot report how
        // many input bytes it consumed (it reads ahead in chunks), so the boundary is found by validating
        // trailers: every position where a NEXT member's magic could begin is a candidate, plus the end
        // of the data, and the first candidate whose crc32 AND isize match the inflated output wins.
        // The end-of-data candidate is tried last and is the authoritative single-member reading, so its
        // failure is the one reported.
        private static byte[] InflateMember(EvalContext c, byte[] data, int bodyStart, out int end)
        {
            for (int k = bodyStart + 8; k <= data.Length; k++)
            {
                bool last = k == data.Length;
                if (!last && !(k + 2 < data.Length && data[k] == 0x1F && data[k + 1] == 0x8B && data[k + 2] == 8))
                    continue;
                byte[] outp;
                try
                {
                    outp = ZlibModule.InflateRaw(c, data, bodyStart, k - 8 - bodyStart);
                }
                catch (ScriptException)
                {
                    if (last)
                        throw;
                    continue;
                }
                if (BinasciiModule.Crc32Raw(c, outp, 0) != ZlibModule.ReadLE32(data, k - 8))
                {
                    if (last)
                        throw BadGzip(c, "CRC check failed");
                    continue;
                }
                if ((uint)outp.Length != ZlibModule.ReadLE32(data, k - 4))
                {
                    if (last)
                        throw BadGzip(c, "Incorrect length of data produced");
                    continue;
                }
                end = k;
                return outp;
            }
            throw BadGzip(c, "CRC check failed");   // unreachable: the end-of-data candidate always throws
        }

        // RFC 1952 header: fixed 10 bytes, then optional FEXTRA/FNAME/FCOMMENT/FHCRC by FLG.
        private static int ParseHeader(EvalContext c, byte[] d, int start)
        {
            if (d.Length - start < 10 || d[start] != 0x1F || d[start + 1] != 0x8B)
                throw BadGzip(c, "Not a gzipped file");
            if (d[start + 2] != 8)
                throw BadGzip(c, "Unknown compression method");
            byte flg = d[start + 3];
            int pos = start + 10;
            if ((flg & 4) != 0)   // FEXTRA
            {
                if (pos + 2 > d.Length)
                    throw BadGzip(c, "Compressed file ended before the end-of-stream marker was reached");
                int xlen = d[pos] | (d[pos + 1] << 8);
                pos += 2 + xlen;
            }
            if ((flg & 8) != 0)   // FNAME
                pos = SkipZeroTerminated(c, d, pos);
            if ((flg & 16) != 0)  // FCOMMENT
                pos = SkipZeroTerminated(c, d, pos);
            if ((flg & 2) != 0)   // FHCRC
                pos += 2;
            if (pos > d.Length)
                throw BadGzip(c, "Compressed file ended before the end-of-stream marker was reached");
            return pos;
        }

        private static int SkipZeroTerminated(EvalContext c, byte[] d, int pos)
        {
            while (pos < d.Length && d[pos] != 0)
                pos++;
            if (pos >= d.Length)
                throw BadGzip(c, "Compressed file ended before the end-of-stream marker was reached");
            return pos + 1;
        }
    }
}

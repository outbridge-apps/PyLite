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
    internal static class GzipModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["compress"] = BuiltinFunctionValue.Make("compress", (s, a, kw, c) => Compress(c, a, kw));
            m["decompress"] = BuiltinFunctionValue.Make("decompress",
                Args.BoundKeywords("decompress", 1, (s, a, kw, c) => Decompress(c, ZlibModule.DataArg(c, a, "decompress")), "data"));
            m["BadGzipFile"] = ModuleKit.ExceptionClass(PyExceptionTypes.BadGzipFile);
            return ctx.Values.Module("gzip", m);
        }

        private static ScriptException BadGzip(EvalContext ctx, string msg)
        {
            return Raise.Make(ctx, PyExceptionTypes.BadGzipFile, msg);
        }

        // CPython's _read_exact: a member cut short is an EOFError, not a BadGzipFile
        private static ScriptException Truncated(EvalContext ctx)
        {
            return Raise.Make(ctx, PyExceptionTypes.EOFError, "Compressed file ended before the end-of-stream marker was reached");
        }

        private static ScriptValue Compress(EvalContext c, ScriptValue[] a, KwArgs kw)
        {
            byte[] data = ZlibModule.DataArg(c, a, "compress");
            int level = 9;
            Args.AtMost(c, a, "compress", 2);
            if (a.Length >= 2)
                level = ZlibModule.IntArg(c, a[1]);
            var r = new KwReader(c, kw, "compress");
            ScriptValue v, mtimeArg;
            if (r.TryGet("compresslevel", out v))
                level = ZlibModule.IntArg(c, v);
            bool timed = r.TryGet("mtime", out mtimeArg);
            r.RejectInvalid();
            if (level < 0 || level > 9)
                throw Raise.ValueError(c, "Bad compression level");
            // read after the level, as CPython packs the header after zlib has compressed
            uint mtime = timed ? MtimeArg(c, mtimeArg) : 0;
            byte[] body = ZlibModule.DeflateRaw(c, data, level);
            return c.Values.Bytes(GzipWrap(c, body, data, mtime, level));
        }

        private static uint MtimeArg(EvalContext c, ScriptValue v)
        {
            // mtime=None is 0 too, where CPython writes the time now (docs/deviations.md). CPython packs int(mtime)
            // as 'L': out of range is struct.error, not a wrapped value
            System.Numerics.BigInteger n = v.Kind == ValueKind.None ? System.Numerics.BigInteger.Zero : Builtins.IntOf(c, v);
            if (n < 0 || n > uint.MaxValue)
                throw Raise.Make(c, PyExceptionTypes.StructError, "'L' format requires 0 <= number <= 4294967295");
            return (uint)n;
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
        // firstOnly: zlib.decompress with a gzip wbits reads one member and ignores what follows it.
        internal static ScriptValue Decompress(EvalContext c, byte[] data, bool firstOnly = false)
        {
            List<byte[]> members = null;
            byte[] first = null;
            int offset = 0;
            while (offset < data.Length)
            {
                if (first != null && firstOnly)
                    break;
                if (first != null)
                {
                    // zero padding after a member is skipped, as CPython's _read_eof does
                    while (offset < data.Length && data[offset] == 0)
                        offset++;
                    if (offset == data.Length)
                        break;
                }
                int pos = ParseHeader(c, data, offset);
                if (data.Length - pos < 8)
                    throw Truncated(c);
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
                uint crc = BinasciiModule.Crc32Raw(c, outp, 0);
                if (crc != ZlibModule.ReadLE32(data, k - 8) || (uint)outp.Length != ZlibModule.ReadLE32(data, k - 4))
                {
                    if (!last)
                        continue;
                    // Bytes after the last member (zero padding, junk): the stream ends where it ends, so the
                    // output is the same; its trailer is the first 8 bytes that match it. None: the old errors.
                    int t = FirstTrailer(data, bodyStart + 8, k, crc, (uint)outp.Length);
                    if (t > 0)
                    {
                        end = t;
                        return outp;
                    }
                    if (crc != ZlibModule.ReadLE32(data, k - 8))
                        throw BadGzip(c, "CRC check failed");
                    throw BadGzip(c, "Incorrect length of data produced");
                }
                end = k;
                return outp;
            }
            throw BadGzip(c, "CRC check failed");   // unreachable: the end-of-data candidate always throws
        }

        private static int FirstTrailer(byte[] data, int from, int to, uint crc, uint size)
        {
            for (int k = from; k < to; k++)
            {
                if (ZlibModule.ReadLE32(data, k - 8) == crc && ZlibModule.ReadLE32(data, k - 4) == size)
                    return k;
            }
            return 0;
        }

        private static byte[] Slice(byte[] d, int start, int max)
        {
            int n = Math.Min(max, d.Length - start);
            var r = new byte[n];
            Buffer.BlockCopy(d, start, r, 0, n);
            return r;
        }

        // RFC 1952 header: fixed 10 bytes, then optional FEXTRA/FNAME/FCOMMENT/FHCRC by FLG.
        private static int ParseHeader(EvalContext c, byte[] d, int start)
        {
            if (d.Length - start < 2 || d[start] != 0x1F || d[start + 1] != 0x8B)
                throw BadGzip(c, "Not a gzipped file (" + c.Values.Bytes(Slice(d, start, 2)).Repr(c) + ")");
            if (d.Length - start < 10)
                throw Truncated(c);
            if (d[start + 2] != 8)
                throw BadGzip(c, "Unknown compression method");
            byte flg = d[start + 3];
            int pos = start + 10;
            if ((flg & 4) != 0)   // FEXTRA
            {
                if (pos + 2 > d.Length)
                    throw Truncated(c);
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
                throw Truncated(c);
            return pos;
        }

        private static int SkipZeroTerminated(EvalContext c, byte[] d, int pos)
        {
            while (pos < d.Length && d[pos] != 0)
                pos++;
            if (pos >= d.Length)
                throw Truncated(c);
            return pos + 1;
        }
    }
}

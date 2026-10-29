using System;
using System.Collections.Generic;
using System.Text;
using Outbridge.PyLite.Modules;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // zipfile.ZipFile over an io.BytesIO, following CPython's zipfile.py record by record: the end record
    // found from the back (ZIP64 included), the central directory, local headers checked against it,
    // members written at the directory's place and the directory rewritten on close.
    //
    // The archive is never trusted for an allocation: a member is inflated in charged chunks and stops at
    // its recorded size, the entry count is capped by MaxCollectionItems, every directory entry costs a
    // step, and a member's CRC is checked when it is read to the end.
    internal sealed class ZipFileValue : ScriptValue, IContextManager
    {
        private static readonly ScriptTypeInfo SType = new ScriptTypeInfo("zipfile.ZipFile", BuildSlots());
        internal static ScriptTypeInfo ZipFileType { get { return SType; } }

        internal const int Stored = 0, Deflated = 8, Bzip2 = 12, Lzma = 14;
        private const int SizeEndCentDir = 22, SizeCentralDir = 46, SizeFileHeader = 30;
        private const int SizeEndCentDir64 = 56, SizeEndCentDir64Locator = 20;
        private const uint SigEndArchive = 0x06054b50, SigCentralDir = 0x02014b50, SigFileHeader = 0x04034b50;
        private const uint SigEndArchive64 = 0x06064b50, SigEndArchive64Locator = 0x07064b50;
        private const long Zip64Limit = (1L << 31) - 1;
        private const int FileCountLimit = (1 << 16) - 1;
        private const int MaxExtractVersion = 63;
        private const int MaxComment = (1 << 16) - 1;

        private BytesIOValue _fp;   // null once closed
        private readonly char _mode;
        private readonly int _compression;
        private readonly ScriptValue _compressLevel;
        private readonly bool _allowZip64;
        private readonly string _metadataEncoding;
        private readonly List<ZipInfoValue> _files = new List<ZipInfoValue>();
        private readonly Dictionary<string, ZipInfoValue> _byName = new Dictionary<string, ZipInfoValue>(StringComparer.Ordinal);
        private byte[] _comment = Array.Empty<byte>();
        private bool _didModify;
        private long _startDir;
        private bool _writing;   // an open(name, 'w') handle is outstanding
        private byte[] _pwd;

        internal ZipFileValue(EvalContext ctx, BytesIOValue fp, char mode, int compression, ScriptValue compressLevel,
            bool allowZip64, string metadataEncoding)
        {
            ctx.Values.PreCharge(256);
            fp.CheckOpen(ctx);
            _fp = fp;
            _mode = mode;
            _compression = compression;
            _compressLevel = compressLevel;
            _allowZip64 = allowZip64;
            _metadataEncoding = metadataEncoding;
            switch (mode)
            {
                case 'r':
                    ReadContents(ctx);
                    break;
                case 'w':
                case 'x':
                    _didModify = true;
                    _startDir = fp.Position;
                    break;
                default:   // 'a'
                    try
                    {
                        ReadContents(ctx);
                        fp.Position = _startDir;
                    }
                    catch (ScriptException se) when (se.Value.ExcType == PyExceptionTypes.BadZipFile)
                    {
                        fp.Position = fp.Length;   // not an archive: a new one starts at the end
                        _didModify = true;
                        _startDir = fp.Position;
                    }
                    break;
            }
        }

        public override ScriptTypeInfo TypeInfo { get { return SType; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        public ScriptValue Enter(EvalContext ctx)
        {
            return this;
        }

        public bool Exit(EvalContext ctx, ExceptionValue pending)
        {
            Close(ctx);
            return false;
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append(_fp == null ? "<zipfile.ZipFile [closed]>" : "<zipfile.ZipFile file=<io.BytesIO> mode='" + _mode + "'>");
        }

        // ---------- reading the directory ----------

        internal static ScriptException Bad(EvalContext ctx, string msg)
        {
            return Raise.Make(ctx, PyExceptionTypes.BadZipFile, msg);
        }

        private static uint U16(byte[] b, long at) { return (uint)(b[at] | b[at + 1] << 8); }
        private static uint U32(byte[] b, long at) { return (uint)(b[at] | b[at + 1] << 8 | b[at + 2] << 16 | b[at + 3] << 24); }
        private static long U64(byte[] b, long at) { return (long)((ulong)U32(b, at) | (ulong)U32(b, at + 4) << 32); }

        // The end record: size (0..), offset, entries, comment, its own position, whether ZIP64 records
        // precede it. Null when there is none (CPython's _EndRecData).
        private sealed class EndRecord
        {
            internal long Size, Offset, Entries, Location;
            internal byte[] Comment;
            internal bool Zip64;
        }

        private static EndRecord FindEndRecord(EvalContext ctx, byte[] b, int len)
        {
            if (len < SizeEndCentDir)
                return null;
            int at = -1;
            int fast = len - SizeEndCentDir;
            if (U32(b, fast) == SigEndArchive && b[len - 2] == 0 && b[len - 1] == 0)
            {
                at = fast;
            }
            else
            {
                // An archive comment follows the record; it is at most 64 KB, so search only that far back.
                int lowest = Math.Max(len - (1 << 16) - SizeEndCentDir, 0);
                ctx.Budget.ChargeLinear(len - lowest);
                for (int i = len - 4; i >= lowest; i--)
                {
                    if (b[i] == 0x50 && b[i + 1] == 0x4b && b[i + 2] == 0x05 && b[i + 3] == 0x06)
                    {
                        at = i;
                        break;
                    }
                }
                if (at < 0 || at + SizeEndCentDir > len)
                    return null;
            }
            var r = new EndRecord
            {
                Entries = U16(b, at + 10),
                Size = U32(b, at + 12),
                Offset = U32(b, at + 16),
                Location = at,
            };
            int commentLen = (int)Math.Min(U16(b, at + 20), (uint)(len - at - SizeEndCentDir));
            r.Comment = new byte[commentLen];
            Buffer.BlockCopy(b, at + SizeEndCentDir, r.Comment, 0, commentLen);

            int loc = at - SizeEndCentDir64Locator;
            if (loc >= 0 && U32(b, loc) == SigEndArchive64Locator)
            {
                uint diskNo = U32(b, loc + 4), disks = U32(b, loc + 16);
                if (diskNo != 0 || disks > 1)
                    throw Bad(ctx, "zipfiles that span multiple disks are not supported");
                int rec = loc - SizeEndCentDir64;
                if (rec >= 0 && U32(b, rec) == SigEndArchive64)
                {
                    r.Entries = U64(b, rec + 32);
                    r.Size = U64(b, rec + 40);
                    r.Offset = U64(b, rec + 48);
                    r.Zip64 = true;
                }
            }
            return r;
        }

        private void ReadContents(EvalContext ctx)
        {
            byte[] b = _fp.RawBuffer;
            int len = _fp.Length;
            EndRecord end = FindEndRecord(ctx, b, len);
            if (end == null)
                throw Bad(ctx, "File is not a zip file");
            _comment = end.Comment;
            // "concat": bytes in front of the archive (a self-extractor stub), zero otherwise.
            long concat = end.Location - end.Size - end.Offset;
            if (end.Zip64)
                concat -= SizeEndCentDir64 + SizeEndCentDir64Locator;
            _startDir = end.Offset + concat;
            if (_startDir < 0 || end.Size < 0)
                throw Bad(ctx, "Bad offset for central directory");
            long cdEnd = Math.Min(_startDir + end.Size, (long)len);
            long p = _startDir;
            while (p - _startDir < end.Size)
            {
                ctx.Budget.Step();
                if (p + SizeCentralDir > cdEnd)
                    throw Bad(ctx, "Truncated central directory");
                if (U32(b, p) != SigCentralDir)
                    throw Bad(ctx, "Bad magic number for central directory");
                int nameLen = (int)U16(b, p + 28), extraLen = (int)U16(b, p + 30), commentLen = (int)U16(b, p + 32);
                if (p + SizeCentralDir + nameLen + extraLen + commentLen > cdEnd)
                    throw Bad(ctx, "Truncated central directory");
                if (_files.Count >= ctx.Limits.MaxCollectionItems)
                    throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxCollectionItems", ctx.Limits.MaxCollectionItems, _files.Count + 1L);
                int flags = (int)U16(b, p + 8);
                byte[] rawName = Slice(b, p + SizeCentralDir, nameLen);
                var x = new ZipInfoValue(ctx, DecodeName(ctx, rawName, flags), ZipInfoValue.DefaultDateTime());
                ctx.Values.PreCharge(extraLen + commentLen);
                x.Extra = Slice(b, p + SizeCentralDir + nameLen, extraLen);
                x.Comment = Slice(b, p + SizeCentralDir + nameLen + extraLen, commentLen);
                x.CreateVersion = b[p + 4];
                x.CreateSystem = b[p + 5];
                x.ExtractVersion = b[p + 6];
                x.Reserved = b[p + 7];
                x.FlagBits = flags;
                x.CompressType = (int)U16(b, p + 10);
                int t = (int)U16(b, p + 12), d = (int)U16(b, p + 14);
                x.Crc = U32(b, p + 16);
                x.CompressSize = U32(b, p + 20);
                x.FileSize = U32(b, p + 24);
                x.Volume = (int)U16(b, p + 34);
                x.InternalAttr = (int)U16(b, p + 36);
                x.ExternalAttr = U32(b, p + 38);
                x.HeaderOffset = U32(b, p + 42);
                if (x.ExtractVersion > MaxExtractVersion)
                    throw Raise.NotImplemented(ctx, "zip file version " + (x.ExtractVersion / 10.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                x.DateTime = new[] { (d >> 9) + 1980, (d >> 5) & 0xF, d & 0x1F, t >> 11, (t >> 5) & 0x3F, (t & 0x1F) * 2 };
                DecodeExtra(ctx, x, rawName);
                x.HeaderOffset += concat;
                _files.Add(x);
                _byName[x.Filename] = x;
                p += SizeCentralDir + nameLen + extraLen + commentLen;
            }
            MarkEndOffsets();
        }

        // CPython 3.12's overlap guard: each member ends where the next one (by offset) or the directory
        // begins, so a "zip bomb" whose entries share one compressed stream is refused when read.
        private void MarkEndOffsets()
        {
            var order = new int[_files.Count];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;
            Array.Sort(order, (x, y) =>
            {
                int c = _files[y].HeaderOffset.CompareTo(_files[x].HeaderOffset);
                return c != 0 ? c : x.CompareTo(y);
            });
            long end = _startDir;
            foreach (int i in order)
            {
                _files[i].EndOffset = end;
                end = _files[i].HeaderOffset;
            }
        }

        private static byte[] Slice(byte[] b, long at, int count)
        {
            if (count == 0)
                return Array.Empty<byte>();
            var r = new byte[count];
            Buffer.BlockCopy(b, (int)at, r, 0, count);
            return r;
        }

        private string DecodeName(EvalContext ctx, byte[] raw, int flags)
        {
            if ((flags & 0x800) != 0)
                return Encoding.UTF8.GetString(raw);
            if (_metadataEncoding != null)
                return BytesCodec.Decode(ctx, raw, _metadataEncoding, "strict");
            return Cp437.Decode(raw);
        }

        // The ZIP64 extended field (0x0001) carries the sizes and the offset that did not fit, in that
        // order; Info-ZIP's Unicode path (0x7075) replaces the name when its CRC matches the raw one.
        private static void DecodeExtra(EvalContext ctx, ZipInfoValue x, byte[] rawName)
        {
            byte[] e = x.Extra;
            int i = 0;
            while (i + 4 <= e.Length)
            {
                int tp = (int)U16(e, i), ln = (int)U16(e, i + 2);
                if (i + 4 + ln > e.Length)
                    throw Bad(ctx, "Corrupt extra field " + tp.ToString("x4") + " (size=" + ln + ")");
                int at = i + 4;
                if (tp == 0x0001)
                {
                    int end = at + ln;
                    if (x.FileSize == 0xFFFFFFFF)
                    {
                        if (at + 8 > end)
                            throw Bad(ctx, "Corrupt zip64 extra field. File size not found.");
                        x.FileSize = U64(e, at);
                        at += 8;
                    }
                    if (x.CompressSize == 0xFFFFFFFF)
                    {
                        if (at + 8 > end)
                            throw Bad(ctx, "Corrupt zip64 extra field. Compress size not found.");
                        x.CompressSize = U64(e, at);
                        at += 8;
                    }
                    if (x.HeaderOffset == 0xFFFFFFFF)
                    {
                        if (at + 8 > end)
                            throw Bad(ctx, "Corrupt zip64 extra field. Header offset not found.");
                        x.HeaderOffset = U64(e, at);
                    }
                }
                else if (tp == 0x7075 && ln >= 5 && e[at] == 1)
                {
                    if (U32(e, at + 1) == BinasciiModule.Crc32Raw(ctx, rawName, 0))
                    {
                        string uname = new UTF8Encoding(false, false).GetString(e, at + 5, ln - 5);
                        int nul = uname.IndexOf('\0');
                        x.Filename = nul >= 0 ? uname.Substring(0, nul) : uname;
                    }
                }
                i += 4 + ln;
            }
        }

        // ---------- the open file ----------

        internal void CheckOpen(EvalContext ctx)
        {
            if (_fp == null)
                throw Raise.ValueError(ctx, "Attempt to use ZIP archive that was already closed");
        }

        internal ZipInfoValue GetInfo(EvalContext ctx, string name)
        {
            ZipInfoValue zi;
            if (!_byName.TryGetValue(name, out zi))
                throw Raise.KeyError(ctx, ctx.Values.Str("There is no item named " + ctx.Values.Str(name).Repr(ctx) + " in the archive"));
            return zi;
        }

        private ZipInfoValue InfoArg(EvalContext ctx, ScriptValue v)
        {
            ZipInfoValue zi = v as ZipInfoValue;
            if (zi != null)
                return zi;
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, "name must be str or ZipInfo, not " + v.PyTypeName);
            return GetInfo(ctx, s.Value);
        }

        // A member's bytes after its local header is checked against the directory (CPython's open()
        // and ZipExtFile). A bad CRC or a short stream is raised by the read that reaches the end.
        internal BytesIOValue OpenMember(EvalContext ctx, ScriptValue nameOrInfo, byte[] pwd)
        {
            CheckOpen(ctx);
            _fp.CheckOpen(ctx);   // the caller closed the BytesIO under the archive: CPython's ValueError
            if (_writing)
                throw Raise.ValueError(ctx, "Can't read from the ZIP file while there is an open writing handle on it. Close the writing handle before trying to read.");
            ZipInfoValue zi = InfoArg(ctx, nameOrInfo);
            if (zi.CompressSize < 0 || zi.FileSize < 0 || zi.HeaderOffset < 0)
                throw Bad(ctx, "Corrupt sizes or offset for file " + ctx.Values.Str(zi.Filename).Repr(ctx));   // a ZIP64 field past 2**63, or set so
            byte[] b = _fp.RawBuffer;
            long len = _fp.Length;
            long h = zi.HeaderOffset;
            if (h < 0 || h + SizeFileHeader > len)
                throw Bad(ctx, "Truncated file header");
            if (U32(b, h) != SigFileHeader)
                throw Bad(ctx, "Bad magic number for file header");
            int nameLen = (int)U16(b, h + 26), extraLen = (int)U16(b, h + 28);
            if (h + SizeFileHeader + nameLen + extraLen > len)
                throw Raise.Make(ctx, PyExceptionTypes.EOFError, "");
            if ((zi.FlagBits & 0x20) != 0)
                throw Raise.NotImplemented(ctx, "compressed patched data (flag bit 5)");
            if ((zi.FlagBits & 0x40) != 0)
                throw Raise.NotImplemented(ctx, "strong encryption (flag bit 6)");
            byte[] rawName = Slice(b, h + SizeFileHeader, nameLen);
            string fname = DecodeName(ctx, rawName, (int)U16(b, h + 6));
            if (fname != zi.OrigFilename)
            {
                throw Bad(ctx, "File name in directory " + ctx.Values.Str(zi.OrigFilename).Repr(ctx) + " and header "
                    + ctx.Values.Bytes(rawName).Repr(ctx) + " differ.");
            }
            if ((zi.FlagBits & 0x1) != 0)
            {
                if (pwd == null)
                    pwd = _pwd;
                if (pwd == null || pwd.Length == 0)
                    throw Raise.RuntimeError(ctx, "File " + ctx.Values.Str(zi.Filename).Repr(ctx) + " is encrypted, password required for extraction");
                throw Raise.NotImplemented(ctx, "encrypted zip members are not supported in this dialect");
            }
            CheckCompression(ctx, zi.CompressType);

            long dataStart = h + SizeFileHeader + nameLen + extraLen;
            if (zi.EndOffset >= 0 && dataStart + zi.CompressSize > zi.EndOffset)
                throw Bad(ctx, "Overlapped entries: " + ctx.Values.Str(zi.OrigFilename).Repr(ctx) + " (possible zip bomb)");
            if (dataStart + zi.CompressSize > len)
                throw Raise.Make(ctx, PyExceptionTypes.EOFError, "");
            byte[] data;
            if (zi.CompressType == Stored)
            {
                long take = Math.Min(zi.CompressSize, zi.FileSize);
                ctx.Values.PreCharge(take);
                ctx.Budget.ChargeLinear(take);
                data = Slice(b, dataStart, (int)take);
            }
            else
            {
                ctx.Budget.ChargeLinear(zi.CompressSize);
                data = ZlibModule.InflateRaw(ctx, b, (int)dataStart, (int)zi.CompressSize, zi.FileSize);
            }
            Func<EvalContext, Exception> failAtEnd = null;
            if (BinasciiModule.Crc32Raw(ctx, data, 0) != (uint)zi.Crc)
                failAtEnd = c => Bad(c, "Bad CRC-32 for file " + c.Values.Str(zi.Filename).Repr(c));
            return BytesIOValue.ZipMember(ctx, data, zi.Filename, zi.CompressType, failAtEnd);
        }

        internal static void CheckCompression(EvalContext ctx, int compression)
        {
            if (compression != Stored && compression != Deflated)
                throw Raise.NotImplemented(ctx, "That compression method is not supported");
        }

        // ---------- writing ----------

        private void WriteCheck(EvalContext ctx, ZipInfoValue zi)
        {
            if (_mode != 'w' && _mode != 'x' && _mode != 'a')
                throw Raise.ValueError(ctx, "write() requires mode 'w', 'x', or 'a'");
            if (_fp == null)
                throw Raise.ValueError(ctx, "Attempt to write ZIP archive that was already closed");
            CheckCompression(ctx, zi.CompressType);
            if (!_allowZip64)
            {
                string requires = null;
                if (_files.Count >= FileCountLimit)
                    requires = "Files count";
                else if (zi.FileSize > Zip64Limit)
                    requires = "Filesize";
                else if (zi.HeaderOffset > Zip64Limit)
                    requires = "Zipfile size";
                if (requires != null)
                    throw Raise.Make(ctx, PyExceptionTypes.LargeZipFile, requires + " would require ZIP64 extensions");
            }
        }

        // writestr / open('w') share this: the local header, the data, then the header rewritten with the
        // real CRC and sizes (the BytesIO is seekable, so no data descriptor is needed).
        internal void WriteMember(EvalContext ctx, ZipInfoValue zi, byte[] data)
        {
            if (_fp == null)
                throw Raise.ValueError(ctx, "Attempt to write to ZIP archive that was already closed");
            if (_writing)
                throw Raise.ValueError(ctx, "Can't write to ZIP archive while an open writing handle exists.");
            zi.FileSize = data.Length;
            zi.CompressSize = 0;
            zi.Crc = 0;
            zi.FlagBits = 0;
            if (zi.ExternalAttr == 0)
                zi.ExternalAttr = 0x180L << 16;   // 0o600: ?rw-------
            _fp.Position = _startDir;
            zi.HeaderOffset = _fp.Position;
            WriteCheck(ctx, zi);
            _didModify = true;

            byte[] body;
            if (zi.CompressType == Deflated)
            {
                int level = -1;
                if (zi.CompressLevel.Kind != ValueKind.None)
                    level = Coerce.ToInt32(ctx, zi.CompressLevel, "compresslevel");
                ctx.Budget.ChargeLinear(data.Length);
                body = ZlibModule.DeflateRaw(ctx, data, level);
            }
            else
            {
                body = data;
            }
            zi.Crc = BinasciiModule.Crc32Raw(ctx, data, 0);
            zi.CompressSize = body.Length;
            _fp.Write(ctx, FileHeader(ctx, zi));
            _fp.Write(ctx, body);
            _startDir = _fp.Position;
            ctx.Values.PreCharge(48);
            _files.Add(zi);
            _byName[zi.Filename] = zi;
        }

        private static byte[] FileHeader(EvalContext ctx, ZipInfoValue zi)
        {
            int dostime, dosdate, flagBits;
            zi.DosDateTime(ctx, out dostime, out dosdate);
            byte[] name = zi.EncodeFilename(out flagBits);
            byte[] extra = zi.Extra;
            if (name.Length > 0xFFFF || extra.Length > 0xFFFF)
                throw Raise.ValueError(ctx, "zip member name or extra field is too long");
            var h = new byte[SizeFileHeader + name.Length + extra.Length];
            PutU32(h, 0, SigFileHeader);
            h[4] = (byte)zi.ExtractVersion;
            h[5] = (byte)zi.Reserved;
            PutU16(h, 6, flagBits);
            PutU16(h, 8, zi.CompressType);
            PutU16(h, 10, dostime);
            PutU16(h, 12, dosdate);
            PutU32(h, 14, (uint)zi.Crc);
            PutU32(h, 18, (uint)zi.CompressSize);
            PutU32(h, 22, (uint)zi.FileSize);
            PutU16(h, 26, name.Length);
            PutU16(h, 28, extra.Length);
            Buffer.BlockCopy(name, 0, h, SizeFileHeader, name.Length);
            Buffer.BlockCopy(extra, 0, h, SizeFileHeader + name.Length, extra.Length);
            return h;
        }

        private static void PutU16(byte[] b, int at, int v)
        {
            b[at] = (byte)v;
            b[at + 1] = (byte)(v >> 8);
        }

        private static void PutU32(byte[] b, int at, uint v)
        {
            b[at] = (byte)v;
            b[at + 1] = (byte)(v >> 8);
            b[at + 2] = (byte)(v >> 16);
            b[at + 3] = (byte)(v >> 24);
        }

        private static void PutU64(byte[] b, int at, long v)
        {
            PutU32(b, at, (uint)v);
            PutU32(b, at + 4, (uint)((ulong)v >> 32));
        }

        // The central directory and the end record at the directory's place (CPython's _write_end_record).
        // Only the entry count can need ZIP64 here: sizes and offsets stay far below 2 GB under the caps.
        private void WriteEndRecord(EvalContext ctx)
        {
            _fp.Position = _startDir;
            foreach (ZipInfoValue zi in _files)
            {
                ctx.Budget.Step();
                int dostime, dosdate, flagBits;
                zi.DosDateTime(ctx, out dostime, out dosdate);
                byte[] name = zi.EncodeFilename(out flagBits);
                var cd = new byte[SizeCentralDir];
                PutU32(cd, 0, SigCentralDir);
                cd[4] = (byte)zi.CreateVersion;
                cd[5] = (byte)zi.CreateSystem;
                cd[6] = (byte)zi.ExtractVersion;
                cd[7] = (byte)zi.Reserved;
                PutU16(cd, 8, flagBits);
                PutU16(cd, 10, zi.CompressType);
                PutU16(cd, 12, dostime);
                PutU16(cd, 14, dosdate);
                PutU32(cd, 16, (uint)zi.Crc);
                PutU32(cd, 20, (uint)zi.CompressSize);
                PutU32(cd, 24, (uint)zi.FileSize);
                PutU16(cd, 28, name.Length);
                PutU16(cd, 30, zi.Extra.Length);
                PutU16(cd, 32, zi.Comment.Length);
                PutU16(cd, 34, 0);
                PutU16(cd, 36, zi.InternalAttr);
                PutU32(cd, 38, (uint)zi.ExternalAttr);
                PutU32(cd, 42, (uint)zi.HeaderOffset);
                _fp.Write(ctx, cd);
                _fp.Write(ctx, name);
                _fp.Write(ctx, zi.Extra);
                _fp.Write(ctx, zi.Comment);
            }
            long pos2 = _fp.Position;
            long count = _files.Count;
            long size = pos2 - _startDir;
            long offset = _startDir;
            if (count > FileCountLimit)
            {
                if (!_allowZip64)
                    throw Raise.Make(ctx, PyExceptionTypes.LargeZipFile, "Files count would require ZIP64 extensions");
                var rec = new byte[SizeEndCentDir64 + SizeEndCentDir64Locator];
                PutU32(rec, 0, SigEndArchive64);
                PutU64(rec, 4, 44);
                PutU16(rec, 12, ZipInfoValue.Zip64Version);
                PutU16(rec, 14, ZipInfoValue.Zip64Version);
                PutU64(rec, 24, count);
                PutU64(rec, 32, count);
                PutU64(rec, 40, size);
                PutU64(rec, 48, offset);
                PutU32(rec, SizeEndCentDir64, SigEndArchive64Locator);
                PutU64(rec, SizeEndCentDir64 + 8, pos2);
                PutU32(rec, SizeEndCentDir64 + 16, 1);
                _fp.Write(ctx, rec);
                count = Math.Min(count, 0xFFFF);
            }
            var end = new byte[SizeEndCentDir];
            PutU32(end, 0, SigEndArchive);
            PutU16(end, 8, (int)count);
            PutU16(end, 10, (int)count);
            PutU32(end, 12, (uint)size);
            PutU32(end, 16, (uint)offset);
            PutU16(end, 20, _comment.Length);
            _fp.Write(ctx, end);
            _fp.Write(ctx, _comment);
            if (_mode == 'a')
                _fp.TruncateAtPosition();
        }

        internal void Close(EvalContext ctx)
        {
            if (_fp == null)
                return;
            if (_writing)
                throw Raise.ValueError(ctx, "Can't close the ZIP file while there is an open writing handle on it. Close the writing handle before closing the zip.");
            try
            {
                if (_mode != 'r' && _didModify)
                    WriteEndRecord(ctx);
            }
            finally
            {
                _fp = null;   // the caller's BytesIO stays open, as CPython leaves a file object it was given
            }
        }

        // ---------- open(name, 'w') ----------

        private sealed class WriteHandle : ScriptValue, IContextManager
        {
            private static readonly ScriptTypeInfo HType = new ScriptTypeInfo("zipfile._ZipWriteFile", BuildHandleSlots());
            private readonly ZipFileValue _zip;
            private readonly ZipInfoValue _info;
            private readonly BytesIOValue _data;
            private bool _closed;

            internal WriteHandle(EvalContext ctx, ZipFileValue zip, ZipInfoValue info)
            {
                _zip = zip;
                _info = info;
                _data = new BytesIOValue(ctx, Array.Empty<byte>());
            }

            public override ScriptTypeInfo TypeInfo { get { return HType; } }
            internal override ValueKind Kind { get { return ValueKind.Opaque; } }

            public ScriptValue Enter(EvalContext ctx) { return this; }

            public bool Exit(EvalContext ctx, ExceptionValue pending)
            {
                Close(ctx);
                return false;
            }

            private void Close(EvalContext ctx)
            {
                if (_closed)
                    return;
                _closed = true;
                _zip._writing = false;
                _zip.WriteMember(ctx, _info, _data.Snapshot(ctx));
            }

            private static WriteHandle Open(ScriptValue v, EvalContext ctx)
            {
                var h = (WriteHandle)v;
                if (h._closed)
                    throw Raise.ValueError(ctx, "I/O operation on closed file.");
                return h;
            }

            private static IDictionary<string, SlotDescriptor> BuildHandleSlots()
            {
                var s = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
                s["write"] = SlotDescriptor.MakeMethod("write", (self, a, kw, c) =>
                {
                    Args.Exactly(c, a, kw, "write", 1);
                    return c.Values.Int(Open(self, c)._data.Write(c, BytesIOValue.RequireBytes(c, a[0])));
                });
                s["close"] = SlotDescriptor.MakeMethod("close", Args.Positional("close", 0, 0, (self, a, kw, c) =>
                {
                    ((WriteHandle)self).Close(c);
                    return c.Values.None;
                }));
                s["closed"] = SlotDescriptor.MakeProperty("closed", (self, c) => c.Values.Bool(((WriteHandle)self)._closed));
                s["writable"] = SlotDescriptor.MakeMethod("writable", Args.Positional("writable", 0, 0, (self, a, kw, c) =>
                {
                    Open(self, c);
                    return c.Values.Bool(true);
                }));
                s["readable"] = SlotDescriptor.MakeMethod("readable", Args.Positional("readable", 0, 0, (self, a, kw, c) =>
                {
                    Open(self, c);
                    return c.Values.Bool(false);
                }));
                s["flush"] = SlotDescriptor.MakeMethod("flush", Args.Positional("flush", 0, 0, (self, a, kw, c) =>
                {
                    Open(self, c);
                    return c.Values.None;
                }));
                return s;
            }
        }

        // ---------- the script-facing surface ----------

        private static ZipFileValue Self(ScriptValue v) { return (ZipFileValue)v; }

        private ZipInfoValue NewInfo(EvalContext ctx, string name, int[] dateTime)
        {
            var zi = new ZipInfoValue(ctx, name, dateTime);
            zi.CompressType = _compression;
            zi.CompressLevel = _compressLevel;
            return zi;
        }

        internal static int[] Now(EvalContext ctx)
        {
            DateTime t = ctx.Clock.UtcNow + ctx.Clock.LocalOffset;
            return new[] { t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second };
        }

        private static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            var s = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
            s["namelist"] = SlotDescriptor.MakeMethod("namelist", (self, a, kw, c) =>
            {
                Args.None(c, a, kw, "namelist");
                ZipFileValue z = Self(self);
                ListValue r = c.Values.List(z._files.Count);
                foreach (ZipInfoValue zi in z._files)
                    r.Add(c.Values.Str(zi.Filename), c);
                return r;
            });
            s["infolist"] = SlotDescriptor.MakeMethod("infolist", (self, a, kw, c) =>
            {
                Args.None(c, a, kw, "infolist");
                ZipFileValue z = Self(self);
                ListValue r = c.Values.List(z._files.Count);
                foreach (ZipInfoValue zi in z._files)
                    r.Add(zi, c);
                return r;
            });
            s["getinfo"] = SlotDescriptor.MakeMethod("getinfo", (self, a, kw, c) =>
            {
                Args.Exactly(c, a, kw, "getinfo", 1);
                StrValue name = a[0] as StrValue;
                if (name == null)
                    throw Raise.TypeError(c, "getinfo() argument must be str, not " + a[0].PyTypeName);
                return Self(self).GetInfo(c, name.Value);
            });
            s["read"] = SlotDescriptor.MakeMethod("read", (self, a, kw, c) =>
            {
                ScriptValue name, pwd;
                ReadNamePwd(c, a, kw, "read", out name, out pwd);
                BytesIOValue member = Self(self).OpenMember(c, name, PwdArg(c, pwd));
                return c.Values.Bytes(member.Read(c, -1));
            });
            s["open"] = SlotDescriptor.MakeMethod("open", OpenMethod);
            s["writestr"] = SlotDescriptor.MakeMethod("writestr", WriteStr);
            s["write"] = SlotDescriptor.MakeMethod("write", (self, a, kw, c) =>
            {
                throw Raise.TypeError(c, "ZipFile.write() reads a file from disk; there is no file system in this environment - use writestr()");
            });
            BuiltinDelegate noDisk = (self, a, kw, c) =>
            {
                throw Raise.TypeError(c, "extracting writes files to disk; there is no file system in this environment - use read() or open()");
            };
            s["extract"] = SlotDescriptor.MakeMethod("extract", noDisk);
            s["extractall"] = SlotDescriptor.MakeMethod("extractall", noDisk);
            s["testzip"] = SlotDescriptor.MakeMethod("testzip", (self, a, kw, c) =>
            {
                Args.None(c, a, kw, "testzip");
                ZipFileValue z = Self(self);
                foreach (ZipInfoValue zi in z._files.ToArray())
                {
                    try
                    {
                        z.OpenMember(c, zi, null).Read(c, -1);
                    }
                    catch (ScriptException se) when (se.Value.ExcType == PyExceptionTypes.BadZipFile)
                    {
                        return c.Values.Str(zi.Filename);
                    }
                }
                return c.Values.None;
            });
            s["setpassword"] = SlotDescriptor.MakeMethod("setpassword", (self, a, kw, c) =>
            {
                Args.Exactly(c, a, kw, "setpassword", 1);
                Self(self)._pwd = PwdArg(c, a[0]);
                return c.Values.None;
            });
            s["printdir"] = SlotDescriptor.MakeMethod("printdir", PrintDir);
            s["close"] = SlotDescriptor.MakeMethod("close", (self, a, kw, c) =>
            {
                Args.None(c, a, kw, "close");
                Self(self).Close(c);
                return c.Values.None;
            });
            s["filelist"] = SlotDescriptor.MakeProperty("filelist", (self, c) =>
            {
                ZipFileValue z = Self(self);
                ListValue r = c.Values.List(z._files.Count);
                foreach (ZipInfoValue zi in z._files)
                    r.Add(zi, c);
                return r;
            });
            s["NameToInfo"] = SlotDescriptor.MakeProperty("NameToInfo", (self, c) =>
            {
                DictValue d = c.Values.Dict(Self(self)._byName.Count);
                foreach (ZipInfoValue zi in Self(self)._files)
                    d.SetItem(c.Values.Str(zi.Filename), Self(self)._byName[zi.Filename], c);
                return d;
            });
            s["comment"] = SlotDescriptor.MakeProperty("comment", (self, c) => c.Values.Bytes(Self(self)._comment));
            s["mode"] = SlotDescriptor.MakeProperty("mode", (self, c) => c.Values.Str(Self(self)._mode.ToString()));
            s["filename"] = SlotDescriptor.MakeProperty("filename", (self, c) => c.Values.None);
            s["fp"] = SlotDescriptor.MakeProperty("fp", (self, c) => (ScriptValue)Self(self)._fp ?? c.Values.None);
            s["compression"] = SlotDescriptor.MakeProperty("compression", (self, c) => c.Values.Int(Self(self)._compression));
            s["compresslevel"] = SlotDescriptor.MakeProperty("compresslevel", (self, c) => Self(self)._compressLevel);
            s["start_dir"] = SlotDescriptor.MakeProperty("start_dir", (self, c) => c.Values.Int(Self(self)._startDir));
            s["debug"] = SlotDescriptor.MakeProperty("debug", (self, c) => c.Values.Int(0));
            return s;
        }

        // comment is the one writable ZipFile attribute: bytes, cut at 65535 as CPython does (it warns).
        protected internal override bool TrySetAttrCore(string name, ScriptValue value, EvalContext ctx)
        {
            if (name != "comment")
                return false;
            BytesValue b = value as BytesValue;
            if (b == null)
                throw Raise.TypeError(ctx, "comment: expected bytes, got " + value.PyTypeName);
            byte[] data = b.Data;
            if (data.Length > MaxComment)
            {
                data = new byte[MaxComment];
                Buffer.BlockCopy(b.Data, 0, data, 0, MaxComment);
            }
            _comment = data;
            _didModify = true;
            return true;
        }

        private static void ReadNamePwd(EvalContext c, ScriptValue[] a, KwArgs kw, string fn, out ScriptValue name, out ScriptValue pwd)
        {
            Args.Between(c, a, fn, 1, 2);
            name = a[0];
            pwd = a.Length > 1 ? a[1] : null;
            for (int i = 0; i < kw.Count; i++)
            {
                if (kw.NameAt(i) != "pwd")
                    throw Raise.TypeError(c, fn + "() got an unexpected keyword argument '" + kw.NameAt(i) + "'");
                pwd = kw.ValueAt(i);
            }
        }

        private static byte[] PwdArg(EvalContext c, ScriptValue v)
        {
            if (v == null || v.Kind == ValueKind.None)
                return null;
            BytesValue b = v as BytesValue;
            if (b == null)
                throw Raise.TypeError(c, "pwd: expected bytes, got " + v.PyTypeName);
            return b.Data;
        }

        // open(name, mode='r', pwd=None, *, force_zip64=False)
        private static ScriptValue OpenMethod(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext c)
        {
            ZipFileValue z = Self(self);
            Args.Between(c, a, "open", 1, 3);
            ScriptValue name = a[0];
            string mode = "r";
            ScriptValue pwd = a.Length > 2 ? a[2] : null;
            ScriptValue modeArg = a.Length > 1 ? a[1] : null;
            for (int i = 0; i < kw.Count; i++)
            {
                switch (kw.NameAt(i))
                {
                    case "mode": modeArg = kw.ValueAt(i); break;
                    case "pwd": pwd = kw.ValueAt(i); break;
                    case "force_zip64": break;   // sizes here never need ZIP64
                    default: throw Raise.TypeError(c, "open() got an unexpected keyword argument '" + kw.NameAt(i) + "'");
                }
            }
            if (modeArg != null)
            {
                StrValue ms = modeArg as StrValue;
                mode = ms == null ? null : ms.Value;
            }
            if (mode != "r" && mode != "w")
                throw Raise.ValueError(c, "open() requires mode \"r\" or \"w\"");
            if (mode == "w" && PwdArg(c, pwd) != null)
                throw Raise.ValueError(c, "pwd is only supported for reading files");
            z.CheckOpen(c);
            if (mode == "r")
                return z.OpenMember(c, name, PwdArg(c, pwd));

            ZipInfoValue zi = name as ZipInfoValue;
            if (zi == null)
            {
                StrValue s = name as StrValue;
                if (s == null)
                    throw Raise.TypeError(c, "name must be str or ZipInfo, not " + name.PyTypeName);
                zi = z.NewInfo(c, s.Value, ZipInfoValue.DefaultDateTime());
            }
            if (z._writing)
                throw Raise.ValueError(c, "Can't write to the ZIP file while there is another write handle open on it. Close the first handle before opening another.");
            z.WriteCheck(c, zi);
            z._writing = true;
            return new WriteHandle(c, z, zi);
        }

        // writestr(zinfo_or_arcname, data, compress_type=None, compresslevel=None)
        private static ScriptValue WriteStr(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext c)
        {
            ZipFileValue z = Self(self);
            Args.Between(c, a, "writestr", 2, 4);
            ScriptValue compressType = a.Length > 2 ? a[2] : null, compressLevel = a.Length > 3 ? a[3] : null;
            for (int i = 0; i < kw.Count; i++)
            {
                switch (kw.NameAt(i))
                {
                    case "compress_type": compressType = kw.ValueAt(i); break;
                    case "compresslevel": compressLevel = kw.ValueAt(i); break;
                    default: throw Raise.TypeError(c, "writestr() got an unexpected keyword argument '" + kw.NameAt(i) + "'");
                }
            }
            byte[] data;
            StrValue text = a[1] as StrValue;
            if (text != null)
            {
                c.Values.PreCharge(3L * text.Value.Length);
                c.Budget.ChargeLinear(text.Value.Length);
                data = Encoding.UTF8.GetBytes(text.Value);
            }
            else
            {
                data = BytesIOValue.RequireBytes(c, a[1]);
            }

            ZipInfoValue zi = a[0] as ZipInfoValue;
            if (zi == null)
            {
                StrValue arc = a[0] as StrValue;
                if (arc == null)
                    throw Raise.TypeError(c, "zinfo_or_arcname must be str or ZipInfo, not " + a[0].PyTypeName);
                zi = z.NewInfo(c, arc.Value, Now(c));
                zi.ExternalAttr = zi.IsDir ? (0x41FDL << 16) | 0x10 : 0x180L << 16;   // drwxrwxr-x + MS-DOS dir flag : ?rw-------
            }
            if (compressType != null && compressType.Kind != ValueKind.None)
                zi.CompressType = Coerce.ToInt32(c, compressType, "compress_type");
            if (compressLevel != null && compressLevel.Kind != ValueKind.None)
            {
                Coerce.ToInt32(c, compressLevel, "compresslevel");
                zi.CompressLevel = compressLevel;
            }
            z.WriteMember(c, zi, data);
            return c.Values.None;
        }

        // printdir(file=None): CPython's listing, to the print output or to an object with write().
        private static ScriptValue PrintDir(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext c)
        {
            Args.AtMost(c, a, "printdir", 1);
            ScriptValue file = a.Length > 0 ? a[0] : null;
            ScriptValue fk;
            if (kw.TryGet("file", out fk))
                file = fk;
            var sb = new StringBuilder();
            sb.Append(string.Format("{0,-46} {1,19} {2,12}\n", "File Name", "Modified    ", "Size"));
            foreach (ZipInfoValue zi in Self(self)._files)
            {
                c.Budget.Step();
                int[] d = zi.DateTime;
                string date = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0}-{1:00}-{2:00} {3:00}:{4:00}:{5:00}", d[0], d[1], d[2], d[3], d[4], d[5]);
                sb.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0,-46} {1} {2,12}\n", zi.Filename, date, zi.FileSize));
            }
            if (file == null || file.Kind == ValueKind.None)
            {
                Builtins.Emit(c, sb.ToString());
            }
            else
            {
                ScriptValue write = PyOps.GetAttr(file, "write", c);
                c.CallHook(write, new ScriptValue[] { c.Values.Str(sb.ToString()) }, KwArgs.Empty);
            }
            return c.Values.None;
        }

        // is_zipfile: an end record whose directory starts with a directory entry, or an empty archive.
        internal static bool LooksLikeZip(EvalContext ctx, BytesIOValue fp)
        {
            byte[] b = fp.RawBuffer;
            int len = fp.Length;
            EndRecord end;
            try
            {
                end = FindEndRecord(ctx, b, len);
            }
            catch (ScriptException se) when (se.Value.ExcType == PyExceptionTypes.BadZipFile)
            {
                return false;
            }
            if (end == null)
                return false;
            if (end.Entries == 0 && end.Size == 0 && end.Offset == 0)
                return true;
            long concat = end.Location - end.Size - end.Offset;
            if (end.Zip64)
                concat -= SizeEndCentDir64 + SizeEndCentDir64Locator;
            long start = end.Offset + concat;
            return end.Size >= SizeCentralDir && start >= 0 && start + SizeCentralDir <= len && U32(b, start) == SigCentralDir;
        }
    }

    // Code page 437, the encoding of a zip member name without the UTF-8 flag. ASCII below 0x80.
    internal static class Cp437
    {
        private const string High =
            "ÇüéâäàåçêëèïîìÄÅ"
            + "ÉæÆôöòûùÿÖÜ¢£¥₧ƒ"
            + "áíóúñÑªº¿⌐¬½¼¡«»"
            + "░▒▓│┤╡╢╖╕╣║╗╝╜╛┐"
            + "└┴┬├─┼╞╟╚╔╩╦╠═╬╧"
            + "╨╤╥╙╘╒╓╫╪┘┌█▄▌▐▀"
            + "αßΓπΣσµτΦΘΩδ∞φε∩"
            + "≡±≥≤⌠⌡÷≈°∙·√ⁿ²■ ";

        internal static string Decode(byte[] raw)
        {
            var sb = new StringBuilder(raw.Length);
            foreach (byte x in raw)
                sb.Append(x < 0x80 ? (char)x : High[x - 0x80]);
            return sb.ToString();
        }

        internal static int HighLength { get { return High.Length; } }
    }
}

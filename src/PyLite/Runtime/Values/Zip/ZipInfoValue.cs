using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // zipfile.ZipInfo: one member's metadata, CPython's attributes (all writable, nothing else settable).
    internal sealed class ZipInfoValue : ScriptValue
    {
        private static readonly ScriptTypeInfo SType = new ScriptTypeInfo("zipfile.ZipInfo", BuildSlots());
        internal static ScriptTypeInfo ZipInfoType { get { return SType; } }

        internal const int DefaultVersion = 20;
        internal const int Zip64Version = 45;

        internal string OrigFilename;
        internal string Filename;
        internal int[] DateTime;
        internal int CompressType;
        internal ScriptValue CompressLevel;   // an int or None
        internal byte[] Comment = Array.Empty<byte>();
        internal byte[] Extra = Array.Empty<byte>();
        internal int CreateSystem = 3;        // CPython on a unix-y system; Windows CPython writes 0
        internal int CreateVersion = DefaultVersion;
        internal int ExtractVersion = DefaultVersion;
        internal int Reserved;
        internal int FlagBits;
        internal int Volume;
        internal int InternalAttr;
        internal long ExternalAttr;
        internal long HeaderOffset;
        internal long Crc;
        internal long CompressSize;
        internal long FileSize;
        internal long EndOffset = -1;   // where the next member or the directory starts: a member may not run past it

        internal ZipInfoValue(EvalContext ctx, string filename, int[] dateTime)
        {
            ctx.Values.PreCharge(160 + 4L * filename.Length);
            OrigFilename = filename;
            int nul = filename.IndexOf('\0');
            Filename = nul >= 0 ? filename.Substring(0, nul) : filename;   // a NUL ends the name, as in CPython
            if (dateTime[0] < 1980)
                throw Raise.ValueError(ctx, "ZIP does not support timestamps before 1980");
            DateTime = dateTime;
            CompressLevel = ctx.Values.None;
        }

        public override ScriptTypeInfo TypeInfo { get { return SType; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        internal bool IsDir { get { return Filename.EndsWith("/", StringComparison.Ordinal); } }

        internal static int[] DefaultDateTime() { return new[] { 1980, 1, 1, 0, 0, 0 }; }

        // A date_time argument: a sequence of six ints (year, month, day, hour, minute, second).
        internal static int[] ReadDateTime(EvalContext ctx, ScriptValue v)
        {
            IScriptIterator it = PyOps.TryGetIterator(v, ctx);
            if (it == null)
                throw Raise.TypeError(ctx, "date_time must be a sequence of six ints, not " + v.PyTypeName);
            var parts = new List<int>(6);
            ScriptValue item;
            while (it.MoveNext(ctx, out item))
            {
                if (parts.Count == 6)
                    throw Raise.TypeError(ctx, "date_time must be a sequence of six ints");
                parts.Add(Coerce.ToInt32(ctx, item, "date_time"));
            }
            if (parts.Count != 6)
                throw Raise.TypeError(ctx, "date_time must be a sequence of six ints");
            return parts.ToArray();
        }

        // (dostime, dosdate) as CPython packs them; a year past 2107 does not fit and is struct.error.
        internal void DosDateTime(EvalContext ctx, out int dostime, out int dosdate)
        {
            int[] dt = DateTime;
            long date = ((long)(dt[0] - 1980) << 9) | ((long)dt[1] << 5) | (long)dt[2];
            long time = ((long)dt[3] << 11) | ((long)dt[4] << 5) | (long)(dt[5] / 2);
            if (date < 0 || date > 0xFFFF || time < 0 || time > 0xFFFF)
                throw Raise.Make(ctx, PyExceptionTypes.StructError, "'H' format requires 0 <= number <= 65535");
            dostime = (int)time;
            dosdate = (int)date;
        }

        // ASCII names are written as they are; anything else as UTF-8 with general-purpose bit 11.
        internal byte[] EncodeFilename(out int flagBits)
        {
            flagBits = FlagBits;
            for (int i = 0; i < Filename.Length; i++)
            {
                if (Filename[i] > 0x7F)
                {
                    flagBits |= 0x800;
                    return Encoding.UTF8.GetBytes(Filename);
                }
            }
            return Encoding.ASCII.GetBytes(Filename);
        }

        private static ZipInfoValue Self(ScriptValue v) { return (ZipInfoValue)v; }

        private static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            var s = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
            s["filename"] = SlotDescriptor.MakeProperty("filename", (self, c) => c.Values.Str(Self(self).Filename));
            s["orig_filename"] = SlotDescriptor.MakeProperty("orig_filename", (self, c) => c.Values.Str(Self(self).OrigFilename));
            s["date_time"] = SlotDescriptor.MakeProperty("date_time", (self, c) =>
            {
                int[] dt = Self(self).DateTime;
                var items = new ScriptValue[6];
                for (int i = 0; i < 6; i++)
                    items[i] = c.Values.Int(dt[i]);
                return c.Values.Tuple(items);
            });
            s["compress_type"] = SlotDescriptor.MakeProperty("compress_type", (self, c) => c.Values.Int(Self(self).CompressType));
            s["compress_level"] = SlotDescriptor.MakeProperty("compress_level", (self, c) => Self(self).CompressLevel);
            s["comment"] = SlotDescriptor.MakeProperty("comment", (self, c) => c.Values.Bytes(Self(self).Comment));
            s["extra"] = SlotDescriptor.MakeProperty("extra", (self, c) => c.Values.Bytes(Self(self).Extra));
            s["create_system"] = SlotDescriptor.MakeProperty("create_system", (self, c) => c.Values.Int(Self(self).CreateSystem));
            s["create_version"] = SlotDescriptor.MakeProperty("create_version", (self, c) => c.Values.Int(Self(self).CreateVersion));
            s["extract_version"] = SlotDescriptor.MakeProperty("extract_version", (self, c) => c.Values.Int(Self(self).ExtractVersion));
            s["reserved"] = SlotDescriptor.MakeProperty("reserved", (self, c) => c.Values.Int(Self(self).Reserved));
            s["flag_bits"] = SlotDescriptor.MakeProperty("flag_bits", (self, c) => c.Values.Int(Self(self).FlagBits));
            s["volume"] = SlotDescriptor.MakeProperty("volume", (self, c) => c.Values.Int(Self(self).Volume));
            s["internal_attr"] = SlotDescriptor.MakeProperty("internal_attr", (self, c) => c.Values.Int(Self(self).InternalAttr));
            s["external_attr"] = SlotDescriptor.MakeProperty("external_attr", (self, c) => c.Values.Int(Self(self).ExternalAttr));
            s["header_offset"] = SlotDescriptor.MakeProperty("header_offset", (self, c) => c.Values.Int(Self(self).HeaderOffset));
            s["CRC"] = SlotDescriptor.MakeProperty("CRC", (self, c) => c.Values.Int(Self(self).Crc));
            s["compress_size"] = SlotDescriptor.MakeProperty("compress_size", (self, c) => c.Values.Int(Self(self).CompressSize));
            s["file_size"] = SlotDescriptor.MakeProperty("file_size", (self, c) => c.Values.Int(Self(self).FileSize));
            s["is_dir"] = SlotDescriptor.MakeMethod("is_dir", (self, a, kw, c) =>
            {
                Args.None(c, a, kw, "is_dir");
                return c.Values.Bool(Self(self).IsDir);
            });
            return s;
        }

        protected internal override bool TrySetAttrCore(string name, ScriptValue value, EvalContext ctx)
        {
            switch (name)
            {
                case "filename": Filename = StrArg(ctx, name, value); return true;
                case "orig_filename": OrigFilename = StrArg(ctx, name, value); return true;
                case "date_time": DateTime = ReadDateTime(ctx, value); return true;
                case "compress_type": CompressType = Coerce.ToInt32(ctx, value, name); return true;
                case "compress_level":
                    if (value.Kind != ValueKind.None)
                        Coerce.ToInt32(ctx, value, name);
                    CompressLevel = value;
                    return true;
                case "comment": Comment = BytesArg(ctx, name, value); return true;
                case "extra": Extra = BytesArg(ctx, name, value); return true;
                case "create_system": CreateSystem = Coerce.ToInt32(ctx, value, name); return true;
                case "create_version": CreateVersion = Coerce.ToInt32(ctx, value, name); return true;
                case "extract_version": ExtractVersion = Coerce.ToInt32(ctx, value, name); return true;
                case "reserved": Reserved = Coerce.ToInt32(ctx, value, name); return true;
                case "flag_bits": FlagBits = Coerce.ToInt32(ctx, value, name); return true;
                case "volume": Volume = Coerce.ToInt32(ctx, value, name); return true;
                case "internal_attr": InternalAttr = Coerce.ToInt32(ctx, value, name); return true;
                case "external_attr": ExternalAttr = LongArg(ctx, value, name); return true;
                case "header_offset": HeaderOffset = LongArg(ctx, value, name); return true;
                case "CRC": Crc = LongArg(ctx, value, name); return true;
                case "compress_size": CompressSize = LongArg(ctx, value, name); return true;
                case "file_size": FileSize = LongArg(ctx, value, name); return true;
                default: return false;
            }
        }

        private static string StrArg(EvalContext ctx, string name, ScriptValue v)
        {
            StrValue s = v as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, name + " must be str, not " + v.PyTypeName);
            return s.Value;
        }

        private static byte[] BytesArg(EvalContext ctx, string name, ScriptValue v)
        {
            BytesValue b = v as BytesValue;
            if (b == null)
                throw Raise.TypeError(ctx, name + ": expected bytes, got " + v.PyTypeName);
            return b.Data;
        }

        private static long LongArg(EvalContext ctx, ScriptValue v, string name)
        {
            BigInteger n = Coerce.ToIndex(ctx, v, name);
            if (n > long.MaxValue || n < long.MinValue)
                throw Raise.Overflow(ctx, "Python int too large to convert to C long");
            return (long)n;
        }

        private static readonly Dictionary<int, string> CompressorNames = new Dictionary<int, string>
        {
            { 0, "store" }, { 1, "shrink" }, { 2, "reduce" }, { 3, "reduce" }, { 4, "reduce" }, { 5, "reduce" },
            { 6, "implode" }, { 7, "tokenize" }, { 8, "deflate" }, { 9, "deflate64" }, { 10, "implode" },
            { 12, "bzip2" }, { 14, "lzma" }, { 18, "terse" }, { 19, "lz77" }, { 97, "wavpack" }, { 98, "ppmd" },
        };

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            sb.Append("<ZipInfo filename=");
            sb.Append(ctx.Values.Str(Filename).Repr(ctx));
            if (CompressType != 0)
            {
                string cname;
                sb.Append(" compress_type=");
                sb.Append(CompressorNames.TryGetValue(CompressType, out cname) ? cname : CompressType.ToString());
            }
            long hi = ExternalAttr >> 16, lo = ExternalAttr & 0xFFFF;
            if (hi != 0)
                sb.Append(" filemode='" + FileMode(hi) + "'");
            if (lo != 0)
                sb.Append(" external_attr=0x" + lo.ToString("x"));
            bool isDir = IsDir;
            if (!isDir || FileSize != 0)
                sb.Append(" file_size=" + FileSize);
            if ((!isDir || CompressSize != 0) && (CompressType != 0 || FileSize != CompressSize))
                sb.Append(" compress_size=" + CompressSize);
            sb.Append('>');
        }

        // stat.filemode: the file type letter (? when the type bits name none) and the nine permissions.
        internal static string FileMode(long mode)
        {
            var r = new StringBuilder(10);
            switch (mode & 0xF000)
            {
                case 0x8000: r.Append('-'); break;
                case 0x4000: r.Append('d'); break;
                case 0xA000: r.Append('l'); break;
                case 0x6000: r.Append('b'); break;
                case 0x2000: r.Append('c'); break;
                case 0x1000: r.Append('p'); break;
                case 0xC000: r.Append('s'); break;
                default: r.Append('?'); break;
            }
            Perm(r, mode, 0x100, 0x80, 0x40, 0x800, 's', 'S');
            Perm(r, mode, 0x20, 0x10, 0x8, 0x400, 's', 'S');
            Perm(r, mode, 0x4, 0x2, 0x1, 0x200, 't', 'T');
            return r.ToString();
        }

        private static void Perm(StringBuilder r, long mode, int read, int write, int exec, int special, char both, char onlySpecial)
        {
            r.Append((mode & read) != 0 ? 'r' : '-');
            r.Append((mode & write) != 0 ? 'w' : '-');
            bool x = (mode & exec) != 0, sp = (mode & special) != 0;
            r.Append(x && sp ? both : sp ? onlySpecial : x ? 'x' : '-');
        }
    }
}

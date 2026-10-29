using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // zipfile over in-memory files: ZipFile(io.BytesIO, mode) reads, writes ('w'/'x') and appends ('a')
    // STORED and DEFLATED archives, reads ZIP64 records, and checks every member's CRC. There is no file
    // system, so a path is refused by name; bzip2/lzma and decryption are NotImplementedError.
    internal static class ZipfileModule
    {
        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["ZipFile"] = ctx.Values.Type("zipfile.ZipFile", ZipFileCtor, ZipFileValue.ZipFileType, null);
            m["ZipInfo"] = ctx.Values.Type("zipfile.ZipInfo", ZipInfoCtor, ZipInfoValue.ZipInfoType, null);
            m["ZipExtFile"] = ctx.Values.Type("zipfile.ZipExtFile", (self, a, kw, c) =>
            {
                throw Raise.TypeError(c, "ZipExtFile objects come from ZipFile.open()");
            }, BytesIOValue.ZipExtFileType, null);
            m["is_zipfile"] = BuiltinFunctionValue.Make("is_zipfile", IsZipfile);
            m["BadZipFile"] = ModuleKit.ExceptionClass(PyExceptionTypes.BadZipFile);
            m["BadZipfile"] = m["BadZipFile"];   // the old spelling, still exported by CPython
            m["error"] = m["BadZipFile"];
            m["LargeZipFile"] = ModuleKit.ExceptionClass(PyExceptionTypes.LargeZipFile);
            m["ZIP_STORED"] = ctx.Values.Int(ZipFileValue.Stored);
            m["ZIP_DEFLATED"] = ctx.Values.Int(ZipFileValue.Deflated);
            m["ZIP_BZIP2"] = ctx.Values.Int(ZipFileValue.Bzip2);
            m["ZIP_LZMA"] = ctx.Values.Int(ZipFileValue.Lzma);
            m["DEFAULT_VERSION"] = ctx.Values.Int(ZipInfoValue.DefaultVersion);
            m["ZIP64_VERSION"] = ctx.Values.Int(ZipInfoValue.Zip64Version);
            m["ZIP64_LIMIT"] = ctx.Values.Int((1L << 31) - 1);
            m["ZIP_FILECOUNT_LIMIT"] = ctx.Values.Int((1 << 16) - 1);
            m["ZIP_MAX_COMMENT"] = ctx.Values.Int((1 << 16) - 1);
            return ctx.Values.Module("zipfile", m);
        }

        private static BytesIOValue FileArg(EvalContext c, ScriptValue v, string who)
        {
            BytesIOValue fp = v as BytesIOValue;
            if (fp != null && fp.TypeInfo == BytesIOValue.BytesIOType)
                return fp;
            if (v is StrValue || v is BytesValue)
            {
                throw Raise.TypeError(c, who + "() takes a file object (io.BytesIO), not a path: there is no file system"
                    + " in this environment; wrap the archive bytes in io.BytesIO");
            }
            throw Raise.TypeError(c, who + "() needs an io.BytesIO in this dialect, not " + v.PyTypeName);
        }

        // ZipFile(file, mode='r', compression=ZIP_STORED, allowZip64=True, compresslevel=None, *,
        //         strict_timestamps=True, metadata_encoding=None)
        private static ScriptValue ZipFileCtor(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext c)
        {
            Args.Between(c, a, "ZipFile", 0, 5);
            ScriptValue file = a.Length > 0 ? a[0] : null;
            ScriptValue mode = a.Length > 1 ? a[1] : null, compression = a.Length > 2 ? a[2] : null;
            ScriptValue allowZip64 = a.Length > 3 ? a[3] : null, compressLevel = a.Length > 4 ? a[4] : null;
            ScriptValue metadataEncoding = null;
            for (int i = 0; i < kw.Count; i++)
            {
                ScriptValue v = kw.ValueAt(i);
                switch (kw.NameAt(i))
                {
                    case "file": file = v; break;
                    case "mode": mode = v; break;
                    case "compression": compression = v; break;
                    case "allowZip64": allowZip64 = v; break;
                    case "compresslevel": compressLevel = v; break;
                    case "strict_timestamps": break;   // matters only for write() from disk, which is refused
                    case "metadata_encoding": metadataEncoding = v; break;
                    default: throw Raise.TypeError(c, "ZipFile() got an unexpected keyword argument '" + kw.NameAt(i) + "'");
                }
            }
            if (file == null)
                throw Raise.TypeError(c, "ZipFile() missing required argument 'file' (pos 1)");
            string ms = mode == null ? "r" : (mode as StrValue)?.Value;
            if (ms != "r" && ms != "w" && ms != "x" && ms != "a")
                throw Raise.ValueError(c, "ZipFile requires mode 'r', 'w', 'x', or 'a'");
            int comp = compression == null ? ZipFileValue.Stored : Coerce.ToInt32(c, compression, "compression");
            ZipFileValue.CheckCompression(c, comp);
            ScriptValue level = compressLevel ?? c.Values.None;
            if (level.Kind != ValueKind.None)
                Coerce.ToInt32(c, level, "compresslevel");
            string enc = null;
            if (metadataEncoding != null && metadataEncoding.Kind != ValueKind.None)
            {
                if (ms != "r")
                    throw Raise.ValueError(c, "metadata_encoding is only supported for reading files");
                enc = BytesCodec.Normalize(c, metadataEncoding);
            }
            BytesIOValue fp = FileArg(c, file, "ZipFile");
            return new ZipFileValue(c, fp, ms[0], comp, level, allowZip64 == null || allowZip64.IsTruthy(c), enc);
        }

        // ZipInfo(filename='NoName', date_time=(1980, 1, 1, 0, 0, 0))
        private static ScriptValue ZipInfoCtor(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext c)
        {
            Args.AtMost(c, a, "ZipInfo", 2);
            ScriptValue name = a.Length > 0 ? a[0] : null, dateTime = a.Length > 1 ? a[1] : null;
            for (int i = 0; i < kw.Count; i++)
            {
                switch (kw.NameAt(i))
                {
                    case "filename": name = kw.ValueAt(i); break;
                    case "date_time": dateTime = kw.ValueAt(i); break;
                    default: throw Raise.TypeError(c, "ZipInfo() got an unexpected keyword argument '" + kw.NameAt(i) + "'");
                }
            }
            string filename = "NoName";
            if (name != null)
            {
                StrValue s = name as StrValue;
                if (s == null)
                    throw Raise.TypeError(c, "ZipInfo() filename must be str, not " + name.PyTypeName);
                filename = s.Value;
            }
            int[] dt = dateTime == null ? ZipInfoValue.DefaultDateTime() : ZipInfoValue.ReadDateTime(c, dateTime);
            return new ZipInfoValue(c, filename, dt);
        }

        private static ScriptValue IsZipfile(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext c)
        {
            Args.Exactly(c, a, kw, "is_zipfile", 1);
            BytesIOValue fp = a[0] as BytesIOValue;
            if (fp == null)
            {
                if (a[0] is StrValue || a[0] is BytesValue)
                    FileArg(c, a[0], "is_zipfile");
                return c.Values.Bool(false);
            }
            fp.CheckOpen(c);
            return c.Values.Bool(ZipFileValue.LooksLikeZip(c, fp));
        }
    }
}

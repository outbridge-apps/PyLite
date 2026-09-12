using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Syntax;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // Shared str<->bytes codec for the binary stack. Supported encodings: utf-8, utf-8-sig
    // ascii, latin-1, cp1251, cp1252, cp866, koi8-r, iso-8859-5, utf-16, utf-16-le, utf-16-be. Strict failures raise
    // UnicodeDecodeError / UnicodeEncodeError with CPython's (encoding, object, start, end, reason) args,
    // so the message text and e.start/e.reason match.
    //
    // Error handlers: strict, ignore, replace, backslashreplace, xmlcharrefreplace and namereplace (both encode only) and
    // surrogateescape. Every failure site goes through EncodeFallback / DecodeFallback so the set is the
    // same for every codec, and an unknown name is a LookupError instead of silently meaning something.
    // Like CPython the handler is resolved only once a character or byte actually fails, so a bogus name
    // over clean input is not an error.
    internal static class BytesCodec
    {
        private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

        internal static string Normalize(EvalContext ctx, ScriptValue encArg)
        {
            StrValue s = encArg as StrValue;
            if (s == null)
                throw Raise.TypeError(ctx, "encoding must be a string");
            string e = s.Value.ToLowerInvariant().Replace('_', '-');
            switch (e)
            {
                case "utf-8": case "utf8": case "u8": return "utf-8";
                case "utf-8-sig": case "utf8-sig": return "utf-8-sig";
                case "ascii": case "us-ascii": return "ascii";
                case "latin-1": case "latin1": case "iso-8859-1": case "l1": return "latin-1";
                case "cp1251": case "windows-1251": return "cp1251";
                case "cp1252": case "windows-1252": return "cp1252";
                // the three other Cyrillic pages an integration meets: DOS exports, old mail, ISO feeds
                case "cp866": case "866": case "ibm866": return "cp866";
                case "koi8-r": case "koi8r": case "cskoi8r": return "koi8-r";
                case "iso-8859-5": case "iso8859-5": case "cyrillic": case "csisolatincyrillic": return "iso-8859-5";
                case "utf-16": case "utf16": case "u16": return "utf-16";
                case "utf-16-le": case "utf-16le": case "utf16-le": return "utf-16-le";
                case "utf-16-be": case "utf-16be": case "utf16-be": return "utf-16-be";
                default: throw Raise.Make(ctx, PyExceptionTypes.LookupError, "unknown encoding: " + s.Value);
            }
        }

        internal static byte[] Encode(EvalContext ctx, string s, string enc)
        {
            return Encode(ctx, s, enc, "strict");
        }

        internal static byte[] Encode(EvalContext ctx, string s, string enc, string errors)
        {
            ctx.Budget.ChargeLinear(s.Length);   // every codec is one pass over the text
            switch (enc)
            {
                case "ascii":
                    return EncodeNarrow(ctx, s, 0x7F, "ascii", errors);
                case "latin-1":
                    return EncodeNarrow(ctx, s, 0xFF, "latin-1", errors);
                case "cp1251":
                    return EncodeCodePage(ctx, s, 1251, "cp1251", errors);
                case "cp1252":
                    return EncodeCodePage(ctx, s, 1252, "cp1252", errors);
                case "cp866":
                    return EncodeCodePage(ctx, s, 866, "cp866", errors);
                case "koi8-r":
                    return EncodeCodePage(ctx, s, 20866, "koi8-r", errors);
                case "iso-8859-5":
                    return EncodeCodePage(ctx, s, 28595, "iso-8859-5", errors);
                case "utf-16":
                    return Concat(new byte[] { 0xFF, 0xFE }, EncodeUtf16(ctx, s, enc, errors, false));
                case "utf-16-le":
                    return EncodeUtf16(ctx, s, enc, errors, false);
                case "utf-16-be":
                    return EncodeUtf16(ctx, s, enc, errors, true);
                case "utf-8-sig":
                    return Concat(Utf8Bom, EncodeUtf8(ctx, s, errors));
                default:   // utf-8
                    return EncodeUtf8(ctx, s, errors);
            }
        }

        internal static string Decode(EvalContext ctx, byte[] data, string enc, string errors)
        {
            ctx.Budget.ChargeLinear(data.Length);
            switch (enc)
            {
                case "ascii":
                    return DecodeNarrow(ctx, data, 0x7F, "ascii", errors);
                case "latin-1":
                    return DecodeNarrow(ctx, data, 0xFF, "latin-1", errors);
                case "cp1251":
                    return DecodeCodePage(ctx, data, 1251, "cp1251", errors);
                case "cp1252":
                    return DecodeCodePage(ctx, data, 1252, "cp1252", errors);
                case "cp866":
                    return DecodeCodePage(ctx, data, 866, "cp866", errors);
                case "koi8-r":
                    return DecodeCodePage(ctx, data, 20866, "koi8-r", errors);
                case "iso-8859-5":
                    return DecodeCodePage(ctx, data, 28595, "iso-8859-5", errors);
                case "utf-16":
                case "utf-16-le":
                case "utf-16-be":
                    return DecodeUtf16(ctx, data, enc, errors);
                case "utf-8-sig":
                    return DecodeUtf8(ctx, StripBom(data, Utf8Bom), errors);
                default:   // utf-8
                    return DecodeUtf8(ctx, data, errors);
            }
        }

        // ---- error handlers ----

        // One unencodable run s[i..end) resolved per the handler: either replacement TEXT, which the
        // caller re-encodes in the target codec exactly as CPython does, or raw BYTES (surrogateescape).
        private static void EncodeFallback(EvalContext ctx, string enc, string s, int i, int end,
            string reason, string errors, out string text, out byte[] raw)
        {
            text = null;
            raw = null;
            switch (errors)
            {
                case "strict":
                    throw EncodeError(ctx, enc, s, i, end, reason);
                case "ignore":
                    text = "";
                    return;
                case "replace":
                    text = "?";
                    return;
                case "backslashreplace":
                    text = Backslashed(s, i, end);
                    return;
                case "xmlcharrefreplace":
                    text = CharRefs(s, i, end);
                    return;
                case "namereplace":
                    text = Named(s, i, end);
                    return;
                case "surrogateescape":
                    raw = SurrogateBytes(ctx, enc, s, i, end, reason);
                    return;
                default:
                    throw UnknownHandler(ctx, errors);
            }
        }

        // The replacement text for one undecodable run data[i..end).
        private static string DecodeFallback(EvalContext ctx, string enc, byte[] data, int i, int end,
            string reason, string errors)
        {
            switch (errors)
            {
                case "strict":
                    throw DecodeError(ctx, enc, data, i, end, reason);
                case "ignore":
                    return "";
                case "replace":
                    return "�";        // one per malformed run, not per byte
                case "backslashreplace":
                    return BackslashedBytes(data, i, end);
                case "surrogateescape":
                    return SurrogateChars(ctx, enc, data, i, end, reason);
                case "xmlcharrefreplace":
                case "namereplace":
                    throw Raise.TypeError(ctx, "don't know how to handle UnicodeDecodeError in error callback");
                default:
                    throw UnknownHandler(ctx, errors);
            }
        }

        private static ScriptException UnknownHandler(EvalContext ctx, string errors)
        {
            return Raise.Make(ctx, PyExceptionTypes.LookupError, "unknown error handler name '" + errors + "'");
        }

        // A failing run is one UTF-16 unit, or the two units of a surrogate pair so the handlers see the
        // code point CPython sees ("\\U0001f600", not two lone surrogates).
        private static int FailEnd(string s, int i)
        {
            if (i + 1 < s.Length && char.IsHighSurrogate(s[i]) && char.IsLowSurrogate(s[i + 1]))
                return i + 2;
            return i + 1;
        }

        private static string Backslashed(string s, int i, int end)
        {
            var sb = new StringBuilder();
            while (i < end)
            {
                if (i + 1 < end && char.IsHighSurrogate(s[i]) && char.IsLowSurrogate(s[i + 1]))
                {
                    sb.Append("\\U").Append(char.ConvertToUtf32(s[i], s[i + 1]).ToString("x8", CultureInfo.InvariantCulture));
                    i += 2;
                    continue;
                }
                sb.Append(s[i] > 0xFF ? "\\u" : "\\x")
                  .Append(((int)s[i]).ToString(s[i] > 0xFF ? "x4" : "x2", CultureInfo.InvariantCulture));
                i++;
            }
            return sb.ToString();
        }

        private static string CharRefs(string s, int i, int end)
        {
            var sb = new StringBuilder();
            while (i < end)
            {
                int cp = s[i];
                int width = 1;
                if (i + 1 < end && char.IsHighSurrogate(s[i]) && char.IsLowSurrogate(s[i + 1]))
                {
                    cp = char.ConvertToUtf32(s[i], s[i + 1]);
                    width = 2;
                }
                sb.Append("&#").Append(cp.ToString(CultureInfo.InvariantCulture)).Append(';');
                i += width;
            }
            return sb.ToString();
        }

        // namereplace: \N{NAME} for a character that has one, the backslash escape for one that does not.
        private static string Named(string s, int i, int end)
        {
            var sb = new StringBuilder();
            while (i < end)
            {
                int width = FailEnd(s, i) - i;
                int cp = width == 2 ? char.ConvertToUtf32(s[i], s[i + 1]) : s[i];
                string name;
                if (PyUnicodeNames.TryName(cp, out name))
                    sb.Append("\\N{").Append(name).Append('}');
                else
                    sb.Append(Backslashed(s, i, i + width));
                i += width;
            }
            return sb.ToString();
        }

        private static string BackslashedBytes(byte[] data, int i, int end)
        {
            var sb = new StringBuilder((end - i) * 4);
            for (; i < end; i++)
                sb.Append("\\x").Append(data[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        // surrogateescape only round-trips bytes that were escaped INTO the U+DC80..U+DCFF band; any other
        // unencodable character is still an error, as in CPython.
        private static byte[] SurrogateBytes(EvalContext ctx, string enc, string s, int i, int end, string reason)
        {
            var buf = new byte[end - i];
            for (int k = i; k < end; k++)
            {
                if (s[k] < '\uDC80' || s[k] > '\uDCFF')
                    throw EncodeError(ctx, enc, s, i, end, reason);
                buf[k - i] = (byte)(s[k] & 0xFF);
            }
            return buf;
        }

        // ...and only escapes non-ASCII bytes; an undecodable byte below 0x80 has no lone-surrogate slot.
        private static string SurrogateChars(EvalContext ctx, string enc, byte[] data, int i, int end, string reason)
        {
            var sb = new StringBuilder(end - i);
            for (int k = i; k < end; k++)
            {
                if (data[k] < 0x80)
                    throw DecodeError(ctx, enc, data, i, end, reason);
                sb.Append((char)(0xDC00 + data[k]));
            }
            return sb.ToString();
        }

        private static void AppendAscii(List<byte> buf, string text)
        {
            for (int k = 0; k < text.Length; k++)
                buf.Add((byte)text[k]);
        }

        // ---- utf-8 ----

        // .NET silently turns a lone surrogate into U+FFFD where CPython raises, so a string carrying one
        // leaves the BCL encoder for the manual walk; everything else takes the fast path.
        private static byte[] EncodeUtf8(EvalContext ctx, string s, string errors)
        {
            if (!HasSurrogate(s))
                return Encoding.UTF8.GetBytes(s);
            var buf = new List<byte>(s.Length + 8);
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    AppendUtf8(buf, char.ConvertToUtf32(c, s[i + 1]));
                    i += 2;
                    continue;
                }
                if (char.IsSurrogate(c))
                {
                    string text;
                    byte[] raw;
                    EncodeFallback(ctx, "utf-8", s, i, i + 1, "surrogates not allowed", errors, out text, out raw);
                    if (raw != null)
                        buf.AddRange(raw);
                    else
                        AppendAscii(buf, text);
                    i++;
                    continue;
                }
                AppendUtf8(buf, c);
                i++;
            }
            return buf.ToArray();
        }

        private static bool HasSurrogate(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (char.IsSurrogate(s[i]))
                    return true;
            return false;
        }

        private static void AppendUtf8(List<byte> buf, int cp)
        {
            if (cp < 0x80)
            {
                buf.Add((byte)cp);
            }
            else if (cp < 0x800)
            {
                buf.Add((byte)(0xC0 | (cp >> 6)));
                buf.Add((byte)(0x80 | (cp & 0x3F)));
            }
            else if (cp < 0x10000)
            {
                buf.Add((byte)(0xE0 | (cp >> 12)));
                buf.Add((byte)(0x80 | ((cp >> 6) & 0x3F)));
                buf.Add((byte)(0x80 | (cp & 0x3F)));
            }
            else
            {
                buf.Add((byte)(0xF0 | (cp >> 18)));
                buf.Add((byte)(0x80 | ((cp >> 12) & 0x3F)));
                buf.Add((byte)(0x80 | ((cp >> 6) & 0x3F)));
                buf.Add((byte)(0x80 | (cp & 0x3F)));
            }
        }

        private static string DecodeUtf8(EvalContext ctx, byte[] data, string errors)
        {
            int start, end;
            string reason;
            if (!NextUtf8Error(data, 0, out start, out end, out reason))
                return Encoding.UTF8.GetString(data);
            var sb = new StringBuilder(data.Length);
            int at = 0;
            do
            {
                if (start > at)
                    sb.Append(Encoding.UTF8.GetString(data, at, start - at));
                sb.Append(DecodeFallback(ctx, "utf-8", data, start, end, reason, errors));
                at = end;
            }
            while (NextUtf8Error(data, at, out start, out end, out reason));
            if (at < data.Length)
                sb.Append(Encoding.UTF8.GetString(data, at, data.Length - at));
            return sb.ToString();
        }

        // Walks the UTF-8 grammar and reports the next malformed run the way CPython does: start..end
        // covers the start byte plus the continuation bytes accepted before the bad one, and reason names
        // the failure. Scanning resumes at end, so the byte that broke the sequence is re-read as a start.
        private static bool NextUtf8Error(byte[] data, int from, out int start, out int end, out string reason)
        {
            int i = from;
            int n = data.Length;
            while (i < n)
            {
                byte b = data[i];
                if (b < 0x80)
                {
                    i++;
                    continue;
                }
                int need;
                if (b >= 0xC2 && b <= 0xDF)
                    need = 1;
                else if (b >= 0xE0 && b <= 0xEF)
                    need = 2;
                else if (b >= 0xF0 && b <= 0xF4)
                    need = 3;
                else
                {
                    start = i;
                    end = i + 1;
                    reason = "invalid start byte";
                    return true;
                }
                for (int k = 1; k <= need; k++)
                {
                    if (i + k >= n)
                    {
                        start = i;
                        end = n;
                        reason = "unexpected end of data";
                        return true;
                    }
                    byte c = data[i + k];
                    int lo = 0x80, hi = 0xBF;
                    if (k == 1)
                    {
                        if (b == 0xE0)
                            lo = 0xA0;   // no overlongs
                        else if (b == 0xED)
                            hi = 0x9F;   // no surrogates
                        else if (b == 0xF0)
                            lo = 0x90;
                        else if (b == 0xF4)
                            hi = 0x8F;   // <= U+10FFFF
                    }
                    if (c < lo || c > hi)
                    {
                        start = i;
                        end = i + k;
                        reason = "invalid continuation byte";
                        return true;
                    }
                }
                i += need + 1;
            }
            start = 0;
            end = 0;
            reason = null;
            return false;
        }

        // ---- single-byte codecs ----

        private static byte[] EncodeNarrow(EvalContext ctx, string s, int max, string enc, string errors)
        {
            var buf = new List<byte>(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                if (s[i] <= max)
                {
                    buf.Add((byte)s[i]);
                    i++;
                    continue;
                }
                int end = FailEnd(s, i);
                string text;
                byte[] raw;
                EncodeFallback(ctx, enc, s, i, end, "ordinal not in range(" + (max + 1) + ")", errors, out text, out raw);
                if (raw != null)
                    buf.AddRange(raw);
                else
                    AppendAscii(buf, text);   // every handler's text is ASCII, which these codecs encode 1:1
                i = end;
            }
            return buf.ToArray();
        }

        private static string DecodeNarrow(EvalContext ctx, byte[] data, int max, string enc, string errors)
        {
            var sb = new StringBuilder(data.Length);
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] > max)
                {
                    sb.Append(DecodeFallback(ctx, enc, data, i, i + 1,
                        "ordinal not in range(" + (max + 1) + ")", errors));
                    continue;
                }
                sb.Append((char)data[i]);
            }
            return sb.ToString();
        }

        // Windows single-byte code pages. The framework's tables are read once into a 256-entry map,
        // because they round-trip the code page's UNDEFINED positions to the same-numbered C1 control
        // (cp1251 0x98; cp1252 0x81 0x8D 0x8F 0x90 0x9D) while CPython has no mapping there at all.
        // Those positions are marked '￿' — a non-character no single-byte page can produce.
        private static readonly ConcurrentDictionary<int, char[]> DecodeTables = new ConcurrentDictionary<int, char[]>();
        private static readonly ConcurrentDictionary<int, Dictionary<char, byte>> EncodeTables =
            new ConcurrentDictionary<int, Dictionary<char, byte>>();

        private static bool IsUndefined(int codePage, int b)
        {
            switch (codePage)
            {
                case 1251:
                    return b == 0x98;
                case 1252:
                    return b == 0x81 || b == 0x8D || b == 0x8F || b == 0x90 || b == 0x9D;
                default:
                    return false;   // cp866, koi8-r and iso-8859-5 define all 256 positions in CPython too
            }
        }

        private static char[] DecodeTable(int codePage)
        {
            return DecodeTables.GetOrAdd(codePage, cp =>
            {
                Encoding e = Encoding.GetEncoding(cp);
                var one = new byte[1];
                var t = new char[256];
                for (int i = 0; i < 256; i++)
                {
                    one[0] = (byte)i;
                    string s = e.GetString(one);
                    t[i] = s.Length == 1 && !IsUndefined(cp, i) ? s[0] : '￿';
                }
                return t;
            });
        }

        private static Dictionary<char, byte> EncodeTable(int codePage)
        {
            return EncodeTables.GetOrAdd(codePage, cp =>
            {
                char[] dec = DecodeTable(cp);
                var m = new Dictionary<char, byte>(256);
                for (int i = 0; i < 256; i++)
                    if (dec[i] != '￿' && !m.ContainsKey(dec[i]))
                        m[dec[i]] = (byte)i;
                return m;
            });
        }

        private static byte[] EncodeCodePage(EvalContext ctx, string s, int codePage, string enc, string errors)
        {
            Dictionary<char, byte> table = EncodeTable(codePage);
            var buf = new List<byte>(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                byte b;
                if (table.TryGetValue(s[i], out b))
                {
                    buf.Add(b);
                    i++;
                    continue;
                }
                int end = FailEnd(s, i);
                string text;
                byte[] raw;
                EncodeFallback(ctx, enc, s, i, end, "character maps to <undefined>", errors, out text, out raw);
                if (raw != null)
                    buf.AddRange(raw);
                else
                    AppendAscii(buf, text);
                i = end;
            }
            return buf.ToArray();
        }

        private static string DecodeCodePage(EvalContext ctx, byte[] data, int codePage, string enc, string errors)
        {
            char[] table = DecodeTable(codePage);
            var sb = new StringBuilder(data.Length);
            for (int i = 0; i < data.Length; i++)
            {
                char ch = table[data[i]];
                if (ch == '￿')
                {
                    sb.Append(DecodeFallback(ctx, enc, data, i, i + 1, "character maps to <undefined>", errors));
                    continue;
                }
                sb.Append(ch);
            }
            return sb.ToString();
        }

        // ---- utf-16 ----

        // A lone surrogate has no utf-16 encoding: CPython raises, .NET would silently emit U+FFFD. Only a
        // string that carries one leaves the BCL encoder.
        private static byte[] EncodeUtf16(EvalContext ctx, string s, string enc, string errors, bool bigEndian)
        {
            var e = new UnicodeEncoding(bigEndian, false);
            if (!HasLoneSurrogate(s))
                return e.GetBytes(s);
            var buf = new List<byte>(s.Length * 2 + 8);
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    AppendUnit(buf, c, bigEndian);
                    AppendUnit(buf, s[i + 1], bigEndian);
                    i += 2;
                    continue;
                }
                if (char.IsSurrogate(c))
                {
                    string text;
                    byte[] raw;
                    EncodeFallback(ctx, enc, s, i, i + 1, "surrogates not allowed", errors, out text, out raw);
                    if (raw != null)
                        buf.AddRange(raw);
                    else
                        for (int k = 0; k < text.Length; k++)   // the replacement text is re-encoded as units
                            AppendUnit(buf, text[k], bigEndian);
                    i++;
                    continue;
                }
                AppendUnit(buf, c, bigEndian);
                i++;
            }
            return buf.ToArray();
        }

        private static bool HasLoneSurrogate(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (!char.IsSurrogate(s[i]))
                    continue;
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    i++;
                    continue;
                }
                return true;
            }
            return false;
        }

        private static void AppendUnit(List<byte> buf, char c, bool bigEndian)
        {
            if (bigEndian)
            {
                buf.Add((byte)(c >> 8));
                buf.Add((byte)c);
            }
            else
            {
                buf.Add((byte)c);
                buf.Add((byte)(c >> 8));
            }
        }

        // "utf-16" consumes a BOM and defaults to little-endian without one (CPython's behaviour on a
        // little-endian host); the explicit -le/-be forms never touch a BOM. Walking the units by hand
        // (rather than through a DecoderFallback) is what gives every handler the exact malformed span.
        private static string DecodeUtf16(EvalContext ctx, byte[] data, string enc, string errors)
        {
            bool bigEndian = enc == "utf-16-be";
            int start = 0;
            if (enc == "utf-16" && data.Length >= 2)
            {
                if (data[0] == 0xFF && data[1] == 0xFE)
                {
                    bigEndian = false;
                    start = 2;
                }
                else if (data[0] == 0xFE && data[1] == 0xFF)
                {
                    bigEndian = true;
                    start = 2;
                }
            }
            var sb = new StringBuilder((data.Length - start) / 2 + 1);
            int i = start;
            while (i + 1 < data.Length)
            {
                char c = Unit(data, i, bigEndian);
                if (char.IsHighSurrogate(c) && i + 3 < data.Length && char.IsLowSurrogate(Unit(data, i + 2, bigEndian)))
                {
                    sb.Append(c).Append(Unit(data, i + 2, bigEndian));
                    i += 4;
                    continue;
                }
                if (char.IsSurrogate(c))
                {
                    sb.Append(DecodeFallback(ctx, enc, data, i, i + 2, "illegal encoding", errors));
                    i += 2;
                    continue;
                }
                sb.Append(c);
                i += 2;
            }
            if (i < data.Length)
                sb.Append(DecodeFallback(ctx, enc, data, i, data.Length, "truncated data", errors));
            return sb.ToString();
        }

        private static char Unit(byte[] data, int i, bool bigEndian)
        {
            return bigEndian ? (char)((data[i] << 8) | data[i + 1]) : (char)((data[i + 1] << 8) | data[i]);
        }

        // ---- shared ----

        private static byte[] StripBom(byte[] data, byte[] bom)
        {
            if (data.Length < bom.Length)
                return data;
            for (int i = 0; i < bom.Length; i++)
                if (data[i] != bom[i])
                    return data;
            var rest = new byte[data.Length - bom.Length];
            System.Buffer.BlockCopy(data, bom.Length, rest, 0, rest.Length);
            return rest;
        }

        private static byte[] Concat(byte[] head, byte[] tail)
        {
            var buf = new byte[head.Length + tail.Length];
            System.Buffer.BlockCopy(head, 0, buf, 0, head.Length);
            System.Buffer.BlockCopy(tail, 0, buf, head.Length, tail.Length);
            return buf;
        }

        private static ScriptException DecodeError(EvalContext ctx, string enc, byte[] data, int start, int end, string reason)
        {
            ScriptValue[] args =
            {
                ctx.Values.Str(enc), ctx.Values.Bytes(data), ctx.Values.Int(start), ctx.Values.Int(end), ctx.Values.Str(reason),
            };
            return Raise.Make(ctx, PyExceptionTypes.UnicodeDecodeError, args);
        }

        private static ScriptException EncodeError(EvalContext ctx, string enc, string s, int start, int end, string reason)
        {
            ScriptValue[] args =
            {
                ctx.Values.Str(enc), ctx.Values.Str(s), ctx.Values.Int(start), ctx.Values.Int(end), ctx.Values.Str(reason),
            };
            return Raise.Make(ctx, PyExceptionTypes.UnicodeEncodeError, args);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Modules
{
    // uuid: UUID over byte[16] big-endian; uuid1/3/4/5 and NAMESPACE_* constants.
    // MD5/SHA1 from System.Security.Cryptography (no managed MD5 port). uuid4 reads the OS generator, the uuid1
    // node is drawn once per run and clock_seq from ctx.Random; time from ctx.Clock.
    internal static class UuidModule
    {
        private const long Uuid1582Offset = 122192928000000000L;   // 0x01b21dd213814000

        // uuid1's state, per run: a node shared by the process told one tenant's script that another's ran in the same
        // process. A random multicast node (the run's random.seed does not reach it) and the last timestamp handed out.
        private sealed class Uuid1State
        {
            internal BigInteger Node;
            internal long LastTimestamp;
        }

        private static Uuid1State RunState(EvalContext ctx)
        {
            object o;
            if (ctx.ModuleState.TryGetValue("uuid1", out o))
                return (Uuid1State)o;
            var bytes = new byte[8];
            using (var rng = new RNGCryptoServiceProvider())
                rng.GetBytes(bytes);
            var s = new Uuid1State
            {
                Node = (new BigInteger(BitConverter.ToUInt64(bytes, 0)) & new BigInteger(0xFFFFFFFFFFFF))
                    | new BigInteger(0x010000000000),   // the multicast bit marks a node that is no real address
            };
            ctx.ModuleState["uuid1"] = s;
            return s;
        }
        private static readonly BigInteger TwoPow128 = BigInteger.One << 128;

        public static ModuleValue Create(EvalContext ctx)
        {
            var m = new Dictionary<string, ScriptValue>(StringComparer.Ordinal);
            m["UUID"] = ctx.Values.EngineType("UUID", Args.KnownKeywords("UUID", 6, UuidCtor, "hex", "bytes", "bytes_le", "fields", "int", "version", "is_safe"), null, null);
            m["uuid1"] = BuiltinFunctionValue.Make("uuid1", Args.KnownKeywords("uuid1", 2, Uuid1, "node", "clock_seq"));
            m["uuid3"] = BuiltinFunctionValue.Make("uuid3", Args.BoundKeywords("uuid3", 2, Uuid3, "namespace", "name"));
            m["uuid4"] = BuiltinFunctionValue.Make("uuid4", Args.Positional("uuid4", 0, 0, Uuid4));
            m["uuid5"] = BuiltinFunctionValue.Make("uuid5", Args.BoundKeywords("uuid5", 2, Uuid5, "namespace", "name"));
            m["NAMESPACE_DNS"] = FromHex("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
            m["NAMESPACE_URL"] = FromHex("6ba7b811-9dad-11d1-80b4-00c04fd430c8");
            m["NAMESPACE_OID"] = FromHex("6ba7b812-9dad-11d1-80b4-00c04fd430c8");
            m["NAMESPACE_X500"] = FromHex("6ba7b814-9dad-11d1-80b4-00c04fd430c8");
            m["RESERVED_NCS"] = ctx.Values.Str(UuidValue.RESERVED_NCS);
            m["RFC_4122"] = ctx.Values.Str(UuidValue.RFC_4122);
            m["RESERVED_MICROSOFT"] = ctx.Values.Str(UuidValue.RESERVED_MICROSOFT);
            m["RESERVED_FUTURE"] = ctx.Values.Str(UuidValue.RESERVED_FUTURE);
            return ctx.Values.Module("uuid", m);
        }

        private static UuidValue FromHex(string canonical)
        {
            return new UuidValue(UuidValue.FromInt(ParseHex(canonical)));
        }

        // UUID(hex=None, int=None, bytes=None, *, version=None). bytes_le/fields -> v2.
        private static ScriptValue UuidCtor(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            // UUID(hex, bytes, bytes_le, fields, int, version): the positions after the first were dropped
            ScriptValue hexArg = args.Length >= 1 ? args[0] : null;
            ScriptValue bytesArg = args.Length >= 2 ? args[1] : null, bytesLeArg = args.Length >= 3 ? args[2] : null;
            ScriptValue fieldsArg = args.Length >= 4 ? args[3] : null, intArg = args.Length >= 5 ? args[4] : null;
            ScriptValue versionArg = args.Length >= 6 ? args[5] : null, v;
            if (kw.TryGet("hex", out v)) hexArg = v;
            if (kw.TryGet("int", out v)) intArg = v;
            if (kw.TryGet("bytes", out v)) bytesArg = v;
            if (kw.TryGet("bytes_le", out v)) bytesLeArg = v;
            if (kw.TryGet("fields", out v)) fieldsArg = v;
            if (kw.TryGet("version", out v)) versionArg = v;

            bool hasHex = hexArg != null && hexArg.Kind != ValueKind.None;
            bool hasInt = intArg != null && intArg.Kind != ValueKind.None;
            bool hasBytes = bytesArg != null && bytesArg.Kind != ValueKind.None;
            bool hasBytesLe = bytesLeArg != null && bytesLeArg.Kind != ValueKind.None;
            bool hasFields = fieldsArg != null && fieldsArg.Kind != ValueKind.None;
            int given = (hasHex ? 1 : 0) + (hasInt ? 1 : 0) + (hasBytes ? 1 : 0) + (hasBytesLe ? 1 : 0) + (hasFields ? 1 : 0);
            if (given != 1)
                throw Raise.TypeError(ctx, "one of the hex, bytes, bytes_le, fields, or int arguments must be given");

            BigInteger intval;
            if (hasHex)
            {
                StrValue hs = hexArg as StrValue;
                if (hs == null)
                    throw Raise.TypeError(ctx, "badly formed hexadecimal UUID string");
                intval = ParseHexImpl(hs.Value, ctx);
            }
            else if (hasBytes)
            {
                BytesValue bv = bytesArg as BytesValue;
                if (bv == null || bv.Data.Length != 16)
                    throw Raise.ValueError(ctx, "bytes is not a 16-char string");
                intval = BytesToInt(bv.Data);
            }
            else if (hasBytesLe)
            {
                BytesValue bl = bytesLeArg as BytesValue;
                if (bl == null || bl.Data.Length != 16)
                    throw Raise.ValueError(ctx, "bytes_le is not a 16-char string");
                intval = BytesToInt(UuidValue.SwapLe(bl.Data));   // LE first three fields -> BE
            }
            else if (hasFields)
            {
                intval = FromFields(ctx, fieldsArg);
            }
            else
            {
                if (intArg.Kind != ValueKind.Int && intArg.Kind != ValueKind.Bool)
                    throw Raise.TypeError(ctx, "int is not an integer");
                intval = NumericOps.AsBigInteger(intArg);
                if (intval < 0 || intval >= TwoPow128)
                    throw Raise.ValueError(ctx, "int is out of range (need a 128-bit value)");
            }

            if (versionArg != null && versionArg.Kind != ValueKind.None)
            {
                BigInteger ver = Coerce.ToIndex(ctx, versionArg, "version");
                if (ver < 1 || ver > 5)
                    throw Raise.ValueError(ctx, "illegal version number");
                intval = ApplyVariantVersion(intval, (int)ver);
            }
            return new UuidValue(UuidValue.FromInt(intval));
        }

        // strip urn:/uuid: prefixes and braces, remove dashes -> 32 hex digits. A null ctx (module constants)
        // throws FormatException on bad input; a real ctx throws a script ValueError.
        private static BigInteger ParseHex(string hex)
        {
            return ParseHexImpl(hex, null);
        }

        private static BigInteger ParseHexImpl(string hex, EvalContext ctx)
        {
            string h = hex.Replace("urn:", "").Replace("uuid:", "").Trim('{', '}').Replace("-", "");   // no whitespace is stripped
            if (h.Length != 32 || !IsHex(h))
            {
                if (ctx != null)
                    throw Raise.ValueError(ctx, "badly formed hexadecimal UUID string");
                throw new FormatException("bad uuid hex");
            }
            return BigInteger.Parse("0" + h, System.Globalization.NumberStyles.HexNumber);
        }

        private static bool IsHex(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok)
                    return false;
            }
            return true;
        }

        // fields=(time_low, time_mid, time_hi_version, clock_seq_hi_variant, clock_seq_low, node).
        private static BigInteger FromFields(EvalContext ctx, ScriptValue fieldsArg)
        {
            IScriptIterator it = PyOps.GetIterator(fieldsArg, ctx);
            var vals = new List<BigInteger>(6);
            ScriptValue item;
            while (it.MoveNext(ctx, out item))
            {
                if (item.Kind != ValueKind.Int && item.Kind != ValueKind.Bool)
                    throw Raise.TypeError(ctx, "fields must be integers");
                if (vals.Count == 6)
                    throw Raise.ValueError(ctx, "fields is not a 6-tuple");   // an endless iterator stops here
                vals.Add(NumericOps.AsBigInteger(item));
            }
            if (vals.Count != 6)
                throw Raise.ValueError(ctx, "fields is not a 6-tuple");
            CheckField(ctx, vals[0], 32, "field 1 out of range (need a 32-bit value)");
            CheckField(ctx, vals[1], 16, "field 2 out of range (need a 16-bit value)");
            CheckField(ctx, vals[2], 16, "field 3 out of range (need a 16-bit value)");
            CheckField(ctx, vals[3], 8, "field 4 out of range (need an 8-bit value)");
            CheckField(ctx, vals[4], 8, "field 5 out of range (need an 8-bit value)");
            CheckField(ctx, vals[5], 48, "field 6 out of range (need a 48-bit value)");
            return (vals[0] << 96) | (vals[1] << 80) | (vals[2] << 64) | (vals[3] << 56) | (vals[4] << 48) | vals[5];
        }

        private static void CheckField(EvalContext ctx, BigInteger v, int bits, string msg)
        {
            if (v.Sign < 0 || v >= (BigInteger.One << bits))
                throw Raise.ValueError(ctx, msg);
        }

        private static BigInteger ApplyVariantVersion(BigInteger intval, int version)
        {
            intval &= ~(new BigInteger(0xc000) << 48);
            intval |= new BigInteger(0x8000) << 48;             // variant RFC 4122
            intval &= ~(new BigInteger(0xf000) << 64);
            intval |= new BigInteger(version) << 76;            // version
            return intval;
        }

        // ---- uuid1/3/4/5 ----

        // CPython's uuid4 reads os.urandom, not the random module: a seeded run must not repeat its ids.
        private static ScriptValue Uuid4(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            var bytes = new byte[16];
            RandomState.OsBytes(bytes);
            BigInteger intval = BytesToInt(bytes);
            ctx.Values.ChargeObject(2);
            return new UuidValue(UuidValue.FromInt(ApplyVariantVersion(intval, 4)));
        }

        private static ScriptValue Uuid3(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            byte[] digest;
            using (var md5 = new Md5Managed())   // refused on a FIPS-enforcing machine otherwise
                digest = md5.ComputeHash(NamespacePlusName(ctx, args, "uuid3"));
            return HashUuid(digest, 3);
        }

        private static ScriptValue Uuid5(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            byte[] digest;
            using (var sha1 = SHA1.Create())
                digest = sha1.ComputeHash(NamespacePlusName(ctx, args, "uuid5"));
            return HashUuid(digest, 5);
        }

        private static UuidValue HashUuid(byte[] digest, int version)
        {
            var first16 = new byte[16];
            Array.Copy(digest, first16, 16);
            BigInteger intval = BytesToInt(first16);
            return new UuidValue(UuidValue.FromInt(ApplyVariantVersion(intval, version)));
        }

        private static byte[] NamespacePlusName(EvalContext ctx, ScriptValue[] args, string fn)
        {
            Args.Exactly(ctx, args, fn, 2);
            UuidValue ns = args[0] as UuidValue;
            if (ns == null)
                throw Raise.TypeError(ctx, "namespace must be a UUID");
            StrValue name = args[1] as StrValue;
            if (name == null)
                throw Raise.TypeError(ctx, "name must be a string");
            byte[] nameBytes = Utf8Strict(ctx, name.Value);
            var combined = new byte[16 + nameBytes.Length];
            Array.Copy(ns.BytesBE, combined, 16);
            Array.Copy(nameBytes, 0, combined, 16, nameBytes.Length);
            return combined;
        }

        private static byte[] Utf8Strict(EvalContext ctx, string s)
        {
            try
            {
                Encoding enc = Encoding.GetEncoding("utf-8", new EncoderExceptionFallback(), new DecoderExceptionFallback());
                return enc.GetBytes(s);
            }
            catch (EncoderFallbackException)
            {
                throw Raise.ValueError(ctx, "name contains a lone surrogate and cannot be UTF-8 encoded");
            }
        }

        private static ScriptValue Uuid1(ScriptValue self, ScriptValue[] args, KwArgs kw, EvalContext ctx)
        {
            ScriptValue nodeArg = args.Length >= 1 ? args[0] : null;
            ScriptValue clockSeqArg = args.Length >= 2 ? args[1] : null;
            ScriptValue v;
            if (kw.TryGet("node", out v)) nodeArg = v;
            if (kw.TryGet("clock_seq", out v)) clockSeqArg = v;

            RandomState r = Rand(ctx);

            // 60-bit timestamp: 100-ns intervals since 1582-10-15, strictly increasing within the run as CPython's
            // _last_timestamp is; runs differ by their node.
            Uuid1State state = RunState(ctx);
            DateTime now = ctx.Clock.UtcNow;
            long timestamp = (now - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks + Uuid1582Offset;
            if (timestamp <= state.LastTimestamp)
                timestamp = state.LastTimestamp + 1;
            state.LastTimestamp = timestamp;

            BigInteger clockSeq;
            if (clockSeqArg != null && clockSeqArg.Kind != ValueKind.None)
                clockSeq = Support.Coerce.ToIndex(ctx, clockSeqArg, "'" + clockSeqArg.PyTypeName + "' object cannot be interpreted as an integer") & 0x3fff;
            else
                clockSeq = new BigInteger(r.NextUint32()) & 0x3fff;

            BigInteger node;
            if (nodeArg != null && nodeArg.Kind != ValueKind.None)
            {
                node = Support.Coerce.ToIndex(ctx, nodeArg, "'" + nodeArg.PyTypeName + "' object cannot be interpreted as an integer") & new BigInteger(0xFFFFFFFFFFFF);
            }
            else
            {
                node = state.Node;
            }

            BigInteger timeLow = timestamp & 0xffffffff;
            BigInteger timeMid = (timestamp >> 32) & 0xffff;
            BigInteger timeHi = ((timestamp >> 48) & 0x0fff) | 0x1000;   // version 1
            BigInteger clockSeqLow = clockSeq & 0xff;
            BigInteger clockSeqHi = ((clockSeq >> 8) & 0x3f) | 0x80;     // variant RFC 4122

            BigInteger intval = (timeLow << 96) | (timeMid << 80) | (timeHi << 64)
                | (clockSeqHi << 56) | (clockSeqLow << 48) | node;
            return new UuidValue(UuidValue.FromInt(intval));
        }

        private static BigInteger BytesToInt(byte[] b)
        {
            BigInteger v = BigInteger.Zero;
            for (int i = 0; i < 16; i++)
                v = (v << 8) | b[i];
            return v;
        }

        private static RandomState Rand(EvalContext ctx)
        {
            if (ctx.Random == null)
                ctx.Random = RandomState.CreateEntropy();
            return ctx.Random;
        }
    }
}

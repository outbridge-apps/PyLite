using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // zlib + gzip: round-trips, checksum goldens, wbits containers, foreign .NET streams,
    // error paths and the decompression-bomb budget guard.
    [Trait("Category", "Compatibility")]
    public class ZlibGzipTests
    {
        private const string P = "import zlib\nimport gzip\n";

        private static string Body(string body)
        {
            return Ev.Run(P + body).Repr("r");
        }

        private static ScriptException Raises(string body)
        {
            return Assert.Throws<ScriptException>(() => Ev.Run(P + body + "\n"));
        }

        [Theory]
        [InlineData("r = zlib.decompress(zlib.compress(b'hello world'))", "b'hello world'")]
        [InlineData("r = zlib.decompress(zlib.compress(b''))", "b''")]
        [InlineData("r = zlib.decompress(zlib.compress(b'x'))", "b'x'")]
        [InlineData("data = b'abc123' * 20000\nr = zlib.decompress(zlib.compress(data)) == data", "True")]
        [InlineData("data = b'\\x00\\x01\\x02 bin \\xfd\\xfe\\xff\\x80\\x7f' * 500\nr = zlib.decompress(zlib.compress(data)) == data", "True")]
        [InlineData("data = b'level-payload' * 100\nr = all(zlib.decompress(zlib.compress(data, n)) == data for n in [-1, 0, 1, 5, 6, 9])", "True")]
        [InlineData("r = zlib.decompress(zlib.compress(b'raw-payload', wbits=-15), wbits=-15)", "b'raw-payload'")]
        [InlineData("blob = zlib.compress(b'gz-payload', wbits=31)\nr = (blob[0], blob[1], zlib.decompress(blob, wbits=31))", "(31, 139, b'gz-payload')")]
        [InlineData("r = (zlib.decompress(zlib.compress(b'a1', wbits=31), wbits=47), zlib.decompress(zlib.compress(b'a2'), wbits=47))", "(b'a1', b'a2')")]
        [InlineData("b = zlib.compress(b'x')\nr = (b[0], (b[0] * 256 + b[1]) % 31)", "(120, 0)")]
        [InlineData("r = (zlib.MAX_WBITS, zlib.DEFLATED, zlib.Z_DEFAULT_COMPRESSION, zlib.Z_BEST_SPEED, zlib.Z_BEST_COMPRESSION)", "(15, 8, -1, 1, 9)")]
        public void ZlibRoundTrips(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("r = zlib.crc32(b'')", "0")]
        [InlineData("r = zlib.crc32(b'hello')", "907060870")]
        [InlineData("r = zlib.crc32(b'lo', zlib.crc32(b'hel'))", "907060870")]
        [InlineData("r = zlib.crc32(b'hello world')", "222957957")]
        [InlineData("r = zlib.adler32(b'')", "1")]
        [InlineData("r = zlib.adler32(b'hello')", "103547413")]
        [InlineData("r = zlib.adler32(b'lo', zlib.adler32(b'hel'))", "103547413")]
        [InlineData("r = zlib.adler32(b'x' * 100000) == zlib.adler32(b'x' * 100000)", "True")]
        public void Checksums(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        // ---- multi-member gzip streams ----

        [Theory]
        [InlineData("r = gzip.decompress(gzip.compress(b'a') + gzip.compress(b'b'))", "b'ab'")]
        [InlineData("r = gzip.decompress(gzip.compress(b'') + gzip.compress(b'x'))", "b'x'")]
        [InlineData("r = gzip.decompress(gzip.compress(b'a') + gzip.compress(b'') + gzip.compress(b'cd'))", "b'acd'")]
        [InlineData("r = gzip.decompress(gzip.compress(b'a'))", "b'a'")]
        [InlineData("r = zlib.decompress(gzip.compress(b'a') + gzip.compress(b'b'), 47)", "b'ab'")]
        [InlineData("r = zlib.decompress(gzip.compress(b'a') + gzip.compress(b'b'), 31)", "b'ab'")]
        // a payload that CONTAINS the gzip magic is not mistaken for a member boundary
        [InlineData("r = gzip.decompress(gzip.compress(b'\\x1f\\x8b\\x08' * 40)) == b'\\x1f\\x8b\\x08' * 40", "True")]
        [InlineData("g = gzip.compress(b'x' * 5000)\nr = len(gzip.decompress(g + g))", "10000")]
        public void GzipMultiMember(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Theory]
        [InlineData("x = zlib.decompress(b'garbage!')", "error")]
        [InlineData("x = zlib.decompress(zlib.compress(b'hello world')[:8])", "error")]
        [InlineData("b = zlib.compress(b'hello')\nx = zlib.decompress(b[:-1] + bytes([b[-1] ^ 1]))", "error")]
        [InlineData("x = zlib.decompress(bytes([0x78, 0x00]) + b'xxxx')", "error")]
        [InlineData("x = zlib.decompress(bytes([0x78, 0x20]) + b'xxxx')", "error")]
        [InlineData("x = zlib.compress(b'x', 10)", "error")]
        [InlineData("x = zlib.compress(b'x', -2)", "error")]
        [InlineData("x = zlib.compress(b'x', wbits=3)", "error")]
        [InlineData("x = zlib.compress('str not bytes')", "TypeError")]
        [InlineData("x = zlib.compress(b'x', bogus=1)", "TypeError")]
        [InlineData("x = gzip.decompress(b'nonsense!')", "BadGzipFile")]
        [InlineData("x = gzip.decompress(zlib.compress(b'zlib stream'))", "BadGzipFile")]
        [InlineData("g = gzip.compress(b'hello')\nx = gzip.decompress(g[:-5] + bytes([g[-5] ^ 1]) + g[-4:])", "BadGzipFile")]
        [InlineData("g = gzip.compress(b'hello')\nx = gzip.decompress(g[:12])", "BadGzipFile")]
        [InlineData("x = gzip.compress(b'x', 10)", "ValueError")]
        [InlineData("x = gzip.compress(b'x', 1, 0)", "TypeError")]
        // Checksum-seed / mtime type confusion: these once leaked an InvalidCastException as an
        // EngineFault (host-boundary breach); they must now be ordinary catchable TypeErrors.
        [InlineData("x = zlib.crc32(b'x', 'notanint')", "TypeError")]
        [InlineData("x = zlib.crc32(b'x', 1.5)", "TypeError")]
        [InlineData("x = zlib.crc32(b'x', None)", "TypeError")]
        [InlineData("x = zlib.crc32(b'x', [1, 2])", "TypeError")]
        [InlineData("x = zlib.adler32(b'x', 'notanint')", "TypeError")]
        [InlineData("import binascii\nx = binascii.crc32(b'x', 'notanint')", "TypeError")]
        [InlineData("x = gzip.compress(b'x', mtime='notanint')", "TypeError")]
        [InlineData("x = gzip.compress(b'x', mtime=[1])", "TypeError")]
        public void Errors(string body, string excType)
        {
            Assert.Equal(excType, Raises(body).Value.ExcType.Name);
        }

        // A huge/negative int seed is accepted (masked to 32 bits), like CPython.
        [Theory]
        [InlineData("r = zlib.crc32(b'', 10 ** 100) == (10 ** 100) & 0xffffffff", "True")]
        [InlineData("r = zlib.crc32(b'', -5) == (-5) & 0xffffffff", "True")]
        [InlineData("r = zlib.adler32(b'', 10 ** 100) == (10 ** 100) & 0xffffffff", "True")]
        [InlineData("g = gzip.compress(b'x', mtime=10 ** 100)\nr = len(g) > 0", "True")]
        public void SeedAndMtimeMaskLargeInts(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        [Fact]
        public void ZlibErrorIsCatchableExceptionSubclass()
        {
            string src = P + "caught = ''\ntry:\n    zlib.decompress(b'junk')\nexcept zlib.error:\n    caught = 'z'\n"
                + "try:\n    gzip.decompress(b'junk')\nexcept gzip.BadGzipFile:\n    caught += 'g'\n"
                + "try:\n    zlib.decompress(b'junk')\nexcept Exception:\n    caught += 'e'\nr = caught\n";
            Assert.Equal("'zge'", Ev.Run(src).Repr("r"));
        }

        [Theory]
        [InlineData("g = gzip.compress(b'gz data')\nr = gzip.decompress(g)", "b'gz data'")]
        [InlineData("g = gzip.compress(b'x')\nr = (g[0], g[1], g[2], g[4], g[5], g[6], g[7])", "(31, 139, 8, 0, 0, 0, 0)")]
        [InlineData("g = gzip.compress(b'x', mtime=1234567890)\nr = (g[4], g[5], g[6], g[7])", "(210, 2, 150, 73)")]
        [InlineData("data = b'gzip-level' * 300\nr = all(gzip.decompress(gzip.compress(data, n)) == data for n in [0, 1, 5, 9])", "True")]
        [InlineData("r = gzip.decompress(gzip.compress(b''))", "b''")]
        [InlineData("r = gzip.decompress(zlib.compress(b'via zlib wbits', wbits=25))", "b'via zlib wbits'")]
        public void GzipRoundTrips(string body, string expected)
        {
            Assert.Equal(expected, Body(body));
        }

        // ---- interop: our streams are standard (the .NET inflater reads them) ----

        [Fact]
        public void OurZlibStreamIsStandardDeflate()
        {
            var r = Ev.Run(P + "blob = zlib.compress(b'interop payload 123')\n");
            byte[] blob = ((BytesValue)r.Get("blob")).Data;
            using (var ms = new MemoryStream(blob, 2, blob.Length - 6))   // skip zlib header + adler
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                ds.CopyTo(outMs);
                Assert.Equal("interop payload 123", System.Text.Encoding.ASCII.GetString(outMs.ToArray()));
            }
        }

        [Fact]
        public void ForeignDotNetGzipStreamDecompresses()
        {
            byte[] payload = System.Text.Encoding.ASCII.GetBytes("foreign gzip payload");
            byte[] blob;
            using (var outMs = new MemoryStream())
            {
                using (var gz = new GZipStream(outMs, CompressionLevel.Optimal, true))
                {
                    gz.Write(payload, 0, payload.Length);
                }
                blob = outMs.ToArray();
            }
            string b64 = Convert.ToBase64String(blob);
            string src = "import gzip\nimport base64\nr = gzip.decompress(base64.b64decode('" + b64 + "'))\n";
            Assert.Equal("b'foreign gzip payload'", Ev.Run(src).Repr("r"));
        }

        [Fact]
        public void GzipHeaderWithFilenameFieldIsSkipped()
        {
            // Take our own gzip output and graft an FNAME field into the fixed 10-byte header.
            var r = Ev.Run(P + "blob = gzip.compress(b'named payload')\n");
            byte[] blob = ((BytesValue)r.Get("blob")).Data;
            byte[] name = System.Text.Encoding.ASCII.GetBytes("name.txt\0");
            var withName = new byte[blob.Length + name.Length];
            Buffer.BlockCopy(blob, 0, withName, 0, 10);
            withName[3] = 8;   // FLG.FNAME
            Buffer.BlockCopy(name, 0, withName, 10, name.Length);
            Buffer.BlockCopy(blob, 10, withName, 10 + name.Length, blob.Length - 10);
            string src = "import gzip\nimport base64\nr = gzip.decompress(base64.b64decode('"
                + Convert.ToBase64String(withName) + "'))\n";
            Assert.Equal("b'named payload'", Ev.Run(src).Repr("r"));
        }

        // ---- the bomb guard: inflation charges the budget BEFORE the output grows ----

        [Fact]
        public void DecompressionBombAbortsOnMemoryBudget()
        {
            // A ~32 KB zlib blob that inflates to 32 MB of zeros, run under an 8 MB allocation budget.
            const int InflatedLen = 32 * 1024 * 1024;
            byte[] deflated;
            using (var outMs = new MemoryStream())
            {
                using (var ds = new DeflateStream(outMs, CompressionLevel.Optimal, true))
                {
                    var zeros = new byte[64 * 1024];
                    for (int i = 0; i < InflatedLen / zeros.Length; i++)
                    {
                        ds.Write(zeros, 0, zeros.Length);
                    }
                }
                deflated = outMs.ToArray();
            }
            var blob = new byte[deflated.Length + 6];
            blob[0] = 0x78;
            blob[1] = 0x9C;
            Buffer.BlockCopy(deflated, 0, blob, 2, deflated.Length);
            uint adler = ((uint)(InflatedLen % 65521) << 16) | 1;   // adler32 of all-zeros
            blob[blob.Length - 4] = (byte)(adler >> 24);
            blob[blob.Length - 3] = (byte)(adler >> 16);
            blob[blob.Length - 2] = (byte)(adler >> 8);
            blob[blob.Length - 1] = (byte)adler;

            string src = "import zlib\nimport base64\nr = zlib.decompress(base64.b64decode('"
                + Convert.ToBase64String(blob) + "'))\n";
            var lim = ResourceLimits.Default.With(maxAllocBytes: 8 * 1024 * 1024);
            var abort = Assert.Throws<EngineAbort>(() => Ev.Run(src, lim));
            Assert.Equal(EngineAbortKind.Memory, abort.Kind);
        }

        // The host-boundary contract at the real ScriptEngine.Run API: a hostile compression call
        // never returns EngineFault (an engine bug) — it is always a clean, catchable script error.
        [Theory]
        [InlineData("import zlib\nzlib.crc32(b'x', 'notanint')\n")]
        [InlineData("import zlib\nzlib.adler32(b'x', [1])\n")]
        [InlineData("import gzip\ngzip.compress(b'x', mtime='bad')\n")]
        [InlineData("import zlib\nzlib.decompress(b'garbage that is not a stream')\n")]
        [InlineData("import gzip\ngzip.decompress(b'\\x1f\\x8b\\x08\\x04\\x00\\x00\\x00\\x00\\x00\\xff\\xff\\xff')\n")]
        [InlineData("import zlib\nzlib.compress(b'x', wbits='big')\n")]
        public void HostileCallNeverFaultsAtEngineBoundary(string src)
        {
            var engine = new ScriptEngine(new EngineOptions { DefaultLimits = ResourceLimits.Batch });
            RunResult r = engine.Run(src, new RunRequest());   // must not throw to this thread
            Assert.False(r.Succeeded);
            Assert.NotEqual(ScriptErrorKind.EngineFault, r.Error.Kind);
            Assert.Equal(ScriptErrorKind.RuntimeError, r.Error.Kind);
        }

        [Fact]
        public void GzipDecompressionBombAbortsOnMemoryBudget()
        {
            // A valid gzip blob whose payload is 32 MB of zeros, run under an 8 MB budget.
            byte[] blob;
            var payload = new byte[32 * 1024 * 1024];
            using (var outMs = new MemoryStream())
            {
                using (var gz = new GZipStream(outMs, CompressionLevel.Optimal, true))
                {
                    gz.Write(payload, 0, payload.Length);
                }
                blob = outMs.ToArray();
            }
            string src = "import gzip\nimport base64\nr = gzip.decompress(base64.b64decode('"
                + Convert.ToBase64String(blob) + "'))\n";
            var lim = ResourceLimits.Default.With(maxAllocBytes: 8 * 1024 * 1024);
            var abort = Assert.Throws<EngineAbort>(() => Ev.Run(src, lim));
            Assert.Equal(EngineAbortKind.Memory, abort.Kind);
        }

        // Zero-retention (RFC-004): a decompressed payload the script produced and handed to a host
        // builtin is unrooted once Run returns — the native compression path leaves nothing behind.
        [Fact]
        public void DecompressedPayloadIsCollectedAfterRun()
        {
            var engine = new ScriptEngine(new EngineOptions());
            WeakReference w = RunDecompressAndCaptureWeak(engine);

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);

            Assert.False(w.IsAlive, "the decompressed payload is still rooted after the run");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference RunDecompressAndCaptureWeak(ScriptEngine engine)
        {
            var stasher = new Stasher();
            HostFunctionTable builtins = HostFunctionTable.CreateDefault();
            builtins.Set("stash", stasher.Stash);
            RunResult r = engine.Run(
                "import zlib\nblob = zlib.compress(b'payload-' * 20000)\nfor i in range(20):\n    stash(zlib.decompress(blob))\n",
                new RunRequest { Builtins = builtins });
            Assert.True(r.Succeeded);
            Assert.NotNull(stasher.Last);
            return stasher.Last;
        }

        private sealed class Stasher
        {
            public WeakReference Last;
            public Outbridge.PyLite.Runtime.Values.ScriptValue Stash(
                Outbridge.PyLite.Runtime.Values.ScriptValue self,
                Outbridge.PyLite.Runtime.Values.ScriptValue[] args,
                Outbridge.PyLite.Runtime.Evaluator.KwArgs kw,
                Outbridge.PyLite.Runtime.EvalContext ctx)
            {
                if (args.Length > 0)
                    Last = new WeakReference(args[0]);
                return args.Length > 0 ? args[0] : self;
            }
        }
    }
}

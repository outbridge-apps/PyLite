using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modules
{
    // zipfile: interoperability with .NET's ZipArchive in both directions, ZIP64 records, and what only
    // the engine guarantees - bombs stop on the budget, a forged size allocates nothing, the entry cap,
    // overlapped entries, nested archives, the deadline, and the refusal of a path.
    [Trait("Category", "Compatibility")]
    public class ZipfileTests
    {
        private const string P = "import io\nimport zipfile\nimport base64\n";

        private static string B64(byte[] data)
        {
            return Convert.ToBase64String(data);
        }

        private static string Load(byte[] archive)
        {
            return "blob = base64.b64decode('" + B64(archive) + "')\n";
        }

        private static EvResult Run(string body, ResourceLimits limits = null)
        {
            return Ev.Run(P + body, limits ?? ResourceLimits.Default.With(maxAllocBytes: 512 * 1024 * 1024));
        }

        private static string R(string body, ResourceLimits limits = null)
        {
            return Run(body, limits).Repr("r");
        }

        private static byte[] Bytes(string body, ResourceLimits limits = null)
        {
            return ((BytesValue)Run(body, limits).Get("r")).Data;
        }

        // For archives too big to inline in a source: the bytes arrive as a host input (base64 text) and
        // the answer leaves as repr(r).
        private static string RunHosted(string body, byte[] blob)
        {
            var engine = new ScriptEngine(new EngineOptions { DefaultLimits = ResourceLimits.Batch });
            RunResult res = engine.Run(P + "blob = base64.b64decode(b64)\n" + body + "out = repr(r)\n", new RunRequest
            {
                ScalarsIn = new Dictionary<string, string> { ["b64"] = B64(blob) },
                ScalarOutputNames = new[] { "out" },
            });
            Assert.True(res.Succeeded, res.Error?.Message);
            return res.ScalarsOut["out"];
        }

        private static byte[] DotNetZip(Action<ZipArchive> fill)
        {
            using (var ms = new MemoryStream())
            {
                using (var za = new ZipArchive(ms, ZipArchiveMode.Create, true, Encoding.UTF8))
                    fill(za);
                return ms.ToArray();
            }
        }

        private static void Add(ZipArchive za, string name, byte[] data, CompressionLevel level)
        {
            ZipArchiveEntry e = za.CreateEntry(name, level);
            using (Stream s = e.Open())
                s.Write(data, 0, data.Length);
        }

        // ---------- the engine writes, .NET reads ----------

        [Fact]
        public void ArchivesTheEngineWritesOpenInZipArchive()
        {
            byte[] zip = Bytes("buf = io.BytesIO()\nwith zipfile.ZipFile(buf, 'w', zipfile.ZIP_DEFLATED) as z:\n"
                + "    z.writestr('deflated.txt', 'hello ' * 1000)\n"
                + "    z.writestr('stored.bin', bytes(range(256)), compress_type=zipfile.ZIP_STORED)\n"
                + "    z.writestr('dir/', b'')\n"
                + "    z.writestr('ünïcode/файл.txt', 'привет')\n"
                + "    with z.open('streamed.txt', 'w') as f:\n        f.write(b'part1-')\n        f.write(b'part2')\n"
                + "    z.comment = b'made by the engine'\n"
                + "r = buf.getvalue()\n");
            using (var za = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                var names = new List<string>();
                foreach (ZipArchiveEntry e in za.Entries)
                    names.Add(e.FullName);
                Assert.Equal(new[] { "deflated.txt", "stored.bin", "dir/", "ünïcode/файл.txt", "streamed.txt" }, names);
                Assert.Equal(Encoding.ASCII.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("hello ", 1000))), ReadEntry(za, "deflated.txt"));
                byte[] range = new byte[256];
                for (int i = 0; i < 256; i++)
                    range[i] = (byte)i;
                Assert.Equal(range, ReadEntry(za, "stored.bin"));
                Assert.Equal("привет", Encoding.UTF8.GetString(ReadEntry(za, "ünïcode/файл.txt")));
                Assert.Equal("part1-part2", Encoding.ASCII.GetString(ReadEntry(za, "streamed.txt")));
                Assert.Equal(0, za.GetEntry("dir/").Length);
            }
        }

        private static byte[] ReadEntry(ZipArchive za, string name)
        {
            using (Stream s = za.GetEntry(name).Open())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        // ---------- .NET writes, the engine reads ----------

        [Fact]
        public void ZipArchiveOutputIsReadByTheEngine()
        {
            byte[] zip = DotNetZip(za =>
            {
                Add(za, "optimal.txt", Encoding.ASCII.GetBytes(new string('a', 5000) + "end"), CompressionLevel.Optimal);
                Add(za, "fastest.txt", Encoding.ASCII.GetBytes("fast " + new string('b', 300)), CompressionLevel.Fastest);
                Add(za, "stored.bin", new byte[] { 0, 1, 2, 255 }, CompressionLevel.NoCompression);
                za.CreateEntry("folder/");
                Add(za, "ünï.txt", Encoding.UTF8.GetBytes("ü"), CompressionLevel.Optimal);
            });
            string r = R(Load(zip) + "z = zipfile.ZipFile(io.BytesIO(blob))\n"
                + "r = (z.namelist(), z.read('optimal.txt')[-3:], len(z.read('optimal.txt')), z.read('fastest.txt')[:5],"
                + " z.read('stored.bin'), z.getinfo('folder/').is_dir(), z.read('ünï.txt').decode(), z.testzip(),"
                + " z.getinfo('stored.bin').compress_type, z.getinfo('optimal.txt').compress_type)\n");
            Assert.Equal("(['optimal.txt', 'fastest.txt', 'stored.bin', 'folder/', 'ünï.txt'], b'end', 5003, b'fast ', "
                + "b'\\x00\\x01\\x02\\xff', True, 'ü', None, 8, 8)", r);   // net472's NoCompression is deflate with stored blocks
        }

        [Fact]
        public void AnArchiveCommentWrittenByHandIsRead()
        {
            byte[] zip = DotNetZip(za => Add(za, "a.txt", Encoding.ASCII.GetBytes("x"), CompressionLevel.Optimal));
            byte[] comment = Encoding.ASCII.GetBytes("a comment from elsewhere");
            byte[] withComment = new byte[zip.Length + comment.Length];
            Buffer.BlockCopy(zip, 0, withComment, 0, zip.Length);
            Buffer.BlockCopy(comment, 0, withComment, zip.Length, comment.Length);
            withComment[zip.Length - 2] = (byte)comment.Length;
            Assert.Equal("(b'a comment from elsewhere', b'x')",
                R(Load(withComment) + "z = zipfile.ZipFile(io.BytesIO(blob))\nr = (z.comment, z.read('a.txt'))\n"));
        }

        [Fact]
        public void ZipArchiveWithMoreThan65535EntriesUsesZip64AndIsRead()
        {
            byte[] zip = DotNetZip(za =>
            {
                for (int i = 0; i < 65540; i++)
                    za.CreateEntry("e" + i, CompressionLevel.NoCompression);
            });
            Assert.Equal("(65540, 'e65539')", RunHosted("z = zipfile.ZipFile(io.BytesIO(blob))\nn = z.namelist()\nr = (len(n), n[-1])\n", zip));
        }

        [Fact]
        public void TheEngineWritesZip64EndRecordsPast65535Entries()
        {
            // 65,537 members written twice: a minute of deadline, so a loaded machine running the suite in
            // parallel cannot turn the records under test into a timeout (alone it takes about 2 s).
            ResourceLimits roomy = ResourceLimits.Default.With(maxAllocBytes: 512 * 1024 * 1024, deadlineMs: 60_000);
            byte[] zip = Bytes("buf = io.BytesIO()\nwith zipfile.ZipFile(buf, 'w') as z:\n    for i in range(65537):\n        z.writestr('f%d' % i, b'')\nr = buf.getvalue()\n", roomy);
            using (var za = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
                Assert.Equal(65537, za.Entries.Count);
            Assert.Equal("(65537, False)", RunHosted("z = zipfile.ZipFile(io.BytesIO(blob))\nr = (len(z.namelist()), zipfile.is_zipfile(io.BytesIO(b'x')))\n", zip));
            Assert.Equal("LargeZipFile", ErrorType("buf = io.BytesIO()\nwith zipfile.ZipFile(buf, 'w', allowZip64=False) as z:\n    for i in range(65536):\n        z.writestr('f%d' % i, b'')\n", roomy));
        }

        // A hand-built archive whose sizes and offset live only in ZIP64 extra fields, as some tools write
        // even for small members.
        private static byte[] Zip64Archive(string name, byte[] data)
        {
            byte[] nameBytes = Encoding.ASCII.GetBytes(name);
            uint crc = Crc32(data);
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(0x04034b50u);
            w.Write((ushort)45); w.Write((ushort)0); w.Write((ushort)0);
            w.Write((ushort)0); w.Write((ushort)0x21);
            w.Write(crc); w.Write(0xFFFFFFFFu); w.Write(0xFFFFFFFFu);
            w.Write((ushort)nameBytes.Length); w.Write((ushort)20);
            w.Write(nameBytes);
            w.Write((ushort)1); w.Write((ushort)16); w.Write((long)data.Length); w.Write((long)data.Length);
            w.Write(data);
            long cdOffset = ms.Position;
            w.Write(0x02014b50u);
            w.Write((byte)45); w.Write((byte)3); w.Write((byte)45); w.Write((byte)0);
            w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0x21);
            w.Write(crc); w.Write(0xFFFFFFFFu); w.Write(0xFFFFFFFFu);
            w.Write((ushort)nameBytes.Length); w.Write((ushort)28); w.Write((ushort)0);
            w.Write((ushort)0); w.Write((ushort)0); w.Write(0u); w.Write(0xFFFFFFFFu);
            w.Write(nameBytes);
            w.Write((ushort)1); w.Write((ushort)24); w.Write((long)data.Length); w.Write((long)data.Length); w.Write(0L);
            long cdSize = ms.Position - cdOffset;
            long z64 = ms.Position;
            w.Write(0x06064b50u); w.Write(44L); w.Write((ushort)45); w.Write((ushort)45); w.Write(0u); w.Write(0u);
            w.Write(1L); w.Write(1L); w.Write(cdSize); w.Write(cdOffset);
            w.Write(0x07064b50u); w.Write(0u); w.Write(z64); w.Write(1u);
            w.Write(0x06054b50u); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0xFFFF); w.Write((ushort)0xFFFF);
            w.Write(0xFFFFFFFFu); w.Write(0xFFFFFFFFu); w.Write((ushort)0);
            w.Flush();
            return ms.ToArray();
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return ~crc;
        }

        [Fact]
        public void Zip64ExtraFieldsAndEndRecordsAreRead()
        {
            byte[] zip = Zip64Archive("big.txt", Encoding.ASCII.GetBytes("zip64 payload"));
            Assert.Equal("(['big.txt'], b'zip64 payload', 13, 13, 0, None)",
                R(Load(zip) + "z = zipfile.ZipFile(io.BytesIO(blob))\ni = z.getinfo('big.txt')\n"
                    + "r = (z.namelist(), z.read('big.txt'), i.file_size, i.compress_size, i.header_offset, z.testzip())\n"));
        }

        // ---------- bombs and caps ----------

        private static string ErrorType(string body, ResourceLimits limits = null)
        {
            var ex = Assert.Throws<ScriptException>(() => Run(body, limits));
            return ex.Value.ExcType.Name;
        }

        private static EngineAbort Abort(string body, ResourceLimits limits)
        {
            return Assert.Throws<EngineAbort>(() => Run(body, limits));
        }

        private static byte[] Zeros(int n)
        {
            return new byte[n];
        }

        [Fact]
        public void TenMegabytesOfZerosInATinyArchiveStopOnTheBudget()
        {
            byte[] zip = DotNetZip(za => Add(za, "zeros", Zeros(10 * 1024 * 1024), CompressionLevel.Optimal));
            Assert.True(zip.Length < 64 * 1024, "archive is " + zip.Length + " bytes");
            EngineAbort a = Abort(Load(zip) + "z = zipfile.ZipFile(io.BytesIO(blob))\nz.read('zeros')\n",
                ResourceLimits.Default.With(maxAllocBytes: 4 * 1024 * 1024));
            Assert.Equal(EngineAbortKind.Memory, a.Kind);
            Assert.Equal("10485760", R(Load(zip) + "r = len(zipfile.ZipFile(io.BytesIO(blob)).read('zeros'))\n"));
        }

        // The recorded size is patched in both headers; ZipArchive writes the local header at offset 0.
        private static byte[] PatchSizes(byte[] zip, uint newSize)
        {
            byte[] z = (byte[])zip.Clone();
            WriteU32(z, 22, newSize);
            int cd = LastIndexOf(z, new byte[] { 0x50, 0x4b, 0x01, 0x02 });
            WriteU32(z, cd + 24, newSize);
            return z;
        }

        private static void WriteU32(byte[] b, int at, uint v)
        {
            b[at] = (byte)v;
            b[at + 1] = (byte)(v >> 8);
            b[at + 2] = (byte)(v >> 16);
            b[at + 3] = (byte)(v >> 24);
        }

        private static int LastIndexOf(byte[] b, byte[] sig)
        {
            for (int i = b.Length - sig.Length; i >= 0; i--)
            {
                bool hit = true;
                for (int k = 0; k < sig.Length && hit; k++)
                    hit = b[i + k] == sig[k];
                if (hit)
                    return i;
            }
            return -1;
        }

        [Fact]
        public void AForgedSmallSizeStopsInflatingThereAndFailsTheCrc()
        {
            byte[] zip = PatchSizes(DotNetZip(za => Add(za, "zeros", Zeros(8 * 1024 * 1024), CompressionLevel.Optimal)), 10);
            // Under a 2 MB budget: the 8 MB stream is never inflated, only the 10 recorded bytes.
            Assert.Equal("BadZipFile", ErrorType(Load(zip) + "zipfile.ZipFile(io.BytesIO(blob)).read('zeros')\n",
                ResourceLimits.Default.With(maxAllocBytes: 2 * 1024 * 1024)));
        }

        [Fact]
        public void AForgedHugeSizeAllocatesNothing()
        {
            byte[] zip = PatchSizes(DotNetZip(za => Add(za, "small", Encoding.ASCII.GetBytes("only this"), CompressionLevel.Optimal)), 0x7FFFFFFF);
            Assert.Equal("(b'only this', 2147483647)", R(Load(zip) + "z = zipfile.ZipFile(io.BytesIO(blob))\nr = (z.read('small'), z.getinfo('small').file_size)\n",
                ResourceLimits.Default.With(maxAllocBytes: 2 * 1024 * 1024)));
        }

        [Fact]
        public void EntriesPastTheCollectionCapStopTheRun()
        {
            byte[] zip = DotNetZip(za =>
            {
                for (int i = 0; i < 1500; i++)
                    za.CreateEntry("e" + i, CompressionLevel.NoCompression);
            });
            EngineAbort a = Abort(Load(zip) + "zipfile.ZipFile(io.BytesIO(blob))\n", ResourceLimits.Default.With(maxCollectionItems: 1000));
            Assert.Equal(EngineAbortKind.Memory, a.Kind);
            Assert.Equal("MaxCollectionItems", a.LimitName);
        }

        [Fact]
        public void OverlappedEntriesAreRefusedAsAPossibleBomb()
        {
            byte[] zip = DotNetZip(za =>
            {
                Add(za, "a", Encoding.ASCII.GetBytes("first member data"), CompressionLevel.NoCompression);
                Add(za, "b", Encoding.ASCII.GetBytes("second member data"), CompressionLevel.NoCompression);
            });
            // Stretch a's compressed size in the directory so its data runs into b's local header.
            int cd = LastIndexOf(zip, new byte[] { 0x50, 0x4b, 0x01, 0x02 });
            int firstCd = -1;
            for (int i = 0; i < zip.Length - 4; i++)
            {
                if (zip[i] == 0x50 && zip[i + 1] == 0x4b && zip[i + 2] == 0x01 && zip[i + 3] == 0x02)
                {
                    firstCd = i;
                    break;
                }
            }
            Assert.True(firstCd >= 0 && firstCd < cd);
            byte[] z = (byte[])zip.Clone();
            WriteU32(z, firstCd + 20, 60);
            WriteU32(z, firstCd + 24, 60);
            string body = Load(z) + "f = zipfile.ZipFile(io.BytesIO(blob))\nr = f.read('b')\ntry:\n    f.read('a')\n    r = 'no error'\n"
                + "except zipfile.BadZipFile as e:\n    r = (r, str(e))\n";
            Assert.Equal("(b'second member data', \"Overlapped entries: 'a' (possible zip bomb)\")", R(body));

            // Two directory entries sharing one local header: the second name does not match it.
            byte[] twin = (byte[])zip.Clone();
            WriteU32(twin, cd + 42, 0);
            Assert.Contains("differ", ErrorText(Load(twin), "zipfile.ZipFile(io.BytesIO(blob)).read('b')"));
        }

        // The message of the exception `stmt` raises, caught inside the script.
        private static string ErrorText(string prelude, string stmt)
        {
            return R(prelude + "try:\n    " + stmt + "\n    r = None\nexcept Exception as e:\n    r = str(e)\n");
        }

        [Fact]
        public void NestedArchivesAreChargedAtEveryLevel()
        {
            byte[] inner = DotNetZip(za => Add(za, "zeros", Zeros(6 * 1024 * 1024), CompressionLevel.Optimal));
            byte[] outer = DotNetZip(za =>
            {
                for (int i = 0; i < 3; i++)
                    Add(za, "inner" + i + ".zip", inner, CompressionLevel.Optimal);
            });
            string body = Load(outer) + "def walk(data, depth):\n    total = 0\n    with zipfile.ZipFile(io.BytesIO(data)) as z:\n"
                + "        for n in z.namelist():\n            member = z.read(n)\n            if n.endswith('.zip'):\n"
                + "                total += walk(member, depth + 1)\n            else:\n                total += len(member)\n    return total\n"
                + "r = walk(blob, 0)\n";
            Assert.Equal("18874368", R(body));
            EngineAbort a = Abort(body, ResourceLimits.Default.With(maxAllocBytes: 16 * 1024 * 1024));
            Assert.Equal(EngineAbortKind.Memory, a.Kind);
        }

        [Fact]
        public void InflatingSeesTheDeadline()
        {
            byte[] zip = DotNetZip(za => Add(za, "zeros", Zeros(15 * 1024 * 1024), CompressionLevel.Optimal));
            EngineAbort a = Abort(Load(zip) + "z = zipfile.ZipFile(io.BytesIO(blob))\nwhile True:\n    z.read('zeros')\n",
                ResourceLimits.Default.With(deadlineMs: 300, maxAllocBytes: 1_073_741_824));
            Assert.True(a.Kind == EngineAbortKind.Deadline || a.Kind == EngineAbortKind.Memory, a.Kind.ToString());
        }

        [Fact]
        public void WritingIsChargedAndCapped()
        {
            EngineAbort a = Abort("buf = io.BytesIO()\nwith zipfile.ZipFile(buf, 'w') as z:\n    for i in range(100):\n        z.writestr('m%d' % i, b'x' * 1000000)\n",
                ResourceLimits.Default.With(maxStrChars: 20_000_000, maxAllocBytes: 64 * 1024 * 1024));
            Assert.Equal(EngineAbortKind.Memory, a.Kind);
        }

        // ---------- the sandbox and the host boundary ----------

        [Theory]
        [InlineData("zipfile.ZipFile('archive.zip')")]
        [InlineData("zipfile.ZipFile(b'archive.zip')")]
        [InlineData("zipfile.is_zipfile('archive.zip')")]
        public void APathIsRefusedByName(string call)
        {
            Assert.Equal("TypeError", ErrorType(call + "\n"));
            Assert.Contains("no file system", ErrorText("", call));
        }

        [Theory]
        [InlineData("zipfile.ZipFile(io.StringIO('x'))", "TypeError")]
        [InlineData("zipfile.ZipFile(io.BytesIO(), 'w').write('x.txt')", "TypeError")]
        [InlineData("zipfile.ZipFile(io.BytesIO(), 'w').extractall()", "TypeError")]
        [InlineData("zipfile.ZipFile(io.BytesIO(), compression=zipfile.ZIP_BZIP2)", "NotImplementedError")]
        [InlineData("zipfile.ZipFile(io.BytesIO(), compression=zipfile.ZIP_LZMA)", "NotImplementedError")]
        [InlineData("zipfile.ZipFile(io.BytesIO(), 'r', metadata_encoding='cp437', mode='w')", "ValueError")]
        [InlineData("zipfile.ZipInfo('x', (2020, 1))", "TypeError")]
        public void MisuseIsAScriptError(string call, string type)
        {
            Assert.Equal(type, ErrorType(call + "\n"));
        }

        [Fact]
        public void AnEncryptedMemberAsksForAPassword()
        {
            byte[] zip = DotNetZip(za => Add(za, "secret", Encoding.ASCII.GetBytes("s"), CompressionLevel.NoCompression));
            byte[] z = (byte[])zip.Clone();
            z[6] |= 1;   // local header flag bit 0
            int cd = LastIndexOf(z, new byte[] { 0x50, 0x4b, 0x01, 0x02 });
            z[cd + 8] |= 1;
            Assert.Equal("RuntimeError", ErrorType(Load(z) + "zipfile.ZipFile(io.BytesIO(blob)).read('secret')\n"));
            Assert.Equal("NotImplementedError", ErrorType(Load(z) + "zipfile.ZipFile(io.BytesIO(blob)).read('secret', pwd=b'pw')\n"));
        }

        [Theory]
        [InlineData("file_size")]
        [InlineData("compress_size")]
        [InlineData("header_offset")]
        public void ANegativeSizeOrOffsetIsBadZipFileNotAFault(string attr)
        {
            Assert.Equal("BadZipFile", ErrorType("buf = io.BytesIO()\nwith zipfile.ZipFile(buf, 'w') as z:\n    z.writestr('a', b'abc')\n"
                + "z = zipfile.ZipFile(buf)\ni = z.getinfo('a')\ni." + attr + " = -5\nz.read(i)\n"));
        }

        [Fact]
        public void ReadingAfterTheBytesIOWasClosedIsValueError()
        {
            Assert.Equal("ValueError", ErrorType("buf = io.BytesIO()\nwith zipfile.ZipFile(buf, 'w') as z:\n    z.writestr('a', 'b')\n"
                + "z = zipfile.ZipFile(buf)\nbuf.close()\nz.read('a')\n"));
        }

        [Fact]
        public void TheCallersBytesIOStaysOpenAfterTheArchiveCloses()
        {
            Assert.Equal("(False, True)", R("buf = io.BytesIO()\nwith zipfile.ZipFile(buf, 'w') as z:\n    z.writestr('a', 'b')\nr = (buf.closed, zipfile.is_zipfile(buf))\n"));
        }

        [Fact]
        public void Cp437NamesAreDecoded()
        {
            Assert.Equal(128, Outbridge.PyLite.Runtime.Values.Cp437.HighLength);
            // 0x81 is u-umlaut in code page 437; no UTF-8 flag on this name.
            byte[] zip = DotNetZip(za => Add(za, "x.txt", Encoding.ASCII.GetBytes("1"), CompressionLevel.NoCompression));
            byte[] z = (byte[])zip.Clone();
            z[30] = 0x81;
            z[7] &= 0xF7;   // ZipArchive set the UTF-8 flag (bit 11); this name is code page 437
            int cd = LastIndexOf(z, new byte[] { 0x50, 0x4b, 0x01, 0x02 });
            z[cd + 46] = 0x81;
            z[cd + 9] &= 0xF7;
            Assert.Equal("['ü.txt']", R(Load(z) + "r = zipfile.ZipFile(io.BytesIO(blob)).namelist()\n"));
        }

        [Fact]
        public void HostileCallsNeverFaultAtTheEngineBoundary()
        {
            var engine = new ScriptEngine(new EngineOptions { DefaultLimits = ResourceLimits.Batch });
            var rnd = new Random(11);
            byte[] good = DotNetZip(za =>
            {
                Add(za, "a.txt", Encoding.ASCII.GetBytes(new string('q', 3000)), CompressionLevel.Optimal);
                Add(za, "b.bin", new byte[] { 1, 2, 3 }, CompressionLevel.NoCompression);
            });
            for (int trial = 0; trial < 300; trial++)
            {
                byte[] z = (byte[])good.Clone();
                for (int k = 0; k < 1 + trial % 4; k++)
                    z[rnd.Next(z.Length)] = (byte)rnd.Next(256);
                string src = P + Load(z) + "try:\n    zf = zipfile.ZipFile(io.BytesIO(blob))\n    for n in zf.namelist():\n        zf.read(n)\n"
                    + "except (zipfile.BadZipFile, EOFError, NotImplementedError, RuntimeError, ValueError, KeyError, UnicodeDecodeError, OSError, Exception):\n    pass\n";
                RunResult r = engine.Run(src, new RunRequest());
                Assert.True(r.Succeeded || r.Error.Kind != ScriptErrorKind.EngineFault, "trial " + trial + ": " + r.Error?.Message);
            }
        }
    }
}

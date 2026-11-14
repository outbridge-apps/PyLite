using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Outbridge.PyLite.Tests.Infra
{
    // A FIPS-enforcing Windows makes .NET's non-validated algorithms throw, and a web host's .NET 4.5 mode keeps that
    // legacy throw for the managed implementations: md5 was an engine fault there. No machine here runs
    // the FIPS policy, so the gate is the source: none of those types is named anywhere in the engine.
    [Trait("Category", "Regression")]
    public class FipsSafeCryptoTests
    {
        private static readonly Regex Banned = new Regex(
            @"\b(MD5|RIPEMD160|HMACMD5|HMACRIPEMD160)(\.Create|CryptoServiceProvider|Cng|Managed)?\s*\(|\b(SHA1|SHA256|SHA384|SHA512|Rijndael|Aes)Managed\b|\bRijndael\b",
            RegexOptions.CultureInvariant);

        [Fact]
        public void TheEngineNamesNoAlgorithmFipsRefuses()
        {
            string src = Path.Combine(RepoPaths.Root(), "src", "PyLite");
            var hits = Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories)
                .Where(f => f.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) < 0)
                .SelectMany(f => File.ReadAllLines(f).Select((line, i) => new { f, i, line }))
                .Where(x => !x.line.TrimStart().StartsWith("//", StringComparison.Ordinal) && Banned.IsMatch(x.line))
                .Select(x => Path.GetFileName(x.f) + ":" + (x.i + 1) + ": " + x.line.Trim())
                .ToList();
            Assert.True(hits.Count == 0, string.Join("\n", hits));
        }

        [Fact]
        public void TheBanCatchesWhatItIsFor()
        {
            Assert.Matches(Banned, "using (var h = MD5.Create())");
            Assert.Matches(Banned, "new SHA256Managed()");
            Assert.Matches(Banned, "new HMACMD5(key)");
            Assert.DoesNotMatch(Banned, "SHA256.Create()");
            Assert.DoesNotMatch(Banned, "Md5Managed.Hash(data)");
        }
    }
}

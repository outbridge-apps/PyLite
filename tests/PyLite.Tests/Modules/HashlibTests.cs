using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Tests;
using Outbridge.PyLite.Tests.Evaluator;
using Xunit;

namespace Outbridge.PyLite.Tests.Modern
{
    // RFC-001 §2 hashlib + hmac (known test vectors).
    [Trait("Category", "Compatibility")]
    public class HashlibTests
    {
        private static string H(string body, string var) => Ev.Run("import hashlib\nimport hmac\n" + body).Repr(var);

        [Theory]
        [InlineData("r = hashlib.md5(b'abc').hexdigest()", "'900150983cd24fb0d6963f7d28e17f72'")]
        [InlineData("r = hashlib.sha1(b'abc').hexdigest()", "'a9993e364706816aba3e25717850c26c9cd0d89d'")]
        [InlineData("r = hashlib.sha256(b'abc').hexdigest()", "'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad'")]
        [InlineData("r = hashlib.sha256(b'').hexdigest()", "'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'")]
        [InlineData("r = hashlib.new('sha256', b'abc').hexdigest()", "'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad'")]
        [InlineData("r = hashlib.sha256(b'abc').digest_size", "32")]
        [InlineData("r = hashlib.sha256(b'abc').block_size", "64")]
        [InlineData("r = hashlib.sha512(b'abc').digest_size", "64")]
        [InlineData("r = hashlib.sha256(b'abc').name", "'sha256'")]
        [InlineData("r = len(hashlib.sha256(b'abc').digest())", "32")]
        // sha224 is the managed SHA-256 core with SHA-224's IV; sha384 comes from the framework.
        [InlineData("r = hashlib.sha224(b'abc').hexdigest()", "'23097d223405d8228642a477bda255b32aadbce4bda0b3f7e36c9da7'")]
        [InlineData("r = hashlib.sha224(b'').hexdigest()", "'d14a028c2a3a2bc9476102bb288234c415a2b01f828ea62ac5b3e42f'")]
        [InlineData("r = hashlib.sha224(b'abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq').hexdigest()",
            "'75388b16512776cc5dba5da1fd890150b0c6455cb4f58b1952522525'")]   // two-block FIPS vector
        [InlineData("r = hashlib.sha384(b'abc').hexdigest()",
            "'cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7'")]
        [InlineData("r = hashlib.sha384(b'').hexdigest()",
            "'38b060a751ac96384cd9327eb1b1e36a21fdb71114be07434c0cc7bf63f6e1da274edebfe76f65fbd51ad2f14898b95b'")]
        [InlineData("r = (hashlib.sha224().digest_size, hashlib.sha224().block_size, hashlib.sha224().name)", "(28, 64, 'sha224')")]
        [InlineData("r = (hashlib.sha384().digest_size, hashlib.sha384().block_size, hashlib.sha384().name)", "(48, 128, 'sha384')")]
        [InlineData("r = hashlib.new('sha224', b'abc').hexdigest() == hashlib.sha224(b'abc').hexdigest()", "True")]
        [InlineData("r = hashlib.new('sha384', b'abc').hexdigest() == hashlib.sha384(b'abc').hexdigest()", "True")]
        // RFC 4231 test case 2: HMAC-SHA-224 / HMAC-SHA-384, neither of which the framework ships
        [InlineData("r = hmac.new(b'Jefe', b'what do ya want for nothing?', 'sha224').hexdigest()",
            "'a30e01098bc6dbbf45690f3a7e9e6d0f8bbea2a39e6148008fd05e44'")]
        [InlineData("r = hmac.new(b'Jefe', b'what do ya want for nothing?', 'sha384').hexdigest()",
            "'af45d2e376484031617f78d2b58a6b1b9c7ef464f5a01b47e42ec3736322445e8e2240ca5e69e2c78b3239ecfab21649'")]
        public void HashlibVectors(string body, string expected)
        {
            Assert.Equal(expected, H(body, "r"));
        }

        [Fact]
        public void IncrementalUpdateMatchesOneShot()
        {
            var src = "h = hashlib.sha256()\nh.update(b'a')\nh.update(b'bc')\n"
                + "r = h.hexdigest() == hashlib.sha256(b'abc').hexdigest()\n";
            Assert.Equal("True", H(src, "r"));
        }

        [Fact]
        public void CopyIsIndependent()
        {
            var src = "h = hashlib.sha256(b'a')\nh2 = h.copy()\nh.update(b'b')\n"
                + "same = h2.hexdigest() == hashlib.sha256(b'a').hexdigest()\n"
                + "diff = h.hexdigest() == hashlib.sha256(b'ab').hexdigest()\n";
            var r = Ev.Run("import hashlib\n" + src);
            Assert.Equal("True", r.Repr("same"));
            Assert.Equal("True", r.Repr("diff"));
        }

        [Fact]
        public void HmacSha256Vector()
        {
            // RFC known vector: HMAC-SHA256(key='key', msg='The quick brown fox jumps over the lazy dog')
            Assert.Equal("'f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8'",
                H("r = hmac.new(b'key', b'The quick brown fox jumps over the lazy dog', 'sha256').hexdigest()", "r"));
        }

        [Fact]
        public void HmacIncremental()
        {
            var src = "h = hmac.new(b'key', digestmod='sha256')\nh.update(b'The quick brown fox ')\nh.update(b'jumps over the lazy dog')\n"
                + "r = h.hexdigest()\n";
            Assert.Equal("'f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8'", H(src, "r"));
        }

        [Theory]
        [InlineData("r = hmac.compare_digest(b'abc', b'abc')", "True")]
        [InlineData("r = hmac.compare_digest(b'abc', b'abd')", "False")]
        [InlineData("r = hmac.compare_digest(b'abc', b'ab')", "False")]
        [InlineData("r = hmac.compare_digest('abc', 'abc')", "True")]
        public void CompareDigest(string body, string expected)
        {
            Assert.Equal(expected, H(body, "r"));
        }

        // msg= and digestmod= bind by name (msg= used to be dropped: the HMAC of an empty message, B9).
        [Theory]
        [InlineData("r = hmac.new(b'k', msg=b'x', digestmod='sha256').hexdigest() == hmac.new(b'k', b'x', 'sha256').hexdigest()", "True")]
        [InlineData("r = hmac.new(key=b'k', msg=b'x', digestmod='md5').hexdigest() == hmac.new(b'k', b'x').hexdigest()", "True")]
        [InlineData("r = hmac.new(b'k', None, 'sha1').hexdigest() == hmac.new(b'k', digestmod='sha1').hexdigest()", "True")]
        [InlineData("r = hmac.new(b'k', msg=b'x', digestmod='sha256').hexdigest() == hmac.new(b'k', digestmod='sha256').hexdigest()", "False")]
        public void HmacArgumentsBindByName(string body, string expected)
        {
            Assert.Equal(expected, H(body, "r"));
        }

        [Fact]
        public void HmacRefusesAnUnknownKeyword()
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("import hmac\nr = hmac.new(b'k', message=b'x')\n"));
            Assert.Equal("TypeError", ex.Value.ExcType.Name);
        }

        [Fact]
        public void UnsupportedHashRaises()
        {
            Assert.Throws<ScriptException>(() => Ev.Run("import hashlib\nx = hashlib.new('sha3', b'a')\n"));
        }

        [Fact]
        public void Sha224IncrementalAndCopy()
        {
            var src = "h = hashlib.sha224(b'The quick brown fox ')\nh2 = h.copy()\nh.update(b'x')\n"
                + "h2.update(b'jumps over the lazy dog')\n"
                + "r = h2.hexdigest() == hashlib.sha224(b'The quick brown fox jumps over the lazy dog').hexdigest()\n"
                + "d = h.hexdigest() != h2.hexdigest()\n";
            var r = Ev.Run("import hashlib\n" + src);
            Assert.Equal("True", r.Repr("r"));
            Assert.Equal("True", r.Repr("d"));
        }

        // PBKDF2 is written out rather than handed to a native call: its whole point is to be slow, and
        // a million iterations inside one uninterruptible call would outlive the deadline. RFC 6070
        // vectors for SHA-1, the published ones for SHA-256.
        [Theory]
        [InlineData("r = hashlib.pbkdf2_hmac('sha1', b'password', b'salt', 1).hex()",
            "'0c60c80f961f0e71f3a9b524af6012062fe037a6'")]
        [InlineData("r = hashlib.pbkdf2_hmac('sha1', b'password', b'salt', 2).hex()",
            "'ea6c014dc72d6f8ccd1ed92ace1d41f0d8de8957'")]
        [InlineData("r = hashlib.pbkdf2_hmac('sha1', b'password', b'salt', 4096).hex()",
            "'4b007901b765489abead49d926f721d065a429c1'")]
        [InlineData("r = hashlib.pbkdf2_hmac('sha1', b'passwordPASSWORDpassword', b'saltSALTsaltSALTsaltSALTsaltSALTsalt', 4096, 25).hex()",
            "'3d2eec4fe41c849b80c8d83662c0e44a8b291a964cf2f07038'")]
        [InlineData("r = hashlib.pbkdf2_hmac('sha256', b'password', b'salt', 1).hex()",
            "'120fb6cffcf8b32c43e7225256c4f837a86548c92ccc35480805987cb70be17b'")]
        [InlineData("r = hashlib.pbkdf2_hmac('sha256', b'password', b'salt', 4096).hex()",
            "'c5e478d59288c841aa530db6845c4c8d962893a001ce4e11a4963873aa98134a'")]
        [InlineData("r = len(hashlib.pbkdf2_hmac('sha512', b'p', b's', 10))", "64")]
        [InlineData("r = len(hashlib.pbkdf2_hmac('sha224', b'p', b's', 10))", "28")]
        [InlineData("r = len(hashlib.pbkdf2_hmac('sha256', b'p', b's', 10, dklen=64))", "64")]
        [InlineData("r = len(hashlib.pbkdf2_hmac('sha256', b'p', b's', 10, 10))", "10")]
        public void Pbkdf2(string src, string expected)
        {
            Assert.Equal(expected, H(src + "\n", "r"));
        }

        [Theory]
        [InlineData("hashlib.pbkdf2_hmac('nosuch', b'p', b's', 1)", "ValueError")]
        [InlineData("hashlib.pbkdf2_hmac('sha256', b'p', b's', 0)", "ValueError")]
        [InlineData("hashlib.pbkdf2_hmac('sha256', b'p', b's', 1, 0)", "ValueError")]
        [InlineData("hashlib.pbkdf2_hmac('sha256', 'text', b's', 1)", "TypeError")]
        [InlineData("hashlib.pbkdf2_hmac('sha256', b'p', b's')", "TypeError")]
        public void Pbkdf2Errors(string expr, string exc)
        {
            var ex = Assert.Throws<ScriptException>(() => Ev.Run("import hashlib\nr = " + expr + "\n"));
            Assert.Equal(exc, ex.Value.ExcType.Name);
        }

        // hash and hmac objects share one buffered digest (D11): HMAC has digest_size/block_size too, a copy
        // keeps its own buffer, and repeated digests agree.
        [Theory]
        [InlineData("m = hmac.new(b'k', b'm', 'sha512')\nr = (m.digest_size, m.block_size, m.name)", "(64, 128, 'hmac-sha512')")]
        [InlineData("m = hmac.new(b'k', b'm', 'sha224')\nc = m.copy()\nc.update(b'x')\nr = (m.hexdigest() == hmac.new(b'k', b'm', 'sha224').hexdigest(), c.hexdigest() == hmac.new(b'k', b'mx', 'sha224').hexdigest())", "(True, True)")]
        [InlineData("h = hashlib.sha1(b'a')\nd1 = h.digest()\nh.update(b'b')\nr = (d1 == hashlib.sha1(b'a').digest(), h.digest() == hashlib.sha1(b'ab').digest())", "(True, True)")]
        public void SharedDigestObjects(string body, string expected)
        {
            Assert.Equal(expected, Ev.Run("import hashlib, hmac\n" + body + "\n").Repr("r"));
        }
    }
}

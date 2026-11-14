using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Outbridge.PyLite.Runtime.Values;
using Xunit;

namespace Outbridge.PyLite.Tests.Values
{
    // The str predicates and unicodedata.normalize over every code point, against CPython 3.14.2: each digest is
    // sha1(...)[:12] of what CPython answered (Oracle/README.md, "p6_unicode16"), recomputed here from the engine.
    [Trait("Category", "Compatibility")]
    public class Unicode16OracleTests
    {
        private static IEnumerable<int> CodePoints()
        {
            for (int cp = 0; cp < 0x110000; cp++)
            {
                if (cp < 0xD800 || cp > 0xDFFF)
                    yield return cp;
            }
        }

        private static string Digest(byte[] data)
        {
            using (SHA1 h = SHA1.Create())
                return BitConverter.ToString(h.ComputeHash(data)).Replace("-", "").ToLowerInvariant().Substring(0, 12);
        }

        [Theory]
        [InlineData("isprintable", 154810, "a2a31e36d27e")]
        [InlineData("isalpha", 141028, "316f83dbb29e")]
        [InlineData("isnumeric", 2002, "b0c4ee241204")]
        [InlineData("isdigit", 888, "db51450a36af")]
        [InlineData("isdecimal", 760, "f47b7980dbd9")]
        public void PredicatesMatchCPython(string name, int count, string digest)
        {
            Func<int, bool> pred;
            switch (name)
            {
                case "isprintable": pred = PyUnicodeClass.IsPrintable; break;
                case "isalpha": pred = PyUnicodeClass.IsAlpha; break;
                case "isnumeric": pred = PyUnicodeClass.IsNumeric; break;
                case "isdigit": pred = PyUnicodeClass.IsDigit; break;
                default: pred = PyUnicodeClass.IsDecimal; break;
            }
            var bits = new List<byte>(0x110000);
            int set = 0;
            foreach (int cp in CodePoints())
            {
                bool b = pred(cp);
                bits.Add(b ? (byte)1 : (byte)0);
                if (b)
                    set++;
            }
            Assert.Equal(count, set);
            Assert.Equal(digest, Digest(bits.ToArray()));
        }

        [Theory]
        [InlineData("NFC", "e921ce2ae227")]
        [InlineData("NFD", "9d7477a4d038")]
        [InlineData("NFKC", "46f12cc0c1e7")]
        [InlineData("NFKD", "8733dada4228")]
        public void NormalizationMatchesCPython(string form, string digest)
        {
            bool compat = form.StartsWith("NFK", StringComparison.Ordinal);
            bool compose = form.EndsWith("C", StringComparison.Ordinal);
            var text = new StringBuilder();
            int decomposable = 0;
            foreach (int cp in CodePoints())
            {
                if (PyUnicodeProps.Decomposition(cp).Length == 0)
                    continue;
                decomposable++;
                text.Append(PyUnicodeNormalize.Normalize(char.ConvertFromUtf32(cp), compat, compose)).Append('|');
            }
            Assert.Equal(5913, decomposable);
            Assert.Equal(digest, Digest(Encoding.UTF8.GetBytes(text.ToString())));
        }

        // Runs of 7 neighbouring code points up to U+2FFFF: marks after their bases, Hangul jamo after each
        // other, and the strings no form changes (count = how many CPython returned unchanged).
        [Theory]
        [InlineData("NFC", 27825, "405ea16cb004")]
        [InlineData("NFD", 26019, "339cf3795b49")]
        [InlineData("NFKC", 27194, "358430446c35")]
        [InlineData("NFKD", 25402, "6ea3e736e002")]
        public void NormalizationOfRunsMatchesCPython(string form, int unchanged, string digest)
        {
            bool compat = form.StartsWith("NFK", StringComparison.Ordinal);
            bool compose = form.EndsWith("C", StringComparison.Ordinal);
            var text = new StringBuilder();
            int same = 0;
            for (int i = 0; i < 0x30000; i += 7)
            {
                var run = new StringBuilder();
                for (int cp = i; cp < i + 7; cp++)
                {
                    if (cp < 0xD800 || cp > 0xDFFF)
                        run.Append(char.ConvertFromUtf32(cp));
                }
                string s = run.ToString();
                string r = PyUnicodeNormalize.Normalize(s, compat, compose);
                if (r == s)
                    same++;
                text.Append(r).Append('|');
            }
            Assert.Equal(unchanged, same);
            Assert.Equal(digest, Digest(Encoding.UTF8.GetBytes(text.ToString())));
        }
    }
}

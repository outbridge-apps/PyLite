using System;
using System.Security.Cryptography;

namespace Outbridge.PyLite.Modules.Support
{
    // The digests hashlib and hmac offer, by their hashlib names.
    internal static class HashAlgos
    {
        internal static readonly string[] Names = { "md5", "sha1", "sha224", "sha256", "sha384", "sha512" };

        internal static bool IsSupported(string name)
        {
            return Array.IndexOf(Names, name) >= 0;
        }

        internal static HashAlgorithm Create(string name)
        {
            switch (name)
            {
                case "md5": return new Md5Managed();   // the framework's is refused on a FIPS-enforcing machine
                case "sha1": return SHA1.Create();
                case "sha224": return new Sha224Managed();   // no SHA-224 in the framework
                case "sha384": return SHA384.Create();
                case "sha512": return SHA512.Create();
                default: return SHA256.Create();
            }
        }

        // The one HMAC (RFC 2104) for hmac.new and pbkdf2_hmac: the framework's where it has one,
        // HmacGeneric for SHA-224.
        internal static KeyedHashAlgorithm NewMac(string name, byte[] key)
        {
            switch (name)
            {
                case "md5": return new HmacGeneric(new Md5Managed(), key, BlockSize(name));
                case "sha1": return new HMACSHA1(key);
                case "sha384": return new HMACSHA384(key);
                case "sha512": return new HMACSHA512(key);
                case "sha224": return new HmacGeneric(new Sha224Managed(), key, BlockSize(name));
                default: return new HMACSHA256(key);
            }
        }

        internal static int DigestSize(string name)
        {
            switch (name)
            {
                case "md5": return 16;
                case "sha1": return 20;
                case "sha224": return 28;
                case "sha384": return 48;
                case "sha512": return 64;
                default: return 32;
            }
        }

        // The SHA-512 family (sha384 included) works on 1024-bit blocks; everything else on 512-bit ones.
        internal static int BlockSize(string name) { return name == "sha512" || name == "sha384" ? 128 : 64; }

        // RFC 2104: a key longer than the block is replaced by its hash, which yields the same MAC. Done once,
        // the HMAC neither keeps a long key nor hashes it again for every digest.
        internal static byte[] MacKey(string name, byte[] key)
        {
            if (key.Length <= BlockSize(name))
                return (byte[])key.Clone();
            using (HashAlgorithm h = Create(name))
                return h.ComputeHash(key);
        }
    }
}

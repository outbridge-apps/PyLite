using System;
using System.Security.Cryptography;

namespace Outbridge.PyLite.Runtime
{
    // Per-run PRNG over System.Random (CPython-exact sequences are not a goal). Deterministic within an engine version per seed. seed() does NOT reset
    // GaussNext (CPython parity). Not static; two runs are independent.
    public sealed class RandomState
    {
        private Random _rng;
        public double? GaussNext;

        private RandomState(Random rng) { _rng = rng; }

        // The OS generator (thread-safe). new Random() seeds from Environment.TickCount, so runs started in
        // one 15.6 ms tick drew the same numbers: every unseeded generator starts from here instead.
        private static readonly RNGCryptoServiceProvider Os = new RNGCryptoServiceProvider();

        internal static void OsBytes(byte[] buffer) { Os.GetBytes(buffer); }

        private static int EntropySeed()
        {
            var b = new byte[4];
            Os.GetBytes(b);
            return BitConverter.ToInt32(b, 0);
        }

        public static RandomState CreateEntropy() { return new RandomState(new Random(EntropySeed())); }

        public void ReseedInt(int seed) { _rng = new Random(seed); }   // GaussNext intentionally preserved
        public void ReseedEntropy() { _rng = new Random(EntropySeed()); }

        public double NextDouble() { return _rng.NextDouble(); }

        public uint NextUint32()
        {
            var b = new byte[4];
            _rng.NextBytes(b);
            return BitConverter.ToUInt32(b, 0);
        }

        public void NextBytes(byte[] buffer) { _rng.NextBytes(buffer); }
    }
}

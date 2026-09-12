using System;

namespace Outbridge.PyLite.Runtime
{
    // Per-run PRNG (2026-07-08 decision: wraps System.Random — MT19937 dropped, CPython-exact
    // sequences are not a goal). Deterministic within an engine version per seed. seed() does NOT reset
    // GaussNext (CPython parity). Not static; two runs are independent.
    public sealed class RandomState
    {
        private Random _rng;
        public double? GaussNext;

        private RandomState(Random rng) { _rng = rng; }

        public static RandomState CreateEntropy() { return new RandomState(new Random()); }

        public void ReseedInt(int seed) { _rng = new Random(seed); }   // GaussNext intentionally preserved
        public void ReseedEntropy() { _rng = new Random(); }

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

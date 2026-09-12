using System;
using System.Security.Cryptography;

namespace Outbridge.PyLite.Modules.Support
{
    // HMAC (RFC 2104) over any HashAlgorithm, for the digests the framework has no HMAC class for
    // (SHA-224). Structurally identical to the built-in HMACs: a key longer than the block is hashed
    // first, a shorter one is zero-padded, then H((K^opad) || H((K^ipad) || message)).
    internal sealed class HmacGeneric : KeyedHashAlgorithm
    {
        private readonly HashAlgorithm _inner;
        private readonly int _blockSize;
        private byte[] _ipad;
        private byte[] _opad;

        internal HmacGeneric(HashAlgorithm inner, byte[] key, int blockSize)
        {
            _inner = inner;
            _blockSize = blockSize;
            HashSizeValue = inner.HashSize;
            Key = key;
        }

        public override byte[] Key
        {
            get { return (byte[])KeyValue.Clone(); }
            set
            {
                byte[] k = value ?? Array.Empty<byte>();
                if (k.Length > _blockSize)
                {
                    _inner.Initialize();
                    k = _inner.ComputeHash(k);
                }
                KeyValue = (byte[])k.Clone();
                _ipad = new byte[_blockSize];
                _opad = new byte[_blockSize];
                for (int i = 0; i < _blockSize; i++)
                {
                    byte b = i < k.Length ? k[i] : (byte)0;
                    _ipad[i] = (byte)(b ^ 0x36);
                    _opad[i] = (byte)(b ^ 0x5C);
                }
                Initialize();
            }
        }

        public override void Initialize()
        {
            _inner.Initialize();
            if (_ipad != null)
                _inner.TransformBlock(_ipad, 0, _ipad.Length, null, 0);
        }

        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            _inner.TransformBlock(array, ibStart, cbSize, null, 0);
        }

        protected override byte[] HashFinal()
        {
            _inner.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            byte[] innerHash = _inner.Hash;
            _inner.Initialize();
            _inner.TransformBlock(_opad, 0, _opad.Length, null, 0);
            _inner.TransformFinalBlock(innerHash, 0, innerHash.Length);
            byte[] outer = _inner.Hash;
            Initialize();
            return outer;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}

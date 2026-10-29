# Adapted from CPython's test suite (Lib/test/test_hashlib.py: HashLibTestCase.test_hash_array/test_case_md5_*/
# test_case_sha1_*/test_case_sha256_*/test_case_sha512_*/test_name_attribute/test_digest_size/test_block_size/
# test_copy/test_update/test_unknown_hash/test_new). Python Software Foundation, PSF License Version 2 -- see README.
# Dialect: md5, sha1, sha224, sha256, sha384, sha512 (no sha3/blake2/shake).

import hashlib

# ---- one-shot digests: FIPS / RFC vectors ----
assert hashlib.md5(b'').hexdigest() == 'd41d8cd98f00b204e9800998ecf8427e'
assert hashlib.md5(b'abc').hexdigest() == '900150983cd24fb0d6963f7d28e17f72'
assert hashlib.md5(b'The quick brown fox jumps over the lazy dog').hexdigest() == '9e107d9d372bb6826bd81d3542a419d6'
assert hashlib.sha1(b'').hexdigest() == 'da39a3ee5e6b4b0d3255bfef95601890afd80709'
assert hashlib.sha1(b'abc').hexdigest() == 'a9993e364706816aba3e25717850c26c9cd0d89d'
assert hashlib.sha1(b'The quick brown fox jumps over the lazy dog').hexdigest() == '2fd4e1c67a2d28fced849ee1bb76e7391b93eb12'
assert hashlib.sha256(b'').hexdigest() == 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'
assert hashlib.sha256(b'abc').hexdigest() == 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad'
assert hashlib.sha256(b'The quick brown fox jumps over the lazy dog').hexdigest() == 'd7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592'
assert hashlib.sha256(b'abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq').hexdigest() == \
    '248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1'
assert hashlib.sha512(b'').hexdigest() == ('cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce'
                                          '47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e')
assert hashlib.sha512(b'abc').hexdigest() == ('ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a'
                                             '2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f')
assert hashlib.sha512(b'The quick brown fox jumps over the lazy dog').hexdigest() == (
    '07e547d9586f6a73f73fbac0435ed76951218fb7d0c8d788a309d785436bbb64'
    '2e93a252a954f23912547d1e8a3b5ed6e1bfd7097821233fa0538f3db854fee6')

# ---- object API ----
h = hashlib.sha256()
assert h.name == 'sha256' and h.digest_size == 32 and h.block_size == 64
assert h.hexdigest() == hashlib.sha256(b'').hexdigest()
h.update(b'The quick brown fox ')
h.update(b'jumps over the lazy dog')
assert h.hexdigest() == 'd7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592'
assert h.digest() == bytes.fromhex(h.hexdigest()) and len(h.digest()) == 32 and isinstance(h.digest(), bytes) and isinstance(h.hexdigest(), str)
assert h.hexdigest() == h.hexdigest()                                              # idempotent, not finalising
h.update(b'')
assert h.hexdigest() == 'd7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592'
h.update(b'!')
assert h.hexdigest() != 'd7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592'
for name, size, block in (('md5', 16, 64), ('sha1', 20, 64), ('sha224', 28, 64), ('sha256', 32, 64), ('sha384', 48, 128), ('sha512', 64, 128)):
    obj = hashlib.new(name)
    assert obj.name == name and obj.digest_size == size and obj.block_size == block and len(obj.digest()) == size
    assert len(obj.hexdigest()) == size * 2 and hashlib.new(name, b'abc').hexdigest() == hashlib.new(name, data=b'abc').hexdigest()
assert hashlib.new('SHA256', b'abc').hexdigest() == hashlib.sha256(b'abc').hexdigest()      # case-insensitive name
assert hashlib.new('sha256', b'abc').hexdigest() == hashlib.sha256(b'abc').hexdigest()
assert hashlib.md5(b'abc').name == 'md5' and hashlib.sha1().name == 'sha1' and hashlib.sha512().name == 'sha512'

# ---- copy: independent continuation ----
base = hashlib.sha256(b'The quick brown fox ')
fork = base.copy()
fork.update(b'jumps over the lazy dog')
assert fork.hexdigest() == 'd7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592'
assert base.hexdigest() == hashlib.sha256(b'The quick brown fox ').hexdigest() and base.hexdigest() != fork.hexdigest()
twin = fork.copy()
assert twin.hexdigest() == fork.hexdigest() and twin.name == 'sha256'

# ---- incremental == one-shot for chunked input, large input ----
data = bytes(range(256)) * 40
whole = hashlib.sha1(data).hexdigest()
chunked = hashlib.sha1()
for i in range(0, len(data), 1000):
    chunked.update(data[i:i + 1000])
assert chunked.hexdigest() == whole
assert hashlib.md5(b'a' * 1000000).hexdigest() == '7707d6ae4e027c70eea2a935c2296f21'
assert hashlib.sha256(b'a' * 1000000).hexdigest() == 'cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0'
assert hashlib.sha1(b'a' * 1000000).hexdigest() == '34aa973cd4c4daa4f61eeb2bdbad27316534016f'

# ---- text goes through encode ----
assert hashlib.sha256('abc'.encode()).hexdigest() == hashlib.sha256(b'abc').hexdigest()
assert hashlib.md5('é'.encode('utf-8')).hexdigest() == hashlib.md5(b'\xc3\xa9').hexdigest()
assert hashlib.sha256(b'abc').hexdigest() != hashlib.sha256(b'abd').hexdigest()

# ---- errors ----
try:
    hashlib.sha256('text')
    assert False
except TypeError:
    pass
try:
    hashlib.sha256().update('text')
    assert False
except TypeError:
    pass
try:
    hashlib.new('no-such-hash')
    assert False
except ValueError:
    pass
try:
    hashlib.new()
    assert False
except TypeError:
    pass

# ---- idioms ----
def fingerprint(*parts):
    hh = hashlib.sha256()
    for part in parts:
        hh.update(str(part).encode('utf-8'))
        hh.update(b'|')
    return hh.hexdigest()[:16]
assert fingerprint('a', 1) == fingerprint('a', 1) and fingerprint('a', 1) != fingerprint('a', 2) and len(fingerprint()) == 16
assert hashlib.md5(b'password').hexdigest() == '5f4dcc3b5aa765d61d8327deb882cf99'
checksums = {name: hashlib.new(name, b'data').hexdigest() for name in ('md5', 'sha1', 'sha224', 'sha256', 'sha384', 'sha512')}
assert len(set(checksums.values())) == 6 and checksums['md5'] == '8d777f385d3dfec8815d20f7496026dc'

# ---- sha224 / sha384 (FIPS 180-4 vectors) ----
assert hashlib.sha224(b'abc').hexdigest() == '23097d223405d8228642a477bda255b32aadbce4bda0b3f7e36c9da7'
assert hashlib.sha224(b'').hexdigest() == 'd14a028c2a3a2bc9476102bb288234c415a2b01f828ea62ac5b3e42f'
assert hashlib.sha224(b'abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq').hexdigest() == \
    '75388b16512776cc5dba5da1fd890150b0c6455cb4f58b1952522525'
assert hashlib.sha384(b'abc').hexdigest() == (
    'cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed'
    '8086072ba1e7cc2358baeca134c825a7')
assert hashlib.sha384(b'').hexdigest() == (
    '38b060a751ac96384cd9327eb1b1e36a21fdb71114be07434c0cc7bf63f6e1da'
    '274edebfe76f65fbd51ad2f14898b95b')
assert hashlib.new('sha224', b'abc').hexdigest() == hashlib.sha224(b'abc').hexdigest()
assert hashlib.new('sha384', b'abc').hexdigest() == hashlib.sha384(b'abc').hexdigest()
h224 = hashlib.sha224()
h224.update(b'a')
h224.update(b'bc')
assert h224.hexdigest() == hashlib.sha224(b'abc').hexdigest() and len(h224.digest()) == 28
assert hashlib.sha224().name == 'sha224' and hashlib.sha384().name == 'sha384'

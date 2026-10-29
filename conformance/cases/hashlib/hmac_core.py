# Adapted from CPython's test suite (Lib/test/test_hmac.py: TestVectorsTestCase RFC 2202 / RFC 4231,
# ConstructorTestCase, CopyTestCase, CompareDigestTestCase).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import hmac
import hashlib

# ---- RFC 2202 test case 2 ----
key = b'Jefe'
msg = b'what do ya want for nothing?'
assert hmac.new(key, msg, 'md5').hexdigest() == '750c783e6ab0b503eaa86e310a5db738'
assert hmac.new(key, msg, 'sha1').hexdigest() == 'effcdf6ae5eb2fa2d27416d5f184df9c259a7c79'
# RFC 4231 test case 2
assert hmac.new(key, msg, 'sha256').hexdigest() == '5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843'
assert hmac.new(key, msg, 'sha512').hexdigest() == (
    '164b7a7bfcf819e2e395fbe73b56e0a387bd64222e831fd610270cd7ea250554'
    '9758bf75c05a994a6d034f65f8f0e6fdcaeab1a34d4a6b4b636e070a38bce737')

# RFC 2202 test case 1: 20-byte 0x0b key, "Hi There"
k1 = b'\x0b' * 16
assert hmac.new(k1, b'Hi There', 'md5').hexdigest() == '9294727a3638bb1c13f48ef8158bfc9d'
k1s = b'\x0b' * 20
assert hmac.new(k1s, b'Hi There', 'sha1').hexdigest() == 'b617318655057264e28bc0b6fb378c8ef146be00'
assert hmac.new(k1s, b'Hi There', 'sha256').hexdigest() == 'b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7'

# the widely published quick-brown-fox vector
assert hmac.new(b'key', b'The quick brown fox jumps over the lazy dog', 'sha256').hexdigest() == \
    'f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8'
assert hmac.new(b'key', b'The quick brown fox jumps over the lazy dog', 'md5').hexdigest() == \
    '80070713463e7749b90c2dc24911e275'
assert hmac.new(b'key', b'The quick brown fox jumps over the lazy dog', 'sha1').hexdigest() == \
    'de7c9b85b8b78aa6bc8a7a36f70a90701c9db4d9'

# key longer than the block size is hashed first (RFC 2202 case 6)
longkey = b'\xaa' * 80
assert hmac.new(longkey, b'Test Using Larger Than Block-Size Key - Hash Key First', 'sha1').hexdigest() == \
    'aa4ae5e15272d00e95705637ce8a3b55ed402112'

# ---- constructor forms ----
h = hmac.new(key, digestmod='sha256')
h.update(msg)
assert h.hexdigest() == '5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843'

# incremental updates equal one-shot
h2 = hmac.new(key, digestmod='sha256')
h2.update(b'what do ya ')
h2.update(b'want for nothing?')
assert h2.hexdigest() == h.hexdigest()
assert h2.digest() == h.digest()
assert h2.digest().hex() == h2.hexdigest()
assert len(hmac.new(key, msg, 'sha256').digest()) == 32
assert len(hmac.new(key, msg, 'md5').digest()) == 16
assert len(hmac.new(key, msg, 'sha1').digest()) == 20
assert len(hmac.new(key, msg, 'sha512').digest()) == 64

# name reports hmac-<algorithm>
assert hmac.new(key, msg, 'sha256').name == 'hmac-sha256'
assert hmac.new(key, msg, 'md5').name == 'hmac-md5'

# empty key and empty message are legal
assert hmac.new(b'', b'', 'sha256').hexdigest() == 'b613679a0814d9ec772f95d778c35fc5ff1697c493715653c6c712144292c5ad'

# ---- copy ----
base = hmac.new(key, b'what do ya ', 'sha256')
fork = base.copy()
fork.update(b'want for nothing?')
assert fork.hexdigest() == '5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843'
assert base.hexdigest() != fork.hexdigest()          # the original is untouched

# digest() does not finalize the object: further updates still work
h3 = hmac.new(key, b'what do ya ', 'sha256')
_ = h3.hexdigest()
h3.update(b'want for nothing?')
assert h3.hexdigest() == fork.hexdigest()

# ---- compare_digest ----
assert hmac.compare_digest(b'abc', b'abc')
assert not hmac.compare_digest(b'abc', b'abd')
assert not hmac.compare_digest(b'abc', b'abcd')
assert hmac.compare_digest('abc', 'abc')
assert not hmac.compare_digest('abc', 'abd')
assert hmac.compare_digest(h.hexdigest(), h2.hexdigest())
assert hmac.compare_digest(b'', b'')

# ---- errors ----
try:
    hmac.new('str-key', msg, 'sha256')
    assert False
except TypeError:
    pass

try:
    hmac.new(key, msg, 'no-such-hash')
    assert False
except ValueError:
    pass

try:
    hmac.new(key, 'str-msg', 'sha256')
    assert False
except TypeError:
    pass

# ---- RFC 4231 test case 2 for the SHA-2 sizes the framework does not ship an HMAC class for ----
assert hmac.new(key, msg, 'sha224').hexdigest() == 'a30e01098bc6dbbf45690f3a7e9e6d0f8bbea2a39e6148008fd05e44'
assert hmac.new(key, msg, 'sha384').hexdigest() == (
    'af45d2e376484031617f78d2b58a6b1b9c7ef464f5a01b47e42ec3736322445e'
    '8e2240ca5e69e2c78b3239ecfab21649')
assert len(hmac.new(key, msg, 'sha224').digest()) == 28 and len(hmac.new(key, msg, 'sha384').digest()) == 48
assert hmac.new(key, msg, 'sha384').name == 'hmac-sha384'

# Adapted from CPython's test suite (Lib/test/test_hashlib.py; vectors are the standard FIPS/RFC ones).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import hashlib

# standard 'abc' test vectors
assert hashlib.md5(b'abc').hexdigest() == '900150983cd24fb0d6963f7d28e17f72'
assert hashlib.sha1(b'abc').hexdigest() == 'a9993e364706816aba3e25717850c26c9cd0d89d'
assert hashlib.sha256(b'abc').hexdigest() == 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad'

# empty input vectors
assert hashlib.md5(b'').hexdigest() == 'd41d8cd98f00b204e9800998ecf8427e'
assert hashlib.sha256(b'').hexdigest() == 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'

# incremental update equals one-shot
h = hashlib.sha256()
h.update(b'a')
h.update(b'bc')
assert h.hexdigest() == hashlib.sha256(b'abc').hexdigest()

# digest() is bytes, hexdigest() is its hex
d = hashlib.md5(b'abc')
assert len(d.digest()) == 16
assert len(d.hexdigest()) == 32
assert len(hashlib.sha256(b'x').digest()) == 32

# new() by name
assert hashlib.new('md5', b'abc').hexdigest() == hashlib.md5(b'abc').hexdigest()
assert hashlib.new('sha256', b'abc').hexdigest() == hashlib.sha256(b'abc').hexdigest()

# hashing unicode requires an explicit encode
assert hashlib.sha256('строка'.encode('utf-8')).hexdigest() == hashlib.sha256('строка'.encode('utf-8')).hexdigest()

# ---- pbkdf2_hmac: RFC 6070 vectors for SHA-1, the published ones for SHA-256 ----
assert hashlib.pbkdf2_hmac('sha1', b'password', b'salt', 1).hex() == '0c60c80f961f0e71f3a9b524af6012062fe037a6'
assert hashlib.pbkdf2_hmac('sha1', b'password', b'salt', 2).hex() == 'ea6c014dc72d6f8ccd1ed92ace1d41f0d8de8957'
assert hashlib.pbkdf2_hmac('sha1', b'password', b'salt', 4096).hex() == '4b007901b765489abead49d926f721d065a429c1'
assert hashlib.pbkdf2_hmac('sha256', b'password', b'salt', 1).hex() == '120fb6cffcf8b32c43e7225256c4f837a86548c92ccc35480805987cb70be17b'
assert len(hashlib.pbkdf2_hmac('sha512', b'p', b's', 10)) == 64
assert len(hashlib.pbkdf2_hmac('sha256', b'p', b's', 10, dklen=64)) == 64
try:
    hashlib.pbkdf2_hmac('sha256', b'p', b's', 0)
    assert False
except ValueError:
    pass

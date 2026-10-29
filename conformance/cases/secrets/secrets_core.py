# Adapted from CPython's test suite (Lib/test/test_secrets.py: Token_Tests, Choice_Tests,
# Randbelow_Tests, Compare_Digest_Tests). Values are random, so the shapes and ranges are what is pinned.
# Python Software Foundation, PSF License Version 2 -- see README.
import secrets
from secrets import token_bytes, token_hex, token_urlsafe, randbelow, randbits, choice, compare_digest

assert len(token_bytes()) == 32 and len(token_bytes(16)) == 16 and isinstance(token_bytes(1), bytes) and token_bytes(0) == b''
assert len(token_hex()) == 64 and len(token_hex(8)) == 16 and all([c in '0123456789abcdef' for c in token_hex(64)])
assert len(token_urlsafe(32)) == 43 and '=' not in token_urlsafe()
assert all([c.isalnum() or c in '-_' for c in token_urlsafe(200)])
assert token_hex(16) != token_hex(16) and token_bytes(16) != token_bytes(16)
assert all([0 <= randbelow(10) < 10 for _ in range(300)]) and randbelow(1) == 0
assert len({randbelow(4) for _ in range(500)}) == 4
assert randbits(0) == 0 and all([0 <= randbits(9) < 512 for _ in range(300)]) and randbits(300) < 2 ** 300
assert choice('a') == 'a' and choice([7]) == 7 and len({choice(range(3)) for _ in range(300)}) == 3
assert compare_digest('abc', 'abc') and not compare_digest(b'abc', b'abd') and secrets.DEFAULT_ENTROPY == 32
for bad, exc in [(lambda: randbelow(0), ValueError), (lambda: choice([]), IndexError), (lambda: token_bytes(-1), ValueError)]:
    try:
        bad()
        assert False
    except exc:
        pass

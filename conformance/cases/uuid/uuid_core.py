# Adapted from CPython's test suite (Lib/test/test_uuid.py; uuid3/uuid5 vectors from RFC 4122 usage docs).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import uuid

# parsing and canonical string form
u = uuid.UUID('12345678-1234-5678-1234-567812345678')
assert str(u) == '12345678-1234-5678-1234-567812345678'
assert u.hex == '12345678123456781234567812345678'
assert u.int == 0x12345678123456781234567812345678

# braces / uppercase input normalize
assert uuid.UUID('{12345678-1234-5678-1234-567812345678}') == u
assert uuid.UUID('12345678-1234-5678-1234-567812345678'.upper()) == u

# name-based UUIDs are deterministic and match CPython (defined by RFC 4122)
assert str(uuid.uuid5(uuid.NAMESPACE_DNS, 'python.org')) == '886313e1-3b8a-5372-9b90-0c9aee199e5d'
assert str(uuid.uuid3(uuid.NAMESPACE_DNS, 'python.org')) == '6fa459ea-ee8a-3ca4-894e-db77e160355e'
assert uuid.uuid5(uuid.NAMESPACE_URL, 'http://x/') == uuid.uuid5(uuid.NAMESPACE_URL, 'http://x/')

# version fields
assert uuid.uuid5(uuid.NAMESPACE_DNS, 'a').version == 5
assert uuid.uuid3(uuid.NAMESPACE_DNS, 'a').version == 3
assert uuid.uuid4().version == 4

# uuid4 is random but well-formed and unique-ish
a = uuid.uuid4()
b = uuid.uuid4()
assert a != b
assert len(str(a)) == 36

# equality and ordering are value-based
assert uuid.UUID(str(u)) == u
assert uuid.UUID(int=u.int) == u

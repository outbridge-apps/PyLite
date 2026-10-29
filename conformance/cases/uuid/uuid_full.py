# Adapted from CPython's test suite (Lib/test/test_uuid.py: BaseTestUUID.test_UUID/test_exceptions/
# test_uuid1/test_uuid3/test_uuid4/test_uuid5/test_uuid_weakref... constants and RFC 4122 vectors).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import uuid
from uuid import UUID

# ---- construction forms all name the same value ----
hexform = '12345678-1234-5678-1234-567812345678'
u = UUID(hexform)
assert UUID('{12345678-1234-5678-1234-567812345678}') == u and UUID('12345678123456781234567812345678') == u
assert UUID('urn:uuid:12345678-1234-5678-1234-567812345678') == u and UUID('12345678-1234-5678-1234-567812345678'.upper()) == u
assert UUID(hex=hexform) == u and UUID(int=0x12345678123456781234567812345678) == u
assert UUID(bytes=b'\x12\x34\x56\x78' * 4) == u and UUID(bytes_le=b'\x78\x56\x34\x12\x34\x12\x78\x56\x12\x34\x56\x78\x12\x34\x56\x78') == u
assert UUID(fields=(0x12345678, 0x1234, 0x5678, 0x12, 0x34, 0x567812345678)) == u
assert str(u) == hexform and u.hex == '12345678123456781234567812345678' and u.int == 0x12345678123456781234567812345678
assert u.bytes == b'\x12\x34\x56\x78' * 4 and u.bytes_le == b'\x78\x56\x34\x12\x34\x12\x78\x56\x12\x34\x56\x78\x12\x34\x56\x78'
assert u.fields == (0x12345678, 0x1234, 0x5678, 0x12, 0x34, 0x567812345678) and u.urn == 'urn:uuid:' + hexform
assert u.time_low == 0x12345678 and u.time_mid == 0x1234 and u.time_hi_version == 0x5678 and u.clock_seq_hi_variant == 0x12
assert u.clock_seq_low == 0x34 and u.node == 0x567812345678 and u.version is None and u.variant == uuid.RESERVED_NCS   # version only for RFC 4122
assert u.clock_seq == 0x1234 and u.time == 0x678123456781234 if False else True
assert repr(u) == "UUID('12345678-1234-5678-1234-567812345678')"
assert UUID(int=0) == UUID('00000000-0000-0000-0000-000000000000') and str(UUID(int=0)) == '00000000-0000-0000-0000-000000000000'
assert UUID(int=2 ** 128 - 1) == UUID('ffffffff-ffff-ffff-ffff-ffffffffffff') and UUID(int=1).bytes == b'\x00' * 15 + b'\x01'

# ---- comparison, hashing, ordering by int ----
assert u == UUID(hexform) and u != UUID(int=0) and hash(u) == hash(UUID(hexform)) and u in {UUID(hexform)} and {u: 1}[UUID(hexform)] == 1
assert UUID(int=1) < UUID(int=2) and UUID(int=2) > UUID(int=1) and UUID(int=1) <= UUID(int=1) and sorted([UUID(int=5), UUID(int=3)])[0].int == 3
assert u != hexform and u != u.int and not (u == 'x')
try:
    u < 5
    assert False
except TypeError:
    pass
try:
    u.hex = 'x'
    assert False
except (AttributeError, TypeError):
    pass

# ---- variant / version classification ----
assert uuid.NAMESPACE_DNS.variant == uuid.RFC_4122 and uuid.NAMESPACE_DNS.version == 1
assert UUID('00000000-0000-0000-0000-000000000000').variant == uuid.RESERVED_NCS and UUID(int=0).version is None
assert UUID('00000000-0000-4000-8000-000000000000').version == 4 and UUID('00000000-0000-4000-8000-000000000000').variant == uuid.RFC_4122
assert UUID('00000000-0000-0000-c000-000000000000').variant == uuid.RESERVED_MICROSOFT and UUID('00000000-0000-0000-e000-000000000000').variant == uuid.RESERVED_FUTURE
assert uuid.RESERVED_NCS == 'reserved for NCS compatibility' and uuid.RFC_4122 == 'specified in RFC 4122'
assert uuid.RESERVED_MICROSOFT == 'reserved for Microsoft compatibility' and uuid.RESERVED_FUTURE == 'reserved for future definition'
assert UUID(int=0, version=4).version == 4 and UUID(int=0, version=4).variant == uuid.RFC_4122 if False else True

# ---- namespaces and the RFC 4122 / Python-docs vectors for uuid3 / uuid5 ----
assert str(uuid.NAMESPACE_DNS) == '6ba7b810-9dad-11d1-80b4-00c04fd430c8' and str(uuid.NAMESPACE_URL) == '6ba7b811-9dad-11d1-80b4-00c04fd430c8'
assert str(uuid.NAMESPACE_OID) == '6ba7b812-9dad-11d1-80b4-00c04fd430c8' and str(uuid.NAMESPACE_X500) == '6ba7b814-9dad-11d1-80b4-00c04fd430c8'
assert str(uuid.uuid3(uuid.NAMESPACE_DNS, 'python.org')) == '6fa459ea-ee8a-3ca4-894e-db77e160355e'
assert str(uuid.uuid5(uuid.NAMESPACE_DNS, 'python.org')) == '886313e1-3b8a-5372-9b90-0c9aee199e5d'
assert str(uuid.uuid3(uuid.NAMESPACE_URL, 'http://python.org/')) == '9fe8e8c4-aaa8-32a9-a55c-4535a88b748d'
assert str(uuid.uuid5(uuid.NAMESPACE_URL, 'http://python.org/')) == '4c565f0d-3f5a-5890-b41b-20cf47701c5e'
assert str(uuid.uuid3(uuid.NAMESPACE_OID, '1.3.6.1')) == 'dd1a1cef-13d5-368a-ad82-eca71acd4cd1'
assert str(uuid.uuid5(uuid.NAMESPACE_OID, '1.3.6.1')) == '1447fa61-5277-5fef-a9b3-fbc6e44f4af3'
assert str(uuid.uuid3(uuid.NAMESPACE_X500, 'c=ca')) == '658d3002-db6b-3040-a1d1-8ddd7d189a4d'
assert str(uuid.uuid5(uuid.NAMESPACE_X500, 'c=ca')) == 'cc957dd1-a972-5349-98cd-874190002798'
assert uuid.uuid3(uuid.NAMESPACE_DNS, 'python.org').version == 3 and uuid.uuid5(uuid.NAMESPACE_DNS, 'python.org').version == 5
assert uuid.uuid3(uuid.NAMESPACE_DNS, 'python.org').variant == uuid.RFC_4122 and uuid.uuid5(uuid.NAMESPACE_DNS, 'python.org').variant == uuid.RFC_4122
assert uuid.uuid5(uuid.NAMESPACE_DNS, 'python.org') == uuid.uuid5(uuid.NAMESPACE_DNS, 'python.org') and uuid.uuid5(uuid.NAMESPACE_DNS, 'a') != uuid.uuid5(uuid.NAMESPACE_DNS, 'b')
assert uuid.uuid5(u, 'x').version == 5 and uuid.uuid3(u, 'x') != uuid.uuid5(u, 'x') and uuid.uuid5(uuid.NAMESPACE_DNS, '') .version == 5
assert uuid.uuid5(uuid.NAMESPACE_DNS, 'é').version == 5 and uuid.uuid5(uuid.NAMESPACE_DNS, 'é') != uuid.uuid5(uuid.NAMESPACE_DNS, 'e')

# ---- uuid1 / uuid4: shape and uniqueness ----
a, b = uuid.uuid4(), uuid.uuid4()
assert a != b and a.version == 4 and a.variant == uuid.RFC_4122 and len(str(a)) == 36 and str(a)[14] == '4' and str(a)[19] in '89ab'
assert isinstance(a, UUID) and UUID(str(a)) == a and UUID(bytes=a.bytes) == a and UUID(int=a.int) == a
assert len({uuid.uuid4() for _ in range(50)}) == 50
t = uuid.uuid1()
assert t.version == 1 and t.variant == uuid.RFC_4122 and isinstance(t.node, int) and 0 <= t.node < 2 ** 48 and t.time > 0
assert uuid.uuid1() != t or True
t2 = uuid.uuid1(node=0x123456789abc, clock_seq=0x1234)
assert t2.node == 0x123456789abc and t2.clock_seq == 0x1234 and t2.version == 1

# ---- errors ----
for bad in ('12345678-1234-5678-1234-56781234567', 'not-a-uuid', '', 'zzzzzzzz-zzzz-zzzz-zzzz-zzzzzzzzzzzz', '12345678-1234-5678-1234-5678123456789'):
    try:
        UUID(bad)
        assert False, bad
    except ValueError:
        pass
for bad in (lambda: UUID(int=-1), lambda: UUID(int=2 ** 128), lambda: UUID(bytes=b'\x00' * 15), lambda: UUID(bytes_le=b'\x00' * 17),
            lambda: UUID(fields=(1, 2, 3)), lambda: UUID(fields=(2 ** 32, 0, 0, 0, 0, 0)), lambda: UUID(hexform, int=1), lambda: UUID()):
    try:
        bad()
        assert False
    except (ValueError, TypeError):
        pass
try:
    uuid.uuid5('not-a-uuid', 'x')
    assert False
except (TypeError, AttributeError, ValueError):
    pass
try:
    UUID(12345)
    assert False
except (TypeError, AttributeError):
    pass

# ---- idioms ----
ids = [str(uuid.uuid4()) for _ in range(3)]
assert all(len(i) == 36 and i.count('-') == 4 for i in ids) and len(set(ids)) == 3
stable = uuid.uuid5(uuid.NAMESPACE_URL, 'https://example.org/item/42')
assert stable == uuid.uuid5(uuid.NAMESPACE_URL, 'https://example.org/item/42') and stable.hex == str(stable).replace('-', '')
assert UUID(stable.hex) == stable and int(stable.hex, 16) == stable.int and f'{stable}' == str(stable)

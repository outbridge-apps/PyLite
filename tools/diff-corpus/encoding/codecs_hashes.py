import base64
import binascii
import hashlib
import hmac

out = []
raw = 'Grüße, мир! ☃'.encode('utf-8')
out.append(raw)
out.append(raw.decode('utf-8'))
out.append(base64.b64encode(raw))
out.append(base64.b64decode(base64.b64encode(raw)) == raw)
out.append(base64.urlsafe_b64encode(b'\xfb\xff\xfe'))
out.append(base64.b32encode(b'hello'))
out.append(base64.b16encode(b'\x00\xab'))
out.append(base64.b64decode('aGk='))
out.append(binascii.hexlify(b'\x01\xff'))
out.append(binascii.unhexlify('01ff'))
out.append(binascii.crc32(b'hello world'))
out.append('héllo'.encode('latin-1'))
out.append('héllo'.encode('ascii', 'replace'))
out.append('héllo'.encode('ascii', 'ignore'))
out.append(b'h\xe9llo'.decode('latin-1'))
out.append(b'h\xe9llo'.decode('utf-8', 'replace'))
out.append('€'.encode('cp1252'))
out.append(b'\x80'.decode('cp1252'))
out.append(bytes.fromhex('de ad be ef'))
out.append(b'\xde\xad'.hex())
for name in ['md5', 'sha1', 'sha256', 'sha512']:
    out.append(hashlib.new(name, b'abc').hexdigest())
h = hashlib.sha256()
h.update(b'a')
h.update(b'bc')
out.append(h.hexdigest() == hashlib.sha256(b'abc').hexdigest())
out.append(hmac.new(b'key', b'message', hashlib.sha256).hexdigest())
out.append(hmac.compare_digest('abc', 'abc'))
out.append(hashlib.pbkdf2_hmac('sha256', b'pw', b'salt', 1000).hex())
result = out

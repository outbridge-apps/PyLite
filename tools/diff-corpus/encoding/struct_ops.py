import struct


def err(f):
    try:
        f()
        return 'ok'
    except Exception as e:
        return type(e).__name__


out = []
out.append(struct.pack('<IhB', 1, -2, 255))
out.append(struct.pack('>IhB', 1, -2, 255))
out.append(struct.unpack('<IhB', struct.pack('<IhB', 1, -2, 255)))
out.append(struct.pack('<d', 1.5))
out.append(struct.unpack('<f', struct.pack('<f', 0.5))[0])
out.append(struct.pack('4s3x?', b'ab', True))
out.append(struct.calcsize('<IhB'))
out.append(struct.calcsize('IhB'))
out.append(struct.unpack_from('<H', b'\x00\x01\x02', 1))
out.append(list(struct.iter_unpack('<H', b'\x01\x00\x02\x00')))
out.append(struct.pack('<q', -1))
out.append(err(lambda: struct.pack('<B', 256)))
out.append(err(lambda: struct.unpack('<I', b'\x00')))
out.append(err(lambda: struct.pack('<z', 1)))
result = out

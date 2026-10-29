# Adapted from CPython's test suite (Lib/test/test_memoryio.py: MemoryTestMixin and PyBytesIOTest,
# the BytesIO half). Python Software Foundation, PSF License Version 2 -- see README.
# Left out, as the dialect has none of them: bytearray, memoryview, readinto, getbuffer, pickling and
# subclassing.
import io

buftype = bytes
EOF = b''

def raises(exc, f, *args):
    try:
        f(*args)
    except exc:
        return True
    return False

# ---- write_ops / test_write ----
def write_ops(f):
    assert f.write(b"blah.") == 5
    assert f.seek(0) == 0
    assert f.write(b"Hello.") == 6
    assert f.tell() == 6
    assert f.seek(5) == 5
    assert f.tell() == 5
    assert f.write(b" world\n\n\n") == 9
    assert f.seek(0) == 0
    assert f.write(b"h") == 1
    assert f.truncate(12) == 12
    assert f.tell() == 1

buf = b"hello world\n"
memio = io.BytesIO(buf)
write_ops(memio)
assert memio.getvalue() == buf
memio = io.BytesIO()
write_ops(memio)
assert memio.getvalue() == buf
assert raises(TypeError, memio.write, None)
assert raises(TypeError, memio.write, "unicode")
memio.close()
assert raises(ValueError, memio.write, b"")

# ---- test_writelines ----
buf = b"1234567890"
memio = io.BytesIO()
assert memio.writelines([buf] * 100) is None
assert memio.getvalue() == buf * 100
memio.writelines([])
assert memio.getvalue() == buf * 100
memio = io.BytesIO()
assert raises(TypeError, memio.writelines, [buf] + [1])
assert memio.getvalue() == buf
assert raises(TypeError, memio.writelines, None)
memio.close()
assert raises(ValueError, memio.writelines, [])

# ---- test_truncate ----
buf = b"1234567890"
memio = io.BytesIO(buf)
assert raises(ValueError, memio.truncate, -1)
memio.seek(6)
assert memio.truncate(8) == 8
assert memio.getvalue() == buf[:8]
assert memio.truncate() == 6
assert memio.getvalue() == buf[:6]
assert memio.truncate(4) == 4
assert memio.getvalue() == buf[:4]
assert memio.tell() == 6
memio.seek(0, 2)
memio.write(buf)
assert memio.getvalue() == buf[:4] + buf
pos = memio.tell()
assert memio.truncate(None) == pos
assert memio.tell() == pos
assert raises(TypeError, memio.truncate, '0')
memio.close()
assert raises(ValueError, memio.truncate, 0)

# ---- test_init ----
buf = b"1234567890"
memio = io.BytesIO(buf)
assert memio.getvalue() == buf
memio = io.BytesIO(None)
assert memio.getvalue() == EOF
assert raises(TypeError, io.BytesIO, [])
assert raises(TypeError, io.BytesIO, "unicode")

# ---- test_read ----
buf = b"1234567890"
memio = io.BytesIO(buf)
assert memio.read(0) == EOF
assert memio.read(1) == buf[:1]
assert memio.read(4) == buf[1:5]
assert memio.read(900) == buf[5:]
assert memio.read() == EOF
memio.seek(0)
assert memio.read() == buf
assert memio.read() == EOF
assert memio.tell() == 10
memio.seek(0)
assert memio.read(-1) == buf
memio.seek(0)
assert type(memio.read()) == type(buf)
memio.seek(100)
assert type(memio.read()) == type(buf)
memio.seek(0)
assert memio.read(None) == buf
assert raises(TypeError, memio.read, '')
memio.seek(len(buf) + 1)
assert memio.read(1) == EOF
memio.seek(len(buf) + 1)
assert memio.read() == EOF
memio.close()
assert raises(ValueError, memio.read)

# ---- test_read1 ----
buf = b"1234567890"
memio = io.BytesIO(buf)
assert raises(TypeError, memio.read1, '')
assert memio.read1() == buf
memio.seek(0)
assert memio.read1(None) == buf
memio.seek(0)
assert memio.read1(3) == buf[:3]

# ---- test_readline ----
buf = b"1234567890\n"
memio = io.BytesIO(buf * 2)
assert memio.readline(0) == EOF
assert memio.readline() == buf
assert memio.readline() == buf
assert memio.readline() == EOF
memio.seek(0)
assert memio.readline(5) == buf[:5]
assert memio.readline(5) == buf[5:10]
assert memio.readline(5) == buf[10:15]
memio.seek(0)
assert memio.readline(-1) == buf
memio.seek(0)
assert memio.readline(0) == EOF
memio.seek(0)
assert memio.readline(None) == buf

buf = b"1234567890\n"
memio = io.BytesIO((buf * 3)[:-1])
assert memio.readline() == buf
assert memio.readline() == buf
assert memio.readline() == buf[:-1]
assert memio.readline() == EOF
memio.seek(0)
assert type(memio.readline()) == type(buf)
assert memio.readline() == buf
assert raises(TypeError, memio.readline, '')
memio.close()
assert raises(ValueError, memio.readline)

# ---- test_readlines ----
buf = b"1234567890\n"
memio = io.BytesIO(buf * 10)
assert memio.readlines() == [buf] * 10
memio.seek(5)
assert memio.readlines() == [buf[5:]] + [buf] * 9
memio.seek(0)
assert memio.readlines(15) == [buf] * 2
memio.seek(0)
assert memio.readlines(-1) == [buf] * 10
memio.seek(0)
assert memio.readlines(0) == [buf] * 10
memio.seek(0)
assert type(memio.readlines()[0]) == type(buf)
memio.seek(0)
assert memio.readlines(None) == [buf] * 10
assert raises(TypeError, memio.readlines, '')
memio.close()
assert raises(ValueError, memio.readlines)

# ---- test_iterator ----
buf = b"1234567890\n"
memio = io.BytesIO(buf * 10)
assert iter(memio) is memio
i = 0
for line in memio:
    assert line == buf
    i += 1
assert i == 10
memio.seek(0)
i = 0
for line in memio:
    assert line == buf
    i += 1
assert i == 10
memio.seek(0)
assert next(memio) == buf
assert next(memio, None) == buf
memio = io.BytesIO(buf * 2)
memio.close()
assert raises(ValueError, next, memio)

# ---- test_getvalue ----
buf = b"1234567890"
memio = io.BytesIO(buf)
assert memio.getvalue() == buf
memio.read()
assert memio.getvalue() == buf
assert type(memio.getvalue()) == type(buf)
memio = io.BytesIO(buf * 1000)
assert memio.getvalue()[-3:] == b"890"
memio = io.BytesIO(buf)
memio.close()
assert raises(ValueError, memio.getvalue)

# ---- test_seek ----
buf = b"1234567890"
memio = io.BytesIO(buf)
memio.read(5)
assert raises(ValueError, memio.seek, -1)
assert raises(ValueError, memio.seek, 1, -1)
assert raises(ValueError, memio.seek, 1, 3)
assert memio.seek(0) == 0
assert memio.seek(0, 0) == 0
assert memio.read() == buf
assert memio.seek(3) == 3
assert memio.seek(0, 1) == 3
assert memio.read() == buf[3:]
assert memio.seek(len(buf)) == len(buf)
assert memio.read() == EOF
memio.seek(len(buf) + 1)
assert memio.read() == EOF
assert memio.seek(0, 2) == len(buf)
assert memio.read() == EOF
assert raises(TypeError, memio.seek, 1.5)
memio.close()
assert raises(ValueError, memio.seek, 0)

# ---- test_overseek ----
buf = b"1234567890"
memio = io.BytesIO(buf)
assert memio.seek(len(buf) + 1) == 11
assert memio.read() == EOF
assert memio.tell() == 11
assert memio.getvalue() == buf
memio.write(EOF)
assert memio.getvalue() == buf
memio.write(buf)
assert memio.getvalue() == buf + b'\0' + buf

# ---- test_tell ----
buf = b"1234567890"
memio = io.BytesIO(buf)
assert memio.tell() == 0
memio.seek(5)
assert memio.tell() == 5
memio.seek(10000)
assert memio.tell() == 10000
memio.close()
assert raises(ValueError, memio.tell)

# ---- test_flush ----
buf = b"1234567890"
memio = io.BytesIO(buf)
assert memio.flush() is None

# ---- test_flags ----
memio = io.BytesIO()
assert memio.writable() == True
assert memio.readable() == True
assert memio.seekable() == True
assert memio.isatty() == False
assert memio.closed == False
memio.close()
assert raises(ValueError, memio.writable)
assert raises(ValueError, memio.readable)
assert raises(ValueError, memio.seekable)
assert raises(ValueError, memio.isatty)
assert memio.closed == True

# ---- test_relative_seek ----
buf = b"1234567890"
memio = io.BytesIO(buf)
assert memio.seek(-1, 1) == 0
assert memio.seek(3, 1) == 3
assert memio.seek(-4, 1) == 0
assert memio.seek(-1, 2) == 9
assert memio.seek(1, 1) == 10
assert memio.seek(1, 2) == 11
memio.seek(-3, 2)
assert memio.read() == buf[-3:]
memio.seek(0)
memio.seek(1, 1)
assert memio.read() == buf[1:]

# ---- test_issue5449 ----
buf = b"1234567890"
assert io.BytesIO(initial_bytes=buf).getvalue() == buf
assert raises(TypeError, lambda: io.BytesIO(buf, foo=None))

# ---- test_close: idempotent ----
memio = io.BytesIO(b"x")
memio.close()
memio.close()
assert memio.closed

# ---- isinstance ----
assert isinstance(io.BytesIO(), io.BytesIO)
assert not isinstance(io.BytesIO(), io.StringIO)
assert isinstance(io.StringIO(), io.StringIO)

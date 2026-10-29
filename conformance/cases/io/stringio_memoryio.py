# Adapted from CPython's test suite (Lib/test/test_memoryio.py: MemoryTestMixin as run by PyStringIOTest:
# test_write, test_writelines, test_truncate, test_readlines, test_iterator, test_overseek,
# test_flags). Python Software Foundation, PSF License Version 2 -- see README.
# The basic StringIO surface is in csv/csv_io_full.py; this file covers what 1.1.0 added.
import io

def raises(exc, f, *args):
    try:
        f(*args)
    except exc:
        return True
    return False

def write_ops(f):
    assert f.write("blah.") == 5
    assert f.seek(0) == 0
    assert f.write("Hello.") == 6
    assert f.tell() == 6
    assert f.seek(5) == 5
    assert f.tell() == 5
    assert f.write(" world\n\n\n") == 9
    assert f.seek(0) == 0
    assert f.write("h") == 1
    assert f.truncate(12) == 12
    assert f.tell() == 1

buf = "hello world\n"
memio = io.StringIO(buf)
write_ops(memio)
assert memio.getvalue() == buf
memio = io.StringIO()
write_ops(memio)
assert memio.getvalue() == buf
assert raises(TypeError, memio.write, None)
memio.close()
assert raises(ValueError, memio.write, "")

# ---- writelines ----
buf = "1234567890"
memio = io.StringIO()
assert memio.writelines([buf] * 100) is None
assert memio.getvalue() == buf * 100
memio.writelines([])
assert memio.getvalue() == buf * 100
memio = io.StringIO()
assert raises(TypeError, memio.writelines, [buf] + [1])
assert memio.getvalue() == buf
assert raises(TypeError, memio.writelines, None)
memio.close()
assert raises(ValueError, memio.writelines, [])

# ---- truncate ----
memio = io.StringIO(buf)
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

# ---- read and readline argument types ----
memio = io.StringIO(buf)
assert raises(TypeError, memio.read, '')
assert raises(TypeError, memio.readline, '')
assert memio.read(None) == buf

# ---- readlines with a hint ----
line = "1234567890\n"
memio = io.StringIO(line * 10)
assert memio.readlines(15) == [line] * 2
memio.seek(0)
assert memio.readlines(0) == [line] * 10
memio.seek(0)
assert memio.readlines(None) == [line] * 10
assert raises(TypeError, memio.readlines, '')

# ---- iterator ----
memio = io.StringIO(line * 3)
assert iter(memio) is memio
assert next(memio) == line
assert [l for l in memio] == [line] * 2
assert next(memio, 'end') == 'end'
memio.close()
assert raises(ValueError, next, memio)
memio = io.StringIO(line)
memio.close()
try:
    for l in memio:
        pass
    assert False
except ValueError:
    pass

# ---- overseek pads with NUL ----
memio = io.StringIO(buf)
assert memio.seek(len(buf) + 1) == 11
assert memio.read() == ''
assert memio.tell() == 11
assert memio.getvalue() == buf
memio.write('')
assert memio.getvalue() == buf
memio.write(buf)
assert memio.getvalue() == buf + '\0' + buf

# ---- flags ----
memio = io.StringIO()
assert memio.writable() == True
assert memio.readable() == True
assert memio.seekable() == True
assert memio.isatty() == False
assert memio.flush() is None
memio.close()
assert raises(ValueError, memio.writable)
assert raises(ValueError, memio.readable)
assert raises(ValueError, memio.seekable)
assert raises(ValueError, memio.isatty)
assert raises(ValueError, memio.flush)

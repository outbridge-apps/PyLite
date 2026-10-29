# Adapted from CPython's test suite (Lib/test/test_zipfile/test_core.py: AbstractTestsWithSourceFile as
# run by StoredTestsWithSourceFile and DeflateTestsWithSourceFile). Python Software Foundation, PSF
# License Version 2 -- see conformance/README.md.
# Temporary files become io.BytesIO; zipfp.write(TESTFN, name) from disk becomes writestr(name, data).
# Left out: test_truncated_zipfile (it truncates the file AFTER open(); a member is read whole when it is
# opened here - dialect), test_writing_errors (OSError injection), the path-like variants.
import io
import random
import time
import zipfile

random.seed(42)
FIXEDTEST_SIZE = 1000
line_gen = [("Zipfile test line %d. random float: %f\n" % (i, random.random())).encode('ascii')
            for i in range(FIXEDTEST_SIZE)]
data = b''.join(line_gen)
TESTFN = '@test_member'

def raises(exc, f):
    try:
        f()
    except exc:
        return True
    return False

def make_test_archive(f, compression, compresslevel=None):
    with zipfile.ZipFile(f, "w", compression=compression, compresslevel=compresslevel) as zipfp:
        zipfp.writestr("another.name", data)
        zipfp.writestr(TESTFN, data)
        zipfp.writestr("strfile", data)
        with zipfp.open('written-open-w', mode='w') as wf:
            for line in line_gen:
                wf.write(line)

def zip_test(compression, compresslevel=None):
    f = io.BytesIO()
    make_test_archive(f, compression, compresslevel)
    with zipfile.ZipFile(f, "r", compression) as zipfp:
        assert zipfp.read(TESTFN) == data
        assert zipfp.read("another.name") == data
        assert zipfp.read("strfile") == data

        fp = io.StringIO()
        zipfp.printdir(file=fp)
        directory = fp.getvalue()
        lines = directory.splitlines()
        assert len(lines) == 5   # number of files + header
        assert 'File Name' in lines[0]
        assert 'Modified' in lines[0]
        assert 'Size' in lines[0]
        fn, date, time_, size = lines[1].split()
        assert fn == 'another.name'
        assert time.strptime(date, '%Y-%m-%d')
        assert time.strptime(time_, '%H:%M:%S')
        assert size == str(len(data))

        names = zipfp.namelist()
        assert len(names) == 4
        for n in (TESTFN, "another.name", "strfile", "written-open-w"):
            assert n in names

        infos = zipfp.infolist()
        names = [i.filename for i in infos]
        assert len(names) == 4
        for n in (TESTFN, "another.name", "strfile", "written-open-w"):
            assert n in names
        for i in infos:
            assert i.file_size == len(data)

        for nm in (TESTFN, "another.name", "strfile", "written-open-w"):
            info = zipfp.getinfo(nm)
            assert info.filename == nm
            assert info.file_size == len(data)

        assert zipfp.testzip() is None

def zip_open_test(compression):
    f = io.BytesIO()
    make_test_archive(f, compression)
    with zipfile.ZipFile(f, "r", compression) as zipfp:
        zipdata1 = []
        with zipfp.open(TESTFN) as zipopen1:
            while True:
                read_data = zipopen1.read(256)
                if not read_data:
                    break
                zipdata1.append(read_data)
        zipdata2 = []
        with zipfp.open("another.name") as zipopen2:
            while True:
                read_data = zipopen2.read(256)
                if not read_data:
                    break
                zipdata2.append(read_data)
        assert b''.join(zipdata1) == data
        assert b''.join(zipdata2) == data

def zip_random_open_test(compression):
    f = io.BytesIO()
    make_test_archive(f, compression)
    with zipfile.ZipFile(f, "r", compression) as zipfp:
        zipdata1 = []
        with zipfp.open(TESTFN) as zipopen1:
            while True:
                read_data = zipopen1.read(random.randint(1, 1024))
                if not read_data:
                    break
                zipdata1.append(read_data)
        assert b''.join(zipdata1) == data

def zip_read1_test(compression):
    f = io.BytesIO()
    make_test_archive(f, compression)
    with zipfile.ZipFile(f, "r") as zipfp, zipfp.open(TESTFN) as zipopen:
        zipdata = []
        while True:
            read_data = zipopen.read1(-1)
            if not read_data:
                break
            zipdata.append(read_data)
    assert b''.join(zipdata) == data

def zip_read1_10_test(compression):
    f = io.BytesIO()
    make_test_archive(f, compression)
    with zipfile.ZipFile(f, "r") as zipfp, zipfp.open(TESTFN) as zipopen:
        zipdata = []
        while True:
            read_data = zipopen.read1(10)
            assert len(read_data) <= 10
            if not read_data:
                break
            zipdata.append(read_data)
    assert b''.join(zipdata) == data

def zip_readline_read_test(compression):
    f = io.BytesIO()
    make_test_archive(f, compression)
    with zipfile.ZipFile(f, "r") as zipfp, zipfp.open(TESTFN) as zipopen:
        got = b''
        while True:
            read = zipopen.readline()
            if not read:
                break
            got += read
            read = zipopen.read(100)
            if not read:
                break
            got += read
    assert got == data

def zip_readline_test(compression):
    f = io.BytesIO()
    make_test_archive(f, compression)
    with zipfile.ZipFile(f, "r") as zipfp:
        with zipfp.open(TESTFN) as zipopen:
            for line in line_gen:
                linedata = zipopen.readline()
                assert linedata == line

def zip_readlines_test(compression):
    f = io.BytesIO()
    make_test_archive(f, compression)
    with zipfile.ZipFile(f, "r") as zipfp:
        with zipfp.open(TESTFN) as zipopen:
            ziplines = zipopen.readlines()
        for line, zipline in zip(line_gen, ziplines):
            assert zipline == line

def zip_iterlines_test(compression):
    f = io.BytesIO()
    make_test_archive(f, compression)
    with zipfile.ZipFile(f, "r") as zipfp:
        with zipfp.open(TESTFN) as zipopen:
            for line, zipline in zip(line_gen, zipopen):
                assert zipline == line

for compression in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED):
    zip_test(compression)
    zip_test(compression, compresslevel=9)          # test_compresslevel_basic
    zip_open_test(compression)
    zip_random_open_test(compression)
    zip_read1_test(compression)
    zip_read1_10_test(compression)
    zip_readline_read_test(compression)
    zip_readline_test(compression)
    zip_readlines_test(compression)
    zip_iterlines_test(compression)

    # test_low_compression: compressed data larger than the original
    f = io.BytesIO()
    with zipfile.ZipFile(f, "w", compression) as zipfp:
        zipfp.writestr("strfile", '12')
    with zipfile.ZipFile(f, "r", compression) as zipfp:
        with zipfp.open("strfile") as openobj:
            assert openobj.read(1) == b'1'
            assert openobj.read(1) == b'2'

    # test_writestr_compression
    zipfp = zipfile.ZipFile(io.BytesIO(), "w")
    zipfp.writestr("b.txt", "hello world", compress_type=compression)
    info = zipfp.getinfo('b.txt')
    assert info.compress_type == compression

    # test_writestr_compresslevel
    zipfp = zipfile.ZipFile(io.BytesIO(), "w", compresslevel=1)
    zipfp.writestr("a.txt", "hello world", compress_type=compression)
    zipfp.writestr("b.txt", "hello world", compress_type=compression, compresslevel=2)
    a_info = zipfp.getinfo('a.txt')
    assert a_info.compress_type == compression
    assert a_info.compress_level == 1
    b_info = zipfp.getinfo('b.txt')
    assert b_info.compress_type == compression
    assert b_info.compress_level == 2

    # test_read_return_size
    for test_size in (1, 4095, 4096, 4097, 16384):
        file_size = test_size + 1
        junk = random.randbytes(file_size)
        with zipfile.ZipFile(io.BytesIO(), "w", compression) as zipf:
            zipf.writestr('foo', junk)
            with zipf.open('foo', 'r') as fp:
                buf = fp.read(test_size)
                assert len(buf) == test_size

    # test_repr (the BytesIO itself has no address-free repr to compare)
    fname = 'file.name'
    f = io.BytesIO()
    with zipfile.ZipFile(f, 'w', compression) as zipfp:
        zipfp.writestr(fname, data)
        r = repr(zipfp)
        assert "mode='w'" in r
    with zipfile.ZipFile(f, 'r') as zipfp:
        r = repr(zipfp)
        assert "mode='r'" in r
        r = repr(zipfp.getinfo(fname))
        assert 'filename=%r' % fname in r
        assert 'filemode=' in r
        assert 'file_size=' in r
        if compression != zipfile.ZIP_STORED:
            assert 'compress_type=' in r
            assert 'compress_size=' in r
        with zipfp.open(fname) as zipopen:
            r = repr(zipopen)
            assert 'name=%r' % fname in r
            if compression != zipfile.ZIP_STORED:
                assert 'compress_type=' in r
        assert '[closed]' in repr(zipopen)
    assert '[closed]' in repr(zipfp)

    # test_per_file_compresslevel
    with zipfile.ZipFile(io.BytesIO(), "w", compresslevel=1) as zipfp:
        zipfp.writestr('compress_1', data)
        zipfp.writestr('compress_9', data, compresslevel=9)
        one_info = zipfp.getinfo('compress_1')
        nine_info = zipfp.getinfo('compress_9')
        assert one_info.compress_level == 1
        assert nine_info.compress_level == 9

# StoredTestsWithSourceFile.test_add_file_before_1980 / test_add_file_after_2107, without the disk
assert raises(ValueError, lambda: zipfile.ZipInfo('seventies', (1979, 1, 1, 0, 0, 0)))
import struct
with zipfile.ZipFile(io.BytesIO(), "w") as zipfp:
    assert raises(struct.error, lambda: zipfp.writestr(zipfile.ZipInfo('far', (2108, 12, 30, 0, 0, 0)), b''))

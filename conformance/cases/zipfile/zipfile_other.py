# Adapted from CPython's test suite (Lib/test/test_zipfile/test_core.py: OtherTests,
# AbstractBadCrcTests, TestsWithMultipleOpens' simple cases). Python Software Foundation, PSF License
# Version 2 -- see conformance/README.md.
# Temporary files become io.BytesIO; a mode='w' archive gets a fresh one, as a new file would be.
# Left out: anything that needs the disk (write(path), extract, path-like, exclusive create), warnings
# (a duplicate name or a too-long comment is accepted silently here - dialect: no warnings module), the
# bz2/lzma classes, and ZipFile.write() under an open write handle (it is a TypeError here: no disk).
import io
import struct
import zipfile

def raises(exc, f):
    try:
        f()
    except exc:
        return True
    return False

# ---- test_open_via_zip_info ----
f = io.BytesIO()
with zipfile.ZipFile(f, "w", zipfile.ZIP_STORED) as zipfp:
    zipfp.writestr("name", "foo")
    zipfp.writestr("name", "bar")   # dialect: CPython also warns (UserWarning)
    assert zipfp.namelist() == ["name"] * 2
with zipfile.ZipFile(f, "r") as zipfp:
    infos = zipfp.infolist()
    got = b""
    for info in infos:
        with zipfp.open(info) as zipopen:
            got += zipopen.read()
    assert got in {b"foobar", b"barfoo"}
    got = b""
    for info in infos:
        got += zipfp.read(info)
    assert got in {b"foobar", b"barfoo"}

# ---- test_writestr_extended_local_header_issue1202 ----
f = io.BytesIO()
with zipfile.ZipFile(f, 'w') as orig_zip:
    for d in 'abcdefghijklmnop':
        zinfo = zipfile.ZipInfo(d)
        zinfo.flag_bits |= 0x08   # _MASK_USE_DATA_DESCRIPTOR
        orig_zip.writestr(zinfo, d)
with zipfile.ZipFile(f) as z:
    assert z.read('k') == b'k'

# ---- test_close / test_close_on_exception ----
SMALL_TEST_DATA = [('_ziptest1', '1q2w3e4r5t'),
                   ('ziptest2dir/_ziptest2', 'qawsedrftg'),
                   ('ziptest2dir/ziptest3dir/_ziptest3', 'azsxdcfvgb'),
                   ('ziptest2dir/ziptest3dir/ziptest4dir/_ziptest3', '6y7u8i9o0p')]
f = io.BytesIO()
with zipfile.ZipFile(f, "w") as zipfp:
    for fpath, fdata in SMALL_TEST_DATA:
        zipfp.writestr(fpath, fdata)
        assert zipfp.fp is not None
assert zipfp.fp is None
with zipfile.ZipFile(f, "r") as zipfp:
    assert zipfp.fp is not None
assert zipfp.fp is None
try:
    with zipfile.ZipFile(f, "r") as zipfp2:
        raise zipfile.BadZipFile()
except zipfile.BadZipFile:
    assert zipfp2.fp is None

# ---- test_unsupported_version (the archive is built here and patched: extract_version 120) ----
def one_member(name='x', payload=b'x', extra=b''):
    b = io.BytesIO()
    with zipfile.ZipFile(b, 'w') as z:
        zi = zipfile.ZipInfo(name)
        zi.extra = extra
        z.writestr(zi, payload)
    return b.getvalue()

def patch(blob, at, value):
    return blob[:at] + bytes([value]) + blob[at + 1:]

plain = one_member()
cd = plain.rfind(b'PK\x01\x02')
assert raises(NotImplementedError, lambda: zipfile.ZipFile(io.BytesIO(patch(plain, cd + 6, 120)), 'r'))

# ---- test_write_unicode_filenames / test_read_after_write_unicode_filenames ----
f = io.BytesIO()
with zipfile.ZipFile(f, "w") as zf:
    zf.writestr("foo.txt", "Test for unicode filename")
    zf.writestr("\xf6.txt", "Test for unicode filename")
    assert isinstance(zf.infolist()[0].filename, str)
with zipfile.ZipFile(f, "r") as zf:
    assert zf.filelist[0].filename == "foo.txt"
    assert zf.filelist[1].filename == "\xf6.txt"
with zipfile.ZipFile(io.BytesIO(), 'w') as zipfp:
    zipfp.writestr('приклад', b'sample')
    assert zipfp.read('приклад') == b'sample'

# ---- test_create_zipinfo_before_1980 / test_create_empty_zipinfo_repr / default attributes ----
assert raises(ValueError, lambda: zipfile.ZipInfo('seventies', (1979, 1, 1, 0, 0, 0)))
zi = zipfile.ZipInfo(filename="empty")
assert repr(zi) == "<ZipInfo filename='empty' file_size=0>"
zi = zipfile.ZipInfo()
assert zi.orig_filename == "NoName"
assert zi.filename == "NoName"
assert zi.date_time == (1980, 1, 1, 0, 0, 0)
assert zi.compress_type == zipfile.ZIP_STORED
assert zi.comment == b""
assert zi.extra == b""
assert zi.create_system in (0, 3)
assert zi.create_version == zipfile.DEFAULT_VERSION
assert zi.extract_version == zipfile.DEFAULT_VERSION
assert zi.reserved == 0
assert zi.flag_bits == 0
assert zi.volume == 0
assert zi.internal_attr == 0
assert zi.external_attr == 0
assert zi.file_size == 0
assert zi.compress_size == 0

# ---- test_zipfile_with_short_extra_field (an extra field shorter than its 4-byte header is skipped) ----
zipdata = one_member('abc', b'A', b'\x00\x00\x00')
with zipfile.ZipFile(io.BytesIO(zipdata), 'r') as zipf:
    assert zipf.testzip() is None

# ---- test_open_conflicting_handles ----
msg1 = b"It's fun to charter an accountant!"
msg2 = b"And sail the wide accountant sea"
msg3 = b"To find, explore the funds offshore"
f = io.BytesIO()
with zipfile.ZipFile(f, 'w', zipfile.ZIP_STORED) as zipf:
    with zipf.open('foo', mode='w') as w2:
        w2.write(msg1)
    with zipf.open('bar', mode='w') as w1:
        assert raises(ValueError, lambda: zipf.open('handle', mode='w'))
        assert raises(ValueError, lambda: zipf.open('foo', mode='r'))
        assert raises(ValueError, lambda: zipf.writestr('str', 'abcde'))
        assert raises(ValueError, lambda: zipf.close())
        w1.write(msg2)
    with zipf.open('baz', mode='w') as w2:
        w2.write(msg3)
with zipfile.ZipFile(f, 'r') as zipf:
    assert zipf.read('foo') == msg1
    assert zipf.read('bar') == msg2
    assert zipf.read('baz') == msg3
    assert zipf.namelist() == ['foo', 'bar', 'baz']

# ---- test_seek_tell ----
txt = b"Where's Bruce?"
bloc = txt.find(b"Bruce")
f = io.BytesIO()
with zipfile.ZipFile(f, mode="w") as zipf:
    zipf.writestr("foo.txt", txt)
with zipfile.ZipFile(f, mode="r") as zipf:
    with zipf.open("foo.txt", "r") as fp:
        fp.seek(bloc, 0)
        assert fp.tell() == bloc
        fp.seek(-bloc, 1)
        assert fp.tell() == 0
        fp.seek(bloc, 1)
        assert fp.tell() == bloc
        assert fp.read(5) == txt[bloc:bloc + 5]
        assert fp.tell() == bloc + 5
        fp.seek(0, 2)
        assert fp.tell() == len(txt)
        fp.seek(0, 0)
        assert fp.tell() == 0

# ---- test_is_zip_erroneous_file / test_is_zip_valid_file ----
fp = io.BytesIO()
fp.write(b"this is not a legal zip file\n")
assert not zipfile.is_zipfile(fp)
fp.seek(0, 0)
assert not zipfile.is_zipfile(fp)
fp = io.BytesIO()
with zipfile.ZipFile(fp, mode="w") as zipf:
    zipf.writestr("foo.txt", b"O, for a Muse of Fire!")
zip_contents = fp.getvalue()
fp = io.BytesIO()
fp.write(zip_contents)
assert zipfile.is_zipfile(fp)
fp.seek(0, 0)
assert zipfile.is_zipfile(fp)

# ---- test_damaged_zipfile ----
fp = io.BytesIO()
with zipfile.ZipFile(fp, mode="w") as zipf:
    zipf.writestr("foo.txt", b"O, for a Muse of Fire!")
zipfiledata = fp.getvalue()
for N in range(len(zipfiledata)):
    fp = io.BytesIO(zipfiledata[:N])
    assert raises(zipfile.BadZipFile, lambda: zipfile.ZipFile(fp)), N

# ---- test_empty_file_raises_BadZipFile / test_open_empty_file ----
assert raises(zipfile.BadZipFile, lambda: zipfile.ZipFile(io.BytesIO(b'')))
assert raises(zipfile.BadZipFile, lambda: zipfile.ZipFile(io.BytesIO(b'short file')))
assert raises(zipfile.BadZipFile, lambda: zipfile.ZipFile(io.BytesIO(b''), 'r'))

# ---- test_negative_central_directory_offset_raises_BadZipFile ----
for dirsize in 1, 2 ** 32 - 1:
    buffer = b'PK\x05\x06' + b'\0' * 8 + struct.pack('<L', dirsize) + b'\0' * 6
    assert raises(zipfile.BadZipFile, lambda: zipfile.ZipFile(io.BytesIO(buffer)))

# ---- test_closed_zip_raises_ValueError ----
data = io.BytesIO()
with zipfile.ZipFile(data, mode="w") as zipf:
    zipf.writestr("foo.txt", "O, for a Muse of Fire!")
assert raises(ValueError, lambda: zipf.read("foo.txt"))
assert raises(ValueError, lambda: zipf.open("foo.txt"))
assert raises(ValueError, lambda: zipf.testzip())
assert raises(ValueError, lambda: zipf.writestr("bogus.txt", "bogus"))

# ---- test_bad_constructor_mode / test_bad_open_mode ----
assert raises(ValueError, lambda: zipfile.ZipFile(io.BytesIO(), "q"))
f = io.BytesIO()
with zipfile.ZipFile(f, mode="w") as zipf:
    zipf.writestr("foo.txt", "O, for a Muse of Fire!")
with zipfile.ZipFile(f, mode="r") as zipf:
    zipf.read("foo.txt")
    assert raises(ValueError, lambda: zipf.open("foo.txt", "q"))
    assert raises(ValueError, lambda: zipf.open("foo.txt", "U"))
    assert raises(ValueError, lambda: zipf.open("foo.txt", "rU"))

# ---- test_read0 ----
with zipfile.ZipFile(io.BytesIO(), mode="w") as zipf:
    zipf.writestr("foo.txt", "O, for a Muse of Fire!")
    with zipf.open("foo.txt") as f:
        for i in range(1000):
            assert f.read(0) == b''
        assert f.read() == b"O, for a Muse of Fire!"

# ---- test_open_non_existent_item / test_bad_compression_mode / test_unsupported_compression ----
with zipfile.ZipFile(io.BytesIO(), mode="w") as zipf:
    assert raises(KeyError, lambda: zipf.open("foo.txt", "r"))
assert raises(NotImplementedError, lambda: zipfile.ZipFile(io.BytesIO(), "w", -1))
# test_unsupported_compression: declared as "shrink" (method 1) in both headers
shrunk = patch(patch(plain, 8, 1), cd + 10, 1)
with zipfile.ZipFile(io.BytesIO(shrunk), 'r') as zipf:
    assert raises(NotImplementedError, lambda: zipf.open('x'))

# ---- test_null_byte_in_filename ----
with zipfile.ZipFile(io.BytesIO(), mode="w") as zipf:
    zipf.writestr("foo.txt\x00qqq", b"O, for a Muse of Fire!")
    assert zipf.namelist() == ['foo.txt']

# ---- test_comments ----
f = io.BytesIO()
with zipfile.ZipFile(f, mode="w") as zipf:
    assert zipf.comment == b''
    zipf.writestr("foo.txt", "O, for a Muse of Fire!")
with zipfile.ZipFile(f, mode="r") as zipfr:
    assert zipfr.comment == b''
comment = b'Bravely taking to his feet, he beat a very brave retreat.'
f = io.BytesIO()
with zipfile.ZipFile(f, mode="w") as zipf:
    zipf.comment = comment
    zipf.writestr("foo.txt", "O, for a Muse of Fire!")
with zipfile.ZipFile(f, mode="r") as zipfr:
    assert zipfr.comment == comment
comment2 = ''.join(['%d' % (i ** 3 % 10) for i in range((1 << 16) - 1)])
comment2 = comment2.encode("ascii")
f = io.BytesIO()
with zipfile.ZipFile(f, mode="w") as zipf:
    zipf.comment = comment2
    zipf.writestr("foo.txt", "O, for a Muse of Fire!")
with zipfile.ZipFile(f, mode="r") as zipfr:
    assert zipfr.comment == comment2
f = io.BytesIO()
with zipfile.ZipFile(f, mode="w") as zipf:
    zipf.comment = comment2 + b'oops'   # dialect: CPython also warns (UserWarning)
    zipf.writestr("foo.txt", "O, for a Muse of Fire!")
with zipfile.ZipFile(f, mode="r") as zipfr:
    assert zipfr.comment == comment2
f = io.BytesIO()
with zipfile.ZipFile(f, mode="w") as zipf:
    zipf.comment = b"original comment"
    zipf.writestr("foo.txt", "O, for a Muse of Fire!")
with zipfile.ZipFile(f, mode="a") as zipf:
    zipf.comment = b"an updated comment"
with zipfile.ZipFile(f, mode="r") as zipf:
    assert zipf.comment == b"an updated comment"
f = io.BytesIO()
with zipfile.ZipFile(f, mode="w") as zipf:
    zipf.comment = b"original comment that's longer"
    zipf.writestr("foo.txt", "O, for a Muse of Fire!")
original_zip_size = len(f.getvalue())
with zipfile.ZipFile(f, mode="a") as zipf:
    zipf.comment = b"shorter comment"
assert original_zip_size > len(f.getvalue())
with zipfile.ZipFile(f, mode="r") as zipf:
    assert zipf.comment == b"shorter comment"

# ---- test_unicode_comment / change comment in empty and non-empty archives ----
with zipfile.ZipFile(io.BytesIO(), "w", zipfile.ZIP_STORED) as zipf:
    zipf.writestr("foo.txt", "O, for a Muse of Fire!")
    def set_str_comment():
        zipf.comment = "this is an error"
    assert raises(TypeError, set_str_comment)
f = io.BytesIO()
with zipfile.ZipFile(f, "a", zipfile.ZIP_STORED) as zipf:
    assert not zipf.filelist
    zipf.comment = b"this is a comment"
with zipfile.ZipFile(f, "r") as zipf:
    assert zipf.comment == b"this is a comment"
f = io.BytesIO()
with zipfile.ZipFile(f, "w", zipfile.ZIP_STORED) as zipf:
    zipf.writestr("foo.txt", "O, for a Muse of Fire!")
with zipfile.ZipFile(f, "a", zipfile.ZIP_STORED) as zipf:
    assert zipf.filelist
    zipf.comment = b"this is a comment"
with zipfile.ZipFile(f, "r") as zipf:
    assert zipf.comment == b"this is a comment"

# ---- test_empty_zipfile ----
f = io.BytesIO()
zipf = zipfile.ZipFile(f, mode="w")
zipf.close()
zipf = zipfile.ZipFile(f, mode="r")
f = io.BytesIO()
zipf = zipfile.ZipFile(f, mode="a")
zipf.close()
zipf = zipfile.ZipFile(f, mode="r")

# ---- AbstractTestZip64InSmallFiles' append/concatenation cases, in memory ----
def make(names):
    b = io.BytesIO()
    with zipfile.ZipFile(b, 'w', zipfile.ZIP_DEFLATED) as z:
        for n in names:
            z.writestr(n, n * 10)
    return b.getvalue()

# test_append_to_zip_file
f = io.BytesIO(make(['strfile']))
with zipfile.ZipFile(f, "a") as zipfp:
    zipfp.writestr("strfile2", "strfile2" * 10)
    assert zipfp.namelist() == ["strfile", "strfile2"]
with zipfile.ZipFile(f, "r") as zipfp:
    assert zipfp.read("strfile2") == b"strfile2" * 10

# test_append_to_non_zip_file: a new archive follows the original bytes
data = b'I am not a ZipFile!' * 10
f = io.BytesIO(data)
f.seek(0, 2)
with zipfile.ZipFile(f, "a") as zipfp:
    zipfp.writestr("strfile", "strfile" * 10)
whole = f.getvalue()
assert whole[:len(data)] == data
with zipfile.ZipFile(io.BytesIO(whole), "r") as zipfp:
    assert zipfp.namelist() == ["strfile"]
    assert zipfp.read("strfile") == b"strfile" * 10

# test_read_concatenated_zip_file
zipfiledata = make(['strfile'])
whole = data + zipfiledata
with zipfile.ZipFile(io.BytesIO(whole)) as zipfp:
    assert zipfp.namelist() == ["strfile"]
    assert zipfp.read("strfile") == b"strfile" * 10

# test_append_to_concatenated_zip_file
f = io.BytesIO(data + zipfiledata)
with zipfile.ZipFile(f, "a") as zipfp:
    zipfp.writestr("str2", "str2" * 10)
    assert zipfp.namelist() == ["strfile", "str2"]
whole = f.getvalue()
assert whole[:len(data)] == data
with zipfile.ZipFile(io.BytesIO(whole)) as zipfp:
    assert zipfp.namelist() == ["strfile", "str2"]
    assert zipfp.read("str2") == b"str2" * 10

# test_ignores_newline_at_end / test_ignores_stuff_appended_past_comments
f = io.BytesIO(make(['strfile']) + b'\r\n\00\00\00')
with zipfile.ZipFile(f, "r") as zipfp:
    assert isinstance(zipfp, zipfile.ZipFile)
f = io.BytesIO()
with zipfile.ZipFile(f, "w") as zipfp:
    zipfp.comment = b"this is a comment"
    zipfp.writestr("strfile", "strfile")
f2 = io.BytesIO(f.getvalue() + b'abcdef\r\n')
with zipfile.ZipFile(f2, "r") as zipfp:
    assert zipfp.comment == b"this is a comment"

# ---- AbstractBadCrcTests: a member whose stored CRC is wrong ----
for compression in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED):
    b = io.BytesIO()
    with zipfile.ZipFile(b, 'w', compression) as z:
        z.writestr('afile', b'O, for a Muse of Fire!' * 20)
    good = b.getvalue()
    with zipfile.ZipFile(io.BytesIO(good)) as z:
        info = z.getinfo('afile')
        crc = info.CRC
    bad = good.replace(struct.pack('<L', crc), struct.pack('<L', crc ^ 1))
    with zipfile.ZipFile(io.BytesIO(bad), mode="r") as zipf:
        assert zipf.testzip() == 'afile'
    with zipfile.ZipFile(io.BytesIO(bad), mode="r") as zipf:
        assert raises(zipfile.BadZipFile, lambda: zipf.read('afile'))
    with zipfile.ZipFile(io.BytesIO(bad), mode="r") as zipf:
        with zipf.open('afile', 'r') as corrupt_file:
            assert raises(zipfile.BadZipFile, lambda: corrupt_file.read())
    with zipfile.ZipFile(io.BytesIO(bad), mode="r") as zipf:
        with zipf.open('afile', 'r') as corrupt_file:
            def drain():
                while corrupt_file.read(2):
                    pass
            assert raises(zipfile.BadZipFile, drain)

# ---- the exception classes ----
assert issubclass(zipfile.BadZipFile, Exception)
assert zipfile.BadZipfile is zipfile.BadZipFile and zipfile.error is zipfile.BadZipFile
assert issubclass(zipfile.LargeZipFile, Exception)
try:
    zipfile.ZipFile(io.BytesIO(b'nope'))
except zipfile.BadZipFile as e:
    assert type(e) is zipfile.BadZipFile
    assert str(e) == "File is not a zip file"

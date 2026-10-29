import io
import zipfile
buf = io.BytesIO()
z = zipfile.ZipFile(buf, 'w', zipfile.ZIP_DEFLATED)
z.writestr(zipfile.ZipInfo('a.txt', (2024, 5, 17, 12, 30, 44)), 'alpha ' * 200)
stored = zipfile.ZipInfo('raw.bin', (2001, 2, 3, 4, 5, 6))
stored.compress_type = zipfile.ZIP_STORED
z.writestr(stored, bytes(range(200)))
z.writestr(zipfile.ZipInfo('dir/', (1999, 12, 31, 23, 59, 58)), b'')
z.comment = b'corpus'
z.close()
data = buf.getvalue()
r = zipfile.ZipFile(io.BytesIO(data))
infos = [(i.filename, i.file_size, i.compress_type, i.CRC, i.date_time) for i in r.infolist()]
contents = [len(r.read(n)) for n in r.namelist()]
first = r.read('a.txt')[:12]
comment = r.comment
ok = r.testzip()
r.close()
app = io.BytesIO(data)
a = zipfile.ZipFile(app, 'a')
a.writestr(zipfile.ZipInfo('later.txt', (2020, 1, 1, 0, 0, 0)), b'appended')
a.close()
again = zipfile.ZipFile(io.BytesIO(app.getvalue()))
after = (again.namelist(), again.read('later.txt'))
try:
    zipfile.ZipFile(io.BytesIO(b'not a zip'))
    err = None
except zipfile.BadZipFile as e:
    err = str(e)
try:
    again.getinfo('missing')
except KeyError as e:
    missing = e.args[0]   # str(KeyError) differs on IronPython
result = [infos, contents, first, comment, ok, after, err, missing,
          zipfile.is_zipfile(io.BytesIO(data)), zipfile.is_zipfile(io.BytesIO(b'xyz'))]

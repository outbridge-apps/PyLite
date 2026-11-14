# repeat: 2
# export: csv rows gzipped and zipped, base64 for transport, read back
import csv, io, gzip, zipfile, base64, hashlib, time
rows = [['id', 'name', 'amount', 'date']] + [[str(i), 'Name %d' % i, '%d.%02d' % (i % 999, i % 100), '2026-11-%02d' % (i % 28 + 1)] for i in range(30000)]
t0 = time.perf_counter()
buf = io.StringIO()
csv.writer(buf).writerows(rows)
data = buf.getvalue().encode('utf-8')
gz = gzip.compress(data)
zb = io.BytesIO()
with zipfile.ZipFile(zb, 'w', zipfile.ZIP_DEFLATED) as z:
    z.writestr('export.csv', data)
    z.writestr('export.csv.gz', gz)
b64 = base64.b64encode(zb.getvalue())
t1 = time.perf_counter()
raw = base64.b64decode(b64)
with zipfile.ZipFile(io.BytesIO(raw)) as z:
    names = z.namelist()
    d1 = z.read('export.csv')
    d2 = gzip.decompress(z.read('export.csv.gz'))
back = list(csv.reader(io.StringIO(d1.decode('utf-8'))))
t2 = time.perf_counter()
print(names, d1 == d2 == data, len(back), hashlib.sha256(data).hexdigest()[:16], 'write', round(t1 - t0, 3), 'read', round(t2 - t1, 3))

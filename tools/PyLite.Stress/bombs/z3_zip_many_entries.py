# deadline: 20000
# allocmb: 1024
import io
import zipfile
buf = io.BytesIO()
with zipfile.ZipFile(buf, 'w') as z:
    for i in range(300000):
        z.writestr('f%d' % i, b'')
print(len(zipfile.ZipFile(io.BytesIO(buf.getvalue())).namelist()))

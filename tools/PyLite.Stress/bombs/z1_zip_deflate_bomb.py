# deadline: 20000
# allocmb: 64
import io
import zipfile
buf = io.BytesIO()
with zipfile.ZipFile(buf, 'w', zipfile.ZIP_DEFLATED) as z:
    z.writestr('zeros', bytes(15000000))
zf = zipfile.ZipFile(io.BytesIO(buf.getvalue()))
try:
    for i in range(20):
        zf.read('zeros')
    print('read all')
except MemoryError as e:
    print('clean MemoryError')

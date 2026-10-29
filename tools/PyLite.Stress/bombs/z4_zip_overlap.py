# deadline: 20000
# allocmb: 1024
import io
import struct
import zipfile
buf = io.BytesIO()
with zipfile.ZipFile(buf, 'w') as z:
    z.writestr('a', b'first member data')
    z.writestr('b', b'second member data')
blob = buf.getvalue()
cd = blob.find(b'PK' + bytes([1, 2]))
blob = blob[:cd + 20] + struct.pack('<LL', 100000, 100000) + blob[cd + 28:]
zf = zipfile.ZipFile(io.BytesIO(blob))
print(zf.read('b'))
try:
    zf.read('a')
except zipfile.BadZipFile as e:
    print('clean BadZipFile')

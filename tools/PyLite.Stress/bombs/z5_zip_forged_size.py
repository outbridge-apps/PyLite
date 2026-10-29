# deadline: 20000
# allocmb: 64
import io
import struct
import zipfile
buf = io.BytesIO()
with zipfile.ZipFile(buf, 'w', zipfile.ZIP_DEFLATED) as z:
    z.writestr('zeros', bytes(15000000))
blob = buf.getvalue()
blob = blob[:22] + struct.pack('<L', 2147483647) + blob[26:]
cd = blob.find(b'PK' + bytes([1, 2]))
blob = blob[:cd + 24] + struct.pack('<L', 2147483647) + blob[cd + 28:]
print(len(zipfile.ZipFile(io.BytesIO(blob)).read('zeros')))

# deadline: 3000
# allocmb: 256
import io, zipfile
b = io.BytesIO()
with zipfile.ZipFile(b, 'w') as z:
    z.writestr('a', b'hello')
zf = zipfile.ZipFile(io.BytesIO(b.getvalue()))
zi = zf.getinfo('a')
zi.header_offset = 2**63 - 16
try:
    zf.read(zi)
except zipfile.BadZipFile as x:
    print('clean', type(x).__name__)

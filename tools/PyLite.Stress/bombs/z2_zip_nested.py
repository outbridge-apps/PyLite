# deadline: 20000
# allocmb: 64
import io
import zipfile
def pack(members):
    b = io.BytesIO()
    with zipfile.ZipFile(b, 'w', zipfile.ZIP_DEFLATED) as z:
        for name, data in members:
            z.writestr(name, data)
    return b.getvalue()
inner = pack([('zeros', bytes(15000000))])
outer = pack([('i%d.zip' % k, inner) for k in range(4)])
top = pack([('o%d.zip' % k, outer) for k in range(4)])
def walk(data):
    total = 0
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        for n in z.namelist():
            m = z.read(n)
            total += walk(m) if n.endswith('.zip') else len(m)
    return total
print(len(top), walk(top))

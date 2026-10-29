import io
import json
import gzip
f = io.BytesIO()
n = f.write(b'line one\nline two\n')
f.seek(4)
f.write(b'-ONE')
f.seek(0, 2)
f.writelines([b'tail\n', b'end'])
f.seek(0)
lines = [l for l in f]
f.seek(30)
f.write(b'!')
padded = f.getvalue()
f.truncate(9)
cut = f.getvalue()
doc = json.loads(io.BytesIO(b'{"a": [1, 2, 3], "b": "x"}').read().decode('utf-8'))   # 3.4 json.load wants str
packed = io.BytesIO(gzip.compress(b'payload' * 50))
unpacked = gzip.decompress(packed.read())
result = [n, lines, padded, cut, f.tell(), doc, len(unpacked), io.BytesIO(b'a\nb\nc').readlines(2)]

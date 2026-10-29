# Adapted from CPython's test suite (Lib/test/test_csv.py basics).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import csv
from io import StringIO

# reading
rows = list(csv.reader(StringIO('a,b,c\n1,2,3\n')))
assert rows == [['a', 'b', 'c'], ['1', '2', '3']]

# quoted fields with embedded commas and quotes
rows = list(csv.reader(StringIO('"x,y",plain\n"say ""hi""",2\n')))
assert rows == [['x,y', 'plain'], ['say "hi"', '2']]

# custom delimiter
rows = list(csv.reader(StringIO('a;b\n1;2\n'), delimiter=';'))
assert rows == [['a', 'b'], ['1', '2']]

# writer round trip
buf = StringIO()
w = csv.writer(buf, lineterminator='\n')
w.writerow(['a', 'b'])
w.writerow(['1', 'x,y'])
assert buf.getvalue() == 'a,b\n1,"x,y"\n'

# DictReader uses the header row
dr = csv.DictReader(StringIO('name,age\nann,5\nbob,7\n'))
recs = list(dr)
assert recs[0]['name'] == 'ann'
assert recs[1]['age'] == '7'
assert len(recs) == 2

# DictWriter
buf = StringIO()
dw = csv.DictWriter(buf, ['x', 'y'], lineterminator='\n')   # dialect: fieldnames positionally
dw.writeheader()
dw.writerow({'x': 1, 'y': 2})
assert buf.getvalue() == 'x,y\n1,2\n'

# reader.line_num counts SOURCE lines: a record with an embedded newline advances it by more than one
rows = csv.reader(StringIO('a,b' + chr(10) + '"x' + chr(10) + 'y",2' + chr(10) + 'c,d' + chr(10)))
assert rows.line_num == 0
next(rows)
assert rows.line_num == 1
next(rows)
assert rows.line_num == 3
next(rows)
assert rows.line_num == 4
assert csv.reader(StringIO('a;b'), delimiter=';').dialect.delimiter == ';'
assert csv.reader(StringIO('a,b')).dialect.delimiter == ','

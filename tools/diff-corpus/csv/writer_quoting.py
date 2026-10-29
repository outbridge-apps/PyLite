import csv
import io

out = []
rows = [['id', 'name', 'amount'], [1, 'Smith, J', 2.5], [2, 'say "hi"', None], [3, 'line\nbreak', 0]]
for q in [csv.QUOTE_MINIMAL, csv.QUOTE_ALL, csv.QUOTE_NONNUMERIC]:
    buf = io.StringIO()
    csv.writer(buf, quoting=q).writerows(rows)
    out.append(buf.getvalue())
buf = io.StringIO()
w = csv.writer(buf, delimiter=';', lineterminator='\n')
w.writerow(['a;b', 'c'])
w.writerow([])
out.append(buf.getvalue())
buf = io.StringIO()
csv.writer(buf, quoting=csv.QUOTE_NONE, escapechar='\\').writerow(['a,b', 'c'])
out.append(buf.getvalue())
buf = io.StringIO()
csv.writer(buf, doublequote=False, escapechar='\\').writerow(['x"y'])
out.append(buf.getvalue())
result = out

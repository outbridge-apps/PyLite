import csv
import io


def err(f):
    try:
        f()
        return 'ok'
    except Exception as e:
        return type(e).__name__


out = []
text = 'name,qty\napple,3\npear,5,extra\nfig\n'
rows = list(csv.DictReader(io.StringIO(text), restkey='more', restval='?'))
out.append([sorted(r.items()) for r in rows])
r = csv.DictReader(io.StringIO('a,b\n1,2\n'))
out.append(r.fieldnames)
rows = list(csv.DictReader(io.StringIO('1,2\n3,4\n'), fieldnames=['x', 'y']))
out.append([sorted(r.items()) for r in rows])
buf = io.StringIO()
w = csv.DictWriter(buf, fieldnames=['name', 'qty'], lineterminator='\n')
w.writeheader()
w.writerow({'name': 'apple', 'qty': 3})
w.writerow({'name': 'pear'})
out.append(buf.getvalue())
out.append(err(lambda: csv.DictWriter(io.StringIO(), fieldnames=['a']).writerow({'a': 1, 'b': 2})))
buf = io.StringIO()
csv.DictWriter(buf, fieldnames=['a'], extrasaction='ignore', lineterminator='\n').writerow({'a': 1, 'b': 2})
out.append(buf.getvalue())
out.append(err(lambda: list(csv.reader(['a,"b'], strict=True))))
out.append(err(lambda: csv.writer(io.StringIO(), quoting=csv.QUOTE_NONE).writerow(['a,b'])))
out.append(issubclass(csv.Error, Exception))
result = out

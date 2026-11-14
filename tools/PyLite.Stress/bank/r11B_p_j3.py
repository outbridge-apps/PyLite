import io, csv
b = io.StringIO()
csv.writer(b).writerow([1, 2.5])
print(repr(b.getvalue()))

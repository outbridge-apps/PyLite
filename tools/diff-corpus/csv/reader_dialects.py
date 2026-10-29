import csv
import io

out = []
text = 'id,name,note\n1,"Smith, J","said ""hi"""\n2,Lee,"two\nlines"\n'
out.append(list(csv.reader(io.StringIO(text))))
out.append(list(csv.reader(['a;b;"c;d"'], delimiter=';')))
out.append(list(csv.reader(["a, b,  'c, d'"], skipinitialspace=True, quotechar="'")))
out.append(list(csv.reader(['"1","x",2.5'], quoting=csv.QUOTE_NONNUMERIC)))
out.append(list(csv.reader(['a\tb\tc'], dialect='excel-tab')))
out.append(list(csv.reader(['a|b'], delimiter='|')))
out.append(list(csv.reader(['', 'x'])))
out.append(list(csv.reader(['a,,b,'])))
out.append(list(csv.reader(['a,b'], strict=True)))
r = csv.reader(io.StringIO('x,y\n1,2\n'))
next(r)
out.append((next(r), r.line_num))
result = out

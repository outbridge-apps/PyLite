import csv

out = []
s = csv.Sniffer()
for sample in ['a;b;c\n1;2;3\n4;5;6\n', 'name,age\n"Smith, J",42\n"Lee",37\n', 'x\ty\n1\t2\n3\t4\n',
               "'a'|'b'\n'1'|'2'\n"]:
    d = s.sniff(sample)
    out.append((d.delimiter, d.quotechar, d.skipinitialspace))
out.append(s.has_header('name,age\nAnn,42\nBob,37\n'))
out.append(s.has_header('1,2\n3,4\n5,6\n'))
out.append(s.sniff('a, b, c\n1, 2, 3\n').skipinitialspace)
try:
    s.sniff('')
    out.append('no error')
except csv.Error:
    out.append('Error')
result = out

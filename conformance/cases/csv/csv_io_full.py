# Adapted from CPython's test suite (Lib/test/test_csv.py: Test_Csv.test_write_arg_valid/test_writerows/
# test_quoting, TestDictFields.test_writeheader/test_read_dict_fields/test_write_simple_dict,
# Lib/test/test_memoryio.py: PyStringIOTest.test_read/test_seek/test_getvalue/test_closed).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# Dialect: csv without a subclassable Dialect class; the registry and field limit are per RUN;
# every reader and writer option is passed as a keyword, and an unknown one is a TypeError.

import csv
import io
from io import StringIO

# ================= io.StringIO =================
s = StringIO()
assert s.getvalue() == '' and s.tell() == 0 and not s.closed
assert s.write('hello') == 5 and s.write(' world\n') == 7 and s.getvalue() == 'hello world\n' and s.tell() == 12
assert s.write('') == 0 and s.seek(0) == 0 and s.read(5) == 'hello' and s.read() == ' world\n' and s.read() == ''
assert s.seek(6) == 6 and s.read(5) == 'world' and s.tell() == 11
s.seek(0)
s.write('HELLO')                                          # overwrite in place
assert s.getvalue() == 'HELLO world\n'
s.seek(0, 2)
s.write('second\n')
assert s.getvalue() == 'HELLO world\nsecond\n'
s.seek(0)
assert s.readline() == 'HELLO world\n' and s.readline() == 'second\n' and s.readline() == ''
s.seek(0)
assert s.readlines() == ['HELLO world\n', 'second\n'] and s.readlines() == []
s.seek(0)
assert [line for line in s] == ['HELLO world\n', 'second\n']
s.seek(0)
assert s.readline(3) == 'HEL' and s.read(2) == 'LO' and s.seek(0, 1) == 5 and s.readline(100) == ' world\n'
assert s.seek(-7, 2) == 12 and s.read() == 'second\n'   # dialect: CPython refuses a nonzero relative seek (OSError)
assert StringIO('preset').getvalue() == 'preset' and StringIO('preset').read() == 'preset' and StringIO('preset').tell() == 0
assert StringIO('a\nb').readlines() == ['a\n', 'b'] and StringIO('').read() == '' and StringIO(None).getvalue() == ''
assert StringIO('é☺').read(1) == 'é' and len(StringIO('日本').getvalue()) == 2
init = StringIO('abc')
init.write('X')
assert init.getvalue() == 'Xbc'
s.close()
assert s.closed
for op in (lambda: s.read(), lambda: s.write('x'), lambda: s.getvalue(), lambda: s.seek(0), lambda: s.tell(), lambda: s.readline()):
    try:
        op()
        assert False
    except ValueError:
        pass
s.close()                                                 # closing twice is fine
try:
    StringIO(5)
    assert False
except TypeError:
    pass
try:
    StringIO().write(5)
    assert False
except TypeError:
    pass
try:
    StringIO().seek(-1)
    assert False
except ValueError:
    pass
try:
    StringIO().seek(0, 3)
    assert False
except ValueError:
    pass
assert io.StringIO is StringIO

# ================= csv.reader =================
rows = list(csv.reader(StringIO('a,b,c\n1,2,3\n')))
assert rows == [['a', 'b', 'c'], ['1', '2', '3']]
assert list(csv.reader(['a,b', '1,2'])) == [['a', 'b'], ['1', '2']]                    # any iterable of lines
assert list(csv.reader(StringIO('"x, y",z\n'))) == [['x, y', 'z']] and list(csv.reader(StringIO('"say ""hi""",z\n'))) == [['say "hi"', 'z']]
assert list(csv.reader(StringIO('a;b\n'), delimiter=';')) == [['a', 'b']] and list(csv.reader(StringIO("'a,b',c\n"), quotechar="'")) == [['a,b', 'c']]
assert list(csv.reader(StringIO('"multi\nline",x\n'))) == [['multi\nline', 'x']] and list(csv.reader(StringIO(''))) == []
assert list(csv.reader(StringIO('\n'))) == [[]] and list(csv.reader(StringIO('a,,c\n'))) == [['a', '', 'c']] and list(csv.reader(StringIO('a,b,\n'))) == [['a', 'b', '']]
assert list(csv.reader(StringIO('a\r\nb\r\n'))) == [['a'], ['b']] and list(csv.reader(StringIO('a,b'))) == [['a', 'b']]
assert list(csv.reader(StringIO(' a , b \n'))) == [[' a ', ' b ']] and list(csv.reader(StringIO('"a"b,c\n'))) == [['ab', 'c']]
assert list(csv.reader(StringIO('é,☺\n'))) == [['é', '☺']] and list(csv.reader(StringIO('a\tb\n'), delimiter='\t')) == [['a', 'b']]
r = csv.reader(StringIO('h1,h2\nv1,v2\n'))
assert next(r) == ['h1', 'h2'] and next(r) == ['v1', 'v2']
try:
    next(r)
    assert False
except StopIteration:
    pass
assert csv.QUOTE_MINIMAL == 0 and csv.QUOTE_ALL == 1 and csv.QUOTE_NONNUMERIC == 2 and csv.QUOTE_NONE == 3

# ================= csv.writer =================
out = StringIO()
w = csv.writer(out)
w.writerow(['a', 'b', 'c'])
w.writerow([1, 2.5, None, True])
w.writerow(['has,comma', 'has "quote"', 'has\nnewline', ''])
assert out.getvalue() == 'a,b,c\r\n1,2.5,,True\r\n"has,comma","has ""quote""","has\nnewline",\r\n'
out2 = StringIO()
csv.writer(out2, delimiter=';', lineterminator='\n').writerows([['x', 'y'], ['1', '2']])
assert out2.getvalue() == 'x;y\n1;2\n'
out3 = StringIO()
csv.writer(out3, quoting=csv.QUOTE_ALL, lineterminator='\n').writerow(['a', 1, ''])
assert out3.getvalue() == '"a","1",""\n'
out4 = StringIO()
csv.writer(out4, quoting=csv.QUOTE_NONNUMERIC, lineterminator='\n').writerow(['a', 1, 2.5])
assert out4.getvalue() == '"a",1,2.5\n'
out5 = StringIO()
csv.writer(out5, quotechar="'", lineterminator='\n').writerow(["it's", 'a,b'])
assert out5.getvalue() == "'it''s','a,b'\n"
out6 = StringIO()
csv.writer(out6, lineterminator='\n').writerow([])
assert out6.getvalue() == '\n'
out7 = StringIO()
csv.writer(out7, quoting=csv.QUOTE_NONE, lineterminator='\n').writerow(['plain', 'fields'])
assert out7.getvalue() == 'plain,fields\n'
out8 = StringIO()
csv.writer(out8, lineterminator='\n').writerow(['é☺', 'ok'])
assert out8.getvalue() == 'é☺,ok\n'
try:
    csv.writer(StringIO(), quoting=csv.QUOTE_NONE).writerow(['a,b'])      # nothing can escape the delimiter
    assert False
except csv.Error:
    pass
try:
    csv.writer(StringIO()).writerow(5)
    assert False
except (csv.Error, TypeError):
    pass

# reader and writer round trip
records = [['name', 'note'], ['Ann', 'says "hi", waves'], ['Bob', 'line1\nline2'], ['', ' spaced ']]
rt = StringIO()
csv.writer(rt).writerows(records)
rt.seek(0)
assert list(csv.reader(rt)) == records

# ================= DictReader / DictWriter =================
recs = list(csv.DictReader(StringIO('name,age\nAnn,30\nBob,40\n')))
assert recs == [{'name': 'Ann', 'age': '30'}, {'name': 'Bob', 'age': '40'}] and isinstance(recs[0], dict) and list(recs[0]) == ['name', 'age']
assert list(csv.DictReader(StringIO('1,2\n3,4\n'), fieldnames=['a', 'b'])) == [{'a': '1', 'b': '2'}, {'a': '3', 'b': '4'}]
assert list(csv.DictReader(StringIO('a,b\n1\n'))) == [{'a': '1', 'b': None}]                 # short row -> None
assert list(csv.DictReader(StringIO('a;b\n1;2\n'), delimiter=';')) == [{'a': '1', 'b': '2'}]
assert list(csv.DictReader(StringIO('x,y\n'))) == [] and list(csv.DictReader(StringIO(''))) == []
assert list(csv.DictReader(StringIO('k,v\n"a,b",c\n'))) == [{'k': 'a,b', 'v': 'c'}]
dr = csv.DictReader(StringIO('a,b\n1,2\n3,4\n'))
assert next(dr) == {'a': '1', 'b': '2'} and next(dr) == {'a': '3', 'b': '4'}
dw_out = StringIO()
dw = csv.DictWriter(dw_out, ['name', 'age'], lineterminator='\n')
dw.writeheader()
dw.writerow({'name': 'Ann', 'age': 30})
dw.writerows([{'name': 'Bob', 'age': 40}, {'age': 50, 'name': 'Cid'}])
assert dw_out.getvalue() == 'name,age\nAnn,30\nBob,40\nCid,50\n'
dw2_out = StringIO()
csv.DictWriter(dw2_out, ['a', 'b'], lineterminator='\n').writerow({'a': 1})
assert dw2_out.getvalue() == '1,\n'                                                          # missing -> empty
try:
    csv.DictWriter(StringIO(), ['a']).writerow({'a': 1, 'zzz': 2})
    assert False
except ValueError:
    pass
dw5_out = StringIO()
csv.DictWriter(dw5_out, ['k', 'v'], delimiter='|', lineterminator='\n').writerows([{'k': 'x', 'v': 'a|b'}])
assert dw5_out.getvalue() == 'x|"a|b"\n'
dw6_out = StringIO()
csv.DictWriter(dw6_out, ['a', 'b'], quoting=csv.QUOTE_ALL, lineterminator='\n').writerow({'a': 1, 'b': 'x'})
assert dw6_out.getvalue() == '"1","x"\n'

# DictWriter -> DictReader round trip
rt2 = StringIO()
dwr = csv.DictWriter(rt2, ['id', 'text'])
dwr.writeheader()
dwr.writerows([{'id': 1, 'text': 'a,b'}, {'id': 2, 'text': 'say "x"'}])
rt2.seek(0)
assert list(csv.DictReader(rt2)) == [{'id': '1', 'text': 'a,b'}, {'id': '2', 'text': 'say "x"'}]

# ---- csv.Error ----
assert issubclass(csv.Error, Exception)
try:
    csv.writer(StringIO(), quoting=csv.QUOTE_NONE).writerow(['a,b'])
except Exception as e:
    assert isinstance(e, csv.Error) and str(e) != ''

# ---- skipinitialspace / escapechar / doublequote / strict / QUOTE_NONNUMERIC on read ----
assert list(csv.reader(['a, b,  c'], skipinitialspace=True)) == [['a', 'b', 'c']]
assert list(csv.reader(['a, b'])) == [['a', ' b']]
assert list(csv.reader(['a\\,b,c'], escapechar='\\')) == [['a,b', 'c']]
assert list(csv.reader(['"a\\"b",c'], escapechar='\\')) == [['a"b', 'c']]
assert list(csv.reader(['"a""b"'])) == [['a"b']]                        # doublequote is on by default
assert list(csv.reader(['"a\\"b"'], doublequote=False, escapechar='\\')) == [['a"b']]
assert list(csv.reader(['1,"x",2.5'], quoting=csv.QUOTE_NONNUMERIC)) == [[1.0, 'x', 2.5]]
assert list(csv.reader([',1'], quoting=csv.QUOTE_NONNUMERIC)) == [['', 1.0]]   # an empty field stays str
assert list(csv.reader(['"a"b'])) == [['ab']]                           # lenient: junk after the quote
assert list(csv.reader(['a"b'], strict=True)) == [['a"b']]              # a quote mid-field is literal
assert list(csv.reader(['"a",b'], quoting=csv.QUOTE_NONE)) == [['"a"', 'b']]
try:
    list(csv.reader(['"a"b'], strict=True))
    assert False
except csv.Error:
    pass
try:
    list(csv.reader(['abc'], quoting=csv.QUOTE_NONNUMERIC))
    assert False
except ValueError:
    pass
try:
    list(csv.reader(['a'], nope=1))                                     # refused, not ignored
    assert False
except TypeError:
    pass

# ---- DictReader restkey / restval / fieldnames ----
assert list(csv.DictReader(['a,b', '1,2,3'], restkey='rest')) == [{'a': '1', 'b': '2', 'rest': ['3']}]
assert list(csv.DictReader(['a,b', '1,2,3'])) == [{'a': '1', 'b': '2', None: ['3']}]   # restkey defaults to None
assert list(csv.DictReader(['a,b', '1'], restval='-')) == [{'a': '1', 'b': '-'}]
assert list(csv.DictReader(['a,b', '1'])) == [{'a': '1', 'b': None}]
assert csv.DictReader(['a,b', '1,2']).fieldnames == ['a', 'b'] and csv.DictReader([]).fieldnames is None
assert list(csv.DictReader(['1,2'], fieldnames=['x', 'y'])) == [{'x': '1', 'y': '2'}]

# ---- writer escapechar / doublequote, DictWriter restval / extrasaction ----
def _w(fn, **kw):
    b = io.StringIO()
    fn(csv.writer(b, **kw))
    return b.getvalue()

def _dw_out(d):
    b = io.StringIO()
    csv.writer(b, d).writerow(["x", "y"])
    return b.getvalue()

def _dw(fn, *args, **kw):
    b = io.StringIO()
    fn(csv.DictWriter(b, *args, **kw))
    return b.getvalue()

assert _w(lambda x: x.writerow(['a,b']), quoting=csv.QUOTE_NONE, escapechar='\\') == 'a\\,b\r\n'
assert _w(lambda x: x.writerow(['a"b']), doublequote=False, escapechar='\\') == 'a\\"b\r\n'   # escaped, not quoted
assert _w(lambda x: x.writerow(['a', 'b,c', 'd"e'])) == 'a,"b,c","d""e"\r\n'
try:
    _w(lambda x: x.writerow(['a,b']), quoting=csv.QUOTE_NONE)
    assert False
except csv.Error:
    pass
assert _dw(lambda x: x.writerow({'a': 1, 'b': 2}), ['a'], extrasaction='ignore') == '1\r\n'
assert _dw(lambda x: x.writerow({'a': 1}), ['a', 'b'], restval='-') == '1,-\r\n'
assert _dw(lambda x: x.writerow({'a': 1}), ['a', 'b']) == '1,\r\n'
assert _dw(lambda x: x.writeheader(), fieldnames=['a', 'b']) == 'a,b\r\n'   # fieldnames by keyword
try:
    _dw(lambda x: x.writerow({'a': 1, 'b': 2}), ['a'])
    assert False
except ValueError:
    pass
try:
    _dw(lambda x: None, ['a'], extrasaction='nope')
    assert False
except ValueError:
    pass

# --- named dialects: excel (default), excel-tab, unix; fmtparams override the preset ---
assert list(csv.reader(['a,b'], dialect='excel')) == [['a', 'b']]
assert list(csv.reader(['a,b'], 'excel')) == [['a', 'b']]
assert list(csv.reader(['a\tb'], dialect='excel-tab')) == [['a', 'b']]
assert list(csv.reader(['a;b'], dialect='excel', delimiter=';')) == [['a', 'b']]
assert _w(lambda x: x.writerow(['a', 'b']), dialect='unix') == '"a","b"\n'
assert _w(lambda x: x.writerow(['a', 'b']), dialect='excel-tab') == 'a\tb\r\n'
assert _w(lambda x: x.writerow(['a']), dialect='unix', quoting=csv.QUOTE_MINIMAL) == 'a\n'
try:
    list(csv.reader(['a'], dialect='nope'))     # a name nobody registered
    assert False
except csv.Error:
    pass

# ---- Sniffer, dialect objects, get_dialect / list_dialects ----
sniffer = csv.Sniffer()
d = sniffer.sniff('a;b;c\r\n1;2;3\r\n4;5;6\r\n')
assert d.delimiter == ';' and d.quotechar == '"' and d.quoting == csv.QUOTE_MINIMAL
assert list(csv.reader(StringIO('a;b\r\n1;2\r\n'), d)) == [['a', 'b'], ['1', '2']]
d2 = sniffer.sniff('x|y\r\n"1, 5"|2\r\n')
assert d2.delimiter == '|' and d2.quotechar == '"'
d3 = sniffer.sniff('a, b\r\n1, 2\r\n', delimiters=',;')
assert d3.delimiter == ',' and d3.skipinitialspace
assert sniffer.has_header('name,age\r\nann,31\r\nbob,42\r\n')
assert not sniffer.has_header('1,2\r\n3,4\r\n5,6\r\n')
try:
    sniffer.sniff('')
    assert False
except csv.Error:
    pass
assert sorted(csv.list_dialects()) == ['excel', 'excel-tab', 'unix']
assert csv.get_dialect('excel-tab').delimiter == '\t' and csv.excel_tab.delimiter == '\t'
assert csv.unix_dialect.quoting == csv.QUOTE_ALL and csv.excel.lineterminator == '\r\n'
assert list(csv.reader(StringIO('a\tb\r\n'), csv.excel_tab)) == [['a', 'b']]
assert list(csv.reader(StringIO('a\tb\r\n'), csv.excel_tab, delimiter=';')) == [['a\tb']]   # fmtparams override a copy
assert csv.excel_tab.delimiter == '\t'

# ---- register_dialect / unregister_dialect / field_size_limit (closed 2026-09-11) ----
# dialect: the registry and the limit are per RUN (the module dictionary is shared between runs), and
# subclassing csv.Dialect is not the way in -- a class body here holds no attributes.
csv.register_dialect('semi', delimiter=';', quoting=csv.QUOTE_ALL)
assert list(csv.reader(['a;b;c'], 'semi')) == [['a', 'b', 'c']]
assert list(csv.reader(['a;b'], dialect='semi')) == [['a', 'b']]
assert 'semi' in csv.list_dialects() and csv.get_dialect('semi').delimiter == ';'
assert _w(lambda x: x.writerow(['x', 'y']), dialect='semi') == '"x";"y"\r\n'
assert list(csv.reader(['a|b'], 'semi', delimiter='|')) == [['a', 'b']]   # fmtparams still override
csv.register_dialect('semi2', csv.get_dialect('semi'), delimiter=':')     # a dialect object as the base
csv.register_dialect('tabbed', 'excel', delimiter='\t')                   # or a registered name
assert list(csv.reader(['a:b'], 'semi2')) == [['a', 'b']]
assert list(csv.reader(['a\tb'], 'tabbed')) == [['a', 'b']]
assert list(csv.DictReader(['a;b', '1;2'], dialect='semi')) == [{'a': '1', 'b': '2'}]
assert sorted(csv.list_dialects()) == ['excel', 'excel-tab', 'semi', 'semi2', 'tabbed', 'unix']
csv.unregister_dialect('semi')
csv.unregister_dialect('semi2')
csv.unregister_dialect('tabbed')
for gone in (lambda: csv.get_dialect('semi'), lambda: csv.unregister_dialect('semi'),
             lambda: list(csv.reader(['a;b'], 'semi'))):
    try:
        gone()
        assert False
    except csv.Error:
        pass
assert sorted(csv.list_dialects()) == ['excel', 'excel-tab', 'unix']
csv.register_dialect('excel', delimiter='#')                              # a preset can be shadowed
assert list(csv.reader(['a#b'], 'excel')) == [['a', 'b']]
csv.unregister_dialect('excel')
assert list(csv.reader(['a,b'], 'excel')) == [['a', 'b']]                 # and the preset is back

assert csv.field_size_limit() == 131072                  # CPython's default
assert csv.field_size_limit(20) == 131072                # returns the limit that WAS in force
assert csv.field_size_limit() == 20
try:
    list(csv.reader(['a' * 50 + ',b']))
    assert False
except csv.Error:
    pass
csv.field_size_limit(131072)
assert list(csv.reader(['a' * 50 + ',b']))[0][1] == 'b'

# ---- object attributes and the two return values (closed 2026-09-11) ----
# writerow returns whatever the sink's write() returned (3.5), writeheader likewise (3.8)
_b = StringIO()
assert csv.writer(_b).writerow(['a']) == 3                 # 'a\r\n'
assert csv.writer(_b, lineterminator='\n').writerow(['ab']) == 3
assert csv.writer(_b).writerows([['a'], ['b']]) is None     # writerows stays None
assert csv.DictWriter(StringIO(), ['a']).writeheader() == 3
assert csv.DictWriter(StringIO(), ['a']).writerow({'a': 1}) == 3

assert csv.writer(StringIO(), delimiter=';').dialect.delimiter == ';'
assert csv.reader(['a'], delimiter=';').dialect.delimiter == ';'
assert csv.DictWriter(StringIO(), ['a', 'b']).fieldnames == ['a', 'b']
assert csv.DictWriter(StringIO(), ['a']).restval == '' and csv.DictWriter(StringIO(), ['a'], restval='?').restval == '?'
assert csv.DictWriter(StringIO(), ['a']).extrasaction == 'raise'
assert csv.DictReader(['a'], restkey='x').restkey == 'x' and csv.DictReader(['a']).restkey is None
assert csv.DictReader(['a'], restval='?').restval == '?' and csv.DictReader(['a'], delimiter=';').dialect.delimiter == ';'
assert csv.DictReader(['a,b', '1,2']).fieldnames == ['a', 'b']
_dr = csv.DictReader(['a', '1', '2'])
assert _dr.line_num == 0 and [x for x in _dr] == [{'a': '1'}, {'a': '2'}] and _dr.line_num == 3
assert csv.__version__ == '1.0'
for _absent in ('fieldnames', 'restval', 'extrasaction'):        # a plain writer carries none of them
    try:
        getattr(csv.writer(StringIO()), _absent)
        assert False, _absent
    except AttributeError:
        pass

# QUOTE_NONNUMERIC quotes None (quoted = !PyNumber_Check(field) is computed before the None branch),
# which is why 3.12 added QUOTE_NOTNULL; QUOTE_MINIMAL leaves it bare
assert _w(lambda x: x.writerow(['a', 1, 2.5, None]), quoting=csv.QUOTE_NONNUMERIC) == '"a",1,2.5,""\r\n'
assert _w(lambda x: x.writerow([1, None])) == '1,\r\n'
assert _w(lambda x: x.writerow([1, None]), quoting=csv.QUOTE_ALL) == '"1",""\r\n'

# the Sniffer picks from a frequency analysis, so a one-character sample yields that character
assert csv.Sniffer().sniff('zzzz').delimiter == 'z'
for _blank in ('', '\n\n'):
    try:
        csv.Sniffer().sniff(_blank)
        assert False
    except csv.Error:
        pass

# ---- QUOTE_STRINGS / QUOTE_NOTNULL (3.12) and a dialect-shaped object (closed 2026-09-11) ----
assert csv.QUOTE_STRINGS == 4 and csv.QUOTE_NOTNULL == 5
_row = ['a', 1, 2.5, None, '']
assert _w(lambda x: x.writerow(_row)) == 'a,1,2.5,,\r\n'
assert _w(lambda x: x.writerow(_row), quoting=csv.QUOTE_ALL) == '"a","1","2.5","",""\r\n'
assert _w(lambda x: x.writerow(_row), quoting=csv.QUOTE_NONNUMERIC) == '"a",1,2.5,"",""\r\n'
assert _w(lambda x: x.writerow(_row), quoting=csv.QUOTE_STRINGS) == '"a",1,2.5,,""\r\n'
assert _w(lambda x: x.writerow(_row), quoting=csv.QUOTE_NOTNULL) == '"a","1","2.5",,""\r\n'
# reading: both new modes read an EMPTY UNQUOTED field back as None; past that STRINGS behaves as
# NONNUMERIC and NOTNULL as ALL
assert list(csv.reader(['"a",1,,2.5'])) == [['a', '1', '', '2.5']]
assert list(csv.reader(['"a",1,,2.5'], quoting=csv.QUOTE_NONNUMERIC)) == [['a', 1.0, '', 2.5]]
assert list(csv.reader(['"a",1,,2.5'], quoting=csv.QUOTE_STRINGS)) == [['a', 1.0, None, 2.5]]
assert list(csv.reader(['"a",1,,2.5'], quoting=csv.QUOTE_NOTNULL)) == [['a', '1', None, '2.5']]
assert list(csv.reader(['"",a'], quoting=csv.QUOTE_NOTNULL)) == [['', 'a']]   # an empty QUOTED field is a string
for _bad in (99, -1, 6):
    try:
        list(csv.reader(['a'], quoting=_bad))
        assert False, _bad
    except TypeError:
        pass

# dialect_init reads the eight attributes off WHATEVER object it is handed and clears a missing one, so
# csv.Dialect is a carrier and not a requirement; dialect: subclassing it is out either way, since a
# class body holds no attributes and a base must be a record class
class MyDialect:
    def __init__(self):
        self.delimiter = ';'
        self.quoting = csv.QUOTE_ALL
        self.lineterminator = '\n'

class PartialDialect:
    def __init__(self):
        self.delimiter = '|'

assert list(csv.reader(['a;b'], MyDialect())) == [['a', 'b']]
assert _dw_out(MyDialect()) == '"x";"y"\n'
assert list(csv.reader(['a|b'], PartialDialect())) == [['a', 'b']]      # the other seven default
assert list(csv.reader(['a:b'], MyDialect(), delimiter=':')) == [['a', 'b']]   # fmtparams still override
assert list(csv.DictReader(['a;b', '1;2'], dialect=MyDialect())) == [{'a': '1', 'b': '2'}]
csv.register_dialect('mine', MyDialect())
assert csv.get_dialect('mine').delimiter == ';' and list(csv.reader(['a;b'], 'mine')) == [['a', 'b']]
csv.unregister_dialect('mine')
assert list(csv.reader(['a,b'], 42)) == [['a', 'b']]                    # no attributes: the default dialect
try:
    list(csv.reader(['a'], 'nope'))                                     # a NAME is still a registry lookup
    assert False
except csv.Error:
    pass

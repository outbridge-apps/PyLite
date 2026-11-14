def t(f):
    try:
        print(repr(f()))
    except Exception as e:
        print('EXC', type(e).__name__ + ':', e)
import xml.etree.ElementTree as ET
r = ET.fromstring('<r xmlns="urn:d" xmlns:q="urn:q"><a>x</a><q:a>y</q:a><b><a>z</a></b><c/></r>')
for p in ['{*}a', '{urn:d}a', '{urn:d}*', '{urn:q}*', '{*}*', './/{*}a', '{}a', '{}*', '{*}b/{*}a', './/{urn:q}*', '{*}b[{*}a]', '{*}a[.="y"]', '*', '{urn:x}*']:
    t(lambda: [e.tag for e in r.findall(p)])
t(lambda: r.findtext('{*}a'))
t(lambda: r.find('{*}c').tag)
r2 = ET.fromstring('<r><a>1</a><b><a>2</a></b></r>')
t(lambda: [e.text for e in r2.findall('{}a')])
t(lambda: [e.text for e in r2.findall('.//{}a')])
t(lambda: [e.tag for e in r2.findall('{}*')])
t(lambda: [e.text for e in r2.iterfind('{*}a')])
import json, io
t(lambda: ET.fromstring(b'\xef\xbb\xbf<a>x</a>').text)
t(lambda: ET.fromstring('<a>é</a>'.encode('utf-16')).text)
t(lambda: ET.fromstring('<a>é</a>'.encode('utf-16-le')).text)
t(lambda: ET.fromstring('<?xml version="1.0"?><a>é</a>'.encode('utf-16-be')).text)
t(lambda: ET.fromstring('<?xml version="1.0" encoding="windows-1252"?><a>€</a>'.encode('cp1252')).text)
t(lambda: ET.fromstring('<?xml version="1.0" encoding="koi8-r"?><a>ж</a>'.encode('koi8-r')).text)
t(lambda: ET.fromstring(b'<?xml version="1.0" encoding="foo"?><a/>'))
t(lambda: ET.fromstring(b'<?xml version="1.0" encoding="utf-16"?><a/>'))
t(lambda: ET.fromstring(b'<a>\xff</a>'))
t(lambda: ET.fromstring(b'<a>\n<b>\xe9\xe9</b></a>'))
t(lambda: ET.fromstring(b"<?xml version='1.0' encoding='ISO-8859-1'?><a>\xe9</a>").text)
t(lambda: ET.fromstring(b"<?xml version='1.0' encoding='us-ascii'?><a>x</a>").text)
t(lambda: ET.fromstring('﻿<a>x</a>').text)
t(lambda: ET.fromstring('<?xml version="1.0" encoding="windows-1252"?><a>€</a>').text)
t(lambda: ET.fromstring('<?xml version="1.0" encoding="utf-16"?><a>x</a>').text)
t(lambda: ET.fromstring(b'<?xml version="1.0" encoding="shift_jis"?><a/>'))
t(lambda: ET.parse(io.BytesIO('<?xml version="1.0" encoding="cp1251"?><a>ж</a>'.encode('cp1251'))).getroot().text)
t(lambda: json.loads(b'{"a": 1}'))
t(lambda: json.loads(b'\xef\xbb\xbf{"a": 1}'))
t(lambda: json.loads('{"a": "é"}'.encode('utf-16')))
t(lambda: json.loads('{"a": "é"}'.encode('utf-16-le')))
t(lambda: json.loads('{"a": "é"}'.encode('utf-16-be')))
t(lambda: json.loads('﻿{"a": 1}'))
t(lambda: json.loads(b'12'))
t(lambda: json.loads(b'\x00\x31'))
t(lambda: json.loads(b'"\xff"'))
t(lambda: json.loads(5))
t(lambda: json.load(io.BytesIO(b'\xef\xbb\xbf[3]')))
t(lambda: json.load(io.StringIO('﻿[3]')))
import csv
for src in ['1,"L1\r\nL2"', '1,"L1\nL2"\n3,4', 'a,"x\r\ny\r\nz",b\r\nc,d\r\n']:
    t(lambda: list(csv.reader(io.StringIO(src))))
    t(lambda: list(csv.reader(io.StringIO(src, newline=''))))
t(lambda: list(csv.reader(['a,"b', 'c"'])))
t(lambda: list(csv.reader(['a,"b\n', 'c"'])))
t(lambda: list(csv.reader(['a,"b' + chr(92), 'c"'], escapechar=chr(92))))
t(lambda: list(csv.reader(['a,"b' + chr(92) + chr(10), 'c"'], escapechar=chr(92))))
t(lambda: list(csv.DictReader(io.StringIO('h1,h2\r\n1,"x\r\ny"\r\n'))))
import zoneinfo, datetime
def z(key, *a):
    d = datetime.datetime(*a, tzinfo=zoneinfo.ZoneInfo(key))
    print(key, a, d.utcoffset(), d.dst(), d.tzname())
z('Europe/Moscow', 2012, 1, 1); z('Europe/Moscow', 2012, 7, 1); z('Europe/Moscow', 2020, 7, 1)
z('America/Asuncion', 2020, 7, 1); z('America/Asuncion', 2020, 12, 1); z('America/Asuncion', 2025, 7, 1)
z('Europe/Berlin', 2020, 1, 1); z('Europe/Berlin', 2020, 7, 1); z('America/New_York', 2021, 7, 4); z('America/New_York', 2021, 1, 4)
z('Europe/London', 2021, 7, 1); z('Europe/London', 2021, 1, 1); z('Asia/Almaty', 2025, 6, 1); z('Australia/Sydney', 2021, 1, 1)
z('America/Sao_Paulo', 2018, 12, 1); z('America/Sao_Paulo', 2021, 12, 1); z('Europe/Istanbul', 2020, 7, 1)
x = 'mod'
def f():
    x = 5
    class A:
        x = x
    return A.x
print(f())
class B:
    y: int = 5
    z = y
print(B.z)
order = []
def note(v):
    order.append(v)
    return v
def deco(c):
    order.append('apply')
    return c
class Base: pass
@note(deco)
class C(note(Base)):
    a = note(1)
    def m(self, d=note(2)):
        return d
    b = note(3)
    f1: int = note(4)
print(order[0] is deco, order[1] is Base, order[2:])
def g():
    q = 'local'
    class D:
        r = q
        q = 'cls'
        s = q
    return D.r, D.s
q = 'glob'
print(g())
def h():
    k = 'local'
    class E:
        w = [k for _ in range(1)]
        v = (lambda: k)()
    return E.w, E.v
print(h())

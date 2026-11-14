def show(src, f):
    try:
        print(src, repr(f()))
    except Exception as e:
        print(src, type(e).__name__, e)
import math

class Lcg:
    def __init__(self, seed):
        self.s = seed
    def next(self):
        self.s = (self.s * 6364136223846793005 + 1442695040888963407) & 0xFFFFFFFFFFFFFFFF
        return self.s >> 11
    def unit(self):
        return self.next() / 9007199254740992.0

def sig(h, v):
    return (h * 1000003 + hash(v.hex())) & 0xFFFFFFFFFFFF if False else (h * 1000003 + sum(ord(c) * (i + 1) for i, c in enumerate(v.hex()))) & 0xFFFFFFFFFFFF

def grid(name, f, lo, hi, n=300):
    r = Lcg(len(name) * 7919 + lo * 13 + 100003)
    h = 0
    first = None
    for i in range(n):
        x = (1 + r.unit()) * 2.0 ** (lo + (r.next() % (hi - lo + 1)))
        if r.next() & 1:
            x = -x
        try:
            v = f(x)
        except (ValueError, OverflowError) as e:
            v = 1.5
        h = sig(h, v)
    print(name, lo, hi, h)

for name, f in (('sin', math.sin), ('cos', math.cos), ('tan', math.tan)):
    grid(name, f, -30, -1)
    grid(name, f, 0, 20)
grid('expm1', math.expm1, -60, -1)
grid('expm1', math.expm1, 0, 9)

def hgrid(lo, hi, dims, n=300):
    r = Lcg(dims * 104729 + lo + 5000)
    h = 0
    for i in range(n):
        xs = [(1 + r.unit()) * 2.0 ** (lo + (r.next() % (hi - lo + 1))) for _ in range(dims)]
        h = sig(h, math.hypot(*xs))
    print('hypot', dims, lo, hi, h)
hgrid(-5, 5, 2)
hgrid(-5, 5, 3)
hgrid(-300, 300, 2)
hgrid(0, 3, 10)
hgrid(-1074, -1020, 2)
hgrid(1000, 1022, 2)
bad = 0
for a in range(1, 60):
    for b in range(a, 60):
        c = math.isqrt(a * a + b * b)
        if c * c == a * a + b * b and math.hypot(a, b) != c:
            bad += 1
print('triples', bad)
print(math.dist((1.0, 2.0, 3.0), (4.0, 6.0, 8.0)).hex(), math.dist((1e300,), (-1e300,)), math.hypot(), math.hypot(-3.5), math.hypot(5e-324, 5e-324).hex())

def sgrid(lo, hi, n, mode):
    r = Lcg(n * 31 + lo + 77)
    h = 0
    for k in range(40):
        p = [(1 + r.unit()) * 2.0 ** (lo + (r.next() % (hi - lo + 1))) * (1 if r.next() & 1 else -1) for _ in range(n)]
        if mode == 'int':
            q = [int(r.next() % 1000) - 500 for _ in range(n)]
        elif mode == 'bool':
            q = [r.next() & 1 == 1 for _ in range(n)]
        else:
            q = [(1 + r.unit()) * 2.0 ** (lo + (r.next() % (hi - lo + 1))) for _ in range(n)]
        h = sig(h, float(math.sumprod(p, q)))
    print('sumprod', lo, hi, n, mode, h)
sgrid(-5, 5, 10, 'flt')
sgrid(-5, 5, 100, 'int')
sgrid(-50, 50, 30, 'flt')
sgrid(0, 300, 5, 'flt')
sgrid(400, 500, 5, 'flt')
sgrid(-5, 5, 20, 'bool')

show("math.sumprod([0.1]*10, [1]*10)", lambda: math.sumprod([0.1]*10, [1]*10))
show("math.sumprod([1e100,1,-1e100],[1,1,1])", lambda: math.sumprod([1e100,1,-1e100],[1,1,1]))
show("math.sumprod([1,2,3],[4,5,6])", lambda: math.sumprod([1,2,3],[4,5,6]))
show("math.sumprod([1,2.5],[3,4])", lambda: math.sumprod([1,2.5],[3,4]))
show("math.sumprod([1.5,2],[3,4])", lambda: math.sumprod([1.5,2],[3,4]))
show("math.sumprod([2**62,2**62],[2,2])", lambda: math.sumprod([2**62,2**62],[2,2]))
show("math.sumprod([10**30,1],[2,3])", lambda: math.sumprod([10**30,1],[2,3]))
show("math.sumprod([1.5,10**30],[2.0,1.5])", lambda: math.sumprod([1.5,10**30],[2.0,1.5]))
show("math.sumprod([1e308,1e308],[10.0,1.0])", lambda: math.sumprod([1e308,1e308],[10.0,1.0]))
show("math.sumprod([float('inf'),1.0],[1.0,1.0])", lambda: math.sumprod([float('inf'),1.0],[1.0,1.0]))
show("math.sumprod([float('nan')],[1.0])", lambda: math.sumprod([float('nan')],[1.0]))
show("math.sumprod([],[])", lambda: math.sumprod([],[]))
show("math.sumprod([True,False],[2,3])", lambda: math.sumprod([True,False],[2,3]))
show("math.sumprod([1.5,'a'],[2.0,1])", lambda: math.sumprod([1.5,'a'],[2.0,1]))
show("math.sumprod([1],[1,2])", lambda: math.sumprod([1],[1,2]))
show("math.sumprod(['a','b'],[2,3])", lambda: math.sumprod(['a','b'],[2,3]))
show("math.sumprod([1.5, 2],[True, 2.5])", lambda: math.sumprod([1.5, 2],[True, 2.5]))
show("math.sumprod([1e308, -1e308, 1.0], [10.0, 10.0, 1.0])", lambda: math.sumprod([1e308, -1e308, 1.0], [10.0, 10.0, 1.0]))
show("math.sumprod([10**400, 1.5],[1, 2.0])", lambda: math.sumprod([10**400, 1.5],[1, 2.0]))
show("sum([1.0, 2**53, 1.0])", lambda: sum([1.0, 2**53, 1.0]))
show("sum([1e100, 1, -1e100])", lambda: sum([1e100, 1, -1e100]))
show("sum([b'a'], b'')", lambda: sum([b'a'], b''))
show("sum([1e308,1e308,-1e308])", lambda: sum([1e308,1e308,-1e308]))
show("sum([1.0,2**70,1.0])", lambda: sum([1.0,2**70,1.0]))
show("sum([1.5,True,1e16])", lambda: sum([1.5,True,1e16]))
show("sum([0.1]*10)", lambda: sum([0.1]*10))
show("sum([1,2.5,10**30])", lambda: sum([1,2.5,10**30]))
show("sum([0.0,-0.0])", lambda: sum([0.0,-0.0]))
show("sum([-0.0],-0.0)", lambda: sum([-0.0],-0.0))
show("sum([1,2],1.5)", lambda: sum([1,2],1.5))
show("sum([float('inf'),1,-1.0])", lambda: sum([float('inf'),1,-1.0]))
show("math.sin(7e22)", lambda: math.sin(7e22))
show("math.cos(7e22)", lambda: math.cos(7e22))
show("math.tan(1e19)", lambda: math.tan(1e19))
show("math.sin(1e22)", lambda: math.sin(1e22))
show("math.cos(1e22)", lambda: math.cos(1e22))
show("math.sin(2**1023)", lambda: math.sin(2**1023))
show("math.sin(1.7976931348623157e308)", lambda: math.sin(1.7976931348623157e308))
show("math.cos(1.7976931348623157e308)", lambda: math.cos(1.7976931348623157e308))
show("math.tan(1.7976931348623157e308)", lambda: math.tan(1.7976931348623157e308))
show("math.sin(-0.0)", lambda: math.sin(-0.0))
show("math.tan(-0.0)", lambda: math.tan(-0.0))
show("math.cos(-0.0)", lambda: math.cos(-0.0))
show("math.sin(5e-324)", lambda: math.sin(5e-324))
show("math.sin(float('inf'))", lambda: math.sin(float('inf')))
show("math.cos(float('nan'))", lambda: math.cos(float('nan')))
show("math.tan(float('-inf'))", lambda: math.tan(float('-inf')))
show("math.sin(math.pi)", lambda: math.sin(math.pi))
show("math.cos(math.pi/2)", lambda: math.cos(math.pi/2))
show("math.tan(math.pi/2)", lambda: math.tan(math.pi/2))
show("math.sin(6381956970095103.0 * 2.0**797)", lambda: math.sin(6381956970095103.0 * 2.0**797))
show("math.expm1(1e-5)", lambda: math.expm1(1e-5))
show("math.expm1(709.0)", lambda: math.expm1(709.0))
show("math.expm1(710.0)", lambda: math.expm1(710.0))
show("math.expm1(-50.0)", lambda: math.expm1(-50.0))
show("math.expm1(0.0)", lambda: math.expm1(0.0))
show("math.expm1(-0.0)", lambda: math.expm1(-0.0))
show("math.expm1(1e-300)", lambda: math.expm1(1e-300))
show("math.expm1(float('inf'))", lambda: math.expm1(float('inf')))
show("math.expm1(float('-inf'))", lambda: math.expm1(float('-inf')))
show("math.expm1(float('nan'))", lambda: math.expm1(float('nan')))
show("math.expm1(0.5)", lambda: math.expm1(0.5))
show("math.expm1(-0.5)", lambda: math.expm1(-0.5))
show("math.expm1(1.0)", lambda: math.expm1(1.0))
show("math.expm1(40.0)", lambda: math.expm1(40.0))
from decimal import Decimal, getcontext, ROUND_UP, ROUND_DOWN, DivisionByZero, InvalidOperation
from fractions import Fraction
from datetime import datetime, timezone, timedelta
show("Decimal('1.1') ** 2", lambda: Decimal('1.1') ** 2)
show("Decimal('1.10') ** 2", lambda: Decimal('1.10') ** 2)
show("Decimal('1.1') ** 50", lambda: Decimal('1.1') ** 50)
show("Decimal('1.0000001') ** 1000", lambda: Decimal('1.0000001') ** 1000)
show("Decimal('1.23456789') ** 17", lambda: Decimal('1.23456789') ** 17)
show("Decimal('3') ** 40", lambda: Decimal('3') ** 40)
show("Decimal('3') ** 55", lambda: Decimal('3') ** 55)
show("Decimal('2') ** 90", lambda: Decimal('2') ** 90)
show("Decimal('2') ** -1", lambda: Decimal('2') ** -1)
show("Decimal('2') ** -3", lambda: Decimal('2') ** -3)
show("Decimal('3') ** -1", lambda: Decimal('3') ** -1)
show("Decimal('0.3') ** -7", lambda: Decimal('0.3') ** -7)
show("Decimal('-2') ** 3", lambda: Decimal('-2') ** 3)
show("Decimal('-2') ** 4", lambda: Decimal('-2') ** 4)
show("Decimal('-1.5') ** 3", lambda: Decimal('-1.5') ** 3)
show("Decimal('0') ** 3", lambda: Decimal('0') ** 3)
show("Decimal('0.0') ** 3", lambda: Decimal('0.0') ** 3)
show("Decimal('-0') ** 3", lambda: Decimal('-0') ** 3)
show("Decimal('-0') ** 2", lambda: Decimal('-0') ** 2)
show("Decimal('0') ** 0", lambda: Decimal('0') ** 0)
show("Decimal('1') ** 1000000", lambda: Decimal('1') ** 1000000)
show("Decimal('1.0') ** 3", lambda: Decimal('1.0') ** 3)
show("Decimal('1.00') ** -2", lambda: Decimal('1.00') ** -2)
show("Decimal('1.0000000001') ** 1000000", lambda: Decimal('1.0000000001') ** 1000000)
show("Decimal('0.9999999999') ** 1000000", lambda: Decimal('0.9999999999') ** 1000000)
show("Decimal('10') ** 5", lambda: Decimal('10') ** 5)
show("Decimal('1E+2') ** 2", lambda: Decimal('1E+2') ** 2)
show("Decimal('1E+2') ** 3", lambda: Decimal('1E+2') ** 3)
show("Decimal('2.5') ** 0", lambda: Decimal('2.5') ** 0)
show("Decimal('2.5') ** 1", lambda: Decimal('2.5') ** 1)
show("Decimal('2.5') ** Decimal('3')", lambda: Decimal('2.5') ** Decimal('3'))
show("Decimal('2.5') ** Decimal('3.0')", lambda: Decimal('2.5') ** Decimal('3.0'))
show("Decimal('2.5') ** Decimal('3E+0')", lambda: Decimal('2.5') ** Decimal('3E+0'))
show("Decimal('1.1') ** Decimal('2E+1')", lambda: Decimal('1.1') ** Decimal('2E+1'))
show("Decimal('1.1') ** True", lambda: Decimal('1.1') ** True)
show("2 ** Decimal('3')", lambda: 2 ** Decimal('3'))
show("2 ** Decimal('-3')", lambda: 2 ** Decimal('-3'))
show("Decimal('1.000000000000000000000000001') ** 3", lambda: Decimal('1.000000000000000000000000001') ** 3)
show("Decimal('9.99999999999999999999999999') ** 2", lambda: Decimal('9.99999999999999999999999999') ** 2)
show("Decimal('1234567.891') ** 3", lambda: Decimal('1234567.891') ** 3)
show("Decimal('1.1') ** (10**40)", lambda: Decimal('1.1') ** (10**40))
show("Decimal('1') ** (10**40)", lambda: Decimal('1') ** (10**40))
show("Decimal('12345678901234567890123456789') // Decimal('7')", lambda: Decimal('12345678901234567890123456789') // Decimal('7'))
show("Decimal('12345678901234567890123456789') % Decimal('7')", lambda: Decimal('12345678901234567890123456789') % Decimal('7'))
show("divmod(Decimal('12345678901234567890123456789'), Decimal('7'))", lambda: divmod(Decimal('12345678901234567890123456789'), Decimal('7')))
show("Decimal('9999999999999999999999999999') // Decimal('3')", lambda: Decimal('9999999999999999999999999999') // Decimal('3'))
show("Decimal('9999999999999999999999999999') % Decimal('9')", lambda: Decimal('9999999999999999999999999999') % Decimal('9'))
show("Decimal('1234567890123456789012345678') // Decimal('0.3')", lambda: Decimal('1234567890123456789012345678') // Decimal('0.3'))
show("Decimal('1234567890123456789012345') // Decimal('0.1')", lambda: Decimal('1234567890123456789012345') // Decimal('0.1'))
show("Decimal('1234567890123456789012345') % Decimal('0.1')", lambda: Decimal('1234567890123456789012345') % Decimal('0.1'))
show("Decimal('7') // Decimal('2')", lambda: Decimal('7') // Decimal('2'))
show("Decimal('-7') // Decimal('2')", lambda: Decimal('-7') // Decimal('2'))
show("Decimal('7') // Decimal('-2')", lambda: Decimal('7') // Decimal('-2'))
show("Decimal('-7') % Decimal('2')", lambda: Decimal('-7') % Decimal('2'))
show("Decimal('7.5') % Decimal('2')", lambda: Decimal('7.5') % Decimal('2'))
show("Decimal('7.50') % Decimal('2.0')", lambda: Decimal('7.50') % Decimal('2.0'))
show("Decimal('1') // Decimal('3')", lambda: Decimal('1') // Decimal('3'))
show("Decimal('-1') // Decimal('3')", lambda: Decimal('-1') // Decimal('3'))
show("Decimal('-1') % Decimal('3')", lambda: Decimal('-1') % Decimal('3'))
show("Decimal('0') // Decimal('3')", lambda: Decimal('0') // Decimal('3'))
show("Decimal('-0') // Decimal('3')", lambda: Decimal('-0') // Decimal('3'))
show("Decimal('0.0') % Decimal('3')", lambda: Decimal('0.0') % Decimal('3'))
show("divmod(Decimal('7.5'), Decimal('2'))", lambda: divmod(Decimal('7.5'), Decimal('2')))
show("divmod(Decimal('-7.5'), Decimal('2'))", lambda: divmod(Decimal('-7.5'), Decimal('2')))
show("divmod(Decimal('1E+2'), Decimal('7'))", lambda: divmod(Decimal('1E+2'), Decimal('7')))
show("Decimal('1E+2') // Decimal('7')", lambda: Decimal('1E+2') // Decimal('7'))
show("Decimal('1E+2') % Decimal('7')", lambda: Decimal('1E+2') % Decimal('7'))
show("Decimal('1E+2') % Decimal('30')", lambda: Decimal('1E+2') % Decimal('30'))
show("Decimal('1') % Decimal('0')", lambda: Decimal('1') % Decimal('0'))
show("Decimal('1') // Decimal('0')", lambda: Decimal('1') // Decimal('0'))
show("Decimal('0') // Decimal('0')", lambda: Decimal('0') // Decimal('0'))
show("divmod(Decimal('1'), Decimal('0'))", lambda: divmod(Decimal('1'), Decimal('0')))
show("7 // Decimal('2')", lambda: 7 // Decimal('2'))
show("7 % Decimal('2')", lambda: 7 % Decimal('2'))
show("divmod(7, Decimal('2'))", lambda: divmod(7, Decimal('2')))
show("Fraction(0) ** -1.0", lambda: Fraction(0) ** -1.0)
show("Fraction(1, 2) ** 2.0", lambda: Fraction(1, 2) ** 2.0)
show("Fraction(1, 2) ** Fraction(1, 2)", lambda: Fraction(1, 2) ** Fraction(1, 2))
show("Fraction(0) ** Fraction(-1, 2)", lambda: Fraction(0) ** Fraction(-1, 2))
show("Fraction(4) ** 0.5", lambda: Fraction(4) ** 0.5)
show("2 ** Fraction(2)", lambda: 2 ** Fraction(2))
show("2 ** Fraction(-2)", lambda: 2 ** Fraction(-2))
show("2 ** Fraction(1, 2)", lambda: 2 ** Fraction(1, 2))
show("2.0 ** Fraction(2)", lambda: 2.0 ** Fraction(2))
show("2.0 ** Fraction(-2)", lambda: 2.0 ** Fraction(-2))
show("2.0 ** Fraction(1, 2)", lambda: 2.0 ** Fraction(1, 2))
show("True ** Fraction(2)", lambda: True ** Fraction(2))
show("0 ** Fraction(-1)", lambda: 0 ** Fraction(-1))
show("0.0 ** Fraction(-1)", lambda: 0.0 ** Fraction(-1))
show("Fraction(2) ** 2", lambda: Fraction(2) ** 2)
show("Fraction(2, 3) ** -2", lambda: Fraction(2, 3) ** -2)
show("repr(timezone(timedelta(hours=1), name='CET'))", lambda: repr(timezone(timedelta(hours=1), name='CET')))
show("timezone(timedelta(hours=1), name='CET').tzname(None)", lambda: timezone(timedelta(hours=1), name='CET').tzname(None))
show("timezone(offset=timedelta(hours=2))", lambda: timezone(offset=timedelta(hours=2)))
show("timezone(offset=timedelta(hours=2), name='X').tzname(None)", lambda: timezone(offset=timedelta(hours=2), name='X').tzname(None))
show("timezone(timedelta(0), name='Z')", lambda: timezone(timedelta(0), name='Z'))
show("timezone(timedelta(0), 'Z').tzname(None)", lambda: timezone(timedelta(0), 'Z').tzname(None))
show("timezone(timedelta(0))", lambda: timezone(timedelta(0)))
show("timezone(timedelta(hours=1), 'CET')", lambda: timezone(timedelta(hours=1), 'CET'))
show("timezone('x')", lambda: timezone('x'))
show("timezone(timedelta(0), 1)", lambda: timezone(timedelta(0), 1))
show("timezone(timedelta(0), 'a', 'b')", lambda: timezone(timedelta(0), 'a', 'b'))
show("timezone(timedelta(hours=25))", lambda: timezone(timedelta(hours=25)))
show("datetime(2020, 1, 1, tzinfo=timezone.utc).timestamp()", lambda: datetime(2020, 1, 1, tzinfo=timezone.utc).timestamp())
show("datetime(2020, 1, 1, 0, 0, 0, 123456, tzinfo=timezone.utc).timestamp()", lambda: datetime(2020, 1, 1, 0, 0, 0, 123456, tzinfo=timezone.utc).timestamp())
show("datetime(9999, 12, 31, 23, 59, 59, 999999, tzinfo=timezone.utc).timestamp()", lambda: datetime(9999, 12, 31, 23, 59, 59, 999999, tzinfo=timezone.utc).timestamp())
show("datetime(1, 1, 1, 0, 0, 0, 1, tzinfo=timezone.utc).timestamp()", lambda: datetime(1, 1, 1, 0, 0, 0, 1, tzinfo=timezone.utc).timestamp())
show("datetime(5000, 6, 7, 8, 9, 10, 111111, tzinfo=timezone.utc).timestamp()", lambda: datetime(5000, 6, 7, 8, 9, 10, 111111, tzinfo=timezone.utc).timestamp())
show("datetime(5000, 6, 7, 8, 9, 10, 111113, tzinfo=timezone(timedelta(hours=3))).timestamp()", lambda: datetime(5000, 6, 7, 8, 9, 10, 111113, tzinfo=timezone(timedelta(hours=3))).timestamp())
show("datetime(1969, 12, 31, 23, 59, 59, 500000, tzinfo=timezone.utc).timestamp()", lambda: datetime(1969, 12, 31, 23, 59, 59, 500000, tzinfo=timezone.utc).timestamp())
def lc(seed):
    return (seed * 6364136223846793005 + 1442695040888963407) & 0xFFFFFFFFFFFFFFFF
s = 12345
bad = []
h = 0
for i in range(2000):
    s = lc(s); y = 1 + (s >> 11) % 9999
    s = lc(s); mo = 1 + (s >> 11) % 12
    s = lc(s); d = 1 + (s >> 11) % 28
    s = lc(s); hh = (s >> 11) % 24
    s = lc(s); us = (s >> 11) % 1000000
    t = datetime(y, mo, d, hh, 7, 9, us, tzinfo=timezone.utc).timestamp()
    h = (h * 1000003 + sum(ord(c) * (k + 1) for k, c in enumerate(t.hex()))) & 0xFFFFFFFFFFFF
print('timestamps', h)
h = 0
for i in range(1500):
    s = lc(s); m = 1 + (s >> 11) % 100000
    s = lc(s); e = (s >> 11) % 8
    s = lc(s); n = 1 + (s >> 11) % 45
    v = Decimal(m).scaleb(-e)
    try:
        r = v ** n
        r = str(r) if 1 <= r < Decimal('1E+28') else 'out'
    except Exception as ex:
        r = 'out'
    h = (h * 1000003 + sum(ord(c) * (k + 1) for k, c in enumerate(r))) & 0xFFFFFFFFFFFF
    if i % 300 == 299:
        print('pow', i, h)
for prec, rnd in ((5, ROUND_UP), (9, ROUND_DOWN), (20, ROUND_UP)):
    getcontext().prec = prec
    getcontext().rounding = rnd
    print(Decimal('1.23456789') ** 7, Decimal('3') ** -3, Decimal('1.5') ** 13)
getcontext().prec = 28
getcontext().rounding = 'ROUND_HALF_EVEN'
import re, base64
def enc(s, *a):
    try:
        return s.encode(*a)
    except UnicodeEncodeError as e:
        return (e.encoding, e.start, e.end, e.reason, e.object == s, str(e))
show("re.fullmatch(r'(a)\\123', 'aS') is not None", lambda: re.fullmatch(r'(a)\123', 'aS') is not None)
show("re.fullmatch(r'(a)\\1', 'aa') is not None", lambda: re.fullmatch(r'(a)\1', 'aa') is not None)
show("re.fullmatch(r'(a)\\12', 'a\\n') is not None", lambda: re.fullmatch(r'(a)\12', 'a\n') is not None)
show("re.fullmatch(r'(a)\\18', 'aa8') is not None", lambda: re.fullmatch(r'(a)\18', 'aa8') is not None)
show("re.fullmatch(r'(a)\\1a', 'aaa') is not None", lambda: re.fullmatch(r'(a)\1a', 'aaa') is not None)
show("re.fullmatch(r'(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)\\10', 'abcdefghijj') is not None", lambda: re.fullmatch(r'(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)\10', 'abcdefghijj') is not None)
show("re.fullmatch(r'(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)\\10', 'abcdefghija0') is not None", lambda: re.fullmatch(r'(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)\10', 'abcdefghija0') is not None)
show("re.fullmatch(r'(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)\\1', 'abcdefghija') is not None", lambda: re.fullmatch(r'(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)\1', 'abcdefghija') is not None)
show("re.fullmatch(r'(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)\\11', 'abcdefghij\\t') is not None", lambda: re.fullmatch(r'(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)\11', 'abcdefghij\t') is not None)
show("re.fullmatch(r'\\101', 'A') is not None", lambda: re.fullmatch(r'\101', 'A') is not None)
show("re.fullmatch(r'\\0', '\\x00') is not None", lambda: re.fullmatch(r'\0', '\x00') is not None)
show("re.fullmatch(r'\\08', '\\x008') is not None", lambda: re.fullmatch(r'\08', '\x008') is not None)
show("re.compile(r'\\400')", lambda: re.compile(r'\400'))
show("re.compile(r'(a)\\2')", lambda: re.compile(r'(a)\2'))
show("re.compile(r'(a)\\10')", lambda: re.compile(r'(a)\10'))
show("re.compile(r'\\8')", lambda: re.compile(r'\8'))
show("re.compile(r'(a)\\128')", lambda: re.compile(r'(a)\128'))
show("re.sub(r'(a)\\141', 'X', 'aa')", lambda: re.sub(r'(a)\141', 'X', 'aa'))
show("re.findall(r'[\\w]', 'á²_ ١')", lambda: re.findall(r'[\w]', 'á²_ ١'))
show("re.findall(r'[\\W]', 'á²_ ١')", lambda: re.findall(r'[\W]', 'á²_ ١'))
show("re.findall(r'[^\\w]', 'á²_ ١')", lambda: re.findall(r'[^\w]', 'á²_ ١'))
show("re.findall(r'[^\\W]', 'á²_ ١')", lambda: re.findall(r'[^\W]', 'á²_ ١'))
show("re.findall(r'[\\s]', ' \\t\\x1c\\x1d\\x1e\\x1f\\x85\\xa0a')", lambda: re.findall(r'[\s]', ' \t\x1c\x1d\x1e\x1f\x85\xa0a'))
show("re.findall(r'[\\S]', ' \\t\\x1c\\x1f\\x85\\xa0a')", lambda: re.findall(r'[\S]', ' \t\x1c\x1f\x85\xa0a'))
show("re.findall(r'[^\\s]', ' \\x1c\\x1fa')", lambda: re.findall(r'[^\s]', ' \x1c\x1fa'))
show("re.findall(r'[^\\S]', ' \\x1c\\x1fa')", lambda: re.findall(r'[^\S]', ' \x1c\x1fa'))
show("re.findall(r'[\\w-]', 'a-b c')", lambda: re.findall(r'[\w-]', 'a-b c'))
show("re.findall(r'[\\w\\s]', 'a \\x1cb́-')", lambda: re.findall(r'[\w\s]', 'a \x1cb́-'))
show("re.findall(r'[\\W\\d]', 'a1 -')", lambda: re.findall(r'[\W\d]', 'a1 -'))
show("re.findall(r'[^\\W\\d]', 'a1 -_')", lambda: re.findall(r'[^\W\d]', 'a1 -_'))
show("re.findall(r'[\\w]', 'á²', re.A)", lambda: re.findall(r'[\w]', 'á²', re.A))
show("re.findall(r'[\\d]', '1١a')", lambda: re.findall(r'[\d]', '1١a'))
show("re.findall(r'[\\w]+', 'ABC abc', re.I)", lambda: re.findall(r'[\w]+', 'ABC abc', re.I))
show("re.findall(r'(?a:[\\w])[\\w]', 'a²')", lambda: re.findall(r'(?a:[\w])[\w]', 'a²'))
show("re.findall(r'[\\D]', '1١a')", lambda: re.findall(r'[\D]', '1١a'))
show("enc('a\\xe9\\xe9\\xe9b', 'ascii')", lambda: enc('a\xe9\xe9\xe9b', 'ascii'))
show("enc('a\\xe9\\xe9\\xe9b', 'ascii', 'replace')", lambda: enc('a\xe9\xe9\xe9b', 'ascii', 'replace'))
show("enc('a\\xe9\\xe9\\xe9b', 'ascii', 'ignore')", lambda: enc('a\xe9\xe9\xe9b', 'ascii', 'ignore'))
show("enc('a\\xe9\\xe9\\xe9b', 'ascii', 'backslashreplace')", lambda: enc('a\xe9\xe9\xe9b', 'ascii', 'backslashreplace'))
show("enc('a\\xe9\\xe9\\xe9b', 'ascii', 'xmlcharrefreplace')", lambda: enc('a\xe9\xe9\xe9b', 'ascii', 'xmlcharrefreplace'))
show("enc('a\\xe9\\xe9\\xe9b', 'ascii', 'namereplace')", lambda: enc('a\xe9\xe9\xe9b', 'ascii', 'namereplace'))
show("enc('aĀāb', 'latin-1')", lambda: enc('aĀāb', 'latin-1'))
show("enc('aĀāb', 'latin-1', 'replace')", lambda: enc('aĀāb', 'latin-1', 'replace'))
show("enc('a\\u20ac\\xe9b', 'ascii')", lambda: enc('a\u20ac\xe9b', 'ascii'))
show("enc('a\\U0001F600\\xe9b', 'ascii', 'replace')", lambda: enc('a\U0001F600\xe9b', 'ascii', 'replace'))
show("enc('a\\U0001F600\\xe9b', 'ascii', 'backslashreplace')", lambda: enc('a\U0001F600\xe9b', 'ascii', 'backslashreplace'))
show("enc('aЖЗb', 'cp1252')", lambda: enc('aЖЗb', 'cp1252'))
show("enc('aЖЗb', 'cp1252', 'replace')", lambda: enc('aЖЗb', 'cp1252', 'replace'))
show("enc('a\\ud800\\ud801b', 'utf-8')", lambda: enc('a\ud800\ud801b', 'utf-8'))
show("enc('a\\ud800\\ud801b', 'utf-8', 'replace')", lambda: enc('a\ud800\ud801b', 'utf-8', 'replace'))
show("enc('a\\ud800\\ud801b', 'utf-8', 'ignore')", lambda: enc('a\ud800\ud801b', 'utf-8', 'ignore'))
show("enc('a\\ud800\\ud801b', 'utf-8', 'backslashreplace')", lambda: enc('a\ud800\ud801b', 'utf-8', 'backslashreplace'))
show("enc('a\\ud800\\ud801b', 'utf-16-le')", lambda: enc('a\ud800\ud801b', 'utf-16-le'))
show("enc('\\xe9', 'ascii')", lambda: enc('\xe9', 'ascii'))
show("'abc'.rfind('', 5)", lambda: 'abc'.rfind('', 5))
show("'abc'.rfind('', 2, 1)", lambda: 'abc'.rfind('', 2, 1))
show("'abc'.rfind('', 3)", lambda: 'abc'.rfind('', 3))
show("'abc'.rfind('', 1)", lambda: 'abc'.rfind('', 1))
show("'abc'.rfind('', 1, 2)", lambda: 'abc'.rfind('', 1, 2))
show("'abc'.rindex('', 5)", lambda: 'abc'.rindex('', 5))
show("'abc'.find('', 5)", lambda: 'abc'.find('', 5))
show("'abc'.find('', 3)", lambda: 'abc'.find('', 3))
show("'abc'.find('', 2, 1)", lambda: 'abc'.find('', 2, 1))
show("'abc'.index('', 5)", lambda: 'abc'.index('', 5))
show("b'abc'.rfind(b'', 5)", lambda: b'abc'.rfind(b'', 5))
show("'\\U0001F601'.strip('\\U0001F600')", lambda: '\U0001F601'.strip('\U0001F600'))
show("'\\U0001F600a\\U0001F600'.strip('\\U0001F600')", lambda: '\U0001F600a\U0001F600'.strip('\U0001F600'))
show("'\\U0001F600a\\U0001F600'.lstrip('\\U0001F600')", lambda: '\U0001F600a\U0001F600'.lstrip('\U0001F600'))
show("'\\U0001F600a\\U0001F600'.rstrip('\\U0001F600')", lambda: '\U0001F600a\U0001F600'.rstrip('\U0001F600'))
show("'\\U0001F600\\U0001F601a'.lstrip('\\U0001F601\\U0001F600')", lambda: '\U0001F600\U0001F601a'.lstrip('\U0001F601\U0001F600'))
show("'xa\\U0001F600'.strip('x\\U0001F600')", lambda: 'xa\U0001F600'.strip('x\U0001F600'))
show("'\\U0001F600'.strip('\\U0001F600')", lambda: '\U0001F600'.strip('\U0001F600'))
show("'abc'.strip('\\U0001F600')", lambda: 'abc'.strip('\U0001F600'))
show("'\\ud83d'.strip('\\U0001F600')", lambda: '\ud83d'.strip('\U0001F600'))
show("'\\U00010428'.islower()", lambda: '\U00010428'.islower())
show("'\\U00010400'.isupper()", lambda: '\U00010400'.isupper())
show("'\\U00010400'.istitle()", lambda: '\U00010400'.istitle())
show("'\\U00010428'.isupper()", lambda: '\U00010428'.isupper())
show("'\\U0001E900'.isupper()", lambda: '\U0001E900'.isupper())
show("'\\U0001E922'.islower()", lambda: '\U0001E922'.islower())
show("'\\U00010D50'.isupper()", lambda: '\U00010D50'.isupper())
show("'\\U00010D70'.islower()", lambda: '\U00010D70'.islower())
show("'\\U0001F130'.isupper()", lambda: '\U0001F130'.isupper())
show("'\\U0001F130'.isalpha()", lambda: '\U0001F130'.isalpha())
show("'\\U0001D400'.isupper()", lambda: '\U0001D400'.isupper())
show("'\\U0001DF00'.islower()", lambda: '\U0001DF00'.islower())
show("'\\U0001E030'.islower()", lambda: '\U0001E030'.islower())
show("'\\U00016E40'.isupper()", lambda: '\U00016E40'.isupper())
show("'\\U00010C80a'.istitle()", lambda: '\U00010C80a'.istitle())
show("'\\U00010C80\\U00010CC0'.istitle()", lambda: '\U00010C80\U00010CC0'.istitle())
show("'\\U00010C80\\U00010CC0'.isupper()", lambda: '\U00010C80\U00010CC0'.isupper())
show("'a\\U0001E922'.islower()", lambda: 'a\U0001E922'.islower())
show("'A\\U0001E922'.islower()", lambda: 'A\U0001E922'.islower())
show("'\\U0001F600'.isupper()", lambda: '\U0001F600'.isupper())
show("'\\U0001F600'.islower()", lambda: '\U0001F600'.islower())
show("'\\U00010428'.upper()", lambda: '\U00010428'.upper())
show("'\\U00010400'.lower()", lambda: '\U00010400'.lower())
show("'\\U0001E900'.lower()", lambda: '\U0001E900'.lower())
show("'\\U00010D50'.lower()", lambda: '\U00010D50'.lower())
show("base64.b32decode(b'MFRGG===')", lambda: base64.b32decode(b'MFRGG==='))
show("base64.b32decode(b'mfrgg===')", lambda: base64.b32decode(b'mfrgg==='))
show("base64.b32decode(b'mfrgg===', True)", lambda: base64.b32decode(b'mfrgg===', True))
show("base64.b32decode(b'mfrgg===', casefold=True)", lambda: base64.b32decode(b'mfrgg===', casefold=True))
show("base64.b32decode(b'MFRGG')", lambda: base64.b32decode(b'MFRGG'))
show("base64.b32decode(b'MFRGG==')", lambda: base64.b32decode(b'MFRGG=='))
show("base64.b32decode(b'MF=GG===')", lambda: base64.b32decode(b'MF=GG==='))
show("base64.b32decode(b'MFRGG=A=')", lambda: base64.b32decode(b'MFRGG=A='))
show("base64.b32decode(b'MFRGGZDF')", lambda: base64.b32decode(b'MFRGGZDF'))
show("base64.b32decode(b'MFRGGZA=')", lambda: base64.b32decode(b'MFRGGZA='))
show("base64.b32decode(b'MFRGGZDFMY======')", lambda: base64.b32decode(b'MFRGGZDFMY======'))
show("base64.b32decode(b'MFRA====')", lambda: base64.b32decode(b'MFRA===='))
show("base64.b32decode(b'ME======')", lambda: base64.b32decode(b'ME======'))
show("base64.b32decode(b'M=======')", lambda: base64.b32decode(b'M======='))
show("base64.b32decode(b'========')", lambda: base64.b32decode(b'========'))
show("base64.b32decode(b'MF RGG==')", lambda: base64.b32decode(b'MF RGG=='))
show("base64.b32decode(b'')", lambda: base64.b32decode(b''))
show("base64.b32decode('MFRGG===')", lambda: base64.b32decode('MFRGG==='))
show("base64.b32decode(b'0O======', map01=b'L')", lambda: base64.b32decode(b'0O======', map01=b'L'))
show("base64.b32decode(b'M1======', map01=b'L')", lambda: base64.b32decode(b'M1======', map01=b'L'))
show("base64.b32decode(b'M1======', map01=b'LL')", lambda: base64.b32decode(b'M1======', map01=b'LL'))
show("base64.b32decode(b'M1======')", lambda: base64.b32decode(b'M1======'))
show("base64.b32decode(b'M\\xc9======')", lambda: base64.b32decode(b'M\xc9======'))
show("bytes.fromhex('41 42')", lambda: bytes.fromhex('41 42'))
show("bytes.fromhex('41\\t42\\n43\\r44\\x0b45\\x0c46')", lambda: bytes.fromhex('41\t42\n43\r44\x0b45\x0c46'))
show("bytes.fromhex(' 41 ')", lambda: bytes.fromhex(' 41 '))
show("bytes.fromhex('4 1')", lambda: bytes.fromhex('4 1'))
show("bytes.fromhex('4')", lambda: bytes.fromhex('4'))
show("bytes.fromhex('414')", lambda: bytes.fromhex('414'))
show("bytes.fromhex('41 4')", lambda: bytes.fromhex('41 4'))
show("bytes.fromhex('4g')", lambda: bytes.fromhex('4g'))
show("bytes.fromhex('g4')", lambda: bytes.fromhex('g4'))
show("bytes.fromhex('41\\xa042')", lambda: bytes.fromhex('41\xa042'))
show("bytes.fromhex('41 4')", lambda: bytes.fromhex('41 4'))
show("bytes.fromhex('')", lambda: bytes.fromhex(''))
show("bytes.fromhex(1)", lambda: bytes.fromhex(1))
import xml.etree.ElementTree as ET
import statistics
from urllib.parse import urlparse, urlsplit, urldefrag, ParseResult, SplitResult, DefragResult
from collections import OrderedDict, defaultdict, Counter
from functools import lru_cache
def s(e, **k):
    return ET.tostring(e, encoding='unicode', **k)
def el(tag, text=None, tail=None, **a):
    e = ET.Element(tag, a)
    e.text = text
    e.tail = tail
    return e
e1 = ET.Element('r')
ET.SubElement(e1, 'a')
ET.SubElement(e1, 'b')
ET.SubElement(e1, 'c')
def tags(e):
    return [c.tag for c in e]
def setsl(e, i, v):
    e[i] = v
    return tags(e)
def delsl(e, i):
    del e[i]
    return tags(e)
def mk(n=4):
    e = ET.Element('r')
    for i in range(n):
        ET.SubElement(e, 'c' + str(i))
    return e
def ctxt(f):
    try:
        try:
            1 / 0
        except ZeroDivisionError:
            f()
    except Exception as e:
        return (type(e).__name__, type(e.__context__).__name__, str(e.__context__), type(e.__cause__).__name__, e.__suppress_context__)
class Bad:
    n = 0
    def __hash__(self):
        return 1
    def __eq__(self, o):
        Bad.n += 1
        if Bad.n > 1:
            raise ValueError('eq')
        return self is o
def badmove():
    Bad.n = -1000
    a, b = Bad(), Bad()
    d = OrderedDict()
    d[a] = 1
    d[b] = 2
    Bad.n = -1000
    try:
        Bad.n = 1
        d.move_to_end(b)
    except ValueError as e:
        pass
    Bad.n = -1000
    return len(d)
def nocontext():
    try:
        raise KeyError('a')
    except KeyError:
        pass
    int('x')
def nested():
    try:
        {}['k']
    except KeyError:
        [][1]
def inhandler():
    try:
        1 / 0
    except ZeroDivisionError:
        try:
            int('x')
        except ValueError as e:
            return (str(e.__context__), type(e.__context__).__name__)
fdef = defaultdict(list, a=[1])
@lru_cache(maxsize=2)
def lc(x):
    return x * 2
show("s(el('x', ''))", lambda: s(el('x', '')))
show("s(el('x', 'a'))", lambda: s(el('x', 'a')))
show("s(el('x'))", lambda: s(el('x')))
show("s(el('x', '', ''))", lambda: s(el('x', '', '')))
show("s(el('x', None, 't'))", lambda: s(el('x', None, 't')))
show("s(el('x', '', short=''), short_empty_elements=False)", lambda: s(el('x', '', short=''), short_empty_elements=False))
show("s(el('x', ''), method='html')", lambda: s(el('x', ''), method='html'))
show("s(el('x'), method='html')", lambda: s(el('x'), method='html'))
show("s(el('br'), method='html')", lambda: s(el('br'), method='html'))
show("s(el('BR'), method='html')", lambda: s(el('BR'), method='html'))
show("s(el('br', 'in', 'tl'), method='html')", lambda: s(el('br', 'in', 'tl'), method='html'))
show("s(el('script', '<a&>'), method='html')", lambda: s(el('script', '<a&>'), method='html'))
show("s(el('STYLE', '<a&>'), method='html')", lambda: s(el('STYLE', '<a&>'), method='html'))
show("s(el('p', '<a&>'), method='html')", lambda: s(el('p', '<a&>'), method='html'))
show("s(el('p', a='<&>\"\\n\\t'), method='html')", lambda: s(el('p', a='<&>"\n\t'), method='html'))
show("s(el('p', a='<&>\"\\n\\t'))", lambda: s(el('p', a='<&>"\n\t')))
show("s(el('{u}br'), method='html')", lambda: s(el('{u}br'), method='html'))
show("s(el('{u}script', '<'), method='html')", lambda: s(el('{u}script', '<'), method='html'))
show("s(ET.Comment('c<&'))", lambda: s(ET.Comment('c<&')))
show("s(ET.Comment('c<&'), method='html')", lambda: s(ET.Comment('c<&'), method='html'))
show("s(ET.Comment())", lambda: s(ET.Comment()))
show("s(ET.ProcessingInstruction('t', 'v<'))", lambda: s(ET.ProcessingInstruction('t', 'v<')))
show("s(ET.ProcessingInstruction('t', 'v<'), method='html')", lambda: s(ET.ProcessingInstruction('t', 'v<'), method='html'))
show("s(el(None, 'te<xt', 'tl'))", lambda: s(el(None, 'te<xt', 'tl')))
show("ET.tostring(ET.Comment())", lambda: ET.tostring(ET.Comment()))
show("s(el('{http://www.w3.org/XML/1998/namespace}lang'))", lambda: s(el('{http://www.w3.org/XML/1998/namespace}lang')))
show("s(el('{http://www.w3.org/1999/xhtml}p'))", lambda: s(el('{http://www.w3.org/1999/xhtml}p')))
show("s(el('{http://www.w3.org/2001/XMLSchema}e'))", lambda: s(el('{http://www.w3.org/2001/XMLSchema}e')))
show("s(el('{http://purl.org/dc/elements/1.1/}t'))", lambda: s(el('{http://purl.org/dc/elements/1.1/}t')))
show("s(el('{u1}t', **{'{u2}a': '1', '{u1}b': '2', '{http://www.w3.org/2001/XMLSchema-instance}c': '3', '{u3}d': '4'}))", lambda: s(el('{u1}t', **{'{u2}a': '1', '{u1}b': '2', '{http://www.w3.org/2001/XMLSchema-instance}c': '3', '{u3}d': '4'})))
show("s(el('a', **{'{u9}a': '1'}))", lambda: s(el('a', **{'{u9}a': '1'})))
show("ET.register_namespace('xmlfoo', 'http://x/')", lambda: ET.register_namespace('xmlfoo', 'http://x/'))
show("ET.register_namespace('ns0', 'http://x/')", lambda: ET.register_namespace('ns0', 'http://x/'))
show("ET.register_namespace('ns12', 'http://x/')", lambda: ET.register_namespace('ns12', 'http://x/'))
show("ET.register_namespace('ns', 'http://y/')", lambda: ET.register_namespace('ns', 'http://y/'))
show("ET.register_namespace('nsa', 'http://z/')", lambda: ET.register_namespace('nsa', 'http://z/'))
show("s(el('{http://x/}a'))", lambda: s(el('{http://x/}a')))
show("s(el('{http://y/}a'))", lambda: s(el('{http://y/}a')))
show("s(el('{http://z/}a'))", lambda: s(el('{http://z/}a')))
show("ET.register_namespace('xs', 'http://k/')", lambda: ET.register_namespace('xs', 'http://k/'))
show("s(el('{http://k/}a'))", lambda: s(el('{http://k/}a')))
show("s(el('{http://www.w3.org/2001/XMLSchema}a'))", lambda: s(el('{http://www.w3.org/2001/XMLSchema}a')))
show("ET.register_namespace('', 'http://dflt/')", lambda: ET.register_namespace('', 'http://dflt/'))
show("s(el('{http://dflt/}a', **{'{u}k': 'v'}))", lambda: s(el('{http://dflt/}a', **{'{u}k': 'v'})))
show("s(el('{http://other/}a', **{'{u}k': 'v'}))", lambda: s(el('{http://other/}a', **{'{u}k': 'v'})))
show("s(el('x', 5))", lambda: s(el('x', 5)))
show("s(el(5))", lambda: s(el(5)))
show("s(el('x', **{'a': 5}))", lambda: s(el('x', **{'a': 5})))
show("e1.get('zz', default='d')", lambda: e1.get('zz', default='d'))
show("e1.get('zz', 'd')", lambda: e1.get('zz', 'd'))
show("e1.get('zz')", lambda: e1.get('zz'))
show("e1.get(key='zz')", lambda: e1.get(key='zz'))
show("e1.get(key='zz', default=1)", lambda: e1.get(key='zz', default=1))
show("e1.get()", lambda: e1.get())
show("setsl(mk(), slice(0, 2), [ET.Element('n')])", lambda: setsl(mk(), slice(0, 2), [ET.Element('n')]))
show("setsl(mk(), slice(1, 1), [ET.Element('n'), ET.Element('m')])", lambda: setsl(mk(), slice(1, 1), [ET.Element('n'), ET.Element('m')]))
show("setsl(mk(), slice(None, None, 2), [ET.Element('n'), ET.Element('m')])", lambda: setsl(mk(), slice(None, None, 2), [ET.Element('n'), ET.Element('m')]))
show("setsl(mk(), slice(None, None, -1), [ET.Element('n'), ET.Element('m'), ET.Element('k'), ET.Element('j')])", lambda: setsl(mk(), slice(None, None, -1), [ET.Element('n'), ET.Element('m'), ET.Element('k'), ET.Element('j')]))
show("setsl(mk(), slice(None, None, 2), [ET.Element('n')])", lambda: setsl(mk(), slice(None, None, 2), [ET.Element('n')]))
show("setsl(mk(), slice(0, 2), 5)", lambda: setsl(mk(), slice(0, 2), 5))
show("setsl(mk(), slice(0, 2), [5])", lambda: setsl(mk(), slice(0, 2), [5]))
show("setsl(mk(), slice(0, 2), (ET.Element('t'),))", lambda: setsl(mk(), slice(0, 2), (ET.Element('t'),)))
show("delsl(mk(), slice(0, 2))", lambda: delsl(mk(), slice(0, 2)))
show("delsl(mk(), slice(None, None, 2))", lambda: delsl(mk(), slice(None, None, 2)))
show("delsl(mk(), slice(None, None, -1))", lambda: delsl(mk(), slice(None, None, -1)))
show("delsl(mk(), slice(5, 9))", lambda: delsl(mk(), slice(5, 9)))
show("delsl(mk(), 1)", lambda: delsl(mk(), 1))
show("delsl(mk(), 9)", lambda: delsl(mk(), 9))
show("(lambda e: (e.extend(e), tags(e))[1])(mk(2))", lambda: (lambda e: (e.extend(e), tags(e))[1])(mk(2)))
show("(lambda e: (e.extend([ET.Element('z')]), tags(e))[1])(mk(1))", lambda: (lambda e: (e.extend([ET.Element('z')]), tags(e))[1])(mk(1)))
show("mk(1).extend([1])", lambda: mk(1).extend([1]))
show("mk(1).extend(1)", lambda: mk(1).extend(1))
show("type(statistics.linear_regression([1, 2, 3], [2, 4, 7])) is type(statistics.linear_regression([1, 2, 4], [2, 4, 7]))", lambda: type(statistics.linear_regression([1, 2, 3], [2, 4, 7])) is type(statistics.linear_regression([1, 2, 4], [2, 4, 7])))
show("type(statistics.linear_regression([1, 2, 3], [2, 4, 7])).__name__", lambda: type(statistics.linear_regression([1, 2, 3], [2, 4, 7])).__name__)
show("statistics.linear_regression([1, 2, 3], [2, 4, 7])", lambda: statistics.linear_regression([1, 2, 3], [2, 4, 7]))
show("statistics.LinearRegression(1, 2)", lambda: statistics.LinearRegression(1, 2))
show("statistics.LinearRegression._fields", lambda: statistics.LinearRegression._fields)
show("type(statistics.linear_regression([1, 2, 3], [2, 4, 7])) is statistics.LinearRegression", lambda: type(statistics.linear_regression([1, 2, 3], [2, 4, 7])) is statistics.LinearRegression)
show("type(urlparse('http://a/b')) is ParseResult", lambda: type(urlparse('http://a/b')) is ParseResult)
show("repr(ParseResult)", lambda: repr(ParseResult))
show("ParseResult.__name__", lambda: ParseResult.__name__)
show("repr(type(urlparse('http://a/b')))", lambda: repr(type(urlparse('http://a/b'))))
show("type(urlparse('http://a/b')).__name__", lambda: type(urlparse('http://a/b')).__name__)
show("repr(statistics.LinearRegression)", lambda: repr(statistics.LinearRegression))
show("statistics.LinearRegression.__name__", lambda: statistics.LinearRegression.__name__)
show("ParseResult('a', 'b', 'c', 'd', 'e', 'f').geturl()", lambda: ParseResult('a', 'b', 'c', 'd', 'e', 'f').geturl())
show("type(urlsplit('http://a/b')) is SplitResult", lambda: type(urlsplit('http://a/b')) is SplitResult)
show("type(urldefrag('http://a/b#c')) is DefragResult", lambda: type(urldefrag('http://a/b#c')) is DefragResult)
show("type(urlparse('http://a/')) is type(urlparse('http://b/'))", lambda: type(urlparse('http://a/')) is type(urlparse('http://b/')))
show("type(OrderedDict(a=1) | {'b': 2}).__name__", lambda: type(OrderedDict(a=1) | {'b': 2}).__name__)
show("OrderedDict(a=1) | {'b': 2}", lambda: OrderedDict(a=1) | {'b': 2})
show("{'b': 2} | OrderedDict(a=1)", lambda: {'b': 2} | OrderedDict(a=1))
show("type({'b': 2} | OrderedDict(a=1)).__name__", lambda: type({'b': 2} | OrderedDict(a=1)).__name__)
show("(defaultdict(int, a=1) | {'b': 2})", lambda: (defaultdict(int, a=1) | {'b': 2}))
show("type(defaultdict(int, a=1) | {'b': 2}).__name__", lambda: type(defaultdict(int, a=1) | {'b': 2}).__name__)
show("({'b': 2} | defaultdict(int, a=1))", lambda: ({'b': 2} | defaultdict(int, a=1)))
show("({'b': 2} | defaultdict(int, a=1))['zz']", lambda: ({'b': 2} | defaultdict(int, a=1))['zz'])
show("(defaultdict(list, a=1) | {'b': 2}).default_factory", lambda: (defaultdict(list, a=1) | {'b': 2}).default_factory)
show("OrderedDict(a=1) | defaultdict(int, b=2)", lambda: OrderedDict(a=1) | defaultdict(int, b=2))
show("defaultdict(int, b=2) | OrderedDict(a=1)", lambda: defaultdict(int, b=2) | OrderedDict(a=1))
show("OrderedDict(a=1) | OrderedDict(b=2)", lambda: OrderedDict(a=1) | OrderedDict(b=2))
show("OrderedDict(a=1) | 5", lambda: OrderedDict(a=1) | 5)
show("{} | OrderedDict()", lambda: {} | OrderedDict())
show("Counter(a=1) | Counter(a=2)", lambda: Counter(a=1) | Counter(a=2))
show("OrderedDict(a=1) | Counter(b=2)", lambda: OrderedDict(a=1) | Counter(b=2))
show("{'a': 1} | {'b': 2}", lambda: {'a': 1} | {'b': 2})
show("badmove()", lambda: badmove())
show("(lambda d: (d.move_to_end('a'), list(d))[1])(OrderedDict(a=1, b=2, c=3))", lambda: (lambda d: (d.move_to_end('a'), list(d))[1])(OrderedDict(a=1, b=2, c=3)))
show("(lambda d: (d.move_to_end('c'), list(d))[1])(OrderedDict(a=1, b=2, c=3))", lambda: (lambda d: (d.move_to_end('c'), list(d))[1])(OrderedDict(a=1, b=2, c=3)))
show("(lambda d: (d.move_to_end('b', last=False), list(d))[1])(OrderedDict(a=1, b=2, c=3))", lambda: (lambda d: (d.move_to_end('b', last=False), list(d))[1])(OrderedDict(a=1, b=2, c=3)))
show("(lambda d: (d.move_to_end(1.0), list(d))[1])(OrderedDict([(1, 'x'), (2, 'y')]))", lambda: (lambda d: (d.move_to_end(1.0), list(d))[1])(OrderedDict([(1, 'x'), (2, 'y')])))
show("OrderedDict(a=1).move_to_end('z')", lambda: OrderedDict(a=1).move_to_end('z'))
show("(lambda d: ([d.move_to_end('a') for i in range(100)], d.popitem(), list(d))[1:])(OrderedDict(a=1, b=2))", lambda: (lambda d: ([d.move_to_end('a') for i in range(100)], d.popitem(), list(d))[1:])(OrderedDict(a=1, b=2)))
show("(lc(1), lc(2), lc(1), lc(3), lc(2), lc.cache_info())", lambda: (lc(1), lc(2), lc(1), lc(3), lc(2), lc.cache_info()))
show("ctxt(lambda: int('x'))", lambda: ctxt(lambda: int('x')))
show("ctxt(lambda: {}['k'])", lambda: ctxt(lambda: {}['k']))
show("ctxt(lambda: [][1])", lambda: ctxt(lambda: [][1]))
show("ctxt(lambda: 1 + 'a')", lambda: ctxt(lambda: 1 + 'a'))
show("ctxt(lambda: None.x)", lambda: ctxt(lambda: None.x))
show("ctxt(nested)", lambda: ctxt(nested))
show("ctxt(nocontext)", lambda: ctxt(nocontext))
show("inhandler()", lambda: inhandler())
import statistics as st, operator as op, html, textwrap, functools, copy, secrets, string, collections, math, random, struct, binascii, gzip, uuid, traceback, io, itertools, zipfile, decimal, dataclasses
import datetime as dt
import urllib.parse as up
import xml.etree.ElementTree as ET
def refused(f):
    try:
        f()
        return 'accepted'
    except TypeError:
        return 'TypeError'
    except Exception as e:
        return type(e).__name__
ns = uuid.NAMESPACE_DNS
@dataclasses.dataclass
class Dc:
    a: int = 1
show("list(map(str, [1, 2], strict=True))", lambda: list(map(str, [1, 2], strict=True)))
show("list(map(pow, [1, 2], [3, 4], strict=True))", lambda: list(map(pow, [1, 2], [3, 4], strict=True)))
show("list(map(pow, [1, 2], [3], strict=True))", lambda: list(map(pow, [1, 2], [3], strict=True)))
show("list(map(pow, [1], [3, 4], strict=True))", lambda: list(map(pow, [1], [3, 4], strict=True)))
show("list(map(pow, [1, 2], [3, 4], [5], strict=True))", lambda: list(map(pow, [1, 2], [3, 4], [5], strict=True)))
show("list(map(pow, [1, 2], [3, 4], [5, 6, 7], strict=True))", lambda: list(map(pow, [1, 2], [3, 4], [5, 6, 7], strict=True)))
show("list(map(pow, [1, 2], [3], strict=False))", lambda: list(map(pow, [1, 2], [3], strict=False)))
show("list(map(str, [1], zzz=1))", lambda: list(map(str, [1], zzz=1)))
show("map()", lambda: map())
show("map(str)", lambda: map(str))
show("map(str, strict=True)", lambda: map(str, strict=True))
show("list(zip([1, 2], [3], strict=True))", lambda: list(zip([1, 2], [3], strict=True)))
show("refused(lambda: st.fmean([1], zzz=1))", lambda: refused(lambda: st.fmean([1], zzz=1)))
show("refused(lambda: st.harmonic_mean([1], zzz=1))", lambda: refused(lambda: st.harmonic_mean([1], zzz=1)))
show("refused(lambda: st.correlation([1, 2, 3], [1, 2, 4], zzz=1))", lambda: refused(lambda: st.correlation([1, 2, 3], [1, 2, 4], zzz=1)))
show("refused(lambda: st.linear_regression([1, 2, 3], [1, 2, 4], zzz=1))", lambda: refused(lambda: st.linear_regression([1, 2, 3], [1, 2, 4], zzz=1)))
show("refused(lambda: op.add(1, 2, zzz=1))", lambda: refused(lambda: op.add(1, 2, zzz=1)))
show("refused(lambda: op.neg(1, zzz=1))", lambda: refused(lambda: op.neg(1, zzz=1)))
show("refused(lambda: op.lt(1, 2, zzz=1))", lambda: refused(lambda: op.lt(1, 2, zzz=1)))
show("refused(lambda: op.itemgetter(1, zzz=1))", lambda: refused(lambda: op.itemgetter(1, zzz=1)))
show("refused(lambda: op.attrgetter('a', zzz=1))", lambda: refused(lambda: op.attrgetter('a', zzz=1)))
show("refused(lambda: html.escape('a', zzz=1))", lambda: refused(lambda: html.escape('a', zzz=1)))
show("refused(lambda: html.unescape('a', zzz=1))", lambda: refused(lambda: html.unescape('a', zzz=1)))
show("refused(lambda: textwrap.indent('a', '>', zzz=1))", lambda: refused(lambda: textwrap.indent('a', '>', zzz=1)))
show("refused(lambda: textwrap.dedent('a', zzz=1))", lambda: refused(lambda: textwrap.dedent('a', zzz=1)))
show("refused(lambda: functools.cmp_to_key(lambda a, b: 0, zzz=1))", lambda: refused(lambda: functools.cmp_to_key(lambda a, b: 0, zzz=1)))
show("refused(lambda: functools.lru_cache(1, zzz=1))", lambda: refused(lambda: functools.lru_cache(1, zzz=1)))
show("refused(lambda: copy.copy([1], zzz=1))", lambda: refused(lambda: copy.copy([1], zzz=1)))
show("refused(lambda: secrets.token_hex(1, zzz=1))", lambda: refused(lambda: secrets.token_hex(1, zzz=1)))
show("refused(lambda: string.capwords('a', zzz=1))", lambda: refused(lambda: string.capwords('a', zzz=1)))
show("refused(lambda: collections.namedtuple('P', 'a', zzz=1))", lambda: refused(lambda: collections.namedtuple('P', 'a', zzz=1)))
show("refused(lambda: collections.deque([1], zzz=1))", lambda: refused(lambda: collections.deque([1], zzz=1)))
show("refused(lambda: collections.ChainMap({}, zzz=1))", lambda: refused(lambda: collections.ChainMap({}, zzz=1)))
show("refused(lambda: collections.OrderedDict.fromkeys([1], zzz=1))", lambda: refused(lambda: collections.OrderedDict.fromkeys([1], zzz=1)))
show("refused(lambda: up.quote('a', zzz=1))", lambda: refused(lambda: up.quote('a', zzz=1)))
show("refused(lambda: up.unquote('a', zzz=1))", lambda: refused(lambda: up.unquote('a', zzz=1)))
show("refused(lambda: up.urlencode({}, zzz=1))", lambda: refused(lambda: up.urlencode({}, zzz=1)))
show("refused(lambda: up.urlparse('a', zzz=1))", lambda: refused(lambda: up.urlparse('a', zzz=1)))
show("refused(lambda: up.urljoin('a', 'b', zzz=1))", lambda: refused(lambda: up.urljoin('a', 'b', zzz=1)))
show("refused(lambda: up.urldefrag('a', zzz=1))", lambda: refused(lambda: up.urldefrag('a', zzz=1)))
show("refused(lambda: up.parse_qs('a', zzz=1))", lambda: refused(lambda: up.parse_qs('a', zzz=1)))
show("refused(lambda: ET.fromstring('<a/>', zzz=1))", lambda: refused(lambda: ET.fromstring('<a/>', zzz=1)))
show("refused(lambda: ET.indent(ET.Element('a'), zzz=1))", lambda: refused(lambda: ET.indent(ET.Element('a'), zzz=1)))
show("refused(lambda: ET.iselement(1, zzz=1))", lambda: refused(lambda: ET.iselement(1, zzz=1)))
show("refused(lambda: ET.register_namespace('a', 'b', zzz=1))", lambda: refused(lambda: ET.register_namespace('a', 'b', zzz=1)))
show("refused(lambda: ET.XMLID('<a/>', zzz=1))", lambda: refused(lambda: ET.XMLID('<a/>', zzz=1)))
show("refused(lambda: ET.Comment('a', zzz=1))", lambda: refused(lambda: ET.Comment('a', zzz=1)))
show("refused(lambda: ET.parse(io.StringIO('<a/>'), zzz=1))", lambda: refused(lambda: ET.parse(io.StringIO('<a/>'), zzz=1)))
show("refused(lambda: dataclasses.is_dataclass(1, zzz=1))", lambda: refused(lambda: dataclasses.is_dataclass(1, zzz=1)))
show("refused(lambda: decimal.Decimal('1', zzz=1))", lambda: refused(lambda: decimal.Decimal('1', zzz=1)))
show("refused(lambda: math.isclose(1, 1, zzz=1))", lambda: refused(lambda: math.isclose(1, 1, zzz=1)))
show("refused(lambda: math.prod([1], zzz=1))", lambda: refused(lambda: math.prod([1], zzz=1)))
show("refused(lambda: math.nextafter(1.0, 2.0, zzz=1))", lambda: refused(lambda: math.nextafter(1.0, 2.0, zzz=1)))
show("refused(lambda: random.choices([1], zzz=1))", lambda: refused(lambda: random.choices([1], zzz=1)))
show("refused(lambda: struct.unpack_from('b', b'a', zzz=1))", lambda: refused(lambda: struct.unpack_from('b', b'a', zzz=1)))
show("refused(lambda: binascii.b2a_base64(b'a', zzz=1))", lambda: refused(lambda: binascii.b2a_base64(b'a', zzz=1)))
show("refused(lambda: gzip.decompress(gzip.compress(b'a'), zzz=1))", lambda: refused(lambda: gzip.decompress(gzip.compress(b'a'), zzz=1)))
show("refused(lambda: uuid.uuid4(zzz=1))", lambda: refused(lambda: uuid.uuid4(zzz=1)))
show("refused(lambda: uuid.uuid5(ns, 'a', zzz=1))", lambda: refused(lambda: uuid.uuid5(ns, 'a', zzz=1)))
show("refused(lambda: uuid.uuid1(zzz=1))", lambda: refused(lambda: uuid.uuid1(zzz=1)))
show("refused(lambda: uuid.UUID(int=1, zzz=1))", lambda: refused(lambda: uuid.UUID(int=1, zzz=1)))
show("refused(lambda: traceback.format_exc(zzz=1))", lambda: refused(lambda: traceback.format_exc(zzz=1)))
show("refused(lambda: dt.datetime.now(zzz=1))", lambda: refused(lambda: dt.datetime.now(zzz=1)))
show("refused(lambda: dt.date.fromordinal(1, zzz=1))", lambda: refused(lambda: dt.date.fromordinal(1, zzz=1)))
show("refused(lambda: dt.datetime.fromordinal(1, zzz=1))", lambda: refused(lambda: dt.datetime.fromordinal(1, zzz=1)))
show("refused(lambda: dt.date.fromtimestamp(1, zzz=1))", lambda: refused(lambda: dt.date.fromtimestamp(1, zzz=1)))
show("refused(lambda: dt.datetime.utcfromtimestamp(1, zzz=1))", lambda: refused(lambda: dt.datetime.utcfromtimestamp(1, zzz=1)))
show("refused(lambda: io.StringIO('a', zzz=1))", lambda: refused(lambda: io.StringIO('a', zzz=1)))
show("refused(lambda: itertools.chain([1], foo=1))", lambda: refused(lambda: itertools.chain([1], foo=1)))
show("refused(lambda: itertools.cycle([1], foo=1))", lambda: refused(lambda: itertools.cycle([1], foo=1)))
show("refused(lambda: itertools.permutations([1, 2], foo=1))", lambda: refused(lambda: itertools.permutations([1, 2], foo=1)))
show("refused(lambda: itertools.product([1], foo=1))", lambda: refused(lambda: itertools.product([1], foo=1)))
show("refused(lambda: itertools.zip_longest([1], foo=1))", lambda: refused(lambda: itertools.zip_longest([1], foo=1)))
show("refused(lambda: itertools.islice([1], 1, foo=2))", lambda: refused(lambda: itertools.islice([1], 1, foo=2)))
show("refused(lambda: itertools.starmap(abs, [], foo=1))", lambda: refused(lambda: itertools.starmap(abs, [], foo=1)))
show("refused(lambda: itertools.count(1, foo=1))", lambda: refused(lambda: itertools.count(1, foo=1)))
show("refused(lambda: itertools.repeat(1, 2, foo=1))", lambda: refused(lambda: itertools.repeat(1, 2, foo=1)))
show("refused(lambda: itertools.pairwise([1], foo=1))", lambda: refused(lambda: itertools.pairwise([1], foo=1)))
show("refused(lambda: itertools.batched([1], 1, foo=1))", lambda: refused(lambda: itertools.batched([1], 1, foo=1)))
show("refused(lambda: itertools.groupby([1], foo=1))", lambda: refused(lambda: itertools.groupby([1], foo=1)))
show("refused(lambda: itertools.accumulate([1], foo=1))", lambda: refused(lambda: itertools.accumulate([1], foo=1)))
show("refused(lambda: itertools.compress([1], [1], foo=1))", lambda: refused(lambda: itertools.compress([1], [1], foo=1)))
show("refused(lambda: itertools.takewhile(abs, [1], foo=1))", lambda: refused(lambda: itertools.takewhile(abs, [1], foo=1)))
show("refused(lambda: itertools.dropwhile(abs, [1], foo=1))", lambda: refused(lambda: itertools.dropwhile(abs, [1], foo=1)))
show("refused(lambda: itertools.filterfalse(abs, [1], foo=1))", lambda: refused(lambda: itertools.filterfalse(abs, [1], foo=1)))
show("refused(lambda: itertools.tee([1], foo=1))", lambda: refused(lambda: itertools.tee([1], foo=1)))
show("refused(lambda: itertools.combinations([1], 1, foo=1))", lambda: refused(lambda: itertools.combinations([1], 1, foo=1)))
show("refused(lambda: itertools.combinations_with_replacement([1], 1, foo=1))", lambda: refused(lambda: itertools.combinations_with_replacement([1], 1, foo=1)))
show("refused(lambda: dataclasses.fields(Dc, zzz=1))", lambda: refused(lambda: dataclasses.fields(Dc, zzz=1)))
show("html.escape(s='<a>', quote=False)", lambda: html.escape(s='<a>', quote=False))
show("html.unescape(s='&lt;')", lambda: html.unescape(s='&lt;'))
show("textwrap.indent(text='a', prefix='>')", lambda: textwrap.indent(text='a', prefix='>'))
show("textwrap.dedent(text=' a')", lambda: textwrap.dedent(text=' a'))
show("copy.copy(x=[1])", lambda: copy.copy(x=[1]))
show("collections.OrderedDict.fromkeys(iterable=[1], value=2)", lambda: collections.OrderedDict.fromkeys(iterable=[1], value=2))
show("collections.OrderedDict.fromkeys('ab', value=2)", lambda: collections.OrderedDict.fromkeys('ab', value=2))
show("up.urldefrag(url='a#b')", lambda: up.urldefrag(url='a#b'))
show("up.quote(string='a b', safe='')", lambda: up.quote(string='a b', safe=''))
show("up.unquote(string='a%20b', errors='strict')", lambda: up.unquote(string='a%20b', errors='strict'))
show("up.urlparse(url='http://a/', scheme='x')", lambda: up.urlparse(url='http://a/', scheme='x'))
show("up.urljoin(base='http://a/', url='b')", lambda: up.urljoin(base='http://a/', url='b'))
show("up.parse_qs(qs='a=', keep_blank_values=True)", lambda: up.parse_qs(qs='a=', keep_blank_values=True))
show("up.urlencode(query={'a': 1}, doseq=True)", lambda: up.urlencode(query={'a': 1}, doseq=True))
show("ET.iselement(element=1)", lambda: ET.iselement(element=1))
show("ET.register_namespace(prefix='a', uri='http://q/')", lambda: ET.register_namespace(prefix='a', uri='http://q/'))
show("tuple(itertools.compress(data=[1, 2], selectors=[0, 1]))", lambda: tuple(itertools.compress(data=[1, 2], selectors=[0, 1])))
show("next(itertools.count(start=1, step=2))", lambda: next(itertools.count(start=1, step=2)))
show("tuple(itertools.repeat(object=1, times=2))", lambda: tuple(itertools.repeat(object=1, times=2)))
show("tuple(itertools.permutations(iterable=[1, 2], r=1))", lambda: tuple(itertools.permutations(iterable=[1, 2], r=1)))
show("tuple(itertools.combinations(iterable=[1, 2], r=1))", lambda: tuple(itertools.combinations(iterable=[1, 2], r=1)))
show("tuple(itertools.combinations_with_replacement(iterable=[1], r=1))", lambda: tuple(itertools.combinations_with_replacement(iterable=[1], r=1)))
show("tuple(itertools.accumulate(iterable=[1, 2], func=op.add, initial=1))", lambda: tuple(itertools.accumulate(iterable=[1, 2], func=op.add, initial=1)))
show("tuple(k for k, g in itertools.groupby(iterable=[1, 1], key=abs))", lambda: tuple(k for k, g in itertools.groupby(iterable=[1, 1], key=abs)))
show("tuple(itertools.zip_longest([1], [], fillvalue=0))", lambda: tuple(itertools.zip_longest([1], [], fillvalue=0)))
show("tuple(itertools.product([1], repeat=2))", lambda: tuple(itertools.product([1], repeat=2)))
show("tuple(itertools.batched(iterable=[1, 2], n=1, strict=True))", lambda: tuple(itertools.batched(iterable=[1, 2], n=1, strict=True)))
show("functools.cmp_to_key(mycmp=lambda a, b: 0)(1) == functools.cmp_to_key(mycmp=lambda a, b: 0)(2)", lambda: functools.cmp_to_key(mycmp=lambda a, b: 0)(1) == functools.cmp_to_key(mycmp=lambda a, b: 0)(2))
show("functools.lru_cache(maxsize=1, typed=True)(abs)(-1)", lambda: functools.lru_cache(maxsize=1, typed=True)(abs)(-1))
show("math.isclose(a=1.0, b=1.0, rel_tol=0.1, abs_tol=0.1)", lambda: math.isclose(a=1.0, b=1.0, rel_tol=0.1, abs_tol=0.1))
show("math.prod([2], start=3)", lambda: math.prod([2], start=3))
show("math.nextafter(1.0, 2.0, steps=0)", lambda: math.nextafter(1.0, 2.0, steps=0))
show("random.choices([1], weights=[1], k=2)", lambda: random.choices([1], weights=[1], k=2))
show("random.choices(population=[1], cum_weights=[1], k=1)", lambda: random.choices(population=[1], cum_weights=[1], k=1))
show("struct.unpack_from('b', buffer=b'ab', offset=1)", lambda: struct.unpack_from('b', buffer=b'ab', offset=1))
show("binascii.b2a_base64(b'a', newline=False)", lambda: binascii.b2a_base64(b'a', newline=False))
show("gzip.decompress(data=gzip.compress(b'a'))", lambda: gzip.decompress(data=gzip.compress(b'a')))
show("uuid.UUID(int=1, version=4)", lambda: uuid.UUID(int=1, version=4))
show("str(uuid.UUID(hex='12345678123456781234567812345678'))", lambda: str(uuid.UUID(hex='12345678123456781234567812345678')))
show("str(uuid.uuid5(namespace=ns, name='a'))", lambda: str(uuid.uuid5(namespace=ns, name='a')))
show("str(uuid.uuid3(ns, name='a'))", lambda: str(uuid.uuid3(ns, name='a')))
show("traceback.format_exc(limit=1, chain=False)", lambda: traceback.format_exc(limit=1, chain=False))
show("dt.datetime.fromtimestamp(0, tz=dt.timezone.utc)", lambda: dt.datetime.fromtimestamp(0, tz=dt.timezone.utc))
show("dt.datetime.combine(date=dt.date(2020, 1, 1), time=dt.time(1), tzinfo=None)", lambda: dt.datetime.combine(date=dt.date(2020, 1, 1), time=dt.time(1), tzinfo=None))
show("io.StringIO('a', newline='').read()", lambda: io.StringIO('a', newline='').read())
show("io.StringIO(initial_value='a').read()", lambda: io.StringIO(initial_value='a').read())
show("decimal.Decimal(value='1')", lambda: decimal.Decimal(value='1'))
show("collections.deque(iterable=[1], maxlen=1)", lambda: collections.deque(iterable=[1], maxlen=1))
show("collections.namedtuple(typename='P', field_names='a', rename=False, defaults=None, module=None)(1)", lambda: collections.namedtuple(typename='P', field_names='a', rename=False, defaults=None, module=None)(1))
show("secrets.token_hex(nbytes=0)", lambda: secrets.token_hex(nbytes=0))
show("string.capwords(s='a b', sep=' ')", lambda: string.capwords(s='a b', sep=' '))
show("st.fmean([1, 2], weights=[1, 1])", lambda: st.fmean([1, 2], weights=[1, 1]))
show("st.harmonic_mean([1, 2], weights=[1, 1])", lambda: st.harmonic_mean([1, 2], weights=[1, 1]))
show("st.correlation([1, 2, 3], [1, 2, 4], method='ranked')", lambda: st.correlation([1, 2, 3], [1, 2, 4], method='ranked'))
show("st.linear_regression([1, 2, 3], [1, 2, 4], proportional=True)", lambda: st.linear_regression([1, 2, 3], [1, 2, 4], proportional=True))
show("ET.fromstring('<a/>').tag", lambda: ET.fromstring('<a/>').tag)
show("ET.tostring(ET.Comment(text='a'))", lambda: ET.tostring(ET.Comment(text='a')))
show("dataclasses.is_dataclass(obj=Dc)", lambda: dataclasses.is_dataclass(obj=Dc))
show("[f.name for f in dataclasses.fields(class_or_instance=Dc)]", lambda: [f.name for f in dataclasses.fields(class_or_instance=Dc)])

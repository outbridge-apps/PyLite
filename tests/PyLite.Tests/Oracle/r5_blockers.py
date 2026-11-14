# Review 5, the blockers and the regressions: VERBOSE comments, a StopIteration that is not the one next() ended on,
# timedelta and codec errors with huge ints, str() of a huge int in a try.
import io, datetime, itertools
def t(f):
    try:
        print(repr(f()))
    except BaseException as e:
        print(type(e).__name__, e.args if not isinstance(e, (OverflowError, ValueError)) else str(e)[:70])
def g(x):
    raise StopIteration('boom')
t(lambda: next(x for x in filter(g, [1])))
t(lambda: next(filter(g, [1])))
t(lambda: next(map(g, [1])))
t(lambda: next(map(str, filter(g, [1]))))
t(lambda: next(itertools.chain(filter(g, [1]))))
t(lambda: next(itertools.chain(filter(g, [1]), [2])))
def f(x):
    for y in filter(g, [1]):
        pass
    return False
t(lambda: next(filter(f, [1])))
def sw():
    try:
        next(iter([]))
    except StopIteration:
        pass
    return False
t(lambda: next(x for x in [1] if sw()))
t(lambda: datetime.timedelta(days=10**5000))
t(lambda: datetime.timedelta(1) * (2**999999))
t(lambda: datetime.timedelta(days=10**10))
t(lambda: datetime.timedelta(days=10**9))
t(lambda: datetime.timedelta(days=2**31))
t(lambda: datetime.timedelta(999999999) * 2)
t(lambda: datetime.timedelta(999999999) * 3)
t(lambda: datetime.timedelta(999999999) * 2**40)
def big():
    try:
        return str(2**900000)
    except ValueError as e:
        return str(e)[:40]
t(big)
t(lambda: UnicodeDecodeError('utf-8', b'', 2**999, 2**999 + 1, 'x'))
t(lambda: str(UnicodeDecodeError('utf-8', b'abc', 1, 2, 'x')))
import re
t(lambda: re.compile("(a) # c (d\n*", re.X).match("aa").group(1))
t(lambda: re.compile("(a) # c (d\n", re.X).groups)
t(lambda: re.compile("(?x:(a) # c (d\n)#b").groups)
t(lambda: re.compile("(?x:(a) # (\n)(?-x:#)").pattern)
t(lambda: re.compile("(?x:(a) # (\n)(?-x:#)").match("a#").group())
t(lambda: re.compile("(?-x:#)(b)", re.X).match("#b").groups())
t(lambda: re.compile("(?a:\w(?#c))\w").match("aé").group())
t(lambda: re.compile("a(?#c)*").match("aaa").group())
t(lambda: re.compile("(?P<n>a)(?P=n)*").match("aaa").group())
t(lambda: re.compile("a # c\n *", re.X).match("aaa").group())
t(lambda: re.compile("(?#").pattern)
t(lambda: re.compile("(?x)a #(?#\nb").match("ab").group())
t(lambda: re.compile("\#a", re.X).match("#a").group())
t(lambda: re.compile("[#]a", re.X).match("#a").group())
import xml.etree.ElementTree as ET
r = ET.fromstring('<r><a/><b/><a/></r>')
ET.indent(r, space='', level=10)
print(repr(r.text), repr(r[0].tail), repr(r[-1].tail))

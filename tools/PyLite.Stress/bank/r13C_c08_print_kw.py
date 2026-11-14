# deadline: 30000
import functools, collections, dataclasses, string, datetime, json
import xml.etree.ElementTree as ET
out = []
def t(name, f):
    try:
        r = f()
        out.append(name + '=' + repr(r)[:80])
    except Exception as e:
        out.append(name + '!' + type(e).__name__ + ':' + str(e)[:80])
D = {'k%d' % i: i for i in range(100000)}
def kw(**k):
    return len(k)
def pos(*a):
    return len(a)
def both(a, b=2, /, c=3, *, d=4, **k):
    return (a, b, c, d, len(k))
t('print_kw', lambda: print(1, **{'sep': '', 'zz': 1}))
print(chr(10).join(out))

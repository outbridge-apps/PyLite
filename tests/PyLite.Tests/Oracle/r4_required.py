# Review 4, the rows required before a release: unions of any size, big ints that reach an error text or a
# format, canonical ordering of long runs, sort_keys over many keys.
import json
import unicodedata
from datetime import datetime
from decimal import Decimal
from fractions import Fraction


class A:
    pass


class B:
    pass


def t(f):
    try:
        print(repr(f()))
    except BaseException as e:
        print(type(e).__name__, str(e)[:90])


u = int | str
t(lambda: isinstance(1, int | str | None))
t(lambda: isinstance(None, int | None))
t(lambda: isinstance('a', int | float | str))
t(lambda: isinstance(1.5, int | str | None))
t(lambda: (int | str).__args__)
t(lambda: (int | None | str).__args__)
t(lambda: type(u))
t(lambda: type(u).__name__)
t(lambda: u)
t(lambda: int | str | None)
t(lambda: None | int)
t(lambda: (A | B) | A)
t(lambda: A | B)
t(lambda: (A | int) | (B | None))
t(lambda: int | int)
t(lambda: int | str | int)
t(lambda: (int | str) == (str | int))
t(lambda: hash(int | str) == hash(str | int))
t(lambda: u())
t(lambda: u < u)
t(lambda: len(u))
t(lambda: getattr(u, 'x', 'dflt'))
t(lambda: hasattr(u, '__args__'))
t(lambda: u.x)
t(lambda: issubclass(bool, int | str))
t(lambda: isinstance(1, (str, int | None)))
t(lambda: int | 1)
t(lambda: None | None)
t(lambda: str(u))
t(lambda: f'{u}')
t(lambda: {u: 1}[str | int])

for v in (2, -1, 2**31, -2**31 - 1, 2**70, -2**70, 3**60000):
    t(lambda: datetime(2024, 1, 1, fold=v))
for v in (2**70, -2**70, 3**60000):
    t(lambda: Decimal((0, (1,), v)))
t(lambda: Decimal((0, (1,), 5)))
t(lambda: Decimal((0, (1,), -5)))

t(lambda: format(Fraction(1, 3), '.5000f'))
t(lambda: format(Fraction(1, 3), '.4000f')[:12])
t(lambda: format(Fraction(1, 3), '.4299e')[:12])
t(lambda: format(Fraction(1, 3), '.4300e'))
t(lambda: format(Fraction(3**20000, 7), '>10'))
t(lambda: format(Fraction(7, 3**20000), '>10'))
t(lambda: format(Fraction(3**20000, 7), '.3e'))
t(lambda: format(Fraction(3**20000, 7), '.3f'))
t(lambda: format(Fraction(3**8000, 7), '.3f')[:9])
t(lambda: format(Fraction(1, 3), '.10g'))
t(lambda: format(Fraction(1, 3), ''))
t(lambda: format(Fraction(-22, 7), ',.3f'))

# past the short runs: marks of three classes, stably in class order, with starters between
marks = '̧̖̣́̀ͅ'
for n in (1, 2, 3, 5, 20, 400):
    s = 'a' + marks * n + 'b' + '̣́' * n
    for form in ('NFD', 'NFC', 'NFKD', 'NFKC'):
        r = unicodedata.normalize(form, s)
        print(n, form, len(r), ascii(r[:14]), ascii(r[-6:]), unicodedata.is_normalized(form, r))
print(ascii(unicodedata.normalize('NFKD', 'ﷺ' * 2))[:60], len(unicodedata.normalize('NFKD', 'ﷺ' * 500)))

d = {'k%03d' % ((i * 37) % 200): i for i in range(200)}
print(json.dumps(d, sort_keys=True)[:60])
print(json.dumps({'b': 1, 'a': 2, '': 3, 'ab': 4, 'B': 5, 'é': 6, 'ÿ': 8}, sort_keys=True))
print(json.dumps({'x': {'z': 1, 'y': [{'q': 1, 'p': 2}]}, 'a': 0}, sort_keys=True, indent=1))
print(json.dumps({}, sort_keys=True), json.dumps({'only': 1}, sort_keys=True))

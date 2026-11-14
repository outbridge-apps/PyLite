# Review 4, the rows wanted before a release: del of a list or tuple target, an exception class named like a
# builtin, DictReader's blank lines, DivisionByZero as ZeroDivisionError, a callback's StopIteration, zone offsets
# that Windows keeps as daylight of a whole-year rule.
import csv
import decimal
import io
from datetime import datetime
from zoneinfo import ZoneInfo


text = str


def t(f):
    try:
        print(repr(f()))
    except BaseException as e:
        print(type(e).__name__, text(e)[:90])


def run_del():
    a = 1
    b = [1, 2, 3]
    c = 2
    d = 3
    del [a]
    del (c,)
    del (b[0], d)
    del ()
    del []
    print(b)
    for name, ok in (('a', False), ('c', False), ('d', False)):
        try:
            print(name, eval_name(name))
        except NameError:
            print(name, 'gone')


def eval_name(name):
    raise NameError(name)


run_del()
x, y, z = 1, 2, 3
del [x, [y]]
del (z)
try:
    print(x)
except NameError as e:
    print(e)
try:
    del [x]
except NameError as e:
    print(e)


S = type('')
D = type({})
I = type(0)
L = type([])


class str(Exception):
    pass


class dict(Exception):
    pass


class int(Exception):
    pass


class list(Exception):
    pass


t(lambda: isinstance(str('x'), S))
t(lambda: isinstance(dict('x'), D))
t(lambda: isinstance(int('x'), I))
t(lambda: isinstance(list('x'), L))
t(lambda: S.upper(str('x')))
t(lambda: D.keys(dict('x')))
t(lambda: I.bit_length(int('x')))
t(lambda: L.copy(list('x')))
t(lambda: type(str('x')) is str)
t(lambda: type(str('x')).__name__)
t(lambda: isinstance(str('x'), Exception))
t(lambda: repr(str('x')))
del str, dict, int, list


t(lambda: [dict(r) for r in csv.DictReader(io.StringIO('a,b\n1,2\n\n3,4\n\n'))])
t(lambda: [dict(r) for r in csv.DictReader(io.StringIO('\n\na,b\n\n1,2\n'))])
t(lambda: [dict(r) for r in csv.DictReader(io.StringIO('a,b\n1\n1,2,3\n'), restval='r', restkey='k')])
t(lambda: list(csv.reader(io.StringIO('a,b\n\n1,2\n'))))


def zero():
    try:
        decimal.Decimal(1) / 0
    except ZeroDivisionError as e:
        return type(e).__name__


t(zero)
t(lambda: issubclass(decimal.DivisionByZero, ZeroDivisionError))
t(lambda: issubclass(decimal.DivisionByZero, decimal.DecimalException))
t(lambda: issubclass(decimal.DivisionByZero, ArithmeticError))
t(lambda: issubclass(decimal.InvalidOperation, ZeroDivisionError))
t(lambda: issubclass(ZeroDivisionError, decimal.DivisionByZero))
t(lambda: isinstance(decimal.DivisionByZero(), ZeroDivisionError))


def stop(x):
    raise StopIteration('val', x)


t(lambda: next(map(stop, [1])))
t(lambda: next(zip(map(stop, [1]))))
t(lambda: next(filter(stop, [1])))
t(lambda: next(map(stop, [1]), 'dflt'))
t(lambda: next(iter([])))
t(lambda: list(map(stop, [1])))


def caught():
    try:
        v = next(map(stop, [2]))
    except StopIteration as e:
        return e.args


t(caught)
it = map(stop, [3])
t(lambda: list(it))
t(lambda: next(iter(())))

for z, y, m in (('Asia/Pyongyang', 2015, 7), ('Asia/Pyongyang', 2015, 1), ('Asia/Pyongyang', 2016, 7),
                ('Europe/Volgograd', 2018, 7), ('America/Asuncion', 2024, 11), ('America/Asuncion', 2024, 1),
                ('America/Asuncion', 2023, 11), ('Europe/Minsk', 2011, 7), ('Europe/Minsk', 2010, 7),
                ('Europe/Moscow', 2011, 7), ('Europe/Moscow', 2014, 7), ('Europe/Moscow', 2010, 7),
                ('Europe/Berlin', 2020, 7), ('Australia/Sydney', 2020, 1), ('Australia/Sydney', 2020, 7)):
    d = datetime(y, m, 15, 12, tzinfo=ZoneInfo(z))
    print(z, y, m, d.utcoffset(), d.dst())


# A list, set or dict comprehension runs inside its function (PEP 709); a generator or a lambda does not.
def walrus_self():
    return [y := y + 1 for _ in range(2)]


def read_list():
    v = [y for _ in range(1)]
    y = 1


def read_set():
    v = {y for _ in range(1)}
    y = 1


def read_dict():
    v = {y: 1 for _ in range(1)}
    y = 1


def read_nested():
    v = [[y for _ in range(1)] for _ in range(1)]
    y = 1


def read_gen():
    v = list(y for _ in range(1))
    y = 1


def read_lambda():
    v = [(lambda: y)() for _ in range(1)]
    y = 1


def read_list_in_gen():
    v = list([y for _ in range(1)] for _ in range(1))
    y = 1


def read_free():
    def g():
        return [y for _ in range(1)]
    r = g()
    y = 1


def read_in_class():
    class C:
        v = [y for _ in range(1)]
    y = 1


for f in (walrus_self, read_list, read_set, read_dict, read_nested, read_gen, read_lambda, read_list_in_gen, read_free,
          read_in_class):
    t(f)

D = decimal.Decimal
values = ('0', '0.00', '-0', '1234567.891', '1E+5', '1E-9', '-1.5', '123', '0E+3', '1.5E+10', '9.995', '1E-3')
for spec in ('.3f', 'f', 'e', '.0e', 'g', '.2%', ',.2f', '010.2f', '012,.2f', '#.0f', '+.1f', ' .1f', '<12.2f', '^12.2f',
             '=+12.2f', '.0f', '#.0e', ',f', '020,f', '.30f', '.30e', 'E', 'G', '', '.2', ',', '%', '#g'):
    print(spec, [format(D(v), spec) for v in values])
t(lambda: len(format(D('1.5'), '.100000f')))
t(lambda: len(format(D('-1.5'), '.100000e')))
t(lambda: len(format(D('1E+20'), 'f')))
t(lambda: format(D('1E+20'), ','))
t(lambda: format(D('1E+20'), ',f'))

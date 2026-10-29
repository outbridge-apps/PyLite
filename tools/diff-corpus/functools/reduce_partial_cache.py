import functools


def err(f):
    try:
        f()
        return 'ok'
    except Exception as e:
        return type(e).__name__


out = []
out.append(functools.reduce(lambda a, b: a * b, [1, 2, 3, 4]))
out.append(functools.reduce(lambda a, b: a + b, [], 10))
out.append(err(lambda: functools.reduce(lambda a, b: a + b, [])))


def power(base, exp, mod=None):
    return pow(base, exp, mod) if mod else base ** exp


square = functools.partial(power, exp=2)
out.append((square(7), square(3, mod=5)))
cube_mod = functools.partial(functools.partial(power, exp=3), mod=7)
out.append(cube_mod(4))
out.append(err(lambda: functools.partial(5)))


def cmp(a, b):
    return (len(a) > len(b)) - (len(a) < len(b)) or ((a > b) - (a < b))


out.append(sorted(['ccc', 'a', 'bb', 'aa', 'b'], key=functools.cmp_to_key(cmp)))
calls = []


@functools.lru_cache(maxsize=2)
def slow(n):
    calls.append(n)
    return n * n


out.append([slow(n) for n in [1, 2, 1, 3, 1, 2]])
out.append(calls)
typed_calls = []


@functools.lru_cache(maxsize=None, typed=True)
def typed(x):
    typed_calls.append(x)
    return type(x).__name__


out.append((typed(1), typed(1.0), typed(1), typed_calls))


def deco(f):
    @functools.wraps(f)
    def wrapper(*a, **k):
        return f(*a, **k)
    return wrapper


@deco
def original(x):
    return x + 1


out.append((original.__name__, original(1)))
result = out

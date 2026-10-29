def err(f):
    try:
        f()
        return 'ok'
    except Exception as e:
        return type(e).__name__


out = []
out.append(err(lambda: {}['k']))
out.append(err(lambda: [][0]))
out.append(err(lambda: (1).nope))
out.append(err(lambda: len(5)))
out.append(err(lambda: int('x')))
out.append(err(lambda: 1 / 0))
out.append(err(lambda: 1 % 0))
out.append(err(lambda: 2.0 ** 10000))
out.append(err(lambda: undefined_name))
out.append(err(lambda: 'a' + 1))
out.append(err(lambda: [1, 2].index(3)))
out.append(err(lambda: 'abc'.index('z')))
out.append(err(lambda: next(iter([]))))
out.append(err(lambda: [].pop()))
out.append(err(lambda: {}.popitem()))
out.append(err(lambda: set().pop()))
out.append(err(lambda: int(float('inf'))))
out.append(err(lambda: int(float('nan'))))
out.append(err(lambda: chr(-1)))
out.append(err(lambda: 'abc'.encode('nope')))
out.append(err(lambda: b'\xff'.decode('utf-8')))
out.append(err(lambda: 'é'.encode('ascii')))
out.append(err(lambda: hash([])))
out.append(err(lambda: sorted([1, 'a'])))
out.append(err(lambda: tuple()[0]))
out.append(err(lambda: range(0, 10, 0)))
out.append(err(lambda: list(zip(1))))
out.append(err(lambda: max([])))
out.append(err(lambda: divmod(1, 0)))
out.append(err(lambda: float('x')))


def f(a, b):
    return a


out.append(err(lambda: f(1)))
out.append(err(lambda: f(1, 2, 3)))
out.append(err(lambda: f(1, c=2)))
out.append(err(lambda: f(1, a=2)))


def unbound():
    x = x + 1


out.append(err(unbound))
result = out

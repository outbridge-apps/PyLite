# deadline: 20000
import itertools, functools, operator, collections
N = 100000
out = []
def t(name, mk, use=next):
    try:
        it = mk()
        r = use(it)
        out.append(name + '=' + repr(r)[:60])
    except Exception as e:
        out.append(name + '!' + type(e).__name__ + ':' + str(e)[:60])

def nest(f):
    def mk():
        it = iter([1, 2, 3])
        for i in range(N):
            it = f(it)
        return it
    return mk

t('map_sumprod', nest(lambda it: map(abs, it)), lambda it: __import__('math').sumprod(it, [1, 1, 1]))
print(chr(10).join(out))

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

t('map_csv', nest(lambda it: map(lambda x: str(x) + ',a', it)), lambda it: list(__import__('csv').reader(it)))
print(chr(10).join(out))

# Review 13, row 26: a StopIteration out of a member's __iter__ ends the chain (CPython: [1] [0] [1] ([1], [])).
import itertools
class I:
    def __iter__(self):
        raise StopIteration('iter')
def t(f):
    try:
        print(f())
    except BaseException as e:
        print(type(e).__name__, e)
t(lambda: list(filter(None, itertools.chain([1], I(), [2]))))
t(lambda: list(itertools.filterfalse(None, itertools.chain([0], I(), [2]))))
t(lambda: list(itertools.chain([1], I(), [2])))
def again():
    c = itertools.chain([1], I(), [2])
    a = list(filter(None, c))
    return (a, list(c))
t(again)

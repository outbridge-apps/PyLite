# Review 13 fix review: chain, islice and pairwise drop their source at its end or on its error; batched ends on an empty batch (CPython 3.14.2: [1, 2] [] 3 / [] 2 / islice [] 2 / pairwise [] 2 / batched [(1, 2), (3,)] [] 6).
import itertools
class R:
    def __init__(self, data): self.data = list(data); self.calls = 0
    def __iter__(self): return self
    def __next__(self):
        self.calls += 1
        x = self.data.pop(0) if self.data else None
        if x is None: raise StopIteration
        if x == 'err': raise ValueError('boom')
        return x
r = R([[1], [2], None, [3]]); c = itertools.chain.from_iterable(r)
print(list(c), list(c), r.calls)        # CPython: [1, 2] [] 3
r = R([[1], 'err', [3]]); c = itertools.chain.from_iterable(r)
try: list(c)
except ValueError as e: print('err', e)
print(list(c), r.calls)                 # CPython: [] 2
r = R([1, 2, None, 3]); c = itertools.chain.from_iterable(map(lambda x: [x], r))
print(list(c), list(c), r.calls)        # CPython: [1, 2] [] 3
for nm, mk in (("islice", lambda r: itertools.islice(r, 5)), ("pairwise", lambda r: itertools.pairwise(r))):
    r = R([1, 'err', 2, 3]); it = mk(r)
    try: print(nm, list(it))
    except ValueError: print(nm, 'raised')
    print(nm, list(it), r.calls)   # CPython: [] 2
r = R([1, 2, None, 3]); b = itertools.batched(r, 3)
print("batched", list(b), list(b), r.calls)   # CPython: [(1, 2), (3,)] [] 6

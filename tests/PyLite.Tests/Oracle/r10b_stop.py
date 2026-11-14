import itertools
out = []
def t(name, f):
    try:
        r = f()
        out.append(name + '=' + repr(r)[:200])
    except BaseException as e:
        out.append(name + '!' + type(e).__name__ + ':' + str(e)[:200])
def stop3(v, *a):
    if v == 3:
        raise StopIteration('s%d' % v)
    return v
def stopsum(a, b):
    if b == 3:
        raise StopIteration('acc')
    return a + b
class B:
    def __init__(self, v): self.v = v
    def __bool__(self):
        if self.v == 3:
            raise StopIteration('bool')
        return True
    def __repr__(self): return 'B%d' % self.v
R = [1, 2, 3, 4, 5]
t('map', lambda: list(map(stop3, R)))
t('map2', lambda: list(map(stop3, R, R)))
t('map_strict', lambda: list(map(stop3, R, R, strict=True)))
t('map_next', lambda: [it := map(stop3, R), next(it), next(it), next(it, 'dflt'), next(it, 'after')][1:])
def nx():
    it = map(stop3, R)
    next(it); next(it)
    try:
        next(it)
    except StopIteration as e:
        return e.args, next(it), next(it)
t('map_next_exc', nx)
t('map_nested', lambda: list(map(abs, map(stop3, map(abs, R)))))
t('map_nested_resume', lambda: [it := map(abs, map(stop3, R)), list(it), list(it)][1:])
t('filter', lambda: list(filter(stop3, R)))
t('filter_resume', lambda: [it := filter(stop3, R), list(it), list(it)][1:])
t('filterfalse', lambda: list(itertools.filterfalse(stop3, R)))
t('takewhile', lambda: list(itertools.takewhile(stop3, R)))
t('takewhile_resume', lambda: [it := itertools.takewhile(stop3, R), list(it), list(it)][1:])
t('dropwhile', lambda: list(itertools.dropwhile(lambda v: stop3(v) < 0, R)))
t('dropwhile_resume', lambda: [it := itertools.dropwhile(lambda v: stop3(v) > 0, R), list(it), list(it)][1:])
t('accumulate', lambda: list(itertools.accumulate(R, stopsum)))
t('accumulate_resume', lambda: [it := itertools.accumulate(R, stopsum), list(it), list(it)][1:])
t('starmap', lambda: list(itertools.starmap(stop3, [(1,), (2,), (3,), (4,)])))
t('starmap_resume', lambda: [it := itertools.starmap(stop3, [(1,), (2,), (3,), (4,)]), list(it), list(it)][1:])
t('groupby', lambda: [k for k, g in itertools.groupby(R, stop3)])
t('for_map', lambda: [v for v in map(stop3, R)])
t('sum_map', lambda: sum(map(stop3, R)))
t('zip_map', lambda: list(zip(map(stop3, R), R)))
t('in_map', lambda: 9 in map(stop3, R))
t('unpack_map', lambda: [*map(stop3, R)])
t("genexp_map", lambda: list(v for v in map(stop3, R)))
t("map_genexp", lambda: list(map(stop3, (v for v in R))))
t("genexp_nested", lambda: list(w for w in (v for v in map(stop3, R))))
t('map_list_inner', lambda: list(map(list, [map(stop3, R), map(stop3, [7, 8])])))
t('map_next_inner', lambda: list(map(next, [iter([]), iter([1])])))
t('map_valueerr', lambda: list(map(int, ['1', 'x'])))
def keyerr(v):
    raise KeyError(v)
t('filter_keyerr', lambda: list(filter(keyerr, R)))
class SI(StopIteration): pass
def stopsub(v):
    if v == 2:
        raise SI('sub')
    return v
t('map_subclass', lambda: list(map(stopsub, R)))
t('takewhile_sub', lambda: list(itertools.takewhile(stopsub, R)))
print(chr(10).join(out))

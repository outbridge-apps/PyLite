# per-customer config: deepcopy a nested default, override, compare, json round trip
import copy, json, time
base = {'name': 'x', 'limits': {'credit': 1000, 'days': [30, 60, 90]}, 'contacts': [{'n': 'a', 'tags': ['p', 'q']} for _ in range(5)],
        'flags': {('k%d' % i): (i % 2 == 0) for i in range(20)}, 'path': ('a', 'b', ('c', 'd'))}
t0 = time.perf_counter()
out = []
diffs = 0
for i in range(6000):
    c = copy.deepcopy(base)
    c['name'] = 'C%d' % i
    c['limits']['credit'] += i
    c['contacts'][i % 5]['tags'].append('z')
    s = copy.copy(c)
    s['flags'] = dict(c['flags'])
    if c != base:
        diffs += 1
    out.append(c)
t1 = time.perf_counter()
js = json.dumps(out[:2000], sort_keys=True)
back = json.loads(js)
t2 = time.perf_counter()
print(diffs, len(js), back[1999]['limits']['credit'], 'copy', round(t1 - t0, 3), 'json', round(t2 - t1, 3))

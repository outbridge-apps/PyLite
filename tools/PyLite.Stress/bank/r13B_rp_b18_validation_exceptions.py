# repeat: 2
# validation pipeline: generators, custom exceptions, try/except/else/finally in a hot loop, string checks
import time
class ValidationError(Exception):
    pass
def records(n):
    out = []
    for i in range(n):
        out.append({'id': str(i), 'email': ('u%d@x.com.example.com' % i) if i % 7 else 'bad', 'age': str(i % 120) if i % 11 else 'x', 'zip': '%05d' % i})
    return out
def check(r):
    if '@' not in r['email']:
        raise ValidationError('email', 'missing @')
    try:
        a = int(r['age'])
    except ValueError as e:
        raise ValidationError('age', 'not a number') from e
    if not 0 <= a <= 110:
        raise ValidationError('age', 'out of range')
    if not (r['zip'].isdigit() and len(r['zip']) == 5):
        raise ValidationError('zip', 'bad')
    return a
t0 = time.perf_counter()
ok = 0
errs = {}
fin = 0
for r in records(80000):
    try:
        check(r)
    except ValidationError as e:
        f = e.args[0]
        errs[f] = errs.get(f, 0) + 1
    else:
        ok += 1
    finally:
        fin += 1
t1 = time.perf_counter()
print(ok, sorted(errs.items()), fin, round(t1 - t0, 3))

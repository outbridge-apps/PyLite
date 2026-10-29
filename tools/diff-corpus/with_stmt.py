import io
log = []
class CM:
    def __init__(self, name, swallow=False):
        self.name = name
        self.swallow = swallow
    def __enter__(self):
        log.append('enter ' + self.name)
        return self.name
    def __exit__(self, t, v, tb):
        log.append(('exit', self.name, t.__name__ if t is not None else None, str(v) if v is not None else None))
        return self.swallow
with CM('a') as a, CM('b') as b:
    log.append(a + b)
with CM('s', True):
    raise IndexError('k')   # str(KeyError) differs on IronPython
def f():
    for i in range(3):
        with CM(str(i)):
            if i == 1:
                return i
log.append(f())
try:
    with CM('outer'):
        with CM('inner'):
            raise ValueError('v')
except ValueError as e:
    log.append('caught ' + str(e))
with io.StringIO('x\ny\n') as s:
    lines = s.readlines()
log.append((lines, s.closed))
result = log

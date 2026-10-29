class Status:
    """Codes."""
    NEW = 'new'
    DONE = 'done'
    ALL = (NEW, DONE)
    LIMIT = 3
    def final(self, s, limit=LIMIT):
        return s == self.DONE, limit
class Sub(Status):
    NEW = 'fresh'
    EXTRA = NEW + '!'
class Counter:
    count = 0
    def __init__(self):
        Counter.count += 1
        self.serial = Counter.count
for i in range(3):
    Counter()
class Settings:
    timeout = 30
    def __init__(self, timeout=None):
        if timeout is not None:
            self.timeout = timeout
def describe(self):
    return type(self).__name__
class Thing:
    describe = describe
X = 'module'
class Scoped:
    X = 'class'
    def which(self):
        return X
result = [Status.NEW, Status().DONE, Status.ALL, Status().final('done'), Sub.NEW, Sub.EXTRA, Sub.DONE, Sub().ALL,
          Counter.count, Settings().timeout, Settings(5).timeout, Settings.timeout, Thing().describe(),
          Scoped().which(), Scoped.X, sorted(n for n in dir(Sub) if not n.startswith('_'))]

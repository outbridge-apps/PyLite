# deadline: 2000
d = {}
class K:
    def __init__(self, n):
        self.n = n
    def __hash__(self):
        return 1
    def __eq__(self, other):
        d['x%d' % self.n] = 1     # a new key every time: the table changes, the lookup starts over
        del d['x%d' % self.n]
        return False
d[K(0)] = 1
d[K(1)] = 2
try:
    print(K(2) in d)
except Exception as e:
    print(type(e).__name__, e)

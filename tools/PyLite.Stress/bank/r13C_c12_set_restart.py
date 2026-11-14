# deadline: 2000
d = {}
s = set()
class K:
    def __init__(self, n):
        self.n = n
    def __hash__(self):
        return 1
    def __eq__(self, other):
        s.add("x%d" % self.n)
        s.discard("x%d" % self.n)
        return False
s.add(K(0))
s.add(K(1))
try:
    print(K(2) in s)
except Exception as e:
    print(type(e).__name__, e)

# deadline: 3000
# allocmb: 256
import operator
class C:
    def __init__(self):
        self.a = self
c = C()
f = operator.attrgetter('a.' * 2999999 + 'a')
while True:
    f(c)

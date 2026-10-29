calls = []
class Base:
    def __init__(self, name):
        calls.append('Base')
        self.name = name
    def describe(self):
        return 'base:' + self.name
class Mid(Base):
    LIMIT = 3
    def __init__(self, name, level):
        calls.append('Mid')
        super().__init__(name)
        self.level = level
    def describe(self):
        return super().describe() + '/mid' + str(self.level)
class Leaf(Mid):
    LIMIT = 9
    def __init__(self, name, level, tag):
        calls.append('Leaf')
        super().__init__(name, level=level)
        self.tag = tag
    def describe(self):
        return super().describe() + '/leaf:' + self.tag
    def limits(self):
        return (self.LIMIT, super().LIMIT, super(Mid, self).describe())
class Plain:
    def __init__(self):
        super().__init__()
        self.ok = True
x = Leaf('n', 2, 't')
errors = []
try:
    super(Leaf, Base('b'))
except TypeError as e:
    errors.append(type(e).__name__)
result = [calls, x.describe(), x.limits(), Plain().ok, Leaf.describe(x), errors]

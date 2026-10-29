class Money:
    def __init__(self, amount, currency='EUR'):
        self.amount = amount
        self.currency = currency

    def __repr__(self):
        return 'Money(%r, %r)' % (self.amount, self.currency)

    def __eq__(self, other):
        return (self.amount, self.currency) == (other.amount, other.currency)

    def __lt__(self, other):
        return self.amount < other.amount

    def __hash__(self):
        return hash((self.amount, self.currency))


class Basket:
    def __init__(self):
        self.items = []

    def add(self, m):
        self.items.append(m)
        return self

    def __len__(self):
        return len(self.items)

    def __getitem__(self, i):
        return self.items[i]

    def __contains__(self, m):
        return m in self.items

    def __iter__(self):
        return iter(self.items)


class Priced(Money):
    def __init__(self, amount, label):
        super().__init__(amount)
        self.label = label

    def __repr__(self):
        return self.label + ':' + super().__repr__()


out = []
b = Basket().add(Money(5)).add(Money(2)).add(Priced(9, 'tea'))
out.append((len(b), b[0], Money(2) in b, sorted(b), [repr(m) for m in b]))
out.append(len({Money(1), Money(1), Money(2)}))
out.append((isinstance(b[2], Money), type(b[2]).__name__, Money(1) == Money(1), Money(1) != Money(2)))


def counter():
    n = 0

    def inc(step=1):
        nonlocal n
        n += step
        return n
    return inc


c = counter()
c()
c(5)
out.append(c())
fns = [lambda: i for i in range(3)]
out.append([f() for f in fns])
fns = [lambda i=i: i for i in range(3)]
out.append([f() for f in fns])
total = 0


def add_global(x):
    global total
    total += x


add_global(3)
add_global(4)
out.append(total)


def kw(a, b=2, *args, c, d=4, **rest):
    return (a, b, args, c, d, sorted(rest.items()))


out.append(kw(1, c=3))
out.append(kw(1, 2, 3, 4, c=5, e=6))
out.append((lambda *a, **k: (a, sorted(k)))(1, 2, x=3))
first, *middle, last = [1, 2, 3, 4, 5]
out.append((first, middle, last))
(a, b), c = (1, 2), 3
out.append((a, b, c))
result = out

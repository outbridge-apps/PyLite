class B:
    def __init__(self, n):
        self.n = n
    def __bool__(self):
        if self.n == 2:
            raise StopIteration
        return True
def t(f):
    try:
        return f()
    except BaseException as e:
        return type(e).__name__ + ': ' + str(e)
print(t(lambda: [b.n for b in filter(None, [B(0), B(1), B(2), B(3)])]))
print(t(lambda: [b.n for b in filter(bool, [B(0), B(1), B(2), B(3)])]))
print(t(lambda: [b.n for b in filter(lambda b: b, [B(0), B(1), B(2), B(3)])]))

class B:
    def f(self): return 1
def g():
    class A(B):
        def h(self): return super().f()
    return A().h()
print(g())

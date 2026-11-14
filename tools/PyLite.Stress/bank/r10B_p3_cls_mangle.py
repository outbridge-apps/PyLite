class O:
    class __P: pass
    class Q(__P): pass
print(O.Q.__bases__)

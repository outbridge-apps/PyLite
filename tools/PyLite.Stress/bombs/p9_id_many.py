# deadline: 20000
# allocmb: 1024
class A:
    pass
print(len({id(A()) for _ in range(1000000)}))

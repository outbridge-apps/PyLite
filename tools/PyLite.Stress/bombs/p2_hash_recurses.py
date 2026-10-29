# deadline: 20000
# allocmb: 1024
class A:
    def __hash__(self):
        return hash(self)
try:
    print({A()})
except RecursionError as e:
    print('clean RecursionError')

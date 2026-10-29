# deadline: 20000
# allocmb: 1024
class A:
    def __contains__(self, x):
        return x in self
try:
    print(1 in A())
except RecursionError as e:
    print('clean RecursionError')

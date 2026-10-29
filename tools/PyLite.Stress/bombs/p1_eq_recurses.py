# deadline: 20000
# allocmb: 1024
class A:
    def __eq__(self, o):
        return self == o
try:
    print(A() == A())
except RecursionError as e:
    print('clean RecursionError')

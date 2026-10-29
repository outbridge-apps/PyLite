# deadline: 20000
# allocmb: 1024
class A:
    def __len__(self):
        return len(self)
try:
    print(len(A()))
except RecursionError as e:
    print('clean RecursionError')

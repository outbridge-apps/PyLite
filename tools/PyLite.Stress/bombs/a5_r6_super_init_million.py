# deadline: 20000
# allocmb: 1024
class A:
    def __init__(self):
        self.x = 1
class B(A):
    def __init__(self):
        super().__init__()
for _ in range(1000000):
    B()
print('done')

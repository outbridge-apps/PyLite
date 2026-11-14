# deadline: 3000
# allocmb: 1024
import traceback
e = ValueError('x')
for i in range(100000):
    e.add_note('n')
x = traceback.format_exception_only(e)

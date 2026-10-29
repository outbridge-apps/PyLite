# Adapted from CPython's test suite (structural pattern matching basics, Lib/test/test_patma.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

def http_name(code):
    match code:
        case 200:
            return 'ok'
        case 404:
            return 'not found'
        case _:
            return 'other'
assert http_name(200) == 'ok'
assert http_name(404) == 'not found'
assert http_name(500) == 'other'

def describe(point):
    match point:
        case (0, 0):
            return 'origin'
        case (x, 0):
            return 'on x axis at ' + str(x)
        case (0, y):
            return 'on y axis at ' + str(y)
        case (x, y):
            return 'point ' + str(x) + ',' + str(y)
assert describe((0, 0)) == 'origin'
assert describe((3, 0)) == 'on x axis at 3'
assert describe((0, 2)) == 'on y axis at 2'
assert describe((1, 2)) == 'point 1,2'

def kind(value):
    match value:
        case []:
            return 'empty list'
        case [x]:
            return 'one: ' + str(x)
        case [x, *rest]:
            return 'first ' + str(x) + ' plus ' + str(len(rest))
assert kind([]) == 'empty list'
assert kind([7]) == 'one: 7'
assert kind([1, 2, 3]) == 'first 1 plus 2'

# guard
def sign(n):
    match n:
        case x if x > 0:
            return 1
        case 0:
            return 0
        case _:
            return -1
assert sign(5) == 1 and sign(0) == 0 and sign(-3) == -1

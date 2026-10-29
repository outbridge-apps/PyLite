# Adapted from CPython's test suite (Lib/test/test_exceptions.py: NotImplementedError is a RuntimeError).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: NotImplementedError

class Shape:
    def __init__(self, name):
        self.name = name

    def area(self):
        raise NotImplementedError(self.name + ' must define area()')

class Blob(Shape):
    def __init__(self):
        Shape.__init__(self, 'Blob')

Blob().area()

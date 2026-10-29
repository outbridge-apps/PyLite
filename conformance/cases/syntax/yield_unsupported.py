# Adapted from CPython's test suite (Lib/test/test_generators.py) -- dialect: generators are outside the
# subset, so `yield` is refused at compile time rather than producing a generator.
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: SyntaxError

def gen():
    yield 1

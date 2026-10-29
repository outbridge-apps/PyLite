# A dunder other than __match_args__, __slots__ or __hash__ cannot be assigned in a class body.
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: SyntaxError

class Color:
    __repr__ = str

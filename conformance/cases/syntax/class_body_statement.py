# The class body still refuses what is not a method, a field, a constant or a docstring.
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: SyntaxError

class Color:
    if True:
        RED = 1

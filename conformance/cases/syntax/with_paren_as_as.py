# The parenthesized form (3.10) cannot be followed by 'as' once an item inside it has its own.
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: SyntaxError

with (a as b) as c:
    pass

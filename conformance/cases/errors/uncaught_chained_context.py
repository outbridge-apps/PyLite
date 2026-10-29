# Adapted from CPython's test suite (Lib/test/test_raise.py: TestContext).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: ValueError
# An exception the ENGINE raised and a handler caught in the same frame still carries its raise
# site, so the printed chain has a Traceback header for both halves.


def lookup(d):
    try:
        return d['k']
    except KeyError:
        raise ValueError('no k')


lookup({})

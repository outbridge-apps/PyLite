# Adapted from CPython's test suite (Lib/test/test_coroutines.py) -- dialect: no async/await.
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: SyntaxError

async def f():
    return 1

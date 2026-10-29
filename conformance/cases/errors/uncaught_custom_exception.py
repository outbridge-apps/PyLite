# Adapted from CPython's test suite (Lib/test/test_exceptions.py: user-defined exception classes).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: PaymentDeclined

class AppError(Exception):
    pass

class PaymentDeclined(AppError):
    pass

def charge(amount):
    if amount > 100:
        raise PaymentDeclined('limit is 100')
    return amount

charge(10)
charge(500)

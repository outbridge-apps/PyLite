# Dialect rule (docs/deviations.md): a record class may define the protocol methods the engine has a
# place to call (see cases/grammar/record_protocols.py), and every other dunder is rejected at parse
# time rather than being written and silently ignored. The operator dunders are the largest group.
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: SyntaxError

class T:
    def __init__(self, v):
        self.v = v

    def __add__(self, other):
        return T(self.v + other.v)

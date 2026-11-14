# CPython (and PyLite): str() of a KeyError is the repr of its key, "'k'", so a message names the key
# unambiguously; IronPython 3.4.2 answers the bare key, 'k'.
result = (str(KeyError('k')), str(KeyError(1)), str(KeyError()))

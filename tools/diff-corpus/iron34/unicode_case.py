# CPython (and PyLite) take case mapping from the Unicode tables: the digraph 'ǆ' titlecases to 'ǅ',
# and 'İ'.lower() is 'i' followed by a combining dot. IronPython 3.4.2 answers 'Ǆ' and a bare 'i'.
result = ['ǆ'.title(), 'İ'.lower() == 'i̇']

# Adapted from CPython's test suite (Lib/test/test_unicode.py: test_upper/test_lower/test_casefold/
# test_title/test_capitalize/test_swapcase, Lib/test/test_str.py). Python Software Foundation,
# PSF License Version 2 -- see README.
# Case mapping runs off the generated Unicode 16.0 tables, so the mappings that CHANGE LENGTH, the
# ones .NET's own tables lack, and the final-sigma context rule all behave as in CPython.

# ---- length-changing (SpecialCasing) ----
assert 'Strasse'.upper() == 'STRASSE' and 'ß'.upper() == 'SS'
assert 'ﬁle'.upper() == 'FILE'                    # ligature fi
assert 'ŉ'.upper() == 'ʼN'                   # n preceded by apostrophe
assert 'ᾳ'.upper() == 'ΑΙ'              # alpha with ypogegrammeni
assert 'İ'.lower() == 'i̇'                   # capital I with dot above
assert len('ß'.upper()) == 2                      # the result is LONGER than the input

# ---- casefold is not lower ----
assert 'straße'.casefold() == 'strasse'
assert 'Straße'.casefold() == 'STRASSE'.casefold()
assert 'µ'.casefold() == 'μ'.casefold()      # micro sign folds onto mu
assert 'ſ'.casefold() == 's'.casefold()           # long s
assert 'K'.casefold() == 'k'.casefold()           # kelvin sign

# ---- final sigma: the one context rule CPython applies by default ----
assert 'ΟΣΟΣ'.lower() == 'οσος'
assert 'ΣΩ'.lower() == 'σω'        # not final: a cased letter follows
assert 'Σ'.lower() == 'σ'                    # not final: no cased letter precedes

# ---- mappings .NET's own tables do not carry ----
assert 'ẞ'.lower() == 'ß'                    # capital sharp s (Unicode 5.1)
assert 'ı'.upper() == 'I' and 'ſ'.upper() == 'S'
assert 'ϴ'.lower() == 'θ' and 'Ω'.lower() == 'ω'
assert 'Ԥ'.lower() == 'ԥ'                    # Cyrillic supplement
assert 'ა'.upper() == 'Ა'                    # Georgian Mkhedruli -> Mtavruli (Unicode 11.0)
assert 'Ꭰ'.lower() == 'ꭰ'                    # Cherokee (Unicode 8.0)

# ---- above the BMP: a surrogate pair maps as one code point ----
assert '\U00010428'.upper() == '\U00010400' and len('\U00010428'.upper()) == 2

# ---- title / capitalize / swapcase use the TITLE mapping, not the upper one ----
assert 'ǳ'.title() == 'ǲ' and 'ǳ'.upper() == 'Ǳ'
assert 'ßa'.capitalize() == 'Ssa'
assert 'aß'.swapcase() == 'ASS'
assert 'ǲ'.swapcase() == 'ǲ'                 # titlecase is neither Uppercase nor Lowercase

# ---- the ordinary alphabets are unchanged ----
assert 'Invoice 4711'.upper() == 'INVOICE 4711' and 'INVOICE'.lower() == 'invoice'
assert 'привет'.upper() == 'ПРИВЕТ'
assert 'Tiếng Việt'.upper() == 'TIẾNG VIỆT'
assert 'καλημέρα'.upper() == 'ΚΑΛΗΜΈΡΑ'
assert 'hello world'.title() == 'Hello World' and 'x1y'.title() == 'X1Y'
assert ''.upper() == '' and ''.lower() == '' and ''.title() == '' and ''.capitalize() == ''

# ---- predicates follow the derived properties, as in CPython ----
assert 'Ab Cd'.istitle() and not 'AB'.istitle() and not 'ab'.istitle()
assert 'ab'.islower() and 'AB'.isupper() and not 'Ab'.islower()
assert not 'ǲ'.isupper() and not 'ǲ'.islower() and 'ǲ'.istitle()

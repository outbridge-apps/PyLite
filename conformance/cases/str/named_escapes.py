# Adapted from CPython's test suite (Lib/test/test_ucn.py: UnicodeNamesTest.test_general /
# test_hangul_syllables / test_cjk_unified_ideographs / test_aliases / test_named_sequences).
# Python Software Foundation, PSF License Version 2 -- see README.
# \N{NAME} resolves through the generated Unicode 16.0 name tables.

assert '\N{EURO SIGN}' == '€'
assert '\N{LATIN SMALL LETTER A}' == 'a'
assert '\N{latin small letter a}' == 'a'          # names match case-insensitively
assert '\N{GREEK SMALL LETTER ALPHA}\N{COMBINING ACUTE ACCENT}' == 'ά'
assert len('\N{ARABIC LIGATURE UIGHUR KIRGHIZ YEH WITH HAMZA ABOVE WITH ALEF MAKSURA ISOLATED FORM}') == 1

# aliases: abbreviations, control names and corrections all resolve
assert '\N{NUL}' == '\x00' and '\N{NULL}' == '\x00'
assert '\N{ZWNBSP}' == '﻿'
assert '\N{LATIN CAPITAL LETTER GHA}' == 'Ƣ'

# a named sequence is more than one character
assert '\N{KEYCAP DIGIT ZERO}' == '0️⃣'

# composed rather than stored: Hangul syllables from their jamo, ideographs from their code point
assert '\N{HANGUL SYLLABLE GA}' == '가'
assert '\N{HANGUL SYLLABLE GGWALS}' == '꽔'
assert '\N{HANGUL SYLLABLE HIH}' == '힣'
assert '\N{CJK UNIFIED IDEOGRAPH-4E00}' == '一'
assert '\N{CJK COMPATIBILITY IDEOGRAPH-F900}' == '豈'
assert '\N{TANGUT IDEOGRAPH-17000}' == '\U00017000'
assert '\N{EGYPTIAN HIEROGLYPH-13460}' == '\U00013460'
assert '\N{NUSHU CHARACTER-1B170}' == '\U0001b170'
assert '\N{KHITAN SMALL SCRIPT CHARACTER-18B00}' == '\U00018b00'
assert '\N{GRINNING FACE}' == '\U0001f600'

# a str escape only: bytes keep the backslash, and a raw string keeps the text
assert b'\N{EURO SIGN}' == b'\x5cN{EURO SIGN}'
assert r'\N{EURO SIGN}' == '\x5cN{EURO SIGN}'

# inside an f-string the braces belong to the escape, not to a replacement field
assert f'a\N{EURO SIGN}{1 + 1}' == 'a€2'

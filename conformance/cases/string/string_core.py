# Adapted from CPython's test suite (Lib/test/test_string.py: ModuleTest.test_attrs / test_capwords).
# Python Software Foundation, PSF License Version 2 -- see README.
import string

assert string.ascii_letters == string.ascii_lowercase + string.ascii_uppercase
assert string.ascii_lowercase == 'abcdefghijklmnopqrstuvwxyz'
assert string.ascii_uppercase == 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'
assert string.digits == '0123456789' and string.hexdigits == '0123456789abcdefABCDEF' and string.octdigits == '01234567'
assert string.punctuation == '!"#$%&\'()*+,-./:;<=>?@[\\]^_`{|}~'
assert string.whitespace == ' \t\n\r\x0b\x0c'
assert set(string.printable) == set(string.digits + string.ascii_letters + string.punctuation + string.whitespace)
assert len(string.printable) == 100

assert string.capwords('abc def ghi') == 'Abc Def Ghi'
assert string.capwords('abc\tdef\nghi') == 'Abc Def Ghi'
assert string.capwords('abc\t   def  \nghi') == 'Abc Def Ghi'
assert string.capwords('ABC DEF GHI') == 'Abc Def Ghi'
assert string.capwords('ABC-DEF-GHI', '-') == 'Abc-Def-Ghi'
assert string.capwords('ABC-def DEF-ghi GHI') == 'Abc-def Def-ghi Ghi'
assert string.capwords('   aBc  DeF   ') == 'Abc Def'
assert string.capwords('\taBc\tDeF\t') == 'Abc Def'
assert string.capwords('\taBc\tDeF\t', '\t') == '\tAbc\tDef\t'

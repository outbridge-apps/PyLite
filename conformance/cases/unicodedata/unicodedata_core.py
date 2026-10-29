# Adapted from CPython's test suite (Lib/test/test_unicodedata.py: UnicodeFunctionsTest.test_digit /
# test_numeric / test_decimal / test_category / test_lookup / test_name, NormalizationTest).
# Python Software Foundation, PSF License Version 2 -- see README.
import unicodedata
from unicodedata import normalize, is_normalized, name, lookup, category, numeric, decimal, digit, east_asian_width, combining, bidirectional, mirrored, decomposition

# normalization: composition and decomposition round-trip, compatibility forms fold ligatures/circled digits
e_acute = 'é'
assert normalize('NFD', e_acute) == 'é' and normalize('NFC', 'é') == e_acute
assert normalize('NFKD', 'ﬁ') == 'fi' and normalize('NFKC', '①') == '1'
assert normalize('NFC', 'ascii stays') == 'ascii stays'
assert is_normalized('NFC', e_acute) and not is_normalized('NFD', e_acute) and is_normalized('NFD', 'é')
# dialect: a lone surrogate passes through, as in CPython
assert normalize('NFC', 'a\ud83db') == 'a\ud83db'
try:
    normalize('NFX', 'a')
    assert False
except ValueError as e:
    assert str(e) == 'invalid normalization form'

# the diacritic-stripping idiom
def strip_accents(s):
    return ''.join([c for c in normalize('NFD', s) if category(c) != 'Mn'])
assert strip_accents('Crème Brûlée') == 'Creme Brulee'

# name/lookup both ways, aliases and named sequences resolve, Hangul and CJK are rule-based
assert name('€') == 'EURO SIGN' and lookup('EURO SIGN') == '€'
assert name('\U0001F600') == 'GRINNING FACE' and lookup('grinning face') == '\U0001F600'
assert name('가') == 'HANGUL SYLLABLE GA' and lookup('HANGUL SYLLABLE GA') == '가'
assert name('一') == 'CJK UNIFIED IDEOGRAPH-4E00' and lookup('CJK UNIFIED IDEOGRAPH-4E00') == '一'
assert name('\x00', 'no name') == 'no name'
try:
    name('\x00')
    assert False
except ValueError as e:
    assert str(e) == 'no such name'
try:
    lookup('NO SUCH NAME')
    assert False
except KeyError as e:
    assert 'NO SUCH NAME' in str(e)
try:
    name('ab')
    assert False
except TypeError:
    pass

# categories and numeric properties
assert [category(c) for c in 'aA1 $'] == ['Ll', 'Lu', 'Nd', 'Zs', 'Sc']
assert category('́') == 'Mn' and category('\U0001F600') == 'So' and category('\n') == 'Cc'
assert numeric('½') == 0.5 and numeric('9') == 9.0 and numeric('a', -1) == -1
assert decimal('7') == 7 and decimal('٣') == 3 and decimal('a', None) is None
assert digit('²') == 2 and digit('7') == 7 and digit('½', -1) == -1
for fn in (numeric, decimal, digit):
    try:
        fn('x')
        assert False
    except ValueError:
        pass
assert unicodedata.unidata_version == '16.0.0'

# East Asian Width and canonical combining class (test_east_asian_width / test_combining)
assert east_asian_width('a') == 'Na' and east_asian_width('À') == 'N' and east_asian_width('¡') == 'A'
assert east_asian_width('あ') == 'W' and east_asian_width('Ａ') == 'F' and east_asian_width('｡') == 'H'
assert east_asian_width('\U0001F600') == 'W' and east_asian_width('\U0010FFFD') == 'A' and east_asian_width('\U0010FFFF') == 'N'
def display_width(s):
    return sum([2 if east_asian_width(c) in ('W', 'F') else 1 for c in s])
assert display_width('abあい') == 6
assert combining('a') == 0 and combining('́') == 230 and combining('̧') == 202 and combining('ְ') == 10
assert ''.join([c for c in normalize('NFD', 'Crème') if not combining(c)]) == 'Creme'

# bidi class and mirroring (test_bidirectional / test_mirrored): every assigned code point has a class,
# an unassigned one answers ''
assert [bidirectional(c) for c in 'aא1+$٠,́​\n\t !'] == ['L', 'R', 'EN', 'ES', 'ET', 'AN', 'CS', 'NSM', 'BN', 'B', 'S', 'WS', 'ON']
assert bidirectional('ا') == 'AL' and bidirectional('‪') == 'LRE' and bidirectional('⁦') == 'LRI'
assert bidirectional('一') == 'L' and bidirectional('가') == 'L' and bidirectional('\U0001F600') == 'ON'
assert bidirectional('\U000E0000') == '' and bidirectional('\U0010FFFF') == ''
assert [mirrored(c) for c in '([<a∈)'] == [1, 1, 1, 0, 1, 1]
def has_rtl(s):
    return any([bidirectional(c) in ('R', 'AL') for c in s])
assert has_rtl('abc שלום') and not has_rtl('abc 123')

# decomposition (test_decomposition): the UnicodeData field verbatim, '' when there is none
# (Hangul syllables included - CPython composes them algorithmically only inside normalize)
assert decomposition('À') == '0041 0300' and decomposition('½') == '<fraction> 0031 2044 0032'
assert decomposition('ﬁ') == '<compat> 0066 0069' and decomposition('①') == '<circle> 0031'
assert decomposition('a') == '' and decomposition('가') == '' and decomposition('\U0001F600') == ''
assert decomposition('\U0001D400') == '<font> 0041'
try:
    east_asian_width('ab')
    assert False
except TypeError:
    pass

# ---- long input: normalized in chunks, which must not change the answer ----
# Each unit is normalized on its own, so the concatenation is the reference for the whole string.
for form in ('NFC', 'NFD', 'NFKC', 'NFKD'):
    for unit in ('a' + chr(0x301) + 'bc ', chr(0x9C7) + chr(0x9BE) + 'x ', chr(0x1100) + chr(0x1161) + 'y ',
                 chr(0xFDFA) + ' ', chr(0x1F600) + 'z '):
        big = unit * 4000
        assert normalize(form, big) == normalize(form, unit) * 4000
        assert is_normalized(form, normalize(form, big))
assert normalize('NFC', 'a' + chr(0x301) * 4000) == chr(0xE1) + chr(0x301) * 3999
try:
    normalize('NFC', 'a' + chr(0x301) * 20000)   # no break point in a run that long: refused
    assert False
except ValueError:
    pass

# Adapted from CPython's test suite (Lib/test/test_textwrap.py: WrapTestCase.test_simple /
# test_whitespace / test_hyphenated / test_em_dash / test_break_long / test_initial_indent,
# MaxLinesTestCase, ShortenTestCase, DedentTestCase, IndentTestCase).
# Python Software Foundation, PSF License Version 2 -- see README.
from textwrap import wrap, fill, shorten, dedent, indent

text = "Hello there, how are you this fine day?  I'm glad to hear it!"
assert wrap(text, 12) == ['Hello there,', 'how are you', 'this fine', "day?  I'm", 'glad to hear', 'it!']
assert wrap(text, 42) == ['Hello there, how are you this fine day?', "I'm glad to hear it!"]
assert wrap(text, 80) == [text] and fill(text, 12) == '\n'.join(wrap(text, 12))
assert wrap('  This is a  paragraph that\n\tcontains tabs   and spaces.  ', 30) == ['  This is a  paragraph that', 'contains tabs   and spaces.']
assert wrap('this-is-a-useful-feature-for-reformatting-posts-from-tim-peters', 20) == ['this-is-a-useful-', 'feature-for-', 'reformatting-posts-', 'from-tim-peters']
assert wrap('Em-dashes should be written -- thus.', 25) == ['Em-dashes should be', 'written -- thus.']
long = 'Did you say "supercalifragilisticexpialidocious?" How *do* you spell that odd word, anyways?'
assert wrap(long, 30) == ['Did you say "supercalifragilis', 'ticexpialidocious?" How *do*', 'you spell that odd word,', 'anyways?']
assert wrap(long, 30, break_long_words=False) == ['Did you say', '"supercalifragilisticexpialidocious?"', 'How *do* you spell that odd', 'word, anyways?']
assert fill('This is a short line that wraps once.', 20, initial_indent='* ', subsequent_indent='  ') == '* This is a short\n  line that wraps\n  once.'
assert wrap('A short line.  Note the single space.', 30, fix_sentence_endings=True) == ['A short line.  Note the single', 'space.']
assert wrap('   ', 5) == [] and wrap('', 5) == []
assert wrap(text, 12, max_lines=2) == ['Hello there,', 'how [...]'] and wrap(text, 12, max_lines=3) == ['Hello there,', 'how are you', 'this [...]']
assert shorten('Hello  world!', width=12) == 'Hello world!' and shorten('Hello  world!', width=11) == 'Hello [...]'
assert shorten('The quick brown fox jumps over the lazy dog', 25) == 'The quick brown fox [...]'
assert shorten('Hello world', width=10, placeholder='...') == 'Hello...'
assert dedent('    hello\n      world\n    ') == 'hello\n  world\n' and dedent('\thello\n\t  world') == 'hello\n  world'
assert dedent('  a\n\n  b\n   \n') == 'a\n\nb\n\n' and dedent('a\n  b') == 'a\n  b'
assert indent('a\n\nb\n', '  ') == '  a\n\n  b\n' and indent('a\n \nb', '> ', lambda line: True) == '> a\n>  \n> b'
# the message-template idiom
body = fill(dedent('''\
    Dear customer,
    your order has shipped and should arrive within three business days.'''), width=40)
assert body == 'Dear customer, your order has shipped\nand should arrive within three business\ndays.'   # fill joins the paragraph
try:
    wrap('x', 0)
    assert False
except ValueError as e:
    assert str(e) == 'invalid width 0 (must be > 0)'

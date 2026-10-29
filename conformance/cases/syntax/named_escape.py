# A \N{NAME} escape is resolved while compiling, so a name the Unicode database does not carry is a
# SyntaxError - as in CPython. The names that DO resolve are in str/named_escapes.py.
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# expect: SyntaxError

s = '\N{NO SUCH CHARACTER NAME}'

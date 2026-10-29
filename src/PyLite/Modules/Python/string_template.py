# string.Template, adapted from CPython 3.12's Lib/string.py (Python Software Foundation, PSF License
# Version 2). The engine compiles this source once and runs it in each run that imports `string`.
# CPython builds a class's pattern once, in __init_subclass__; here it is built from the class's
# delimiter, idpattern, braceidpattern and flags when a template is used (re caches the compiled form).
# A subclass's own `pattern` (a str or a compiled pattern) replaces it, as in CPython; Template.pattern
# itself stays None.
import re as _re
from collections import ChainMap as _ChainMap

_sentinel_dict = {}


class Template:
    """A string class for supporting $-substitutions."""

    delimiter = '$'
    idpattern = r'(?a:[_a-z][_a-z0-9]*)'
    braceidpattern = None
    flags = _re.IGNORECASE
    pattern = None

    def __init__(self, template):
        self.template = template

    def _compiled(self):
        pattern = self.pattern
        if pattern is None:
            delim = _re.escape(self.delimiter)
            id = self.idpattern
            bid = self.braceidpattern or self.idpattern
            pattern = fr"""
            {delim}(?:
              (?P<escaped>{delim})  |   # Escape sequence of two delimiters
              (?P<named>{id})       |   # delimiter and a Python identifier
              {{(?P<braced>{bid})}} |   # delimiter and a braced identifier
              (?P<invalid>)             # Other ill-formed delimiter exprs
            )
            """
        if isinstance(pattern, str):
            pattern = _re.compile(pattern, self.flags | _re.VERBOSE)
        return pattern

    def _invalid(self, mo):
        i = mo.start('invalid')
        lines = self.template[:i].splitlines(keepends=True)
        if not lines:
            colno = 1
            lineno = 1
        else:
            colno = i - len(''.join(lines[:-1]))
            lineno = len(lines)
        raise ValueError('Invalid placeholder in string: line %d, col %d' %
                         (lineno, colno))

    def substitute(self, mapping=_sentinel_dict, /, **kws):
        if mapping is _sentinel_dict:
            mapping = kws
        elif kws:
            mapping = _ChainMap(kws, mapping)
        pattern = self._compiled()

        def convert(mo):
            named = mo.group('named') or mo.group('braced')
            if named is not None:
                return str(mapping[named])
            if mo.group('escaped') is not None:
                return self.delimiter
            if mo.group('invalid') is not None:
                self._invalid(mo)
            raise ValueError('Unrecognized named group in pattern', pattern)
        return pattern.sub(convert, self.template)

    def safe_substitute(self, mapping=_sentinel_dict, /, **kws):
        if mapping is _sentinel_dict:
            mapping = kws
        elif kws:
            mapping = _ChainMap(kws, mapping)
        pattern = self._compiled()

        def convert(mo):
            named = mo.group('named') or mo.group('braced')
            if named is not None:
                try:
                    return str(mapping[named])
                except KeyError:
                    return mo.group()
            if mo.group('escaped') is not None:
                return self.delimiter
            if mo.group('invalid') is not None:
                return mo.group()
            raise ValueError('Unrecognized named group in pattern', pattern)
        return pattern.sub(convert, self.template)

    def is_valid(self):
        pattern = self._compiled()
        for mo in pattern.finditer(self.template):
            if mo.group('invalid') is not None:
                return False
            if (mo.group('named') is None
                    and mo.group('braced') is None
                    and mo.group('escaped') is None):
                raise ValueError('Unrecognized named group in pattern', pattern)
        return True

    def get_identifiers(self):
        pattern = self._compiled()
        ids = []
        for mo in pattern.finditer(self.template):
            named = mo.group('named') or mo.group('braced')
            if named is not None and named not in ids:
                ids.append(named)
            elif (named is None
                    and mo.group('invalid') is None
                    and mo.group('escaped') is None):
                raise ValueError('Unrecognized named group in pattern', pattern)
        return ids

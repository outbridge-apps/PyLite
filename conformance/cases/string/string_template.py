# Adapted from CPython's test suite (Lib/test/test_string.py: TestTemplate). Python Software
# Foundation, PSF License Version 2 -- see conformance/README.md.
# dialect: the Mapping/Bag attribute-walking helpers of the original are plain dicts keyed by the
# dotted path; Template.pattern stays None unless a subclass sets one.
from string import Template

def raises(exc, f, *args, **kw):
    try:
        f(*args, **kw)
    except exc as e:
        return str(e) or type(e).__name__
    return None

# ---- test_regular_templates ----
s = Template('$who likes to eat a bag of $what worth $$100')
assert s.substitute(dict(who='tim', what='ham')) == 'tim likes to eat a bag of ham worth $100'
assert raises(KeyError, s.substitute, dict(who='tim')) == "'what'"
assert raises(TypeError, Template.substitute) is not None

# ---- braces, upper case, non-letters ----
s = Template('$who likes ${what} for ${meal}')
d = dict(who='tim', what='ham', meal='dinner')
assert s.substitute(d) == 'tim likes ham for dinner'
assert raises(KeyError, s.substitute, dict(who='tim', what='ham')) is not None
s = Template('$WHO likes ${WHAT} for ${MEAL}')
assert s.substitute(dict(WHO='tim', WHAT='ham', MEAL='dinner')) == 'tim likes ham for dinner'
s = Template('$_wh0_ likes ${_w_h_a_t_} for ${mea1}')
assert s.substitute(dict(_wh0_='tim', _w_h_a_t_='ham', mea1='dinner')) == 'tim likes ham for dinner'

# ---- test_escapes, test_percents, test_stringification, test_tupleargs ----
s = Template('$who likes to eat a bag of $$what worth $$100')
assert s.substitute(dict(who='tim', what='ham')) == 'tim likes to eat a bag of $what worth $100'
s = Template('$who likes $$')
assert s.substitute(dict(who='tim', what='ham')) == 'tim likes $'
s = Template('%(foo)s $foo ${foo}')
assert s.substitute(dict(foo='baz')) == '%(foo)s baz baz'
assert s.safe_substitute(dict(foo='baz')) == '%(foo)s baz baz'
s = Template('tim has eaten $count bags of ham today')
assert s.substitute(dict(count=7)) == 'tim has eaten 7 bags of ham today'
assert s.safe_substitute(dict(count=7)) == 'tim has eaten 7 bags of ham today'
s = Template('$who ate ${meal}')
d = dict(who=('tim', 'fred'), meal=('ham', 'kung pao'))
assert s.substitute(d) == "('tim', 'fred') ate ('ham', 'kung pao')"
assert s.safe_substitute(d) == "('tim', 'fred') ate ('ham', 'kung pao')"

# ---- test_SafeTemplate ----
s = Template('$who likes ${what} for ${meal}')
assert s.safe_substitute(dict(who='tim')) == 'tim likes ${what} for ${meal}'
assert s.safe_substitute(dict(what='ham')) == '$who likes ham for ${meal}'
assert s.safe_substitute(dict(what='ham', meal='dinner')) == '$who likes ham for dinner'
assert s.safe_substitute(dict(who='tim', what='ham')) == 'tim likes ham for ${meal}'
assert s.safe_substitute(dict(who='tim', what='ham', meal='dinner')) == 'tim likes ham for dinner'

# ---- test_invalid_placeholders ----
assert raises(ValueError, Template('$who likes $').substitute, dict(who='tim')) == 'Invalid placeholder in string: line 1, col 12'
assert raises(ValueError, Template('$who likes ${what)').substitute, dict(who='tim')) is not None
assert raises(ValueError, Template('$who likes $100').substitute, dict(who='tim')) is not None
assert raises(ValueError, Template('$who likes $ı').substitute, dict(who='tim')) is not None   # dotless i: ASCII only
assert raises(ValueError, Template('$who likes $İ').substitute, dict(who='tim')) is not None
assert raises(ValueError, Template('line one\n  $ two').substitute) == 'Invalid placeholder in string: line 2, col 3'

# ---- test_idpattern_override, test_flags_override, inside/outside braces ----
class PathPattern(Template):
    idpattern = r'[_a-z][._a-z0-9]*'
m = {'bag.foo.who': 'tim', 'bag.what': 'ham'}
s = PathPattern('$bag.foo.who likes to eat a bag of $bag.what')
assert s.substitute(m) == 'tim likes to eat a bag of ham'

class MyPattern(Template):
    flags = 0
s = MyPattern('$wHO likes ${WHAT} for ${meal}')
d = dict(wHO='tim', WHAT='ham', meal='dinner', w='fred')
assert raises(ValueError, s.substitute, d) is not None
assert s.safe_substitute(d) == 'fredHO likes ${WHAT} for dinner'

class InsideOutside(Template):
    idpattern = r'[a-z]+'
    braceidpattern = r'[A-Z]+'
    flags = 0
m = dict(foo='foo', BAR='BAR')
assert InsideOutside('$foo ${BAR}').substitute(m) == 'foo BAR'
assert raises(ValueError, InsideOutside('$FOO').substitute, m) is not None
assert raises(ValueError, InsideOutside('${bar}').substitute, m) is not None

# ---- test_pattern_override ----
class AtPattern(Template):
    pattern = r"""
    (?P<escaped>@{2})                   |
    @(?P<named>[_a-z][._a-z0-9]*)       |
    @{(?P<braced>[_a-z][._a-z0-9]*)}    |
    (?P<invalid>@)
    """
s = AtPattern('@bag.foo.who likes to eat a bag of @bag.what')
assert s.substitute({'bag.foo.who': 'tim', 'bag.what': 'ham'}) == 'tim likes to eat a bag of ham'

class BadPattern(Template):
    pattern = r"""
    (?P<badname>.*)                     |
    (?P<escaped>@{2})                   |
    @(?P<named>[_a-z][._a-z0-9]*)       |
    @{(?P<braced>[_a-z][._a-z0-9]*)}    |
    (?P<invalid>@)                      |
    """
s = BadPattern('@bag.foo.who likes to eat a bag of @bag.what')
assert raises(ValueError, s.substitute, {}) is not None
assert raises(ValueError, s.safe_substitute, {}) is not None
assert raises(ValueError, s.is_valid) is not None
assert raises(ValueError, s.get_identifiers) is not None

# ---- test_unicode_values, test_keyword_arguments(_safe) ----
s = Template('$who likes $what')
assert s.substitute(dict(who='t\xffm', what='f\xfe\fed')) == 't\xffm likes f\xfe\x0ced'
assert s.substitute(who='tim', what='ham') == 'tim likes ham'
assert s.substitute(dict(who='tim'), what='ham') == 'tim likes ham'
assert s.substitute(dict(who='fred', what='kung pao'), who='tim', what='ham') == 'tim likes ham'
s = Template('the mapping is $mapping')
assert s.substitute(dict(foo='none'), mapping='bozo') == 'the mapping is bozo'
assert s.substitute(dict(mapping='one'), mapping='two') == 'the mapping is two'
assert s.safe_substitute(dict(mapping='one'), mapping='two') == 'the mapping is two'
s = Template('the self is $self')
assert s.substitute(self='bozo') == 'the self is bozo'
assert s.safe_substitute(self='bozo') == 'the self is bozo'

# ---- test_delimiter_override ----
class AmpersandTemplate(Template):
    delimiter = '&'
s = AmpersandTemplate('this &gift is for &{who} &&')
assert s.substitute(gift='bud', who='you') == 'this bud is for you &'
assert raises(KeyError, s.substitute) is not None
assert s.safe_substitute(gift='bud', who='you') == 'this bud is for you &'
assert s.safe_substitute() == 'this &gift is for &{who} &'
s = AmpersandTemplate('this &gift is for &{who} &')
assert raises(ValueError, s.substitute, dict(gift='bud', who='you')) is not None
assert s.safe_substitute() == 'this &gift is for &{who} &'
class PieDelims(Template):
    delimiter = '@'
s = PieDelims('@who likes to eat a bag of @{what} worth $100')
assert s.substitute(dict(who='tim', what='ham')) == 'tim likes to eat a bag of ham worth $100'

# ---- test_is_valid, test_get_identifiers ----
assert Template('$who likes to eat a bag of ${what} worth $$100').is_valid()
assert not Template('$who likes to eat a bag of ${what} worth $100').is_valid()
assert Template('$who likes to eat a bag of ${what} worth $$100').get_identifiers() == ['who', 'what']
s = Template('$who likes to eat a bag of ${what} worth $$100; ${who} likes to eat a bag of $what worth $$100')
assert s.get_identifiers() == ['who', 'what']
assert Template('$who likes to eat a bag of ${what} worth $100').get_identifiers() == ['who', 'what']

# ---- the class itself ----
t = Template('$x')
assert isinstance(t, Template) and t.template == '$x'
assert Template.delimiter == '$' and Template.braceidpattern is None

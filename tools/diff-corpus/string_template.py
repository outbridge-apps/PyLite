from string import Template
s = Template('$who likes ${what}; $$100 and $who again')
errors = []
try:
    s.substitute(who='tim')
except KeyError as e:
    errors.append(type(e).__name__)
try:
    Template('$who likes $').substitute(who='x')
except ValueError as e:
    errors.append(str(e))
class Amp(Template):
    delimiter = '&'
class Path(Template):
    idpattern = '[_a-z][._a-z0-9]*'
result = [s.substitute(who='tim', what='ham'), s.substitute({'who': 'ann'}, what='tea'),
          s.safe_substitute(who='bob'), Template('$n items').substitute(n=7),
          Amp('&gift for &{who} &&').substitute(gift='bud', who='you'),
          Path('$a.b and $c').substitute({'a.b': 1, 'c': 2}), errors]

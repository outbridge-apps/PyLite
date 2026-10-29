# Features shared with the IronPython 3.4.2 oracle. Underscore grouping (PEP 515, 3.6+) is
# unit-tested separately — IronPython 3.4.2 predates it and would raise ValueError here.
n = 1234567
result = [
    format(n, ','),
    f"{n:,}",
    '%(name)s=%(v)d' % {'name': 'qty', 'v': 42},
    '%(x).2f' % {'x': 3.14159},
    '%s' % {'k': 1},
    '{0} {1} {0}'.format('a', 'b'),
    f"{3.14159:>10.2f}",
    format(255, '#x'),
]

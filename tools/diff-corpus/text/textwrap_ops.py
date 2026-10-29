import textwrap

text = ('The quick brown fox jumps over the lazy dog. Pack my box with five dozen liquor jugs. '
        'How vexingly quick daft zebras jump!')
out = []
out.append(textwrap.wrap(text, 30))
out.append(textwrap.fill(text, 40))
out.append(textwrap.fill(text, 25, initial_indent='* ', subsequent_indent='  '))
out.append(textwrap.shorten(text, 40))
out.append(textwrap.shorten('Hello  world!', width=12, placeholder='...'))
out.append(textwrap.dedent('    a\n      b\n    c\n'))
out.append(textwrap.indent('a\n\nb\n', '> '))
out.append(textwrap.wrap('supercalifragilisticexpialidocious', 10))
out.append(textwrap.wrap('well-known long-standing', 12))
out.append(textwrap.wrap('', 10))
out.append(textwrap.fill('x ' * 5, 3))
result = out

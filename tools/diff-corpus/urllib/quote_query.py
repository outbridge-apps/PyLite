from urllib.parse import quote, quote_plus, unquote, unquote_plus, parse_qs, parse_qsl, urlencode

out = []
out.append(quote('a b/c?d=é&ж'))
out.append(quote('a b/c', safe=''))
out.append(quote_plus('a b+c&d'))
out.append(unquote('caf%C3%A9%20x%2F'))
out.append(unquote_plus('a+b%2Bc'))
out.append(unquote('%E0%A4%A'))
out.append(sorted(parse_qs('a=1&b=2&a=3&c=').items()))
out.append(sorted(parse_qs('a=1&c=', keep_blank_values=True).items()))
out.append(parse_qsl('x=1&y=a+b&z=%26'))
out.append(urlencode([('q', 'tea & cake'), ('n', 5)]))
out.append(urlencode({'k': ['a', 'b']}, doseq=True))
out.append(urlencode([('k', 'é')]))
out.append(quote('-._'))
result = out

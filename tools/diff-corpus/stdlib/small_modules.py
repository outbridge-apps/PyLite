import bisect
import heapq
import operator
import copy
import html
import calendar
import uuid

out = []
grades = [60, 70, 80, 90]
out.append([bisect.bisect(grades, s) for s in (55, 70, 85, 99)])
out.append((bisect.bisect_left(grades, 70), bisect.bisect_right(grades, 70)))
lst = [1, 3, 5]
bisect.insort(lst, 4)
bisect.insort_left(lst, 3)
out.append(lst)
h = [5, 1, 8, 3, 9, 2]
heapq.heapify(h)
out.append([heapq.heappop(h) for _ in range(3)])
heapq.heappush(h, 0)
out.append((h[0], heapq.nlargest(2, [4, 1, 7, 3]), heapq.nsmallest(2, [4, 1, 7, 3], key=lambda x: -x)))
out.append(list(heapq.merge([1, 4, 7], [2, 5], [3])))
out.append((heapq.heappushpop([2, 4], 1), heapq.heapreplace([2, 4], 1)))
rows = [{'n': 'b', 'v': 2}, {'n': 'a', 'v': 2}, {'n': 'c', 'v': 1}]
out.append([r['n'] for r in sorted(rows, key=operator.itemgetter('v', 'n'))])
out.append((operator.itemgetter(1, 0)('ab'), operator.methodcaller('upper')('x'), operator.add(2, 3)))
out.append((operator.mul('ab', 2), operator.truediv(7, 2), operator.neg(4), operator.contains([1], 1)))
orig = {'a': [1, [2, 3]], 'b': (4, [5])}
sh = copy.copy(orig)
dp = copy.deepcopy(orig)
orig['a'][1].append(9)
out.append((sh['a'][1], dp['a'][1], sh is orig, dp == orig))
out.append(html.escape('<a href="x">Tom & "Jerry" \'s</a>'))
out.append(html.escape('<b>', quote=False))
out.append(html.unescape('&lt;p&gt; &amp;amp; &eacute; &#233; &#x41; &copy;'))
out.append((calendar.isleap(2024), calendar.isleap(1900), calendar.leapdays(2000, 2026)))
out.append((calendar.monthrange(2026, 2), calendar.weekday(2026, 10, 29), calendar.timegm((2026, 1, 1, 0, 0, 0))))
out.append(calendar.monthcalendar(2026, 2))
out.append((calendar.month_name[10], calendar.day_abbr[0]))
u = uuid.UUID('12345678-1234-5678-1234-567812345678')
out.append((str(u), u.hex, u.version, str(uuid.UUID(bytes=u.bytes))))
out.append(str(uuid.uuid3(uuid.NAMESPACE_DNS, 'example.org')))
out.append(str(uuid.uuid5(uuid.NAMESPACE_URL, 'https://example.org/x')))
out.append(len(str(uuid.uuid4())))
result = out

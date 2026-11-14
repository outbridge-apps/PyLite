# repeat: 2
import zoneinfo, datetime, string, unicodedata, typing, hashlib, hmac, zipfile, io, random, statistics
import textwrap, calendar, itertools, struct, base64, gzip, zlib, heapq, bisect, operator, fractions, math
import urllib.parse, email.utils, uuid, html, binascii, copy, contextlib, functools, collections, enum, dataclasses

z = zoneinfo.ZoneInfo("Europe/Berlin")
d = datetime.datetime(2024, 3, 31, 1, 30, tzinfo=z) + datetime.timedelta(hours=2)
acc = d.utcoffset().seconds
acc += len(string.Template("$x/${y}").safe_substitute(x="a"))
acc += len(unicodedata.normalize("NFKD", "Åﬁ①" * 20)) + len(unicodedata.name("A"))
L = typing.Literal[1, 2, "a"]
acc += (L == typing.Literal[2, 1, "a"])
acc += len(hashlib.sha256(b"x" * 1000).hexdigest()) + len(hmac.new(b"k", b"m", "sha1").hexdigest())
buf = io.BytesIO()
with zipfile.ZipFile(buf, "w") as zf:
    zf.writestr("a.txt", "hello" * 100)
with zipfile.ZipFile(io.BytesIO(buf.getvalue())) as zf:
    acc += len(zf.read("a.txt"))
random.seed(5)
r = random
acc += int(statistics.mean([r.random() for _ in range(200)]) * 100)
acc += len(textwrap.fill("lorem ipsum dolor sit amet " * 20, 30)) + len(calendar.month(2024, 2))
acc += sum(1 for _ in itertools.combinations(range(8), 3)) + len(struct.pack("<iqd", 1, 2, 3.0))
acc += len(base64.b64encode(gzip.compress(b"abc" * 100))) + len(zlib.decompress(zlib.compress(b"q" * 500)))
h = list(range(50, 0, -1))
heapq.heapify(h)
acc += heapq.heappop(h) + bisect.bisect([1, 3, 5], 4) + operator.itemgetter(1)((5, 6)) + operator.attrgetter("real")(3)
acc += fractions.Fraction(3, 7).numerator + math.comb(30, 10) % 9
acc += len(urllib.parse.quote("a b/c?d")) + len(email.utils.formatdate(0, usegmt=True)) + len(html.escape("<a>"))
acc += len(str(uuid.UUID(int=5))) + len(binascii.hexlify(b"abc"))
ChainM = collections.ChainMap({"a": 1}, {"b": 2})
acc += len(copy.deepcopy(ChainM)) + len(collections.Counter("abracadabra").most_common(2))
@functools.cache
def sq(n):
    return n * n
acc += sum(sq(i % 10) for i in range(100))
class F(enum.IntEnum):
    A = 1
    B = 2
acc += F.A + F.B
@dataclasses.dataclass(frozen=True)
class Q:
    a: int
    b: tuple = ()
acc += hash(Q(1)) & 1 + len(dataclasses.asdict(Q(2, (1, 2))))
with contextlib.suppress(KeyError):
    {}["x"]
print(acc)

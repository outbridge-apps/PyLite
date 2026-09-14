# What is implemented

The surface below was read off a running engine at version 1.0.0 — every name in it is what
`dir()` returns for that module — so it says what exists, not what was intended. It does not say
how each name behaves; where behaviour differs from CPython, that belongs in `deviations.md`.

## Builtins

`abs` `all` `any` `ascii` `bin` `bool` `bytes` `callable` `chr` `dict` `dir` `divmod` `enumerate` `filter` `float` `format` `frozenset` `getattr` `hasattr` `hash` `hex` `id` `int` `isinstance` `issubclass` `iter` `len` `list` `map` `max` `min` `next` `oct` `ord` `pow` `print` `range` `repr` `reversed` `round` `set` `setattr` `slice` `sorted` `str` `sum` `tuple` `type` `zip`

Absent by design. Using one raises `NameError: name 'eval' is not defined: this builtin is not
supported in this environment`:

`bytearray` `classmethod` `complex` `eval` `exec` `globals` `input` `locals` `memoryview` `object` `open` `property` `staticmethod` `super` `vars`

`import` reaches the modules listed below and the modules the host registers, nothing else.

## Methods on the built-in types

**str** — `capitalize` `casefold` `center` `count` `encode` `endswith` `expandtabs` `find` `format` `format_map` `index` `isalnum` `isalpha` `isascii` `isdecimal` `isdigit` `isidentifier` `islower` `isnumeric` `isprintable` `isspace` `istitle` `isupper` `join` `ljust` `lower` `lstrip` `partition` `removeprefix` `removesuffix` `replace` `rfind` `rindex` `rjust` `rpartition` `rsplit` `rstrip` `split` `splitlines` `startswith` `strip` `swapcase` `title` `translate` `upper` `zfill`

**bytes** — `capitalize` `center` `count` `decode` `endswith` `expandtabs` `find` `hex` `index` `isalnum` `isalpha` `isascii` `isdigit` `islower` `isspace` `istitle` `isupper` `join` `ljust` `lower` `lstrip` `partition` `removeprefix` `removesuffix` `replace` `rfind` `rindex` `rjust` `rpartition` `rsplit` `rstrip` `split` `splitlines` `startswith` `strip` `swapcase` `title` `translate` `upper` `zfill`

**list** — `append` `clear` `copy` `count` `extend` `index` `insert` `pop` `remove` `reverse` `sort`

**dict** — `clear` `copy` `get` `items` `keys` `pop` `popitem` `setdefault` `update` `values`

**set** — `add` `clear` `copy` `difference` `difference_update` `discard` `intersection` `intersection_update` `isdisjoint` `issubset` `issuperset` `pop` `remove` `symmetric_difference` `symmetric_difference_update` `union` `update`

**tuple** — `count` `index`

**int** — `as_integer_ratio` `bit_count` `bit_length` `conjugate` `denominator` `imag` `numerator` `real` `to_bytes`

**float** — `as_integer_ratio` `conjugate` `hex` `imag` `is_integer` `real`

## Modules

### math

`acos` `acosh` `asin` `asinh` `atan` `atan2` `atanh` `cbrt` `ceil` `comb` `copysign` `cos` `cosh` `degrees` `dist` `e` `exp` `exp2` `expm1` `fabs` `factorial` `floor` `fma` `fmod` `frexp` `fsum` `gcd` `hypot` `inf` `isclose` `isfinite` `isinf` `isnan` `isqrt` `lcm` `ldexp` `log` `log10` `log1p` `log2` `modf` `nan` `nextafter` `perm` `pi` `pow` `prod` `radians` `remainder` `sin` `sinh` `sqrt` `sumprod` `tan` `tanh` `tau` `trunc` `ulp`

### decimal

`Decimal` `DecimalException` `DivisionByZero` `InvalidOperation` `Overflow` `ROUND_05UP` `ROUND_CEILING` `ROUND_DOWN` `ROUND_FLOOR` `ROUND_HALF_DOWN` `ROUND_HALF_EVEN` `ROUND_HALF_UP` `ROUND_UP` `Underflow` `getcontext`

### fractions

`Fraction`

### statistics

`StatisticsError` `correlation` `covariance` `fmean` `geometric_mean` `harmonic_mean` `linear_regression` `mean` `median` `median_grouped` `median_high` `median_low` `mode` `multimode` `pstdev` `pvariance` `quantiles` `stdev` `variance`

### random

`betavariate` `choice` `choices` `expovariate` `gammavariate` `gauss` `getrandbits` `normalvariate` `randbytes` `randint` `random` `randrange` `sample` `seed` `shuffle` `triangular` `uniform`

### secrets

`DEFAULT_ENTROPY` `choice` `compare_digest` `randbelow` `randbits` `token_bytes` `token_hex` `token_urlsafe`

### json

`JSONDecodeError` `dump` `dumps` `load` `loads`

### re

`A` `ASCII` `DOTALL` `I` `IGNORECASE` `L` `LOCALE` `M` `MULTILINE` `S` `U` `UNICODE` `VERBOSE` `X` `compile` `error` `escape` `findall` `finditer` `fullmatch` `match` `purge` `search` `split` `sub` `subn`

### datetime

`MAXYEAR` `MINYEAR` `date` `datetime` `time` `timedelta` `timezone` `tzinfo`

### time

`altzone` `asctime` `ctime` `daylight` `gmtime` `localtime` `mktime` `monotonic` `perf_counter` `sleep` `strftime` `strptime` `struct_time` `time` `timezone` `tzname`

### calendar

`APRIL` `AUGUST` `DECEMBER` `FEBRUARY` `FRIDAY` `IllegalMonthError` `IllegalWeekdayError` `JANUARY` `JULY` `JUNE` `MARCH` `MAY` `MONDAY` `NOVEMBER` `OCTOBER` `SATURDAY` `SEPTEMBER` `SUNDAY` `THURSDAY` `TUESDAY` `WEDNESDAY` `day_abbr` `day_name` `error` `firstweekday` `isleap` `leapdays` `mdays` `month` `month_abbr` `month_name` `monthcalendar` `monthrange` `timegm` `weekday` `weekheader`

### zoneinfo

`TZPATH` `ZoneInfo` `ZoneInfoNotFoundError` `available_timezones`

### collections

`ChainMap` `Counter` `OrderedDict` `defaultdict` `deque` `namedtuple`

### itertools

`accumulate` `batched` `chain` `combinations` `combinations_with_replacement` `compress` `count` `cycle` `dropwhile` `filterfalse` `groupby` `islice` `pairwise` `permutations` `product` `repeat` `starmap` `takewhile` `tee` `zip_longest`

### functools

`cache` `cmp_to_key` `lru_cache` `partial` `reduce` `update_wrapper` `wraps`

### operator

`abs` `add` `and_` `attrgetter` `concat` `contains` `countOf` `delitem` `eq` `floordiv` `ge` `getitem` `gt` `index` `indexOf` `inv` `invert` `is_` `is_not` `itemgetter` `le` `lshift` `lt` `methodcaller` `mod` `mul` `ne` `neg` `not_` `or_` `pos` `pow` `rshift` `setitem` `sub` `truediv` `truth` `xor`

### copy

`Error` `copy` `deepcopy`

### copyreg

`_extension_cache` `_extension_registry` `_inverted_registry` `add_extension` `clear_extension_cache` `constructor` `dispatch_table` `pickle` `remove_extension`

### dataclasses

`InitVar` `MISSING` `asdict` `astuple` `dataclass` `field` `fields` `is_dataclass` `make_dataclass` `replace`

### typing

`AbstractSet` `Annotated` `Any` `AnyStr` `BinaryIO` `Callable` `ChainMap` `ClassVar` `Collection` `Concatenate` `Container` `Counter` `DefaultDict` `Deque` `Dict` `Final` `FrozenSet` `Generator` `Generic` `Hashable` `IO` `Iterable` `Iterator` `List` `Literal` `LiteralString` `Mapping` `MutableMapping` `MutableSequence` `MutableSet` `NamedTuple` `Never` `NewType` `NoReturn` `NotRequired` `Optional` `OrderedDict` `ParamSpec` `Protocol` `Required` `Reversible` `Self` `Sequence` `Set` `Sized` `TYPE_CHECKING` `Text` `TextIO` `Tuple` `Type` `TypeAlias` `TypeVar` `TypeVarTuple` `TypedDict` `Union` `Unpack` `assert_type` `cast` `final` `get_args` `get_origin` `get_type_hints` `no_type_check` `overload` `override` `runtime_checkable`

### string

`ascii_letters` `ascii_lowercase` `ascii_uppercase` `capwords` `digits` `hexdigits` `octdigits` `printable` `punctuation` `whitespace`

### textwrap

`dedent` `fill` `indent` `shorten` `wrap`

### unicodedata

`bidirectional` `category` `combining` `decimal` `decomposition` `digit` `east_asian_width` `is_normalized` `lookup` `mirrored` `name` `normalize` `numeric` `unidata_version`

### html

`escape` `unescape`

### base64

`b16decode` `b16encode` `b32decode` `b32encode` `b64decode` `b64encode` `standard_b64decode` `standard_b64encode` `urlsafe_b64decode` `urlsafe_b64encode`

### binascii

`Error` `a2b_base64` `a2b_hex` `b2a_base64` `b2a_hex` `crc32` `hexlify` `unhexlify`

### hashlib

`md5` `new` `pbkdf2_hmac` `sha1` `sha224` `sha256` `sha384` `sha512`

### hmac

`compare_digest` `new`

### struct

`calcsize` `error` `iter_unpack` `pack` `unpack` `unpack_from`

### zlib

`DEFLATED` `MAX_WBITS` `Z_BEST_COMPRESSION` `Z_BEST_SPEED` `Z_DEFAULT_COMPRESSION` `Z_NO_COMPRESSION` `adler32` `compress` `crc32` `decompress` `error`

### gzip

`BadGzipFile` `compress` `decompress`

### csv

`DictReader` `DictWriter` `Error` `QUOTE_ALL` `QUOTE_MINIMAL` `QUOTE_NONE` `QUOTE_NONNUMERIC` `QUOTE_NOTNULL` `QUOTE_STRINGS` `Sniffer` `__version__` `excel` `excel_tab` `field_size_limit` `get_dialect` `list_dialects` `reader` `register_dialect` `unix_dialect` `unregister_dialect` `writer`

### io

`StringIO`

### uuid

`NAMESPACE_DNS` `NAMESPACE_OID` `NAMESPACE_URL` `NAMESPACE_X500` `RESERVED_FUTURE` `RESERVED_MICROSOFT` `RESERVED_NCS` `RFC_4122` `UUID` `uuid1` `uuid3` `uuid4` `uuid5`

### bisect

`bisect` `bisect_left` `bisect_right` `insort` `insort_left` `insort_right`

### heapq

`heapify` `heappop` `heappush` `heappushpop` `heapreplace` `merge` `nlargest` `nsmallest`

### urllib.parse

`DefragResult` `ParseResult` `SplitResult` `clear_cache` `parse_qs` `parse_qsl` `quote` `quote_plus` `scheme_chars` `unquote` `unquote_plus` `unwrap` `urldefrag` `urlencode` `urljoin` `urlparse` `urlsplit` `urlunparse` `urlunsplit` `uses_netloc` `uses_params` `uses_relative`

### xml.etree.ElementTree

`Comment` `Element` `ElementTree` `PI` `ParseError` `ProcessingInstruction` `QName` `SubElement` `XML` `XMLID` `dump` `fromstring` `fromstringlist` `indent` `iselement` `iterparse` `parse` `register_namespace` `tostring` `tostringlist`

### traceback

`format_exc` `format_exception` `format_exception_only` `print_exc` `print_exception`


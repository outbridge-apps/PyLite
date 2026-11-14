# Deviations from CPython

PyLite runs a Python dialect, not CPython. The contract is the same syntax and the same public API
doing the same thing; byte-for-byte equality is not a goal, and in the places listed here the
behaviour is knowingly different. Everything below was run on the engine at version 1.2.0 — the
outputs are what it actually prints, not what it is meant to print. The reference is CPython 3.14.

What is missing entirely — language features, builtins and module APIs — is in
[modules.md](modules.md). This file is about things that exist and behave differently.

## Text

**A `str` is a sequence of UTF-16 code units, not of code points.** The engine's strings are .NET
strings, and nothing is re-encoded on the way in. A character outside the Basic Multilingual Plane
therefore counts as two units:

```python
len('\U0001F600')            # 2, CPython says 1
'\U0001F600'[0]              # '\ud83d' — one half of the pair
'a\U0001F600b'[0:2]          # 'a\ud83d' — a slice can cut a character in half
len([c for c in '\U0001F600'])   # 2 — so can iteration
''.join(reversed('\U0001F600'))  # not the same string back to front
```

This is the one deviation that can produce a broken string rather than a different answer, so it is
worth knowing where it bites: emoji, the rarer CJK, musical and mathematical symbols — anything
above U+FFFF. Text that stays inside the Basic Multilingual Plane, which includes every European
and most Asian script, behaves exactly as in CPython.

The rest of the engine is not fooled by it. `ord('\U0001F600')` is `128512` and `chr(128512)` gives
those two units back, so the pair is read as one character where a character is what is being
asked for. Everything with a Unicode table behind it — `unicodedata.name`, case mapping,
`str.translate`, `isidentifier` — works in code points. `re`, on the other hand, matches in units:
`re.findall('.', 'a\U0001F600b')` finds four matches, not three, and match offsets are unit
offsets.

Encoding a whole character is unaffected (`'\U0001F600'.encode('utf-8')` is 4 bytes, as in
CPython); encoding a half raises `UnicodeEncodeError: surrogates not allowed`, also as in CPython.
A half handed back to the host arrives as the bare surrogate — not a replacement character — so a
host that writes it out as UTF-8 meets the same error on its side.

Comparison and sorting are ordinal over the same units, which puts an astral character before
`U+FFFD` where CPython puts it after.

Combining marks are not affected by any of this: a base letter plus a combining accent is two
characters and the precomposed letter is one, here and in CPython alike — those are code points,
not a surrogate pair.

Case mapping is Unicode 16.0 and includes the mappings that change length: `'straße'.upper()` is
`'STRASSE'`, so `len(s.upper()) == len(s)` does not hold — as in CPython. `unicodedata` reads the
same Unicode 16.0 data for names, lookup and normalization, but `category`, `numeric`, `decimal` and
`digit` read the platform's older tables, so a character added after Unicode 8 is `'Cn'` there
(`unicodedata.category('\U0001E030')`, `'Lm'` in CPython). Lone surrogates do not leave the engine:
the `surrogatepass` error handler does not exist.

## Regular expressions

`re` runs on .NET's regex engine through a translation of the pattern, so most of sre's behaviour
holds and a few corners do not:

- Matching is in UTF-16 units (see Text): `.` matches each half of an astral character, and a class
  range over astral characters is `bad character range`.
- An empty match right after the previous match follows .NET's rule: `re.sub('a*?', '-', 'aa')` is
  `'-a-a-'`, where CPython gives `'-----'`.
- With `re.IGNORECASE`, a literal, an escaped letter (`\x6b`) and a single class member fold as sre
  folds them (the Kelvin sign matches `k`, `ı` and `İ` match `i`); a class range, a backreference, an
  octal or named escape and an astral letter fold the way .NET folds them.
- In a `re.VERBOSE` pattern, a raw vertical tab (`chr(11)`) is a literal character, where sre skips
  it as white space; the escapes `\v` and `\x0b` and every other white-space character behave as in
  CPython.
- Bytes patterns are not supported: `re.search(b'a', b'xa')` is a `TypeError`; decode to `str`.

A regex that would backtrack forever stops on its own time budget, and compiling a pattern charges the
memory it keeps to the run (see What stops a run).

## Numbers

`int` is arbitrary precision and bounded only by the run's limit (a million bits by default):
`10 ** 100000` is fine. Its decimal text is limited as in CPython: `str()` of an int of more than
4,300 digits is a `ValueError`, and since there is no `sys`, the limit cannot be raised; `hex()`
has no limit. `float` is IEEE-754 double, and since 1.2.0 `repr` is CPython's: the shortest digits
that read back (`0.1 + 0.2` is `0.30000000000000004`, `2.675` is `2.675`), and parsing a float is
correctly rounded. Rounding is half-to-even (`round(2.5)` is `2`, `round(3.5)` is `4`) exactly as in
CPython.

There is no `complex`: `(-8.0) ** (1/3)` is `ValueError: negative number cannot be raised to a
fractional power`. `math.sin`, `cos`, `tan`, `expm1`, `erf` and `erfc` use fdlibm's code on every
platform, and `gamma` and `lgamma` CPython's code over .NET's `exp` and `pow`; CPython uses the
platform's C library, so a last bit can differ between the two, as it does between two C libraries.
`random.seed(n)` gives a stream of its own, not CPython's Mersenne Twister sequence.

**`decimal` has a range, and it is small.** The type is backed by the platform's 128-bit decimal:
28–29 significant digits, largest value `79228162514264337593543950335`, smallest non-zero
`1E-28`. CPython's `decimal` has an exponent range of about ±10^18 and a precision you set.

```python
Decimal(1) / Decimal(7)   # 0.1428571428571428571428571429
Decimal('1e28')           # 1E+28
Decimal('1e29')           # decimal.Overflow
Decimal('1e-40')          # decimal.Underflow
Decimal(10) ** 40         # decimal.Overflow
```

This covers money, which is what the type is for here, and does not cover scientific work. There
are no special values: `Decimal('Infinity')` and `Decimal('NaN')` are `InvalidOperation`. The
context object carries `prec` and `rounding` only — there is no `Emax`, `Emin`, flags or traps;
`localcontext()` and `setcontext()` work with those two. A literal with more significant digits than
the backend holds is rounded to the places that fit, where CPython keeps it exact:
`Decimal('31.41592653589793238462643383279')` is `31.415926535897932384626433833`.

## Classes and objects

Classes are record classes: a class body declares fields and methods, and the engine generates the
value semantics. Single inheritance only.

```python
class A:
    def __init__(self, x):
        self.x = x

A(1) == A(1)   # False — identity, as for a bare class in CPython
repr(A(1))     # 'A(x=1)' where CPython prints '<A object at 0x...>'
```

No address is ever printed by this engine, and `id()` is a counter handed out on demand
(`id(a) == id(b)` exactly when `a is b`), so a run is reproducible.

`@dataclass` behaves as in CPython for the parts that exist: `==` by fields, unhashable unless
`frozen=True`, and `frozen=True` hashes by field values.

A class body may also hold constants (`NEW = 'new'`, read as `Status.NEW` or `self.NEW`, shadowed by
a subclass, settable on the class) and a docstring. An instance's fields are fixed by its class, so
an instance gets its own value for a constant only when `__init__` assigns `self.NEW`; anywhere else
that assignment is an `AttributeError`. Of the dunder names only `__match_args__`, `__slots__`
(accepted; the fields are fixed anyway) and `__hash__ = None` may be assigned in the body. Lambdas
and comprehensions in the body see the constants above them, where CPython hides the class scope
from them.

`super()` and `super(Class, self)` work as in CPython for single inheritance; `type(obj)` is the
object's class. There is no one-argument `super`, and `super()` inside a nested function or lambda
is `RuntimeError: super(): no arguments`.

Twenty-three protocol methods may be defined: `__init__`, `__post_init__`, `__repr__`, `__str__`, the
six comparisons, `__hash__`, `__bool__`, `__len__`, `__call__`, `__contains__`, the item and iteration
protocols, and `__enter__`/`__exit__` for `with`. Any other dunder is refused when the class is
defined rather than ignored at runtime:

```python
class A:
    def __add__(self, o):     # SyntaxError: this dialect does not support '__add__' definitions
        return 1

class B(X, Y):                # SyntaxError: multiple inheritance is not supported in this dialect
    pass
```

An exception class body holds `pass`, a docstring and constants (`class ApiError(Exception): CODE =
500`, read as `ApiError.CODE` and `e.CODE`); a method or a field is a `TypeError`. Exceptions are data
here; behaviour belongs in record classes.

**There is no introspection to climb.** The attributes that lead from a value to the interpreter's
internals do not exist: `__dict__`, `__globals__`, `__code__`, `__closure__`, `__func__`, `__self__`,
`__doc__` of a function, `__mro__`, `__bases__`, `__subclasses__()`, and `__class__` everywhere but
on an instance of a script class (where it is the class, as `type()` is). Neither do `object`, `vars`,
`eval`, `exec`, `compile`, `globals`, `locals`, `__import__`, `__builtins__` or `open`. Each is an
ordinary `AttributeError` or `NameError`; the format-string attribute walk
(`'{0.__init__.__globals__}'.format(x)`) meets the same wall. This is the sandbox, not a gap: `tests/PyLite.Tests/Hosting/IntrospectionEscapeTests.cs`
holds the usual escape routes to it.

Modules are read-only: `setattr(math, 'pi', 3)` raises
`AttributeError: module 'math' is read-only in this environment`, because the module registry is
built once and shared by every run. Static members live on the type only — `str.maketrans` works,
`'a'.maketrans` does not.

## What stops a run

Two different things can end a script, and the difference matters when you write `except`.

**Resource limits are not exceptions.** Steps, deadline, allocated memory, call depth, output
bytes, regex time and total sleep are enforced on a channel the script cannot see. The run ends,
the host gets a `BudgetExceeded` result, and no `except` in the script catches it:

```python
try:
    while True:
        pass
except Exception:
    result = 'caught'      # never reached: script exceeded step limit (200000000)
```

CPython would raise `MemoryError`, `RecursionError` or `KeyboardInterrupt` here, all catchable.
A context manager cannot catch one either: `with contextlib.suppress(Exception):` around that loop
ends the same way, and `__exit__` is not called with it.

**Data depth is an exception,** because it is a property of the data rather than of the script's
control flow. Hashing or comparing containers nested deeper than 128 raises a catchable
`RecursionError: maximum recursion depth exceeded in comparison`, and `json.dumps` of such data
`RecursionError: maximum recursion depth exceeded while encoding a JSON object`, as CPython does past
its recursion limit of about 1000. `repr` of lists, dicts and tuples is not bounded that way — it
walks its own stack and prints a list nested 3000 deep, where CPython raises `RecursionError`; `repr`
of a `deque`, a `ChainMap` or another container whose `repr` calls back into script values counts
two levels for each, so such containers nested 65 deep raise `RecursionError`.

The defaults a host starts from: 200,000,000 steps, a 20-second deadline, 256 MB allocated,
200 frames of call depth, 128 of data depth, 16,000,000 characters in one string, 5,000,000 items
in one collection, 1,000,000 bits in one int, 16 MB of output, 250 ms per regex operation and
5 seconds of total `time.sleep`. Every one of them is per-run and set by the host, which can lower
them for a run and raise them for the engine; [`examples/limits`](../examples/limits) has one script
per limit with the result it gets.

A regex that would backtrack forever stops on its own budget:
`re.match(r'(a+)+b', 'a' * 40)` ends the run with `regular expression exceeded time limit` instead
of hanging. Flags, back-references and look-behind all work. A compiled pattern is charged the memory
.NET keeps for it while the pattern is alive, so a script that keeps many large patterns meets
`MaxAllocBytes`; the charge errs on the high side.

Some things are capped where CPython has no cap, because without one a single call could run past
the deadline or take the host's memory:

- `functools.lru_cache(maxsize=None)`, `functools.cache` and a `maxsize` above 4,096 keep at most
  4,096 entries, evicting the least recently used; `cache_info().maxsize` reports 4096.
- An XML start tag with more than 10,000 attributes is a `ParseError`; walking an XML tree that
  contains itself (`a.append(a)`) ends on `MaxCollectionItems` instead of looping.
- `struct.unpack`, `itertools.product(..., repeat=r)` and `combinations_with_replacement` stop at
  `MaxCollectionItems` items per result, and so do slice assignment and `list.insert`.
- `traceback.format_exc()` and its kind end the run on `MaxStrChars` like any other string.
- Private names in class bodies whose mangled forms come to more than 2,097,152 characters in one
  source are a `SyntaxError`, and blocks nest at most 64 deep
  (`statement too deeply nested (limit 64)`, where CPython takes 99).

A traceback carries script frames only, at most 32, with `[... N more frames omitted ...]` in the
middle.

## json

`json.loads` is strict RFC 8259 — a trailing comma, a comment, a single-quoted string, a leading
zero or `1.` are all `JSONDecodeError` with CPython's message and position. `NaN`, `Infinity` and
`-Infinity` are accepted, as in CPython. Four differences:

| | CPython | PyLite |
|---|---|---|
| a raw control character other than CR/LF inside a string | `JSONDecodeError` | accepted, the character kept |
| a number that overflows `double` (`1e400`) | `inf` | `JSONDecodeError` |
| an integer of more than about 380 digits | exact `int` | `JSONDecodeError` |
| a document nested deeper than the data-depth limit (128) | `RecursionError` past about 1000 | `JSONDecodeError: Too deeply nested` at the position, so an `except ValueError` around untrusted input catches it |

`json.dumps` of data nested that deep raises `RecursionError`, as CPython does; a `default=` hook that
hands back the object it was given (or a container holding it) is `ValueError: Circular reference
detected`, and one that hands back a new object every time is that `RecursionError`, both as in
CPython.

`object_hook`, `parse_float`, `parse_int`, `parse_constant`, `dump` and `load` all work.
`object_pairs_hook` raises `TypeError`, and the `JSONEncoder` / `JSONDecoder` classes do not exist
— use `dumps(default=...)` and `loads(object_hook=...)`.

Dictionaries keep insertion order, so a parsed object preserves the document's key order.

## Compression

`zlib` and `gzip` are one-shot `compress` / `decompress` only; there is no `compressobj`, no
`GzipFile` and no `open`, because there is no file system. The streams are valid and decode
anywhere, but they are not byte-identical to the canonical zlib output — round-trip and
interoperation are the contract, not the bytes. `level=` maps onto the platform's three settings
rather than ten, and `gzip.compress` writes `mtime=0` by default, as CPython 3.14 does, so the same
input gives the same bytes every time.

Decompressing a bomb is bounded like everything else: inflation charges the memory budget as it
goes, and a zip bomb ends the run instead of the process.

## Modules added in 1.1.0

- **`zipfile`** works on an `io.BytesIO` only: a path, `extract` and `extractall` are a `TypeError`
  naming the missing file system. A member is inflated whole when it is opened or read. The deflate
  output is not byte-identical to zlib's, but every archive the engine writes opens in .NET's
  `ZipArchive` and the reverse. bzip2, lzma and encrypted members raise `NotImplementedError`; a
  duplicate name or an over-long comment is accepted without the `UserWarning` (there is no
  `warnings` module); `create_system` is always 3. `zipfile.Path` and `PyZipFile` do not exist.
  Inflation is charged and stops at the recorded size, and the entry count and the archive size are
  capped by the collection and string limits.
- **`contextlib`** has `suppress`, `nullcontext` and `closing`: there are no generators, so there is no
  `contextmanager` and no `ExitStack`.
- **`enum`** has `Enum`, `IntEnum`, `StrEnum`, `auto`, `unique`, the functional form `Enum('Name',
  'A B C')` and the `class Status(str, Enum)` / `(int, Enum)` form; there is no `Flag` or `IntFlag`.
  An `IntEnum` or `StrEnum` member is the int or str itself, known to the run as a member, so
  everything that takes an int or a str takes it. `__members__` is a new `dict` rather than a
  read-only proxy, and `format()` of a `(str, Enum)` member with a spec formats the value
  (`format(S.A, '>3')` is `'  a'`), where CPython formats `str(member)`.
- **`string.Template`** is CPython's own code, run as script code. `Template.pattern` is `None` on
  the base class: the pattern is built from `delimiter`, `idpattern`, `braceidpattern` and `flags`
  when a template is first used, and a subclass's own `pattern` replaces it as in CPython. The repr
  is `Template(template='$a')`.
- **`email.utils`** has the date helpers only (`formatdate`, `format_datetime`, `parsedate`,
  `parsedate_tz`, `parsedate_to_datetime`, `mktime_tz`), CPython's code; the current time and the
  local zone are the engine clock's, as for `time` and `datetime`.

## Other modules

- **`xml.etree.ElementTree`**: `iterparse` reads the whole source before the first event (a source is
  an object with `read()`, so the text is in memory anyway) and gives CPython's events for a document
  read in one chunk; the memory the `root.clear()` idiom saves in CPython is not saved, because the
  tree is held until the loop ends. `ParseError` maps the common faults (mismatched tag, no element
  found, undefined entity, duplicate attribute, junk after the document, invalid character number,
  unclosed token) to expat's text and code; any other fault is `not well-formed (invalid token)`.
  `e[3:1] = [x]` inserts as a list does, where CPython's C code loses a child and doubles another.
- **`copy`**: `copy.copy` of a list, tuple, str, bytes, range, dict, set, deque or reversed iterator
  gives an independent iterator at the same position. Of a `map`, `filter`, `zip` or `enumerate`
  object, an `hmac` object or a `ZipFile` it gives the object itself; CPython gives a new object over
  the same source, so both advance it either way. What CPython cannot copy (generators, `itertools`
  objects, hash objects, `csv` readers and writers) is a `TypeError`, as in CPython.
- **`typing`** is a runtime stub: annotations are not evaluated, `List[int]` is a new alias each time
  (`==` and `hash` agree), `get_args` is `()` and `get_origin` is `None`, and a `TypedDict` is made by
  the functional form only. A bare annotation `x: int` in a function binds nothing, so a nested
  `nonlocal x` does not find it.
- **`zoneinfo`** reads the Windows time zone database: offsets and names are kept only as far back as
  Windows holds them, so some past years read today's rules (Berlin 1975 is `CEST`).
- **`uuid.uuid1()`** draws a random multicast node once per run instead of using the hardware
  address: runs of different tenants share one process.
- **`time.strftime('%Z', time.gmtime(0))`** is `''`: the struct carries no zone name. `datetime`
  with a `tzinfo` gives CPython's name.
- **`heapq.merge`** over items whose comparison raises `StopIteration` lets that exception out, where
  CPython's generator turns it into `RuntimeError`.
- **`traceback.print_exc(file=f)`** writes the whole text in one `write` call.
- **`deque.insert`** and **`remove`** in the middle of a long deque are O(n), about 40 ms per call at
  a million items.

## Smaller things

- A `set` iterates and prints in insertion order, as a `dict` does: `{3, 1}` prints `{3, 1}`, where
  CPython prints `{1, 3}`. CPython documents no order, so code that depends on it is fragile either way.
- The dialect's syntax is the 3.14 syntax minus what it leaves out: `async`, `except*`,
  `import *`, relative imports, `type X = ...`, generic `def f[T]()`, generator functions, complex
  literals and t-strings are a `SyntaxError` with the dialect's own text. A statement that starts
  `match(`, `match[`, `match -`, `match +` or `match *` with `match` a variable is read as a match
  statement. An exception class has no `__mro__`.
- `hash()` of a string is stable across engines and processes by default, where CPython randomises
  it per process. Determinism was worth more here than hash-flood resistance, which the step budget
  bounds anyway. The host can turn randomisation on by setting `EngineOptions.StrHashSeed` to `null`.
- Mutating a `dict` or a `set` while iterating it raises `RuntimeError`, as in CPython.
- `strftime('%Y')` on a year below 1000 always gives four digits (`0001`), where CPython gives
  whatever the platform's C library gives.
- Resource-limit messages, and the `EngineAbort` that carries them, are English and fixed; they are
  meant for the host's log, not for a user.

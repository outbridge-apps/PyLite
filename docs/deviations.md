# Deviations from CPython

PyLite runs a Python dialect, not CPython. The contract is the same syntax and the same public API
doing the same thing; byte-for-byte equality is not a goal, and in the places listed here the
behaviour is knowingly different. Everything below was run on the engine at version 1.0.0 — the
outputs are what it actually prints, not what it is meant to print.

What is missing entirely — language features, builtins and module APIs — is in
[modules.md](modules.md). This file is about things that exist and behave differently.

## Text

**A `str` is a sequence of UTF-16 code units, not of code points.** The engine's strings are .NET
strings, and nothing is re-encoded on the way in. A character outside the Basic Multilingual Plane
therefore counts as two units:

```python
len('\U0001F600')      # 2, CPython says 1
'\U0001F600'[0]        # '\ud83d' — one half of the pair
```

Indexing, slicing and iteration walk those units, so they can split a pair. Encoding is unaffected
(`'\U0001F600'.encode('utf-8')` is 4 bytes, as in CPython), and every library that works in code
points — `unicodedata`, case mapping, `str.translate`, `isidentifier` — works in code points here
too. Comparison and sorting are ordinal over the same units, which puts an astral character before
`U+FFFD` where CPython puts it after.

Case mapping is Unicode 16.0 and includes the mappings that change length: `'straße'.upper()` is
`'STRASSE'`, so `len(s.upper()) == len(s)` does not hold — as in CPython.

## Numbers

`int` is arbitrary precision and bounded only by the run's limit (a million bits by default):
`10 ** 100000` is fine. `float` is IEEE-754 double, `repr` round-trips, and rounding is
half-to-even (`round(2.5)` is `2`, `round(3.5)` is `4`) exactly as in CPython. The shortest-repr
algorithm differs, so a trailing digit can differ from CPython's for some values; the value always
reads back as itself.

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

This covers money, which is what the type is for here, and does not cover scientific work. The
context object carries `prec` and `rounding` only — there is no `Emax`, `Emin`, flags or traps.

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

Roughly twenty protocol methods may be defined — `__init__`, `__repr__`, `__str__`, the six
comparisons, `__hash__`, `__bool__`, `__len__`, `__call__`, `__contains__`, the item and iteration
protocols. Any other dunder is refused when the class is defined rather than ignored at runtime:

```python
class A:
    def __add__(self, o):     # SyntaxError: this dialect does not support '__add__' definitions
        return 1

class B(X, Y):                # SyntaxError: multiple inheritance is not supported in this dialect
    pass
```

An exception class must have an empty body — `class E(Exception): pass` is a class, `E` with a
method is a `TypeError`. Exceptions are data here; behaviour belongs in record classes.

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

**Data depth is an exception,** because it is a property of the data rather than of the script's
control flow. Hashing or comparing containers nested deeper than 128 raises a catchable
`RecursionError: maximum recursion depth exceeded in comparison`. `repr` is not bounded that way —
it walks its own stack and prints a list nested 3000 deep.

The defaults a host starts from: 200,000,000 steps, a 20-second deadline, 256 MB allocated,
200 frames of call depth, 128 of data depth, 16,000,000 characters in one string, 5,000,000 items
in one collection, 1,000,000 bits in one int, 16 MB of output, 250 ms per regex operation and
5 seconds of total `time.sleep`. Every one of them is per-run and set by the host.

A regex that would backtrack forever stops on its own budget:
`re.match(r'(a+)+b', 'a' * 40)` ends the run with `regular expression exceeded time limit` instead
of hanging. Flags, back-references and look-behind all work.

A traceback carries script frames only, at most 32, with `[... N more frames omitted ...]` in the
middle.

## json

`json.loads` is strict RFC 8259 — a trailing comma, a comment, a single-quoted string, a leading
zero or `1.` are all `JSONDecodeError` with CPython's message and position. `NaN`, `Infinity` and
`-Infinity` are accepted, as in CPython. Three differences:

| | CPython | PyLite |
|---|---|---|
| a raw control character other than CR/LF inside a string | `JSONDecodeError` | accepted, the character kept |
| a number that overflows `double` (`1e400`) | `inf` | `JSONDecodeError` |
| an integer of more than about 380 digits | exact `int` | `JSONDecodeError` |

`object_hook`, `parse_float`, `parse_int`, `parse_constant`, `dump` and `load` all work.
`object_pairs_hook` raises `TypeError`, and the `JSONEncoder` / `JSONDecoder` classes do not exist
— use `dumps(default=...)` and `loads(object_hook=...)`.

Dictionaries keep insertion order, so a parsed object preserves the document's key order.

## Compression

`zlib` and `gzip` are one-shot `compress` / `decompress` only; there is no `compressobj`, no
`GzipFile` and no `open`, because there is no file system. The streams are valid and decode
anywhere, but they are not byte-identical to the canonical zlib output — round-trip and
interoperation are the contract, not the bytes. `level=` maps onto the platform's three settings
rather than ten, and `gzip.compress` writes `mtime=0` by default, so the same input gives the same
bytes every time.

Decompressing a bomb is bounded like everything else: inflation charges the memory budget as it
goes, and a zip bomb ends the run instead of the process.

## Smaller things

- `hash()` of a string is stable across engines and processes by default, where CPython randomises
  it per process. Determinism was worth more here than hash-flood resistance, which the step budget
  bounds anyway. The host can turn randomisation on by setting `EngineOptions.StrHashSeed` to `null`.
- Mutating a `dict` or a `set` while iterating it raises `RuntimeError`, as in CPython.
- `strftime('%Y')` on a year below 1000 always gives four digits (`0001`), where CPython gives
  whatever the platform's C library gives.
- Resource-limit messages, and the `EngineAbort` that carries them, are English and fixed; they are
  meant for the host's log, not for a user.

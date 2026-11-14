# PyLite.Stress — the sandbox bomb corpus

A runner and a corpus of hostile one-operation probes. Each probe does ONE thing at script scale —
megabytes of data through one call, or a structure two hundred thousand levels deep through one
walk — under a fresh engine, and the runner prints what it cost:

```
name                            outcome                             ms      cpu     steps    allocMB depth
x7_deep_built_parentmap        ok                                 2290    2251    1215661    43      1
c1_encode_cp866                ok                                 914     902     3923       22      1
```

`ms` is wall time, `cpu` the process's own CPU time for the same run. Other processes on the machine
that measured them (a scanner, a database) inflate wall time by 30% between runs of the same binary; CPU time still
moves ~15% on identical code. To compare two builds, keep both `bin/Release` folders, run them
interleaved over several rounds with `# repeat:` set, take the minimum `cpu` per arm, and treat
anything inside that band as no result.

## What to read

**The steps column, first.** The deadline is checked on the `Budget.Step` cadence, so a loop that
charges nothing is invisible to the deadline as well as to the step limit. A probe that pushes eight
megabytes through one call and leaves a dozen steps on the counter has found such a loop; a charged
pass over the same data shows a few thousand (one `LinearChunk` per `LinearChunkSize` = 4096 units).
The audit that built this corpus (2026-09-11) found nine of them this way.

**The outcome, second.** `ok`, `raise <Type>` and `budget <Limit>` are all passes: the sandbox held,
whether the script finished, raised, or was stopped. A probe fails, and the runner exits 1, when it ends
as `HOSTERROR`, an engine fault or `THREW`, returns more than 500 ms past its deadline (compile time
excluded: the deadline starts after it), leaks its runner thread, grows the process's working set past four
times its memory limit plus 512 MB while it runs (memory the budget never saw), keeps a core more than 60 %
busy in the quarter second after it returned (work that outlived the run), or ends in another kind of outcome than
`bombs/expected.txt` pins for it (`ok`, `raise <Type>`, `budget`, `syntax`; which limit a budget probe
hits first depends on the machine, so it is not pinned; a probe the deadline stops, or that takes half of it,
is pinned `ok|budget`, since a faster or slower machine moves it across). The flags follow the row: `OVERSHOOT`, `LEAK`,
`PEAKMEM`, `BUSYAFTER`, `EXPECTED(<kind>)`, `UNPINNED`; `--tsv` also writes each probe's peak above its start, in MB.

**ms against the deadline, third.** A probe that ends as `budget DeadlineMs` at 20 s with a modest step
count is doing work the counter under-prices (a memmove per element, say); it is bounded, but note it.

## Running

```
cd tools/PyLite.Stress
dotnet run -c Release -- bombs          # the whole corpus, ~5 minutes
dotnet run -c Release -- bombs x7       # every probe whose name contains "x7"
dotnet run -c Release -- bombs --tsv rows.tsv        # also the rows, tab-separated (what the gate reads)
dotnet run -c Release -- bombs --write-expected      # pin this run's outcomes; review the diff
```

A probe sets its own limits in header comments: `# deadline: <ms>` (default 3000), `# allocmb: <MB>`
(default 256), `# repeat: <n>` (best of n), and as a host would raise or lower them
`# datadepth: <n>`, `# sleepms: <ms>`, `# steps: <n>`, `# items: <n>`. Everything else is `ResourceLimits.Default`, and
the engine's hard caps apply (a `datadepth` of 1000000 runs at 10000).

## The probe bank

`bank/` holds every probe the reviewers of 1.2.0 wrote, about 1,200 scripts in the same header format, and
`bank/pinned.tsv` pins each one to what the approved build gave: the kind of outcome, a hash of its output, its
steps and its charged memory, and, for the probes 1.1.0 ran to the end, 1.1.0's steps and memory as a ceiling.
A probe fails when its outcome or output changes, when its steps or charge grow past 1.25 times the pin (a new
false limit or a hidden slowdown on its way), when the sandbox checks above fail, or when it needs more than
1.5 times what 1.1.0 needed without a note saying why. Steps and charges are deterministic, so the gate does
not flicker.

```
dotnet run -c Release -- bank                    # check every probe, ~17 minutes
dotnet run -c Release -- bank x11                # the probes whose name contains "x11"
dotnet run -c Release -- bank x11 --write-pins   # pin them on this build; review the pinned.tsv diff
```

## The corpus

`bombs/` holds the generated probes; `gen_bombs.sh` regenerates them (Git Bash). Groups, by prefix:

| prefix | what it bombs |
|---|---|
| `j` | json: an invalid tail forcing the full RFC scan, many tokens, many spaces, deep nesting, a parse hook per number |
| `c` | codecs on 8M chars, strict and with an error handler |
| `u` | urllib.parse: quote/unquote/parse_qs/urlencode/urlsplit on megabytes, a taught scheme list of 200k |
| `b`, `s` | bytes text methods and str case mappings on 8M |
| `r` | re: a 1M-char pattern, 100k `\N{}` classes, 50k counted repeats |
| `h`, `g`, `t` | hashlib on 8M and 3M pbkdf2 rounds, gzip/zlib bombs under a 64 MB cap, struct.iter_unpack |
| `v` | csv: 2M fields on one line, an 8M quoted field past the limit, Sniffer on 2 MB, a duck dialect 200k times |
| `x` | xml.etree: 500k siblings through every serializer method, a 200k-deep script-built chain through tostring / iter / itertext / `..` / indent / XMLID / deepcopy, quadratic XPath text predicates |
| `d` | deep data: 100k-nested list / tuple / dataclass / record through repr, ==, hash, deepcopy, json.dumps; the opaque containers that re-enter the engine (deque, slice, partial, ChainMap, exception args) through ==, repr |
| `p` | record protocol hooks that recurse or never end: `__eq__`, `__hash__`, `__str__`, `__len__`, `__bool__`, `__iter__`, `__getitem__`, `__contains__`, `__call__`, a million `id()`s |
| `m` | statistics and Decimal at scale and at the edges of the 28-digit backend |
| `w` | the rest touched that week: html, strftime with 1M directives, strptime/fromisoformat on garbage, %-format, textwrap, bisect, heapq, deque, unicodedata, operator, Counter, fromhex, a 400k-digit int |
| `z` | zipfile: a 15 MB-of-zeros member read 20 times under 64 MB, archives nested three deep, 300k entries, overlapped entries, a forged 2 GB size, 8 MB of non-archive bytes |
| `l` | the limits audit of 2026-10-29: leaf re-entry under a host-raised data depth (deque, exception args, slice, `asdict`), chains and big merges of `functools.partial`, `time.sleep` past its budget, million-digit `int`/`Decimal`/`Fraction` literals, million-bit `bit_length`, `pow(b, e, m)` and its inverse, `isqrt`, `gcd`, `str()`, `from_bytes`/`to_bytes`, Fraction parts and powers. Each pins a defect that audit fixed; the deadline-bound ones run under 3 s and should end within ~50 ms of it |
| `n` | native calls that outlived their run (2026-10-30): one .NET call each that kept computing past deadline + 5.25 s on 1.1.0 — huge float/`Decimal` format precision, a zero `Decimal` with a 10⁹ exponent, the csv `Sniffer` regex, an `IntEnum` str value, long `capwords`/`parse_qsl` separators, a 16 MB pbkdf2 salt, `str(Fraction)`, `textwrap.wrap` on 16 MB, `random.seed` of a long str, `re.compile('a{100000}')`, a `range` with a huge step, `statistics.variance` of million-bit ints. All run under 3 s and end on a result or a limit |
| `q` | the sandbox review of 2026-10-30, each of which killed the process or broke it for later runs: `[*[*[*…]]]` 3,000 deep, `str` of an exception nested 200,000 times, `repr` of a nested `itertools.repeat` chain, `<=` of nested `Counter`s, `raise e from e` with megabytes of notes. They end on a SyntaxError, a RecursionError or the run's own exception |
| `k` | the same review: a loop of calls that each did long work for the one step it charged leaked the runner thread — `float`/`Decimal`/`uuid.UUID` of a 16M-char string, `in` and `sort` of long strs, set `<=`, `ChainMap` of 4M maps, `isalnum` of astral text, `StringIO.readline`, `tee` with 1.5M clones, 300K keywords against 10K parameters, an XML tree that contains itself, `hmac` with a 16 MB key, a 200K-char csv line terminator, a 3M-field `namedtuple`, big-int `% 7`, 100K exception notes. Each ends on its deadline, a limit, or its own result |
| `o` | the same review: memory taken far past what the budget charged — `ascii` and `format(.., ".250000000g")` of huge text, a 16M-char `print` sep, `json.dumps` escapes, `strftime("%Z" * 300)` with a long zone name, a repeated `str.format` field, an f-string past the cap, `f(*repeat(x))`, `math.dist` of an endless iterator, kept regex matches (an `endpos` copy, 2,000 groups), `json.dumps` and `repr` of a 5M list, XML `extend` past the item cap, `attrgetter` of 15M dots, `urljoin` of 5M `../`, a csv field across 400 lines. Each ends on the limit it would cross, or succeeds within its budget |
| `s` | the same review: values the engine cast unchecked — XML `text`/`tail`/attribute names/children of the wrong type, `Decimal.compare()` with no argument, `quantiles` past int range, a VERBOSE pattern ending in a comment, `pos > endpos`, `normalize` of 4M noncharacters, a zip `header_offset` of `2**63 - 16` — each a script error or CPython's answer; and sizes past the item cap in one step — `struct.unpack('0s' * 6M)`, `combinations_with_replacement(.., 20M)`, `product(repeat=12M)`, slice assignment and `insert` on a full list |
| `e` | the second sandbox review (2026-10-30): `repr` of a `defaultdict` that is its own factory or a 500K chain of them, `except` with a tuple nested 700,000 deep (stack overflows); chains of `itemgetter`, `lru_cache` wrappers and `cmp_to_key` keys, kept `format(s, '.N')` results and `finditer` copies, a `json.dumps` separator or indent per nesting level, 150 classes of 35,000 fields (memory with nothing charged); `namedtuple` defaults, `DictWriter` fieldnames, `''.join` and `writerow` of an endless iterator. Each ends on a limit or a clean error |
| `f` | the same review: work that grew with a script-chosen size on one step, which leaked the runner thread — a 65,000-term `or` filter, a 200,000-leaf sum, 99,000 `if` clauses, 45,000 keyword-only defaults, a class statement re-walking a 50,000-line `__init__`, `del L[0]` on 5M items, 3,000 long prefixes in `startswith`, a tuple of 16 MB bytes compared, `slice.indices` and `//` of million-bit ints, `asdict` of a 40-level DAG, `randbytes`, `html.unescape` legacy names, a 3M-part `attrgetter`, `deque *=` under a maxlen, `capwords` of 8M words; and `case [a, *rest]` on `range(2**40)`. Each ends on its deadline or a limit |
| `y` | the same review, memory and native loops: an output built in full before its size was checked (`quote`, `re.escape`, XML attribute escaping, `normalize('NFKD')`, `bytes.hex(':')`, `encode('utf-16')`, a growing `replace`, `b32encode`, a csv field with `field_size_limit` raised), parts held uncharged (`rsplit`, `bytes.split`, `textwrap.shorten`, `parse_qs`), kept `Counter.elements()` iterators, `find`/`iterfind`/`itertext` over 6,000 shared copies of a 6,000-child element, `SubElement` past the item cap, `re.sub('', ...)`, `re.split('', ...)`, `JSONDecodeError` over a 16M-char document, `b32encode` in a loop, `factorial(10**7)`. Each ends on the limit it would cross, its deadline, or its own result |

Probe sources are backslash-free on purpose: the shell tooling that generates them halves backslashes.
Spell a non-ASCII character as `chr(...)`.

## Adding a probe

One operation per file, so the steps column is that operation's own count. Build the input cheaply
(`'x' * 8000000` is one allocation) and make the operation the only expensive line. If the operation
is expected to be refused, catch the exception and print `clean <Type>` so the outcome reads `ok`.

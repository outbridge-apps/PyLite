# PyLite.Stress — the sandbox bomb corpus

A runner and a corpus of hostile one-operation probes. Each probe does ONE thing at script scale —
megabytes of data through one call, or a structure two hundred thousand levels deep through one
walk — under a fresh engine, and the runner prints what it cost:

```
name                            outcome                             ms      cpu     steps    allocMB depth
x7_deep_built_parentmap        ok                                 2290    2251    1215661    43      1
c1_encode_cp866                ok                                 914     902     3923       22      1
```

`ms` is wall time, `cpu` the process's own CPU time for the same run. Other processes on this box
(a scanner, a database) inflate wall time by 30% between runs of the same binary; CPU time still
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
whether the script finished, raised, or was stopped. `HOSTERROR` and `THREW` are failures — a probe
that outran its deadline until the host killed the run, or crashed the engine — and the runner's exit
code is 1 when any probe ends that way, so the corpus can gate.

**ms against the deadline, third.** A probe that ends as `budget DeadlineMs` at 20 s with a modest step
count is doing work the counter under-prices (a memmove per element, say); it is bounded, but note it.

## Running

```
cd tools/PyLite.Stress
dotnet run -c Release -- bombs          # the whole corpus, ~5 minutes
dotnet run -c Release -- bombs x7       # every probe whose name contains "x7"
```

A probe sets its own limits in header comments: `# deadline: <ms>` (default 3000), `# allocmb: <MB>`
(default 256), `# repeat: <n>` (best of n), and as a host would raise or lower them
`# datadepth: <n>`, `# sleepms: <ms>`, `# steps: <n>`. Everything else is `ResourceLimits.Default`, and
the engine's hard caps apply (a `datadepth` of 1000000 runs at 10000).

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

Probe sources are backslash-free on purpose: the shell tooling that generates them halves backslashes.
Spell a non-ASCII character as `chr(...)`.

## Adding a probe

One operation per file, so the steps column is that operation's own count. Build the input cheaply
(`'x' * 8000000` is one allocation) and make the operation the only expensive line. If the operation
is expected to be refused, catch the exception and print `clean <Type>` so the outcome reads `ok`.

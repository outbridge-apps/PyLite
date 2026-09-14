# Performance

PyLite was written to replace IronPython inside a host that could not live with it, so the useful
question is not "is it fast" but "what changes if you switch". These are our numbers against
IronPython 3.4.2.

## How this was measured

Both engines run in the same .NET Framework 4.7.2 process, on the same machine, over the same
script text, one after the other. Every script is written in 3.4-era syntax, because IronPython
has to be able to run it too. Each benchmark is warmed up three times and then measured fifteen
times; a run reports the median of those fifteen.

The whole set was run three times end to end, on one build of the engine at version 1.0.0. The
table gives the range across those three runs, not a single number, because a single run on this
hardware is not reproducible: between two runs of unchanged code, individual benchmarks moved
by up to a factor of two. Where the three runs disagree even about the direction, the verdict
says so instead of inventing a multiple.

Absolute milliseconds come from a virtual development server that also hosts a database and an
application server, so background load is part of them. Read the ratios; the milliseconds are
there to show the size of each workload.

The benchmark harness is not part of this repository. These numbers are ours, measured as
described above, and nobody can reproduce them by cloning this repository. Before a decision that
depends on them, measure your own workload.

## Startup

A host that builds an engine per request pays this on every call; a host that builds one engine
and reuses it pays it once.

| | PyLite | IronPython 3.4.2 | |
|---|---:|---:|---|
| first run in a fresh process (JIT included) | 0.7–1.9 s | 4.5–4.8 s | ×2.4–6.2 faster |
| new engine in a warm process | 0.8–1.5 ms | 108–133 ms | ×73–173 faster |

The gap is structural rather than clever: IronPython stands up the DLR and its own compilation
pipeline, PyLite allocates an object graph. The first row is one sample per run, so treat it as an
order of magnitude.

## Language and library work

| workload | PyLite | IronPython | verdict |
|---|---:|---:|---|
| money arithmetic in `decimal`: divide, multiply, quantize to cents, accumulate (5,000) | 30–49 ms | 2.7–3.3 s | **×66–91 faster** |
| serialise, parse and total 10,000 JSON orders | 303–368 ms | 3.07–3.14 s | **×8.5–10.1 faster** |
| integer arithmetic in a loop (1,000,000) | 712–752 ms | 1.37–1.88 s | ×1.9–2.5 faster |
| write and read back 7,500 CSV rows | 114–169 ms | 262–269 ms | ×1.6–2.4 faster |
| list comprehension with a filter (500,000) | 162–175 ms | 261–334 ms | ×1.6–2.1 faster |
| dictionary insert and read-back (200,000) | 474–528 ms | 552–759 ms | ×1.2–1.5 faster |
| calling a function in a loop (500,000) | 418–576 ms | 465–687 ms | about equal |
| compile a regex and scan 10,000 log lines | 69–78 ms | 72–75 ms | about equal |
| datetime arithmetic and weekday filter (25,000) | 81–161 ms | 74–100 ms | about equal, direction varies |
| parse 15,000 delimited lines into a report | 146–154 ms | 128–135 ms | ×1.1–1.2 slower |
| build and join 100,000 strings | 305–313 ms | 197–289 ms | ×1.1–1.6 slower |
| `sorted` with a `key` lambda (100,000) | 303–311 ms | 94–118 ms | **×2.6–3.3 slower** |

## Where PyLite loses, and why

IronPython compiles Python to .NET code through the DLR. In a tight loop that calls back into a
small function — which is exactly what `sorted(key=...)` is — that compiled call beats an
interpreter's dispatch, and no amount of tuning inside an interpreter closes a gap of that shape.
The same effect, much weaker, is what the string and text rows show.

This is the trade the design makes on purpose. An interpreter that never emits code is also an
interpreter that can count steps, stop on a deadline, cap memory and refuse recursion — the
properties the host needed in the first place. If your workload is a hot numeric loop over a
`key` function and nothing about resource limits matters to you, IronPython is the faster tool and
this table says so.

## What it means in practice

The rows that move by an order of magnitude are the ones integration scripts actually live on:
money in `decimal`, JSON in and out, and the cost of having an engine at all. The rows where the
two are within 20% of each other are the ones where the choice does not matter. One row costs you
threefold, and it is a narrow one.

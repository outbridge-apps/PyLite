# Performance

PyLite was written to replace IronPython inside a host that could not live with it, so the useful
question is not "is it fast" but "what changes if you switch". These are our numbers against
IronPython 3.4.2.

## How this was measured

Both engines run in the same .NET Framework 4.7.2 process, on the same machine, over the same
script text, one after the other. Every script is written in 3.4-era syntax, because IronPython
has to be able to run it too. Each benchmark is warmed up three times and then measured fifteen
times; a run reports the median of those fifteen.

The whole set was run three times end to end, each pass in a fresh process, on the release commit of
version 1.1.0; every sample, the allocations and the per-pass medians are in
[benchmarks/results/1.1.0](../benchmarks/results/1.1.0/summary.md). The table gives the range across
those three passes, not a single number, because a single run on this hardware is not reproducible:
between two runs of unchanged code, individual benchmarks moved by up to a factor of two. Where the
three passes disagree even about the direction, the verdict says so instead of inventing a multiple.

Absolute milliseconds come from a virtual development server that also hosts a database and an
application server, so background load is part of them. Read the ratios; the milliseconds are
there to show the size of each workload.

The harness is `tools/PyLite.PerfBench` in this repository, and the scripts it runs are in its
`Program.Corpus.cs`. One pass of the set:

```
dotnet run --project tools/PyLite.PerfBench -c Release -- --full
```

`benchmarks/run.ps1` runs several passes in fresh processes and keeps every sample, the allocations per
run and a summary ([benchmarks/](../benchmarks)). IronPython 3.4.2 comes from NuGet. Your milliseconds will be your machine's; the ratios should
land near these. Before a decision that depends on them, measure your own workload.

## Startup

A host that builds an engine per request pays this on every call; a host that builds one engine
and reuses it pays it once.

| | PyLite | IronPython 3.4.2 | |
|---|---:|---:|---|
| first run in a fresh process (JIT included) | 0.40–1.16 s | 3.1–4.8 s | ×4.2–7.6 faster |
| new engine in a warm process | 0.53–0.65 ms | 120–122 ms | ×187–226 faster |

The gap is structural rather than clever: IronPython stands up the DLR and its own compilation
pipeline, PyLite allocates an object graph. The first row is one sample per run, so treat it as an
order of magnitude.

## Language and library work

| workload | PyLite | IronPython | verdict |
|---|---:|---:|---|
| money arithmetic in `decimal`: divide, multiply, quantize to cents, accumulate (5,000) | 30–34 ms | 2.18–2.21 s | **×65–73 faster** |
| serialise, parse and total 10,000 JSON orders | 159–167 ms | 2.14–2.20 s | **×13–14 faster** |
| write and read back 7,500 CSV rows | 79–80 ms | 198–209 ms | ×2.5–2.6 faster |
| integer arithmetic in a loop (1,000,000) | 536–562 ms | 1.01–1.05 s | ×1.86–1.89 faster |
| list comprehension with a filter (500,000) | 101–113 ms | 191–221 ms | ×1.84–1.95 faster |
| dictionary insert and read-back (200,000) | 296–323 ms | 424–449 ms | ×1.33–1.51 faster |
| calling a function in a loop (500,000) | 239–272 ms | 344–375 ms | ×1.27–1.52 faster |
| parse 15,000 delimited lines into a report | 79–93 ms | 92–99 ms | ×1.07–1.18 faster |
| datetime arithmetic and weekday filter (25,000) | 55–60 ms | 54–55 ms | about equal |
| compile a regex and scan 10,000 log lines | 46–48 ms | 44–45 ms | about equal |
| `sorted` with a `key` lambda (100,000) | 79–86 ms | 58–73 ms | ×1.08–1.36 slower |
| build and join 100,000 strings | 174–226 ms | 155–162 ms | ×1.1–1.46 slower |

Against 1.0.0, three rows changed because the code did: `sorted` with a `key` went from ×2.6–3.3
slower to ×1.08–1.36 (a merge sort that finds the runs already in the data, and keys compared
without boxing), calling a function from about equal to faster (a one-line `def` now runs on the
path lambdas use), and JSON from ×8.5–10 to ×13–14 faster.

PyLite allocates less than IronPython on seven of the twelve workloads (`decimal` 6.7 MB a run
against 71 MB, JSON 22 MB against 147 MB) and more on the five that are mostly calls and small
objects: function calls, the dictionary, string joining, `sorted` with a key and datetime. The
allocations per run, both engines, are in the summary linked above.

## Where PyLite loses, and why

IronPython compiles Python to .NET code through the DLR. In a tight loop that calls back into a
small function — which is exactly what `sorted(key=...)` is — that compiled call beats an
interpreter's dispatch, and no amount of tuning inside an interpreter closes a gap of that shape.
The same effect, much weaker, is what the string row shows.

This is the trade the design makes on purpose. An interpreter that never emits code is also an
interpreter that can count steps, stop on a deadline, cap memory and refuse recursion — the
properties the host needed in the first place. If your workload is a hot loop over a `key`
function and nothing about resource limits matters to you, IronPython is the faster tool and this
table says so.

## What it means in practice

The rows that move by an order of magnitude are the ones integration scripts actually live on:
money in `decimal`, JSON in and out, and the cost of having an engine at all. The rows where the
two are within 20% of each other are the ones where the choice does not matter. The two rows
where PyLite is slower cost 8–46%, on loops that call back into script code or build many small
strings.

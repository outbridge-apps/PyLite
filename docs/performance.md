# Performance

PyLite was written to replace IronPython inside a host that could not live with it, so the useful
question is not "is it fast" but "what changes if you switch". These are our numbers against
IronPython 3.4.2.

## How this was measured

Both engines run in the same .NET Framework 4.7.2 process, on the same machine, over the same
script text, one after the other. Every script is written in 3.4-era syntax, because IronPython
has to be able to run it too. Each benchmark is warmed up three times and then measured fifteen
times; a run reports the median of those fifteen.

The whole set was run three times end to end, on the release build of version 1.1.0. The table
gives the range across those three runs, not a single number, because a single run on this
hardware is not reproducible: between two runs of unchanged code, individual benchmarks moved by
up to a factor of two. Where the three runs disagree even about the direction, the verdict says so
instead of inventing a multiple. One further pass ran into a burst of background load, which
slowed both engines by the same factor on the same rows, and was repeated.

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
| first run in a fresh process (JIT included) | 0.44–0.75 s | 2.9–6.3 s | ×6.0–8.4 faster |
| new engine in a warm process | 0.5–0.6 ms | 98–122 ms | ×196–224 faster |

The gap is structural rather than clever: IronPython stands up the DLR and its own compilation
pipeline, PyLite allocates an object graph. The first row is one sample per run, so treat it as an
order of magnitude.

## Language and library work

| workload | PyLite | IronPython | verdict |
|---|---:|---:|---|
| money arithmetic in `decimal`: divide, multiply, quantize to cents, accumulate (5,000) | 32–40 ms | 2.35–2.46 s | **×59–77 faster** |
| serialise, parse and total 10,000 JSON orders | 165–288 ms | 2.29–2.51 s | **×8.1–15.2 faster** |
| write and read back 7,500 CSV rows | 83–85 ms | 213–252 ms | ×2.5–3.0 faster |
| list comprehension with a filter (500,000) | 98–113 ms | 197–215 ms | ×1.8–2.0 faster |
| integer arithmetic in a loop (1,000,000) | 555–599 ms | 1.03–1.05 s | ×1.75–1.86 faster |
| calling a function in a loop (500,000) | 268–331 ms | 363–405 ms | ×1.1–1.4 faster |
| dictionary insert and read-back (200,000) | 312–339 ms | 373–422 ms | ×1.17–1.24 faster |
| parse 15,000 delimited lines into a report | 92–98 ms | 92–104 ms | about equal |
| datetime arithmetic and weekday filter (25,000) | 55–59 ms | 55–58 ms | about equal |
| compile a regex and scan 10,000 log lines | 50–54 ms | 45–54 ms | about equal, direction varies |
| build and join 100,000 strings | 174–209 ms | 152–184 ms | ×1.1–1.15 slower |
| `sorted` with a `key` lambda (100,000) | 95–107 ms | 69–76 ms | **×1.4 slower** |

Against 1.0.0, two rows changed because the code did: `sorted` with a `key` went from ×2.6–3.3
slower to ×1.4 (a merge sort that finds the runs already in the data, and keys compared without
boxing), and calling a function from about equal to faster (a one-line `def` now runs on the path
lambdas use). JSON measured ×8–15 faster, against ×8.5–10 for 1.0.0.

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
where PyLite is slower cost up to 40%, on loops that call back into script code.

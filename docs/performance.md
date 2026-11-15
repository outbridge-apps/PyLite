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
version 1.2.0; every sample, the allocations and the per-pass medians are in
[benchmarks/results/1.2.0](../benchmarks/results/1.2.0/summary.md), and the 1.1.0 passes, on the same
machine, in [benchmarks/results/1.1.0](../benchmarks/results/1.1.0/summary.md). The table gives the range across
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
| first run in a fresh process (JIT included) | 0.39–0.96 s | 3.0–4.5 s | ×4.7–7.7 faster |
| new engine in a warm process | 0.50–0.61 ms | 91–114 ms | ×166–228 faster |

The gap is structural rather than clever: IronPython stands up the DLR and its own compilation
pipeline, PyLite allocates an object graph. The first row is one sample per run, so treat it as an
order of magnitude.

## Language and library work

| workload | PyLite | IronPython | verdict |
|---|---:|---:|---|
| money arithmetic in `decimal`: divide, multiply, quantize to cents, accumulate (5,000) | 27–33 ms | 2.11–2.22 s | **×66–78 faster** |
| serialise, parse and total 10,000 JSON orders | 122–137 ms | 2.07–2.17 s | **×15–18 faster** |
| write and read back 7,500 CSV rows | 46–51 ms | 198–204 ms | ×4.0–4.5 faster |
| list comprehension with a filter (500,000) | 90–101 ms | 175–255 ms | ×1.9–2.5 faster |
| integer arithmetic in a loop (1,000,000) | 547–556 ms | 0.99–1.00 s | ×1.77–1.83 faster |
| dictionary insert and read-back (200,000) | 243–250 ms | 433–469 ms | ×1.73–1.91 faster |
| calling a function in a loop (500,000) | 254–264 ms | 352–368 ms | ×1.33–1.45 faster |
| parse 15,000 delimited lines into a report | 75–84 ms | 92–96 ms | ×1.1–1.26 faster |
| build and join 100,000 strings | 142–152 ms | 152–166 ms | ×1.06–1.15 faster |
| compile a regex and scan 10,000 log lines | 41–46 ms | 42–50 ms | about equal, direction varies |
| `sorted` with a `key` lambda (100,000) | 65–72 ms | 63–70 ms | about equal, direction varies |
| datetime arithmetic and weekday filter (25,000) | 57–60 ms | 50–54 ms | ×1.06–1.18 slower |

Against 1.1.0, on the same machine: of the two rows where PyLite was slower, `sorted` with a `key`
(×1.08–1.36 then) is now about equal and string joining (×1.1–1.46 slower then) is ×1.06–1.15
faster; CSV went from ×2.5–2.6 to ×4.0–4.5 faster, JSON from ×13–14 to ×15–18, and the dictionary
row from ×1.33–1.51 to ×1.73–1.91. Datetime arithmetic went from about equal to ×1.06–1.18 slower,
although PyLite's own time hardly moved (55–60 ms in 1.1.0, 57–60 ms now): the IronPython passes
ran a little faster this time (54–55 ms then, 50–54 ms now), and a difference of this size is within
what this machine moves between runs.

PyLite allocates less than IronPython on eight of the twelve workloads (`decimal` 6.7 MB a run
against 71 MB, JSON 17 MB against 147 MB, integer arithmetic 122 MB against 245 MB) and more on the
four that are mostly calls and small objects: function calls, string joining, `sorted` with a key and
datetime. Against 1.1.0 it allocates less on eleven of the twelve, and as much on `decimal`: string
joining 18 MB a run instead of 39, integer arithmetic 122 MB instead of 183. The allocations per run,
both engines, are in the summaries linked above.

## Where PyLite loses, and why

IronPython compiles Python to .NET code through the DLR. In a tight loop that calls back into a
small function, that compiled call is the stronger shape, and an interpreter's dispatch can only
narrow the gap: `sorted(key=...)` and string building, where 1.1.0 was slower, now come out equal or
a little ahead, and datetime arithmetic, the one slower row, is 6–18% behind.

This is the trade the design makes on purpose. An interpreter that never emits code is also an
interpreter that can count steps, stop on a deadline, cap memory and refuse recursion — the
properties the host needed in the first place. If your workload is a hot loop and nothing about
resource limits matters to you, measure it: IronPython may be the faster tool there.

## What it means in practice

The rows that move by an order of magnitude are the ones integration scripts actually live on:
money in `decimal`, JSON in and out, and the cost of having an engine at all. The rows where the
two are within 20% of each other are the ones where the choice does not matter; the one row where
PyLite is slower is among them.

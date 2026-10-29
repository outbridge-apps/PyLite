# Benchmarks: PyLite 1.1.0 and IronPython 3.4.2

3 passes of `tools/PyLite.PerfBench --full`, each benchmark warmed up 3 times and measured 15 times per pass. The samples are in the `pass-*.json` files next to this page.

| | |
|---|---|
| PyLite | 1.1.0, commit `8f16a60` |
| IronPython | 3.4.2 (NuGet) |
| OS | Windows Server 2022 Datacenter Evaluation, build 20348.5622 |
| CPU | Intel(R) Xeon(R) CPU E5-2650 v2 @ 2.60GHz, 8 logical processors |
| .NET | .NET Framework 4.8.4795.0 |
| passes | 2026-10-29T19:34:38Z, 2026-10-29T19:37:36Z, 2026-10-29T19:40:32Z |

Each cell gives the range across the passes. The median is the middle sample of a pass; the spread
is the fastest and the slowest single sample over all passes; allocations are the bytes the whole
process allocated per run. The verdict compares the medians pass by pass. Rows where PyLite is slower
stay in the table.

| benchmark | PyLite median | PyLite spread | PyLite allocated | IronPython median | IronPython spread | IronPython allocated | verdict |
|---|---:|---:|---:|---:|---:|---:|---|
| `cold_start` | 402–1162 ms | 402–1162 ms | 0.62 MB | 3072–4828 ms | 3072–4828 ms | 9.5 MB | ×4.2–7.6 faster |
| `engine_spinup` | 0.53–0.65 ms | 0.42–10.7 ms | 0.03 MB | 120–122 ms | 103–134 ms | 6.8 MB | ×187–226 faster |
| `micro_arith_loop` | 536–562 ms | 516–679 ms | 183 MB | 1014–1053 ms | 864–1218 ms | 245 MB | ×1.86–1.89 faster |
| `micro_func_calls` | 239–272 ms | 231–328 ms | 45.6 MB | 344–375 ms | 333–608 ms | 22.9 MB | ×1.27–1.52 faster |
| `micro_dict_ops` | 296–323 ms | 243–628 ms | 61.2 MB | 424–449 ms | 360–1011 ms | 47.1 MB | ×1.33–1.51 faster |
| `micro_str_join` | 174–226 ms | 146–489 ms | 38.7 MB | 155–162 ms | 136–211 ms | 13.8 MB | ×1.1–1.46 slower |
| `micro_list_comp` | 101–113 ms | 67.0–171 ms | 15.5 MB | 191–221 ms | 135–360 ms | 34.0 MB | ×1.84–1.95 faster |
| `micro_sorted_key` | 78.6–86.4 ms | 56.3–109 ms | 14.1 MB | 57.8–73.4 ms | 42.8–88.3 ms | 8.7 MB | ×1.08–1.36 slower |
| `micro_decimal_money` | 30.3–33.6 ms | 28.5–56.8 ms | 6.7 MB | 2176–2209 ms | 1886–2792 ms | 70.9 MB | ×65–73 faster |
| `macro_json_orders` | 159–167 ms | 120–214 ms | 21.9 MB | 2143–2198 ms | 1968–2548 ms | 147 MB | ×13–14 faster |
| `macro_text_report` | 79.1–93.0 ms | 74.2–130 ms | 16.5 MB | 92.5–99.4 ms | 78.2–147 ms | 19.4 MB | ×1.07–1.18 faster |
| `macro_regex_log` | 46.0–47.6 ms | 37.0–86.0 ms | 9.4 MB | 43.7–45.0 ms | 38.6–57.9 ms | 12.5 MB | about equal |
| `macro_datetime_calc` | 55.4–59.5 ms | 51.6–74.8 ms | 9.1 MB | 54.2–54.7 ms | 48.7–70.0 ms | 4.0 MB | about equal |
| `macro_csv_roundtrip` | 79.0–80.4 ms | 75.0–96.7 ms | 20.0 MB | 198–209 ms | 190–308 ms | 20.5 MB | ×2.5–2.6 faster |

`cold_start` is one sample per pass: the first run in a fresh process, JIT included.
